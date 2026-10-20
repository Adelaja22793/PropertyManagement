using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Maintenance;

public enum MaintenanceCategory
{
    Plumbing = 0, Electrical = 1, Carpentry = 2, Painting = 3, Roofing = 4, Appliances = 5, Generator = 6,
    WaterSupply = 7, Security = 8, Cleaning = 9, PestControl = 10, Structural = 11, Other = 12,
}

public enum MaintenanceUrgency { Emergency = 0, High = 1, Normal = 2, Low = 3 }

/// <summary>Request statuses from design 6.11.</summary>
public enum MaintenanceStatus
{
    Submitted = 0, UnderReview = 1, Approved = 2, Assigned = 3, InProgress = 4, AwaitingParts = 5,
    Completed = 6, Rejected = 7, Cancelled = 8,
}

public enum MaintenanceUpdateKind { Created = 0, Comment = 1, StatusChanged = 2, Assigned = 3, Estimated = 4, OwnerApproved = 5, Feedback = 6 }

public sealed record MaintenanceDetails(
    Guid? UnitId, Guid? TenantId, string? ReportedBy, string Title, string? Description,
    MaintenanceCategory Category, MaintenanceUrgency Urgency);

/// <summary>
/// A repair or maintenance job on a property or unit (design 6.11). It moves through review, approval, assignment
/// to a staff member or vendor, and completion, keeping a timeline of every step. When the estimate is above the
/// owner's repair approval limit, the owner's approval must be recorded before the work is approved.
/// The final cost is recorded on completion; posting it as an expense against the owner waits for the finance work.
/// </summary>
public class MaintenanceRequest : IOrganizationScoped
{
    private readonly List<MaintenanceUpdate> _updates = [];

    private MaintenanceRequest() { }

    public MaintenanceRequest(Guid organizationId, int number, Guid propertyId, MaintenanceDetails details, Guid userId, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        Number = number;
        PropertyId = propertyId;
        Status = MaintenanceStatus.Submitted;
        CreatedByUserId = userId;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        Apply(details);
        Log(MaintenanceUpdateKind.Created, null, userId, nowUtc);
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public int Number { get; private set; }
    public string Reference => FormatReference(Number);
    public Guid PropertyId { get; private set; }
    public Guid? UnitId { get; private set; }
    /// <summary>The tenant who reported it, when it came from a tenant.</summary>
    public Guid? TenantId { get; private set; }
    /// <summary>Who reported it, in words, such as "Tenant by phone" or "Caretaker".</summary>
    public string? ReportedBy { get; private set; }
    public string Title { get; private set; } = "";
    public string? Description { get; private set; }
    public MaintenanceCategory Category { get; private set; }
    public MaintenanceUrgency Urgency { get; private set; }
    public MaintenanceStatus Status { get; private set; }

    public Guid? AssignedUserId { get; private set; }
    public Guid? VendorId { get; private set; }

    public decimal? EstimateAmount { get; private set; }
    public string? EstimateNotes { get; private set; }
    /// <summary>The owner's limit at the time of the estimate; above it the owner must approve.</summary>
    public decimal? ApprovalLimit { get; private set; }
    public bool NeedsOwnerApproval => EstimateAmount is { } amount && ApprovalLimit is { } limit && amount > limit;
    public string? OwnerApprovalNote { get; private set; }
    public DateTime? OwnerApprovalRecordedAtUtc { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }

    public DateOnly? CompletedOn { get; private set; }
    public decimal? ActualCost { get; private set; }
    public string? CompletionNotes { get; private set; }
    public string? ClosedReason { get; private set; }
    public int? TenantRating { get; private set; }
    public string? TenantFeedback { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public IReadOnlyList<MaintenanceUpdate> Updates => _updates;

    public bool IsOpen => Status is not (MaintenanceStatus.Completed or MaintenanceStatus.Rejected or MaintenanceStatus.Cancelled);
    public bool IsWorkStage => Status is MaintenanceStatus.Assigned or MaintenanceStatus.InProgress or MaintenanceStatus.AwaitingParts;

    public static string FormatReference(int number) => $"MNT-{number:D5}";

    public void Update(MaintenanceDetails details, Guid userId, DateTime nowUtc)
    {
        RequireOpen();
        Apply(details);
        Touch(nowUtc);
    }

    public void Comment(string text, Guid userId, DateTime nowUtc) =>
        Log(MaintenanceUpdateKind.Comment, Guard.Required(text, "Comment", 2000), userId, nowUtc);

    public void Review(Guid userId, DateTime nowUtc)
    {
        Move(MaintenanceStatus.UnderReview, [MaintenanceStatus.Submitted], null, userId, nowUtc);
    }

    /// <summary>
    /// Records the estimate against the owner's current limit. After the work is approved, an estimate above the limit
    /// must come with the owner's approval.
    /// </summary>
    public void Estimate(decimal amount, string? notes, decimal? approvalLimit, string? ownerApprovalNote, Guid userId, DateTime nowUtc)
    {
        RequireOpen();
        if (amount <= 0) throw new DomainException("The estimate must be more than zero.");
        EstimateAmount = decimal.Round(amount, 2);
        EstimateNotes = Guard.Optional(notes, "Estimate notes", 1000);
        ApprovalLimit = approvalLimit;
        OwnerApprovalNote = null;
        OwnerApprovalRecordedAtUtc = null;
        Log(MaintenanceUpdateKind.Estimated, $"Estimate {EstimateAmount:N2}{(EstimateNotes is null ? "" : $": {EstimateNotes}")}", userId, nowUtc);
        if (!string.IsNullOrWhiteSpace(ownerApprovalNote)) RecordOwnerApproval(ownerApprovalNote, userId, nowUtc);
        else if (NeedsOwnerApproval && ApprovedAtUtc is not null)
            throw new DomainException(
                "The new estimate is above the owner's repair approval limit. Record the owner's approval with it.",
                "owner_approval_required");
    }

    /// <summary>Records how and when the owner approved a repair above their limit.</summary>
    public void RecordOwnerApproval(string note, Guid userId, DateTime nowUtc)
    {
        RequireOpen();
        if (!NeedsOwnerApproval) throw new DomainException("This estimate doesn't need the owner's approval.");
        OwnerApprovalNote = Guard.Required(note, "Owner approval", 500);
        OwnerApprovalRecordedAtUtc = nowUtc;
        Log(MaintenanceUpdateKind.OwnerApproved, OwnerApprovalNote, userId, nowUtc);
    }

    public void Approve(Guid userId, DateTime nowUtc)
    {
        if (NeedsOwnerApproval && OwnerApprovalRecordedAtUtc is null)
            throw new DomainException(
                "The estimate is above the owner's repair approval limit. Record the owner's approval first.",
                "owner_approval_required");
        Move(MaintenanceStatus.Approved, [MaintenanceStatus.Submitted, MaintenanceStatus.UnderReview], null, userId, nowUtc);
        ApprovedByUserId = userId;
        ApprovedAtUtc = nowUtc;
    }

    /// <summary>Gives the job to a staff member, a vendor or both. Reassigning keeps the current stage.</summary>
    public void Assign(Guid? assignedUserId, Guid? vendorId, string description, Guid userId, DateTime nowUtc)
    {
        if (assignedUserId is null && vendorId is null) throw new DomainException("Choose a staff member or a vendor.");
        if (Status != MaintenanceStatus.Approved && !IsWorkStage)
            throw new DomainException("Approve the request before assigning it.", "maintenance_status");
        AssignedUserId = assignedUserId;
        VendorId = vendorId;
        if (Status == MaintenanceStatus.Approved) Status = MaintenanceStatus.Assigned;
        Log(MaintenanceUpdateKind.Assigned, description, userId, nowUtc);
    }

    public void Start(Guid userId, DateTime nowUtc) =>
        Move(MaintenanceStatus.InProgress, [MaintenanceStatus.Assigned, MaintenanceStatus.AwaitingParts], null, userId, nowUtc);

    public void AwaitParts(string? note, Guid userId, DateTime nowUtc) =>
        Move(MaintenanceStatus.AwaitingParts, [MaintenanceStatus.InProgress], Guard.Optional(note, "Note", 1000), userId, nowUtc);

    public void Complete(DateOnly completedOn, decimal? actualCost, string? notes, Guid userId, DateTime nowUtc)
    {
        if (actualCost < 0) throw new DomainException("The cost can't be negative.");
        var completionNotes = Guard.Optional(notes, "Completion notes", 2000);
        Move(MaintenanceStatus.Completed,
            [MaintenanceStatus.Assigned, MaintenanceStatus.InProgress, MaintenanceStatus.AwaitingParts], completionNotes, userId, nowUtc);
        CompletedOn = completedOn;
        ActualCost = actualCost is null ? null : decimal.Round(actualCost.Value, 2);
        CompletionNotes = completionNotes;
    }

    public void Reject(string reason, Guid userId, DateTime nowUtc)
    {
        var text = Guard.Required(reason, "Reason", 500);
        Move(MaintenanceStatus.Rejected, [MaintenanceStatus.Submitted, MaintenanceStatus.UnderReview], text, userId, nowUtc);
        ClosedReason = text;
    }

    public void Cancel(string reason, Guid userId, DateTime nowUtc)
    {
        var text = Guard.Required(reason, "Reason", 500);
        RequireOpen();
        Move(MaintenanceStatus.Cancelled, [Status], text, userId, nowUtc);
        ClosedReason = text;
    }

    /// <summary>The tenant's view of the finished job, recorded once.</summary>
    public void RecordFeedback(int rating, string? comment, Guid userId, DateTime nowUtc)
    {
        if (Status != MaintenanceStatus.Completed) throw new DomainException("Feedback is recorded once the job is completed.", "maintenance_status");
        if (TenantRating is not null) throw new DomainException("Feedback has already been recorded.", "already_decided");
        if (rating is < 1 or > 5) throw new DomainException("Rating must be from 1 to 5.");
        TenantRating = rating;
        TenantFeedback = Guard.Optional(comment, "Feedback", 1000);
        Log(MaintenanceUpdateKind.Feedback, $"{rating}/5{(TenantFeedback is null ? "" : $": {TenantFeedback}")}", userId, nowUtc);
    }

    private void Move(MaintenanceStatus to, MaintenanceStatus[] from, string? text, Guid userId, DateTime nowUtc)
    {
        if (!from.Contains(Status))
            throw new DomainException($"A request that is {Describe(Status)} can't be moved to {Describe(to)}.", "maintenance_status");
        var previous = Status;
        Status = to;
        Log(MaintenanceUpdateKind.StatusChanged, text, userId, nowUtc, previous);
    }

    private void RequireOpen()
    {
        if (!IsOpen) throw new DomainException($"This request is {Describe(Status)}.", "maintenance_status");
    }

    private void Log(MaintenanceUpdateKind kind, string? text, Guid userId, DateTime nowUtc, MaintenanceStatus? from = null)
    {
        _updates.Add(new MaintenanceUpdate(OrganizationId, Id, _updates.Count + 1, kind, text, from, Status, userId, nowUtc));
        Touch(nowUtc);
    }

    private void Touch(DateTime nowUtc) => UpdatedAtUtc = nowUtc;

    private void Apply(MaintenanceDetails d)
    {
        Guard.Defined(d.Category, "Category");
        Guard.Defined(d.Urgency, "Urgency");
        UnitId = d.UnitId;
        TenantId = d.TenantId;
        ReportedBy = Guard.Optional(d.ReportedBy, "Reported by", 200);
        Title = Guard.Required(d.Title, "Title", 200);
        Description = Guard.Optional(d.Description, "Description", 4000);
        Category = d.Category;
        Urgency = d.Urgency;
    }

    private static string Describe(MaintenanceStatus s) => s switch
    {
        MaintenanceStatus.UnderReview => "under review",
        MaintenanceStatus.InProgress => "in progress",
        MaintenanceStatus.AwaitingParts => "awaiting parts",
        _ => s.ToString().ToLowerInvariant(),
    };
}

/// <summary>One entry on a request's timeline: a comment, a status change, an assignment or an estimate.</summary>
public class MaintenanceUpdate : IOrganizationScoped
{
    private MaintenanceUpdate() { }

    internal MaintenanceUpdate(Guid organizationId, Guid requestId, int sequence, MaintenanceUpdateKind kind, string? text,
        MaintenanceStatus? fromStatus, MaintenanceStatus status, Guid userId, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        RequestId = requestId;
        Sequence = sequence;
        Kind = kind;
        Text = text;
        FromStatus = fromStatus;
        Status = status;
        ByUserId = userId;
        AtUtc = nowUtc;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid RequestId { get; private set; }
    /// <summary>Order on the timeline; several entries can share a timestamp.</summary>
    public int Sequence { get; private set; }
    public MaintenanceUpdateKind Kind { get; private set; }
    public string? Text { get; private set; }
    public MaintenanceStatus? FromStatus { get; private set; }
    /// <summary>The request's status after this entry.</summary>
    public MaintenanceStatus Status { get; private set; }
    public Guid ByUserId { get; private set; }
    public DateTime AtUtc { get; private set; }
}
