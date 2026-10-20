using PropertyManagement.Domain.Owners;

namespace PropertyManagement.Application.Owners;

public sealed record OwnerSummaryDto(Guid Id, PartyType Type, string Name, string? Email, string? Phone,
    int PropertyCount, bool HasApprovedBankAccount, bool HasPendingBankAccount, bool HasPortalAccess);

public sealed record OwnedPropertyDto(Guid PropertyId, string Code, string Name, decimal SharePercent, DateOnly EffectiveFrom);

public sealed record OwnerDetailDto(Guid Id, PartyType Type, string Name, string? Email, string? Phone, string? Address,
    string? TaxIdentificationNumber, ContactChannel PreferredChannel, string? Notes, DateTime CreatedAtUtc,
    IReadOnlyList<OwnedPropertyDto> Properties, bool HasPortalAccess);

public sealed record SaveOwnerRequest(PartyType Type, string Name, string? Email, string? Phone, string? Address,
    string? TaxIdentificationNumber, ContactChannel PreferredChannel = ContactChannel.Email, string? Notes = null)
{
    internal OwnerDetails ToDetails() => new(Type, Name, Email, Phone, Address, TaxIdentificationNumber, PreferredChannel, Notes);
}

/// <summary>A payout account. AccountNumber is the full number only for people who manage or approve accounts.</summary>
public sealed record BankAccountDto(Guid Id, string BankName, string AccountNumber, string AccountName,
    BankAccountStatus Status, Guid RequestedByUserId, string? RequestedByName, DateTime RequestedAtUtc,
    Guid? DecidedByUserId, string? DecidedByName, DateTime? DecidedAtUtc, string? DecisionNote);

public sealed record AddBankAccountRequest(string BankName, string AccountNumber, string AccountName);

public sealed record DecideBankAccountRequest(bool Approve, string? Note);

public sealed record AgreementDto(Guid Id, Guid OwnerId, Guid? PropertyId, string? PropertyName, ManagementFeeBasis FeeBasis,
    decimal FeeValue, decimal? LettingFeePercent, PayoutFrequency PayoutFrequency, AdvanceRentRelease AdvanceRentRelease,
    decimal ReserveAmount, DateOnly StartDate, DateOnly? EndDate, string? Notes, decimal? RepairApprovalLimit);

public sealed record SaveAgreementRequest(Guid? PropertyId, ManagementFeeBasis FeeBasis, decimal FeeValue,
    decimal? LettingFeePercent, PayoutFrequency PayoutFrequency, AdvanceRentRelease AdvanceRentRelease,
    decimal ReserveAmount, DateOnly StartDate, DateOnly? EndDate, string? Notes, decimal? RepairApprovalLimit = null)
{
    internal AgreementTerms ToTerms() => new(PropertyId, FeeBasis, FeeValue, LettingFeePercent, PayoutFrequency,
        AdvanceRentRelease, ReserveAmount, StartDate, EndDate, Notes, RepairApprovalLimit);
}
