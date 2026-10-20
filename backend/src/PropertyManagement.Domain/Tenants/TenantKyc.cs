using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Tenants;

public enum IdentityDocumentType { Nin = 0, InternationalPassport = 1, DriversLicence = 2, VotersCard = 3 }

public enum VerificationStatus { Unverified = 0, Verified = 1, Failed = 2 }

/// <summary>
/// A tenant's identity details (design 6.4). ID numbers and BVN are stored encrypted by the application;
/// only the last four digits are kept in clear so staff without Tenants.ViewKyc can tell records apart.
/// </summary>
public class TenantKyc : IOrganizationScoped
{
    private TenantKyc() { }

    public TenantKyc(Guid organizationId, Guid tenantId)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        TenantId = tenantId;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid TenantId { get; private set; }
    public IdentityDocumentType? IdType { get; private set; }
    public string? IdNumberEncrypted { get; private set; }
    public string? IdNumberLast4 { get; private set; }
    public string? BvnEncrypted { get; private set; }
    public string? BvnLast4 { get; private set; }
    public VerificationStatus Status { get; private set; }
    public Guid? VerifiedByUserId { get; private set; }
    public DateTime? VerifiedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>
    /// Replaces the identity numbers and resets verification. <paramref name="protect"/> encrypts a value for storage.
    /// </summary>
    public void SetIdentity(IdentityDocumentType? idType, string? idNumber, string? bvn, Func<string, string> protect, DateTime nowUtc)
    {
        var id = Normalise(idNumber, "ID number", 30);
        var bvnValue = Normalise(bvn, "BVN", 11);
        if (idType is { } t) Guard.Defined(t, "ID type");
        if ((idType is null) != (id is null)) throw new DomainException("Enter both the ID type and the ID number.");
        if (idType == IdentityDocumentType.Nin && (id!.Length != 11 || !id.All(char.IsAsciiDigit)))
            throw new DomainException("A NIN is 11 digits.", "invalid_nin");
        if (bvnValue is not null && (bvnValue.Length != 11 || !bvnValue.All(char.IsAsciiDigit)))
            throw new DomainException("A BVN is 11 digits.", "invalid_bvn");

        IdType = idType;
        IdNumberEncrypted = id is null ? null : protect(id);
        IdNumberLast4 = id?[^Math.Min(4, id.Length)..];
        BvnEncrypted = bvnValue is null ? null : protect(bvnValue);
        BvnLast4 = bvnValue?[^4..];
        // New details have not been checked, whatever the earlier status was.
        Status = VerificationStatus.Unverified;
        VerifiedByUserId = null;
        VerifiedAtUtc = null;
        UpdatedAtUtc = nowUtc;
    }

    public void SetVerification(VerificationStatus status, Guid userId, DateTime nowUtc)
    {
        Guard.Defined(status, "Verification status");
        if (status != VerificationStatus.Unverified && IdNumberEncrypted is null)
            throw new DomainException("Add an ID number before recording a verification result.");
        Status = status;
        VerifiedByUserId = status == VerificationStatus.Unverified ? null : userId;
        VerifiedAtUtc = status == VerificationStatus.Unverified ? null : nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    private static string? Normalise(string? value, string label, int max) =>
        Guard.Optional(value?.Replace(" ", "").Replace("-", ""), label, max)?.ToUpperInvariant();
}
