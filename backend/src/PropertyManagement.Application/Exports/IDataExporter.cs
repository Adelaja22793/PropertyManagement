namespace PropertyManagement.Application.Exports;

/// <summary>
/// Writes the active organization's data as a ZIP of CSV files: everything it holds (design 4.8, so a company can
/// take its data with it), or everything about one person (design 11.4, data subject requests).
/// </summary>
public interface IDataExporter
{
    Task WriteOrganizationAsync(Stream output, bool includeFiles, CancellationToken ct);

    /// <summary>Returns the person's name, for the file name.</summary>
    Task<string> WriteTenantAsync(Stream output, Guid tenantId, CancellationToken ct);

    /// <summary>Returns the person's name, for the file name.</summary>
    Task<string> WriteOwnerAsync(Stream output, Guid ownerId, CancellationToken ct);
}
