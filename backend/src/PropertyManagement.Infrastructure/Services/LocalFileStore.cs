using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using PropertyManagement.Application.Abstractions;

namespace PropertyManagement.Infrastructure.Services;

/// <summary>
/// Stores files on a local or mounted disk under Storage:LocalPath. Fine for one server; with several API servers
/// point every one at the same share, or swap in a cloud store (see README). Keys come only from the server
/// (organization and document ids), and anything else is refused so a key can never walk out of the folder.
/// </summary>
public sealed partial class LocalFileStore : IFileStore
{
    private readonly string _root;

    public LocalFileStore(IConfiguration configuration)
    {
        var configured = configuration["Storage:LocalPath"];
        MaxUploadBytes = configuration.GetValue<long>("Storage:MaxUploadBytes", 10 * 1024 * 1024);
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "App_Data", "documents")
            : configured);
    }

    public long MaxUploadBytes { get; }

    public async Task SaveAsync(string key, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Write to a temporary name first so a half-written file is never served.
        var temp = path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            await stream.WriteAsync(content, ct);
        File.Move(temp, path, overwrite: true);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        if (!KeyPattern().IsMatch(key)) throw new ArgumentException("Invalid storage key.", nameof(key));
        return Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));
    }

    [GeneratedRegex("^[0-9a-f]{32}/[0-9a-f]{32}$")]
    private static partial Regex KeyPattern();
}
