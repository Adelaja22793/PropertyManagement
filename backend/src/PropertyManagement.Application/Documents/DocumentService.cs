using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Application.Leases;
using PropertyManagement.Application.Maintenance;
using PropertyManagement.Application.Portal;
using PropertyManagement.Application.Properties;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Documents;
using PropertyManagement.Domain.Maintenance;

namespace PropertyManagement.Application.Documents;

/// <summary>
/// Documents and photos (design 6.13). Staff reach a document only when they can see the record it is filed against
/// and hold Documents.View (or, for a repair, are the person assigned to it). Identity documents also need
/// Tenants.ViewKyc. Owners and tenants see only what staff shared with them on their own records, and tenants can add
/// photos to their own repair requests. Every file is type-checked from its bytes, size-limited and virus-scanned
/// before it is stored.
/// </summary>
public sealed class DocumentService(
    IAppDbContext db,
    PropertyAccess access,
    LeaseService leases,
    MaintenanceService maintenance,
    PortalScope portal,
    ICurrentOrganization currentOrganization,
    ICurrentUser currentUser,
    IUserDirectory users,
    IFileStore files,
    IMalwareScanner scanner,
    IAuditLogger audit,
    IClock clock)
{
    public const long DefaultMaxBytes = 10 * 1024 * 1024;

    public long MaxBytes => Math.Min(files.MaxUploadBytes, DefaultMaxBytes);

    private bool Can(string permission) => currentOrganization.HasPermission(permission);
    private bool CanViewIdentity => Can(Permissions.Tenants.ViewKyc);

    // Staff

    public async Task<DocumentListDto> ListAsync(DocumentSubject subject, Guid subjectId, CancellationToken ct)
    {
        var target = await ResolveAsync(subject, subjectId, ct);
        if (!target.CanRead) throw new ForbiddenException("You can't see documents here.");
        var query = Live().Where(d => d.Subject == subject && d.SubjectId == subjectId);
        if (!CanViewIdentity) query = query.Where(d => d.Category != DocumentCategory.Identity);
        var items = await query.OrderByDescending(d => d.CreatedAtUtc).ToListAsync(ct);
        var people = await users.GetAsync(items.Select(d => d.UploadedByUserId).Distinct(), ct);
        return new DocumentListDto(
            items.Select(d => ToDto(d, people.GetValueOrDefault(d.UploadedByUserId)?.FullName)).ToList(),
            target.CanWrite, Can(Permissions.Documents.Upload) && target.CanWrite, Can(Permissions.Documents.Delete), CanViewIdentity);
    }

    public async Task<DocumentDto> UploadAsync(UploadDocument upload, CancellationToken ct)
    {
        Guard.Defined(upload.Subject, "Subject");
        var target = await ResolveAsync(upload.Subject, upload.SubjectId, ct);
        if (!target.CanWrite) throw new ForbiddenException("You can't add documents here.");
        if (upload.Category == DocumentCategory.Identity && !CanViewIdentity)
            throw new ForbiddenException("Only members who can see identity details can file identity documents.");
        var title = string.IsNullOrWhiteSpace(upload.Title) ? Path.GetFileNameWithoutExtension(Document.CleanFileName(upload.FileName)) : upload.Title;
        var details = new DocumentDetails(title, upload.Category, upload.ExpiresOn, upload.SharedWithOwner, upload.SharedWithTenant);
        var document = await StoreAsync(upload.Subject, upload.SubjectId, target.PropertyId, details, upload.FileName, upload.Content,
            DocumentSource.Staff, imagesOnly: false, ct);
        return ToDto(document, await MyNameAsync(ct));
    }

    public async Task<DocumentDto> UpdateAsync(Guid id, UpdateDocumentRequest request, CancellationToken ct)
    {
        var document = await LoadAsync(id, ct);
        var target = await ResolveAsync(document.Subject, document.SubjectId, ct);
        if (!target.CanRead || !Can(Permissions.Documents.Upload) || !target.CanWrite)
            throw new ForbiddenException("You can't change documents here.");
        if (request.Category == DocumentCategory.Identity && !CanViewIdentity)
            throw new ForbiddenException("Only members who can see identity details can file identity documents.");
        document.Update(request.ToDetails());
        audit.Record("document.updated", nameof(Document), document.Id.ToString(), details: new
        {
            document.Subject, document.SubjectId, document.Title, document.Category, document.ExpiresOn,
            document.SharedWithOwner, document.SharedWithTenant,
        });
        await db.SaveChangesAsync(ct);
        var uploader = (await users.GetAsync([document.UploadedByUserId], ct)).GetValueOrDefault(document.UploadedByUserId)?.FullName;
        return ToDto(document, uploader);
    }

    /// <summary>Removes the stored file. The record stays, marked deleted, for the audit trail.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var document = await LoadAsync(id, ct);
        var target = await ResolveAsync(document.Subject, document.SubjectId, ct);
        if (!target.CanRead || !Can(Permissions.Documents.Delete)) throw new ForbiddenException("You can't delete documents.");
        document.Delete(currentUser.RequireUserId(), clock.UtcNow);
        audit.Record("document.deleted", nameof(Document), document.Id.ToString(), details: new
        {
            document.Subject, document.SubjectId, document.Title, document.FileName, document.Sha256,
        });
        await db.SaveChangesAsync(ct);
        await files.DeleteAsync(document.StorageKey, ct);
    }

    /// <summary>Documents that expire within <paramref name="days"/> days, or already have, on records the caller can see.</summary>
    public async Task<IReadOnlyList<ExpiringDocumentDto>> ExpiringAsync(int days, CancellationToken ct)
    {
        if (!Can(Permissions.Documents.View)) throw new ForbiddenException("You can't see documents.");
        var until = clock.TodayInNigeria().AddDays(Math.Clamp(days, 0, 366));
        var properties = access.VisibleProperties();
        var canOwners = Can(Permissions.Owners.View);
        var canTenants = Can(Permissions.Tenants.View);
        var canIdentity = CanViewIdentity;
        var items = await Live()
            .Where(d => d.ExpiresOn != null && d.ExpiresOn <= until)
            .Where(d => canIdentity || d.Category != DocumentCategory.Identity)
            .Where(d => (d.PropertyId != null && properties.Any(p => p.Id == d.PropertyId))
                        || (canOwners && d.Subject == DocumentSubject.Owner)
                        || (canTenants && d.Subject == DocumentSubject.Tenant))
            .OrderBy(d => d.ExpiresOn).Take(100).ToListAsync(ct);
        var labels = await LabelsAsync(items, ct);
        return items.Select(d => new ExpiringDocumentDto(d.Id, d.Title, d.Category, d.ExpiresOn!.Value, d.Subject, d.SubjectId,
            labels.GetValueOrDefault(d.Id, ""))).ToList();
    }

    // Portals

    /// <summary>Documents staff shared with the owner: on the owner themselves, and on properties, leases and repairs of properties they hold a share of.</summary>
    public async Task<IReadOnlyList<PortalDocumentDto>> OwnerDocumentsAsync(CancellationToken ct) =>
        await PortalListAsync(await OwnerVisibleAsync(ct), ct);

    public async Task<IReadOnlyList<PortalDocumentDto>> TenantDocumentsAsync(CancellationToken ct) =>
        await PortalListAsync(await TenantVisibleAsync(ct), ct);

    /// <summary>A tenant adds a photo to one of their own open repair requests. Staff and the owner can see it.</summary>
    public async Task<PortalDocumentDto> TenantRepairPhotoAsync(Guid requestId, string fileName, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        var tenantIds = await portal.TenantIdsAsync(ct);
        var request = await portal.TenantRequests(tenantIds).SingleOrDefaultAsync(r => r.Id == requestId, ct)
                      ?? throw new NotFoundException("Maintenance request");
        if (!request.IsOpen) throw new DomainException("This request is closed, so photos can't be added.", "maintenance_status");
        var title = $"Photo from {await MyNameAsync(ct)}";
        var document = await StoreAsync(DocumentSubject.Maintenance, request.Id, request.PropertyId,
            new DocumentDetails(title, DocumentCategory.Photo, null, SharedWithOwner: true, SharedWithTenant: true),
            fileName, content, DocumentSource.TenantPortal, imagesOnly: true, ct);
        return (await PortalListAsync(db.Documents.Where(d => d.Id == document.Id), ct)).Single();
    }

    // Download

    /// <summary>
    /// The file, for anyone allowed to read it: staff through the record it is filed against, owners and tenants
    /// through what was shared with them. Everyone else gets "not found", so ids can't be probed.
    /// </summary>
    public async Task<DocumentContent> OpenAsync(Guid id, CancellationToken ct)
    {
        var document = await Live().SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Document");
        if (!await CanReadAsync(document, ct)) throw new NotFoundException("Document");
        var stream = await files.OpenReadAsync(document.StorageKey, ct) ?? throw new NotFoundException("Document");
        if (document.Category == DocumentCategory.Identity)
            audit.Record("document.identity_viewed", nameof(Document), document.Id.ToString(),
                details: new { document.Subject, document.SubjectId, document.Title });
        await db.SaveChangesAsync(ct);
        return new DocumentContent(stream, document.ContentType, document.FileName, document.IsImage);
    }

    private async Task<bool> CanReadAsync(Document document, CancellationToken ct)
    {
        if (currentOrganization.Permissions.Count > 0)
        {
            var target = await ResolveAsync(document.Subject, document.SubjectId, ct, notFoundOk: true);
            if (target.CanRead && (document.Category != DocumentCategory.Identity || CanViewIdentity)) return true;
        }
        if (portal.IsOwner && document.SharedWithOwner && await (await OwnerVisibleAsync(ct)).AnyAsync(d => d.Id == document.Id, ct))
            return true;
        if (portal.IsTenant && document.SharedWithTenant && await (await TenantVisibleAsync(ct)).AnyAsync(d => d.Id == document.Id, ct))
            return true;
        return false;
    }

    // Shared

    private sealed record Target(bool CanRead, bool CanWrite, Guid? PropertyId);

    /// <summary>
    /// Whether the caller can see the record and its documents, and add to them. Throws not-found when the record
    /// doesn't exist or isn't visible, unless <paramref name="notFoundOk"/> (then it answers "no access").
    /// </summary>
    private async Task<Target> ResolveAsync(DocumentSubject subject, Guid subjectId, CancellationToken ct, bool notFoundOk = false)
    {
        var view = Can(Permissions.Documents.View);
        var upload = Can(Permissions.Documents.Upload);
        Target? target = subject switch
        {
            DocumentSubject.Property => await access.VisibleProperties().AnyAsync(p => p.Id == subjectId, ct)
                ? new Target(view, view && upload, subjectId) : null,
            DocumentSubject.Owner => Can(Permissions.Owners.View) && await db.Owners.AnyAsync(o => o.Id == subjectId, ct)
                ? new Target(view, view && upload, null) : null,
            DocumentSubject.Tenant => Can(Permissions.Tenants.View) && await db.Tenants.AnyAsync(t => t.Id == subjectId, ct)
                ? new Target(view, view && upload, null) : null,
            DocumentSubject.Lease => Can(Permissions.Leases.View)
                ? await leases.VisibleLeases().Where(l => l.Id == subjectId)
                    .Select(l => new Target(view, view && upload, l.PropertyId)).SingleOrDefaultAsync(ct)
                : null,
            DocumentSubject.Maintenance => await MaintenanceTargetAsync(subjectId, view, upload, ct),
            _ => null,
        };
        if (target is null)
        {
            if (notFoundOk) return new Target(false, false, null);
            throw new NotFoundException(subject == DocumentSubject.Maintenance ? "Maintenance request" : subject.ToString());
        }
        return target;
    }

    /// <summary>The person assigned to a repair can see and add photos to it without Documents permissions.</summary>
    private async Task<Target?> MaintenanceTargetAsync(Guid id, bool view, bool upload, CancellationToken ct)
    {
        if (!Can(Permissions.Maintenance.View) && !Can(Permissions.Maintenance.UpdateAssigned)) return null;
        var request = await maintenance.VisibleRequests().Where(r => r.Id == id)
            .Select(r => new { r.PropertyId, r.AssignedUserId }).SingleOrDefaultAsync(ct);
        if (request is null) return null;
        var assignee = request.AssignedUserId == currentUser.UserId && Can(Permissions.Maintenance.UpdateAssigned);
        return new Target(view || assignee, (view && upload) || assignee, request.PropertyId);
    }

    private async Task<Document> StoreAsync(DocumentSubject subject, Guid subjectId, Guid? propertyId, DocumentDetails details,
        string fileName, ReadOnlyMemory<byte> content, DocumentSource source, bool imagesOnly, CancellationToken ct)
    {
        if (content.Length == 0) throw new DomainException("The file is empty.");
        if (content.Length > MaxBytes) throw new DomainException($"Files can be up to {MaxBytes / (1024 * 1024)} MB.", "file_too_large");
        var type = FileTypes.Detect(content.Span, fileName)
                   ?? throw new DomainException($"This type of file isn't accepted. Upload a {FileTypes.Accepted} file.", "file_type");
        if (imagesOnly && !type.IsImage) throw new DomainException("Upload a photo (JPEG, PNG or WebP).", "file_type");

        MalwareScanResult scan;
        try
        {
            scan = await scanner.ScanAsync(content, ct);
        }
        catch (MalwareScanUnavailableException)
        {
            throw new DomainException("The virus check isn't available right now, so the file wasn't saved. Try again shortly.", "scan_unavailable");
        }
        if (!scan.Clean)
        {
            await audit.RecordNowAsync("document.rejected_malware", nameof(Document), null, Domain.Auditing.AuditOutcome.Denied,
                new { subject, subjectId, FileName = Document.CleanFileName(fileName), scan.Threat },
                organizationId: currentOrganization.RequireOrganizationId(), userId: currentUser.RequireUserId(), ct: ct);
            throw new DomainException("This file failed the virus check and was not saved.", "file_rejected");
        }

        // Keep the person's name for the file, but with the extension of what it really is.
        var name = Path.ChangeExtension(Document.CleanFileName(fileName), type.Extension);
        var document = new Document(currentOrganization.RequireOrganizationId(), subject, subjectId, propertyId, details,
            name, type.ContentType, content.Length, Convert.ToHexStringLower(SHA256.HashData(content.Span)), source,
            currentUser.RequireUserId(), clock.UtcNow);
        db.Documents.Add(document);
        audit.Record("document.uploaded", nameof(Document), document.Id.ToString(), details: new
        {
            document.Subject, document.SubjectId, document.Title, document.Category, document.FileName, document.SizeBytes,
            document.Sha256, document.Source, document.SharedWithOwner, document.SharedWithTenant,
        });
        await files.SaveAsync(document.StorageKey, content, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await files.DeleteAsync(document.StorageKey, CancellationToken.None);
            throw;
        }
        return document;
    }

    private IQueryable<Document> Live() => db.Documents.Where(d => d.DeletedAtUtc == null);

    private async Task<Document> LoadAsync(Guid id, CancellationToken ct) =>
        await Live().SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Document");

    private async Task<IQueryable<Document>> OwnerVisibleAsync(CancellationToken ct)
    {
        var ownerIds = await portal.OwnerIdsAsync(ct);
        var propertyIds = await portal.OwnerPropertyIdsAsync(ownerIds, ct);
        // Identity documents reach a portal only when they are the person's own.
        return Live().Where(d => d.SharedWithOwner && (
            (d.Subject == DocumentSubject.Owner && ownerIds.Contains(d.SubjectId))
            || (d.PropertyId != null && propertyIds.Contains(d.PropertyId.Value) && d.Category != DocumentCategory.Identity)));
    }

    private async Task<IQueryable<Document>> TenantVisibleAsync(CancellationToken ct)
    {
        var tenantIds = await portal.TenantIdsAsync(ct);
        var leaseIds = portal.TenantLeases(tenantIds).Select(l => l.Id);
        var requestIds = portal.TenantRequests(tenantIds).Select(r => r.Id);
        return Live().Where(d => d.SharedWithTenant && (
            (d.Subject == DocumentSubject.Tenant && tenantIds.Contains(d.SubjectId))
            || (d.Category != DocumentCategory.Identity && (
                (d.Subject == DocumentSubject.Lease && leaseIds.Contains(d.SubjectId))
                || (d.Subject == DocumentSubject.Maintenance && requestIds.Contains(d.SubjectId))))));
    }

    private async Task<IReadOnlyList<PortalDocumentDto>> PortalListAsync(IQueryable<Document> query, CancellationToken ct)
    {
        var items = await query.OrderByDescending(d => d.CreatedAtUtc).Take(500).ToListAsync(ct);
        var labels = await LabelsAsync(items, ct);
        var me = currentUser.UserId;
        return items.Select(d => new PortalDocumentDto(d.Id, d.Title, d.Category, d.FileName, d.ContentType, d.SizeBytes, d.Subject,
            d.SubjectId, labels.GetValueOrDefault(d.Id, ""), d.ExpiresOn, d.UploadedByUserId == me, d.CreatedAtUtc)).ToList();
    }

    /// <summary>A short name for each document's record: the property, person, lease or repair.</summary>
    private async Task<Dictionary<Guid, string>> LabelsAsync(IReadOnlyCollection<Document> items, CancellationToken ct)
    {
        List<Guid> Ids(DocumentSubject s) => items.Where(d => d.Subject == s).Select(d => d.SubjectId).Distinct().ToList();
        var propertyIds = Ids(DocumentSubject.Property);
        var ownerIds = Ids(DocumentSubject.Owner);
        var tenantIds = Ids(DocumentSubject.Tenant);
        var leaseIds = Ids(DocumentSubject.Lease);
        var requestIds = Ids(DocumentSubject.Maintenance);
        var names = new Dictionary<Guid, string>();
        foreach (var p in await db.Properties.Where(p => propertyIds.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToListAsync(ct))
            names[p.Id] = p.Name;
        foreach (var o in await db.Owners.Where(o => ownerIds.Contains(o.Id)).Select(o => new { o.Id, o.Name }).ToListAsync(ct))
            names[o.Id] = o.Name;
        foreach (var t in await db.Tenants.Where(t => tenantIds.Contains(t.Id)).Select(t => new { t.Id, t.Name }).ToListAsync(ct))
            names[t.Id] = t.Name;
        foreach (var l in await db.Leases.Where(l => leaseIds.Contains(l.Id)).Select(l => new
                 {
                     l.Id,
                     Unit = db.Units.Where(u => u.Id == l.UnitId).Select(u => u.Code).First(),
                     Property = db.Properties.Where(p => p.Id == l.PropertyId).Select(p => p.Name).First(),
                 }).ToListAsync(ct))
            names[l.Id] = $"Lease for {l.Property}, unit {l.Unit}";
        foreach (var r in await db.MaintenanceRequests.Where(r => requestIds.Contains(r.Id)).ToListAsync(ct))
            names[r.Id] = $"{r.Reference}: {r.Title}";
        return items.ToDictionary(d => d.Id, d => names.GetValueOrDefault(d.SubjectId, ""));
    }

    private static DocumentDto ToDto(Document d, string? uploadedBy) =>
        new(d.Id, d.Subject, d.SubjectId, d.Category, d.Title, d.FileName, d.ContentType, d.SizeBytes, d.ExpiresOn,
            d.SharedWithOwner, d.SharedWithTenant, d.Source, uploadedBy, d.CreatedAtUtc);

    private async Task<string> MyNameAsync(CancellationToken ct)
    {
        var id = currentUser.RequireUserId();
        return (await users.GetAsync([id], ct)).GetValueOrDefault(id)?.FullName ?? "a tenant";
    }
}
