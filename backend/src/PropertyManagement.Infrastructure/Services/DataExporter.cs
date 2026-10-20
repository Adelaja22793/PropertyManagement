using System.IO.Compression;
using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Application.Exports;
using PropertyManagement.Application.Reports;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Documents;
using PropertyManagement.Domain.Organizations;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Infrastructure.Services;

/// <summary>
/// Writes exports from the database model itself, so every organization-owned table is included without a list to keep
/// up to date. Columns that only make sense inside the system (row versions, storage keys, token hashes) and encrypted
/// identity numbers are left out; the last four digits of those numbers are included.
/// </summary>
public sealed class DataExporter(
    AppDbContext db,
    ICurrentOrganization currentOrganization,
    IUserDirectory users,
    IFileStore files,
    IAuditLogger audit,
    IClock clock) : IDataExporter
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly MethodInfo AddScopedTableMethod =
        typeof(DataExporter).GetMethod(nameof(AddScopedTableAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public async Task WriteOrganizationAsync(Stream output, bool includeFiles, CancellationToken ct)
    {
        var organizationId = currentOrganization.RequireOrganizationId();
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        await AddAsync(zip, "organization.csv", db.Organizations.Where(o => o.Id == organizationId), ct);
        await AddMembersAsync(zip, ct);
        // Every organization-owned table, filtered to this organization by the global query filter.
        var tables = db.Model.GetEntityTypes()
            .Where(t => typeof(IOrganizationScoped).IsAssignableFrom(t.ClrType) && !t.IsOwned() && t.GetTableName() is not null)
            .OrderBy(t => t.GetTableName());
        foreach (var table in tables)
            await (Task)AddScopedTableMethod.MakeGenericMethod(table.ClrType).Invoke(this, [zip, FileName(table), ct])!;
        await AddAsync(zip, "support-grants.csv", db.SupportGrants.Where(g => g.OrganizationId == organizationId), ct);
        await AddAsync(zip, "audit-log.csv", db.AuditLogs.Where(a => a.OrganizationId == organizationId).OrderBy(a => a.TimestampUtc), ct);

        var fileCount = 0;
        if (includeFiles)
            fileCount = await AddFilesAsync(zip, db.Documents.Where(d => d.DeletedAtUtc == null), ct);
        await AddReadmeAsync(zip, "all data held by this organization", includeFiles, ct);

        await audit.RecordNowAsync("data.exported", nameof(Organization), organizationId.ToString(),
            details: new { includeFiles, files = fileCount }, organizationId: organizationId, ct: ct);
    }

    public async Task<string> WriteTenantAsync(Stream output, Guid tenantId, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenantId, ct) ?? throw new NotFoundException("Tenant");
        var leaseIds = await db.LeaseTenants.Where(l => l.TenantId == tenantId).Select(l => l.LeaseId).ToListAsync(ct);
        var guarantorIds = await db.Guarantors.Where(g => g.TenantId == tenantId).Select(g => g.Id).ToListAsync(ct);
        var repairIds = await db.MaintenanceRequests.Where(m => m.TenantId == tenantId).Select(m => m.Id).ToListAsync(ct);

        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            await AddAsync(zip, "tenant.csv", db.Tenants.Where(t => t.Id == tenantId), ct);
            await AddAsync(zip, "identity.csv", db.TenantKyc.Where(k => k.TenantId == tenantId), ct);
            await AddAsync(zip, "guarantors.csv", db.Guarantors.Where(g => g.TenantId == tenantId), ct);
            await AddAsync(zip, "screening-checks.csv", db.ScreeningChecks.Where(s => s.TenantId == tenantId), ct);
            await AddAsync(zip, "portal-access.csv", db.TenantUserLinks.Where(l => l.TenantId == tenantId), ct);
            await AddAsync(zip, "leases.csv", db.Leases.Where(l => leaseIds.Contains(l.Id)), ct);
            await AddAsync(zip, "lease-charges.csv", db.LeaseCharges.Where(c => leaseIds.Contains(c.LeaseId)), ct);
            await AddAsync(zip, "notices.csv", db.LeaseNotices.Where(n => leaseIds.Contains(n.LeaseId)), ct);
            await AddAsync(zip, "inspections.csv", db.LeaseInspections.Where(i => leaseIds.Contains(i.LeaseId)), ct);
            await AddAsync(zip, "repairs.csv", db.MaintenanceRequests.Where(m => m.TenantId == tenantId), ct);
            var documents = PersonDocuments(d =>
                (d.Subject == DocumentSubject.Tenant && d.SubjectId == tenantId)
                || (d.Subject == DocumentSubject.Lease && leaseIds.Contains(d.SubjectId))
                || (d.Subject == DocumentSubject.Maintenance && repairIds.Contains(d.SubjectId)));
            await AddAsync(zip, "documents.csv", documents, ct);
            await AddFilesAsync(zip, documents, ct);
            await AddReadmeAsync(zip, $"everything held about the tenant {tenant.Name}", true, ct);
        }

        await audit.RecordNowAsync("person.exported", "Tenant", tenantId.ToString(),
            details: new { leases = leaseIds.Count, guarantors = guarantorIds.Count }, organizationId: tenant.OrganizationId, ct: ct);
        return tenant.Name;
    }

    public async Task<string> WriteOwnerAsync(Stream output, Guid ownerId, CancellationToken ct)
    {
        var owner = await db.Owners.AsNoTracking().SingleOrDefaultAsync(o => o.Id == ownerId, ct) ?? throw new NotFoundException("Owner");

        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            await AddAsync(zip, "owner.csv", db.Owners.Where(o => o.Id == ownerId), ct);
            await AddAsync(zip, "bank-accounts.csv", db.OwnerBankAccounts.Where(b => b.OwnerId == ownerId), ct);
            await AddAsync(zip, "management-agreements.csv", db.ManagementAgreements.Where(m => m.OwnerId == ownerId), ct);
            await AddAsync(zip, "property-ownership.csv", db.PropertyOwnerships.Where(p => p.OwnerId == ownerId), ct);
            await AddAsync(zip, "portal-access.csv", db.OwnerUserLinks.Where(l => l.OwnerId == ownerId), ct);
            var documents = PersonDocuments(d => d.Subject == DocumentSubject.Owner && d.SubjectId == ownerId);
            await AddAsync(zip, "documents.csv", documents, ct);
            await AddFilesAsync(zip, documents, ct);
            await AddReadmeAsync(zip, $"everything held about the owner {owner.Name}", true, ct);
        }

        await audit.RecordNowAsync("person.exported", "Owner", ownerId.ToString(), organizationId: owner.OrganizationId, ct: ct);
        return owner.Name;
    }

    /// <summary>A person's documents that are still stored. Identity documents only for those allowed to see them.</summary>
    private IQueryable<Document> PersonDocuments(System.Linq.Expressions.Expression<Func<Document, bool>> filter)
    {
        var query = db.Documents.Where(d => d.DeletedAtUtc == null).Where(filter);
        return currentOrganization.HasPermission(Permissions.Tenants.ViewKyc) ? query : query.Where(d => d.Category != DocumentCategory.Identity);
    }

    private Task AddScopedTableAsync<T>(ZipArchive zip, string name, CancellationToken ct) where T : class =>
        AddAsync(zip, name, db.Set<T>(), ct);

    private async Task AddAsync<T>(ZipArchive zip, string name, IQueryable<T> query, CancellationToken ct) where T : class
    {
        var columns = Columns(db.Model.FindEntityType(typeof(T))!);
        var rows = await query.AsNoTracking().ToListAsync(ct);
        var csv = new StringBuilder();
        Line(csv, columns.Select(c => (object?)c.Name));
        foreach (var row in rows) Line(csv, columns.Select(c => c.GetValue(row)));
        await WriteTextAsync(zip, name, csv.ToString(), ct);
    }

    /// <summary>Staff with their names and email addresses, which the membership table holds only as user ids.</summary>
    private async Task AddMembersAsync(ZipArchive zip, CancellationToken ct)
    {
        var memberships = await db.Memberships.AsNoTracking().Include(m => m.Roles).ToListAsync(ct);
        var people = await users.GetAsync(memberships.Select(m => m.UserId), ct);
        var csv = new StringBuilder();
        Line(csv, ["UserId", "FullName", "Email", "Roles", "Status", "JoinedAtUtc", "RemovedAtUtc"]);
        foreach (var m in memberships)
        {
            var person = people.GetValueOrDefault(m.UserId);
            Line(csv, [m.UserId, person?.FullName, person?.Email, string.Join(" ", m.Roles.Select(r => r.RoleName)), m.Status, m.CreatedAtUtc, m.RevokedAtUtc]);
        }
        await WriteTextAsync(zip, "members.csv", csv.ToString(), ct);
    }

    private async Task<int> AddFilesAsync(ZipArchive zip, IQueryable<Document> documents, CancellationToken ct)
    {
        var list = await documents.AsNoTracking().OrderBy(d => d.CreatedAtUtc).ToListAsync(ct);
        var count = 0;
        foreach (var d in list)
        {
            await using var source = await files.OpenReadAsync(d.StorageKey, ct);
            if (source is null) continue;
            var entry = zip.CreateEntry($"files/{d.Subject.ToString().ToLowerInvariant()}/{d.Id:N}-{d.FileName}", CompressionLevel.Optimal);
            await using var target = entry.Open();
            await source.CopyToAsync(target, ct);
            count++;
        }
        return count;
    }

    private async Task AddReadmeAsync(ZipArchive zip, string what, bool includesFiles, CancellationToken ct)
    {
        var text = $"""
            This download holds {what}, as of {clock.UtcNow:yyyy-MM-dd HH:mm} UTC.

            Each .csv file is one table and opens in Excel or Google Sheets. Columns ending in Id link rows between files.
            Times ending in Utc are in UTC (Lagos is one hour ahead). Text that a spreadsheet would treat as a formula
            starts with an apostrophe.

            Identity numbers (NIN, BVN) are stored encrypted and are not included; their last four digits are.
            {(includesFiles ? "Stored files are in the files folder, named by document id." : "Stored files are not included; ask for them with the files option.")}

            Keep this download safe: it contains personal data.
            """;
        await WriteTextAsync(zip, "README.txt", text, ct);
    }

    private static async Task WriteTextAsync(ZipArchive zip, string name, string text, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        var bytes = Utf8WithBom.GetPreamble().Concat(Utf8WithBom.GetBytes(text)).ToArray();
        await stream.WriteAsync(bytes, ct);
    }

    private static void Line(StringBuilder csv, IEnumerable<object?> cells)
    {
        csv.AppendJoin(',', cells.Select(ReportCsv.Cell));
        csv.Append("\r\n");
    }

    /// <summary>Mapped columns people can use. Row versions, storage keys, token hashes and encrypted values are left out.</summary>
    public static IReadOnlyList<PropertyInfo> Columns(IEntityType entityType) =>
        entityType.GetProperties()
            .Where(p => p.PropertyInfo is not null && p.ClrType != typeof(byte[]) && !Hidden(p.Name))
            .Select(p => p.PropertyInfo!)
            .ToList();

    private static bool Hidden(string column) =>
        column.EndsWith("Encrypted", StringComparison.Ordinal) || column == "StorageKey" || column.Contains("Hash", StringComparison.Ordinal);

    private static string FileName(IEntityType table)
    {
        var name = table.GetTableName()!;
        var kebab = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) kebab.Append('-');
            kebab.Append(char.ToLowerInvariant(name[i]));
        }
        return $"tables/{kebab}.csv";
    }
}
