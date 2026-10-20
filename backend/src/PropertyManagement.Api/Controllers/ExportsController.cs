using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Api.Authorization;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Exports;

namespace PropertyManagement.Api.Controllers;

/// <summary>
/// Downloads of the organization's data as a ZIP of CSV files: all of it (design 4.8), or everything held about one
/// tenant or owner for a data subject request (design 11.4). Admin only, and every export is audited.
/// </summary>
[ApiController]
[Route("api/v1/exports")]
[RequirePermission(Permissions.Data.Export)]
public sealed partial class ExportsController(IDataExporter exporter) : ControllerBase
{
    [HttpGet("organization")]
    public Task<FileStreamResult> Organization([FromQuery] bool includeFiles, CancellationToken ct) =>
        ZipAsync(async stream =>
        {
            await exporter.WriteOrganizationAsync(stream, includeFiles, ct);
            return "organization-data";
        });

    [HttpGet("tenants/{id:guid}")]
    [RequirePermission(Permissions.Tenants.View)]
    public Task<FileStreamResult> Tenant(Guid id, CancellationToken ct) =>
        ZipAsync(async stream => $"tenant-{Slug(await exporter.WriteTenantAsync(stream, id, ct))}");

    [HttpGet("owners/{id:guid}")]
    [RequirePermission(Permissions.Owners.View)]
    public Task<FileStreamResult> Owner(Guid id, CancellationToken ct) =>
        ZipAsync(async stream => $"owner-{Slug(await exporter.WriteOwnerAsync(stream, id, ct))}");

    /// <summary>Builds the ZIP in a temporary file that is deleted once the download ends, so large exports don't sit in memory.</summary>
    private async Task<FileStreamResult> ZipAsync(Func<Stream, Task<string>> write)
    {
        var stream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            var name = await write(stream);
            stream.Position = 0;
            Response.Headers.CacheControl = "no-store";
            return File(stream, "application/zip", $"{name}-{DateTime.UtcNow.AddHours(1):yyyy-MM-dd}.zip");
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private static string Slug(string name)
    {
        var slug = NonWord().Replace(name.ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "person" : slug[..Math.Min(slug.Length, 60)];
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonWord();
}
