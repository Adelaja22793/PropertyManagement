using PropertyManagement.Domain.Maintenance;

namespace PropertyManagement.Application.Maintenance;

public sealed record VendorDto(Guid Id, string Name, string? Trade, string? ContactName, string? Phone, string? Email,
    string? Address, string? Notes, bool IsActive, int OpenJobs, DateTime CreatedAtUtc);

public sealed record SaveVendorRequest(string Name, string? Trade, string? ContactName, string? Phone, string? Email,
    string? Address, string? Notes)
{
    internal VendorDetails ToDetails() => new(Name, Trade, ContactName, Phone, Email, Address, Notes);
}

public sealed record SetVendorActiveRequest(bool IsActive);

public sealed record MaintenanceSearch(string? Search, MaintenanceStatus? Status, bool? OpenOnly, MaintenanceUrgency? Urgency,
    Guid? PropertyId, Guid? UnitId, Guid? VendorId, bool AssignedToMe);

public sealed record MaintenanceSummaryDto(
    Guid Id, string Reference, string Title, MaintenanceCategory Category, MaintenanceUrgency Urgency, MaintenanceStatus Status,
    Guid PropertyId, string PropertyCode, string PropertyName, Guid? UnitId, string? UnitCode,
    string? AssignedToName, string? VendorName, decimal? EstimateAmount, bool AwaitingOwnerApproval,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public sealed record MaintenanceUpdateDto(Guid Id, MaintenanceUpdateKind Kind, string? Text, MaintenanceStatus? FromStatus,
    MaintenanceStatus Status, string? ByName, DateTime AtUtc);

/// <summary>What the caller may do with this request right now, so screens show only the actions that will work.</summary>
public sealed record MaintenanceActionsDto(bool Manage, bool Work);

public sealed record MaintenanceDetailDto(
    Guid Id, string Reference, string Title, string? Description, MaintenanceCategory Category, MaintenanceUrgency Urgency,
    MaintenanceStatus Status, Guid PropertyId, string PropertyCode, string PropertyName, Guid? UnitId, string? UnitCode,
    Guid? TenantId, string? TenantName, string? ReportedBy,
    Guid? AssignedUserId, string? AssignedToName, Guid? VendorId, string? VendorName, string? VendorPhone,
    decimal? EstimateAmount, string? EstimateNotes, decimal? ApprovalLimit, bool NeedsOwnerApproval,
    string? OwnerApprovalNote, DateTime? OwnerApprovalRecordedAtUtc, string? ApprovedByName, DateTime? ApprovedAtUtc,
    DateOnly? CompletedOn, decimal? ActualCost, string? CompletionNotes, string? ClosedReason,
    int? TenantRating, string? TenantFeedback, string? CreatedByName, DateTime CreatedAtUtc, DateTime UpdatedAtUtc,
    IReadOnlyList<MaintenanceUpdateDto> Updates, MaintenanceActionsDto Can);

public sealed record SaveMaintenanceRequest(
    Guid PropertyId, Guid? UnitId, Guid? TenantId, string? ReportedBy, string Title, string? Description,
    MaintenanceCategory Category, MaintenanceUrgency Urgency)
{
    internal MaintenanceDetails ToDetails() => new(UnitId, TenantId, ReportedBy, Title, Description, Category, Urgency);
}

public sealed record ReasonRequest(string Reason);

public sealed record NoteRequest(string? Note);

public sealed record CommentRequest(string Text);

public sealed record EstimateRequest(decimal Amount, string? Notes, string? OwnerApprovalNote);

public sealed record OwnerApprovalRequest(string Note);

public sealed record AssignRequest(Guid? AssignedUserId, Guid? VendorId);

public sealed record CompleteRequest(DateOnly CompletedOn, decimal? ActualCost, string? Notes);

public sealed record FeedbackRequest(int Rating, string? Comment);

public sealed record AssigneeDto(Guid UserId, string FullName, string Email);

/// <summary>The owner's repair approval limit for a property: the lowest limit among its current owners' agreements.</summary>
public sealed record ApprovalLimitDto(decimal? Limit);
