using PropertyManagement.Domain.Leases;
using PropertyManagement.Domain.Maintenance;
using PropertyManagement.Domain.Properties;

namespace PropertyManagement.Application.Portal;

// What owners and tenants see in their portals (design 10.3 and 10.4). These carry only what the person may see:
// no identity numbers, no other tenants' contact details, no staff-only notes.

public sealed record PortalLeaseBriefDto(string Reference, string? TenantName, decimal RentAmount, string Currency,
    BillingFrequency BillingFrequency, DateOnly StartDate, DateOnly EndDate, LeaseStage Stage);

public sealed record PortalUnitDto(string Code, UnitType Type, int? Bedrooms, OccupancyStatus OccupancyStatus, PortalLeaseBriefDto? Lease);

public sealed record PortalPropertyDto(Guid Id, string Code, string Name, string AddressLine, string? City, string State,
    decimal SharePercent, IReadOnlyList<PortalUnitDto> Units);

public sealed record OwnerPortalDto(string OwnerName, string OrganizationName, IReadOnlyList<PortalPropertyDto> Properties,
    int OpenMaintenance, int AwaitingYourApproval);

public sealed record PortalChargeDto(LeaseChargeType Type, string? Description, decimal Amount, ChargeRecurrence Recurrence);

public sealed record PortalNoticeDto(NoticeType Type, DateOnly ServedOn, DateOnly ExpiresOn, string? Notes);

public sealed record PortalLeaseDto(Guid Id, string Reference, string PropertyName, string AddressLine, string? City, string State,
    Guid UnitId, string UnitCode, LeaseStatus Status, LeaseStage Stage, DateOnly StartDate, DateOnly EndDate, decimal RentAmount,
    string Currency, BillingFrequency BillingFrequency, int DueDaysBeforePeriod, int GraceDays, int? NoticePeriodDays,
    string? RenewalTerms, DateOnly? MoveOutDate, IReadOnlyList<string> Tenants, IReadOnlyList<PortalChargeDto> Charges,
    IReadOnlyList<PortalNoticeDto> Notices);

public sealed record TenantPortalDto(string TenantName, string OrganizationName, IReadOnlyList<PortalLeaseDto> Leases);

public sealed record PortalMaintenanceSummaryDto(Guid Id, string Reference, string Title, MaintenanceCategory Category,
    MaintenanceUrgency Urgency, MaintenanceStatus Status, string PropertyName, string? UnitCode, decimal? EstimateAmount,
    bool AwaitingOwnerApproval, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public sealed record PortalMaintenanceUpdateDto(MaintenanceUpdateKind Kind, string? Text, MaintenanceStatus Status, bool ByYou, DateTime AtUtc);

public sealed record PortalMaintenanceDetailDto(Guid Id, string Reference, string Title, string? Description,
    MaintenanceCategory Category, MaintenanceUrgency Urgency, MaintenanceStatus Status, string PropertyName, string? UnitCode,
    string? VendorName, decimal? EstimateAmount, string? EstimateNotes, decimal? ApprovalLimit, bool AwaitingOwnerApproval,
    string? OwnerApprovalNote, DateOnly? CompletedOn, decimal? ActualCost, string? CompletionNotes, string? ClosedReason,
    DateTime CreatedAtUtc, IReadOnlyList<PortalMaintenanceUpdateDto> Updates);

public sealed record PortalRepairRequest(Guid LeaseId, string Title, string? Description, MaintenanceCategory Category,
    MaintenanceUrgency Urgency);

public sealed record PortalCommentRequest(string Text);

public sealed record PortalApprovalRequest(string? Note);

public sealed record PortalDeclineRequest(string Reason);
