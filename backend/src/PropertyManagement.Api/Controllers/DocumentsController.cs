using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using PropertyManagement.Api.Authorization;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Documents;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Documents;

namespace PropertyManagement.Api.Controllers;

/// <summary>
/// Documents and photos filed against properties, owners, tenants, leases and repairs (design 6.13). The service
/// checks access to the record on every call; the attributes only keep out members with no document rights at all.
/// </summary>
[ApiController]
[Route("api/v1/documents")]
public sealed class DocumentsController(DocumentService documents) : ControllerBase
{
    /// <summary>Upload limit for the whole request: the largest file plus room for the form fields.</summary>
    public const long RequestLimit = DocumentService.DefaultMaxBytes + 1024 * 1024;

    [HttpGet]
    [RequireAnyPermission(Permissions.Documents.View, Permissions.Maintenance.UpdateAssigned)]
    public Task<DocumentListDto> List([FromQuery] DocumentSubject subject, [FromQuery] Guid subjectId, CancellationToken ct) =>
        documents.ListAsync(subject, subjectId, ct);

    [HttpGet("expiring")]
    [RequirePermission(Permissions.Documents.View)]
    public Task<IReadOnlyList<ExpiringDocumentDto>> Expiring([FromQuery] int days = 60, CancellationToken ct = default) =>
        documents.ExpiringAsync(days, ct);

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimit)]
    [RequireAnyPermission(Permissions.Documents.Upload, Permissions.Maintenance.UpdateAssigned)]
    public async Task<ActionResult<DocumentDto>> Upload([FromForm] UploadDocumentForm form, CancellationToken ct)
    {
        var content = await UploadReader.ReadAsync(form.File, documents.MaxBytes, ct);
        var saved = await documents.UploadAsync(new UploadDocument(form.Subject, form.SubjectId, form.File!.FileName, content,
            form.Title, form.Category, form.ExpiresOn, form.SharedWithOwner, form.SharedWithTenant), ct);
        return StatusCode(StatusCodes.Status201Created, saved);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Documents.Upload)]
    public Task<DocumentDto> Update(Guid id, UpdateDocumentRequest request, CancellationToken ct) => documents.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.Documents.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await documents.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>
/// Downloads. The organization is in the URL, not a header, so the link works in an image tag or a new tab; the
/// signed-in session and the document's access rules are checked on every request.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/organizations/{organizationId:guid}/documents")]
public sealed class DocumentContentController(DocumentService documents) : ControllerBase
{
    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> Content(Guid id, [FromQuery] bool download = false, CancellationToken ct = default)
    {
        var file = await documents.OpenAsync(id, ct);
        Response.Headers[HeaderNames.CacheControl] = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        // Even an inline PDF or image may not run scripts or load anything.
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox";
        var inline = !download && (file.IsImage || file.ContentType == FileTypes.Pdf.ContentType);
        var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
        disposition.SetHttpFileName(file.FileName);
        Response.Headers[HeaderNames.ContentDisposition] = disposition.ToString();
        return File(file.Content, file.ContentType, enableRangeProcessing: false);
    }
}

public sealed class UploadDocumentForm
{
    public DocumentSubject Subject { get; set; }
    public Guid SubjectId { get; set; }
    public IFormFile? File { get; set; }
    public string? Title { get; set; }
    public DocumentCategory Category { get; set; } = DocumentCategory.Other;
    public DateOnly? ExpiresOn { get; set; }
    public bool SharedWithOwner { get; set; }
    public bool SharedWithTenant { get; set; }
}

public static class UploadReader
{
    /// <summary>The uploaded file's bytes, refusing a missing, empty or oversized file before reading it all.</summary>
    public static async Task<ReadOnlyMemory<byte>> ReadAsync(IFormFile? file, long maxBytes, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new DomainException("Choose a file to upload.");
        if (file.Length > maxBytes) throw new DomainException($"Files can be up to {maxBytes / (1024 * 1024)} MB.", "file_too_large");
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
