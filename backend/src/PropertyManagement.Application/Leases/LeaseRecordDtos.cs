using PropertyManagement.Domain.Leases;

namespace PropertyManagement.Application.Leases;

public sealed record LeaseNoticeDto(
    Guid Id, NoticeType Type, DateOnly ServedOn, ServiceMethod Method, string? ServedBy, DateOnly ExpiresOn,
    string? Notes, NoticeStatus Status, string? WithdrawnReason, string? RecordedByName, DateTime RecordedAtUtc,
    bool HasExpired, int DaysLeft);

/// <summary>Records a notice. Leave <see cref="ExpiresOn"/> empty to use the default period (see the defaults endpoint).</summary>
public sealed record ServeNoticeRequest(NoticeType Type, DateOnly ServedOn, ServiceMethod Method, string? ServedBy, DateOnly? ExpiresOn, string? Notes);

public sealed record WithdrawNoticeRequest(string? Reason);

/// <summary>The suggested expiry for a notice, and why. A starting point a lawyer should confirm.</summary>
public sealed record NoticeDefaultDto(NoticeType Type, DateOnly ServedOn, int Days, DateOnly ExpiresOn, string Basis);

public sealed record InspectionItemDto(string Area, ItemCondition Condition, string? Notes, decimal? ProposedDeduction);

public sealed record LeaseInspectionDto(
    Guid Id, InspectionType Type, DateOnly InspectedOn, bool TenantPresent, bool TenantAgreed, string? Summary,
    string? InspectedByName, DateTime UpdatedAtUtc, IReadOnlyList<InspectionItemDto> Items, decimal TotalProposedDeduction);

/// <summary>Both reports for a lease, with the caution deposit on the lease so deductions can be compared to it.</summary>
public sealed record LeaseInspectionsDto(LeaseInspectionDto? MoveIn, LeaseInspectionDto? MoveOut, decimal? CautionDeposit);

public sealed record SaveInspectionRequest(DateOnly InspectedOn, bool TenantPresent, bool TenantAgreed, string? Summary,
    IReadOnlyList<InspectionItemInput> Items);
