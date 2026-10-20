using PropertyManagement.Domain.Owners;
using PropertyManagement.Domain.Tenants;

namespace PropertyManagement.Application.Tenants;

public sealed record TenantSummaryDto(Guid Id, PartyType Type, string Name, string? Email, string? Phone,
    string? Employer, VerificationStatus KycStatus, int GuarantorCount, bool HasPortalAccess, DateTime CreatedAtUtc);

public sealed record TenantDetailDto(Guid Id, PartyType Type, string Name, string? Email, string? Phone,
    string? AlternatePhone, string? Address, string? Employer, string? Occupation, string? EmergencyContactName,
    string? EmergencyContactPhone, string? CompanyRegistrationNumber, string? TaxIdentificationNumber, string? Notes,
    DateTime CreatedAtUtc, KycSummaryDto Kyc, bool HasPortalAccess);

public sealed record SaveTenantRequest(PartyType Type, string Name, string? Email, string? Phone, string? AlternatePhone,
    string? Address, string? Employer, string? Occupation, string? EmergencyContactName, string? EmergencyContactPhone,
    string? CompanyRegistrationNumber, string? TaxIdentificationNumber, string? Notes)
{
    internal TenantDetails ToDetails() => new(Type, Name, Email, Phone, AlternatePhone, Address, Employer, Occupation,
        EmergencyContactName, EmergencyContactPhone, CompanyRegistrationNumber, TaxIdentificationNumber, Notes);
}

/// <summary>What any tenant viewer sees: the ID type and last four digits, never the full numbers.</summary>
public sealed record KycSummaryDto(IdentityDocumentType? IdType, string? IdNumberLast4, string? BvnLast4,
    VerificationStatus Status, DateTime? VerifiedAtUtc, string? VerifiedByName);

/// <summary>Full identity numbers. Returned only with Tenants.ViewKyc, and every read is audited.</summary>
public sealed record KycDetailDto(IdentityDocumentType? IdType, string? IdNumber, string? Bvn, VerificationStatus Status,
    DateTime? VerifiedAtUtc, string? VerifiedByName);

public sealed record SaveKycRequest(IdentityDocumentType? IdType, string? IdNumber, string? Bvn);

public sealed record SetVerificationRequest(VerificationStatus Status);

public sealed record GuarantorDto(Guid Id, Guid TenantId, string FullName, string? Phone, string? Email, string? Address,
    string? Relationship, string? Employer, string? Occupation, bool SignedFormReceived, DateTime CreatedAtUtc);

public sealed record SaveGuarantorRequest(string FullName, string? Phone, string? Email, string? Address,
    string? Relationship, string? Employer, string? Occupation, bool SignedFormReceived)
{
    internal GuarantorDetails ToDetails() => new(FullName, Phone, Email, Address, Relationship, Employer, Occupation, SignedFormReceived);
}

public sealed record ScreeningCheckDto(Guid Id, ScreeningCheckType Type, ScreeningOutcome Outcome, string? Notes,
    Guid CheckedByUserId, string? CheckedByName, DateTime CheckedAtUtc);

public sealed record AddScreeningCheckRequest(ScreeningCheckType Type, ScreeningOutcome Outcome, string? Notes);
