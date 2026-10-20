using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Documents;

/// <summary>The record a document is filed against.</summary>
public enum DocumentSubject { Property, Owner, Tenant, Lease, Maintenance }

public enum DocumentCategory
{
    Photo, Agreement, Identity, Insurance, Permit, Certificate, Inspection, Quote, Receipt, Notice, Correspondence, Other,
}

/// <summary>Who added the file: staff, or an owner or tenant through their portal.</summary>
public enum DocumentSource { Staff, OwnerPortal, TenantPortal }

public sealed record DocumentDetails(string Title, DocumentCategory Category, DateOnly? ExpiresOn, bool SharedWithOwner, bool SharedWithTenant);

/// <summary>
/// A stored file filed against a property, owner, tenant, lease or repair (design 6.13). The bytes live in private
/// storage under <see cref="StorageKey"/>; only this record says who may read them. Deleting removes the bytes and
/// keeps this record, so the audit trail still names what was there.
/// </summary>
public class Document : IOrganizationScoped
{
    public const int MaxFileNameLength = 255;

    private Document() { }

    public Document(Guid organizationId, DocumentSubject subject, Guid subjectId, Guid? propertyId, DocumentDetails details,
        string fileName, string contentType, long sizeBytes, string sha256, DocumentSource source, Guid uploadedByUserId, DateTime nowUtc)
    {
        Guard.Defined(subject, "Subject");
        Guard.Defined(source, "Source");
        if (sizeBytes <= 0) throw new DomainException("The file is empty.");
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        Subject = subject;
        SubjectId = subjectId;
        PropertyId = propertyId;
        FileName = CleanFileName(fileName);
        ContentType = Guard.Required(contentType, "Content type", 100);
        SizeBytes = sizeBytes;
        Sha256 = Guard.Required(sha256, "Checksum", 64);
        Source = source;
        UploadedByUserId = uploadedByUserId;
        CreatedAtUtc = nowUtc;
        StorageKey = $"{organizationId:N}/{Id:N}";
        Apply(details);
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public DocumentSubject Subject { get; private set; }
    public Guid SubjectId { get; private set; }
    /// <summary>The property the subject belongs to, for property, lease and repair documents. Drives access checks.</summary>
    public Guid? PropertyId { get; private set; }
    public DocumentCategory Category { get; private set; }
    public string Title { get; private set; } = "";
    public string FileName { get; private set; } = "";
    /// <summary>Set by the server from the file's own bytes, never from what the browser claimed.</summary>
    public string ContentType { get; private set; } = "";
    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; } = "";
    public string StorageKey { get; private set; } = "";
    /// <summary>For insurance, permits and certificates that lapse.</summary>
    public DateOnly? ExpiresOn { get; private set; }
    public bool SharedWithOwner { get; private set; }
    public bool SharedWithTenant { get; private set; }
    public DocumentSource Source { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }
    public Guid? DeletedByUserId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsDeleted => DeletedAtUtc is not null;
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.Ordinal);

    public void Update(DocumentDetails details)
    {
        if (IsDeleted) throw new DomainException("This document was deleted.");
        Apply(details);
    }

    public void Delete(Guid userId, DateTime nowUtc)
    {
        if (IsDeleted) throw new DomainException("This document was already deleted.", "already_decided");
        DeletedAtUtc = nowUtc;
        DeletedByUserId = userId;
    }

    private void Apply(DocumentDetails d)
    {
        Guard.Defined(d.Category, "Category");
        Title = Guard.Required(d.Title, "Title", 200);
        Category = d.Category;
        ExpiresOn = d.ExpiresOn;
        SharedWithOwner = d.SharedWithOwner;
        SharedWithTenant = d.SharedWithTenant;
        if (Category == DocumentCategory.Photo && !IsImage) throw new DomainException("Only images can be filed as photos.");
    }

    /// <summary>Keeps the name people recognise, without path parts or characters that break a download header.</summary>
    public static string CleanFileName(string? name)
    {
        var bare = Path.GetFileName((name ?? "").Replace('\\', '/').Split('/').Last()).Trim();
        var clean = new string(bare.Where(c => !char.IsControl(c) && c is not '"' and not '<' and not '>' and not '|' and not ':' and not '*' and not '?').ToArray()).Trim();
        if (clean.Length == 0) clean = "file";
        if (clean.Length > MaxFileNameLength)
        {
            var extension = Path.GetExtension(clean);
            clean = clean[..(MaxFileNameLength - extension.Length)] + extension;
        }
        return clean;
    }
}
