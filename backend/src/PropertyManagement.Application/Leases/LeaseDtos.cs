using PropertyManagement.Domain.Leases;
using PropertyManagement.Domain.Properties;

namespace PropertyManagement.Application.Leases;

public sealed record LeaseSummaryDto(
    Guid Id, string Reference, Guid PropertyId, string PropertyCode, string PropertyName, Guid UnitId, string UnitCode,
    Guid? PrimaryTenantId, string? PrimaryTenantName, int TenantCount, DateOnly StartDate, DateOnly EndDate,
    decimal RentAmount, string Currency, BillingFrequency BillingFrequency, LeaseStatus Status, LeaseStage Stage,
    int? DaysToExpiry, Guid? RenewalLeaseId);

public sealed record LeaseTenantDto(Guid TenantId, string Name, string? Phone, string? Email, bool IsPrimary);

public sealed record LeaseGuarantorDto(Guid GuarantorId, Guid TenantId, string FullName, string? Phone, bool SignedFormReceived);

public sealed record LeaseChargeDto(Guid Id, LeaseChargeType Type, string? Description, decimal Amount, ChargeRecurrence Recurrence);

public sealed record LinkedLeaseDto(Guid Id, string Reference, LeaseStatus Status, DateOnly StartDate, DateOnly EndDate, decimal RentAmount);

public sealed record LeaseDetailDto(
    Guid Id, string Reference, Guid PropertyId, string PropertyCode, string PropertyName, Guid UnitId, string UnitCode,
    LeaseStatus Status, LeaseStage Stage, int? DaysToExpiry, DateOnly StartDate, DateOnly EndDate, decimal RentAmount,
    string Currency, BillingFrequency BillingFrequency, int DueDaysBeforePeriod, int GraceDays, int? NoticePeriodDays,
    decimal? HoldoverRentAmount, string? RenewalTerms, string? Notes,
    IReadOnlyList<LeaseTenantDto> Tenants, IReadOnlyList<LeaseGuarantorDto> Guarantors, IReadOnlyList<LeaseChargeDto> Charges,
    LinkedLeaseDto? PreviousLease, LinkedLeaseDto? RenewalLease,
    DateTime CreatedAtUtc, DateTime? ActivatedAtUtc, string? ActivatedByName, DateOnly? MoveOutDate, string? EndReason);

public sealed record SaveLeaseRequest(
    Guid UnitId,
    IReadOnlyList<Guid> TenantIds,
    IReadOnlyList<Guid>? GuarantorIds,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal RentAmount,
    string? Currency,
    BillingFrequency BillingFrequency,
    int DueDaysBeforePeriod,
    int GraceDays,
    int? NoticePeriodDays,
    decimal? HoldoverRentAmount,
    string? RenewalTerms,
    string? Notes,
    IReadOnlyList<LeaseChargeInput>? Charges)
{
    internal LeaseTerms ToTerms() => new(StartDate, EndDate, RentAmount, Currency, BillingFrequency, DueDaysBeforePeriod,
        GraceDays, NoticePeriodDays, HoldoverRentAmount, RenewalTerms, Notes);
}

/// <summary>Starts a renewal: a new draft lease for the same unit and tenants that follows this one.</summary>
public sealed record RenewLeaseRequest(DateOnly? StartDate, DateOnly EndDate, decimal RentAmount, string? Notes);

/// <summary>Records that the tenant has left. Before the end date this is an early termination and needs a reason.</summary>
public sealed record EndLeaseRequest(DateOnly MoveOutDate, string? Reason);

public sealed record CancelLeaseRequest(string? Reason);

public sealed record LeaseSearch(
    string? Search, LeaseStage? Stage, Guid? PropertyId, Guid? UnitId, Guid? TenantId, int? EndingWithinDays);
