namespace PropertyManagement.Application.Abstractions;

/// <summary>Private file storage (design 6.13). Nothing in it is reachable except through the API's access checks.</summary>
public interface IFileStore
{
    /// <summary>The largest file accepted, from Storage:MaxUploadBytes.</summary>
    long MaxUploadBytes { get; }

    Task SaveAsync(string key, ReadOnlyMemory<byte> content, CancellationToken ct);

    /// <summary>The stored bytes, or null when nothing is stored under the key.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);

    Task DeleteAsync(string key, CancellationToken ct);
}

public sealed record MalwareScanResult(bool Clean, string? Threat)
{
    public static readonly MalwareScanResult Ok = new(true, null);
}

/// <summary>Checks uploads for viruses before they are stored (design 11.2).</summary>
public interface IMalwareScanner
{
    /// <summary>Throws <see cref="MalwareScanUnavailableException"/> when the scanner can't be reached, so nothing unscanned is stored.</summary>
    Task<MalwareScanResult> ScanAsync(ReadOnlyMemory<byte> content, CancellationToken ct);
}

public sealed class MalwareScanUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
