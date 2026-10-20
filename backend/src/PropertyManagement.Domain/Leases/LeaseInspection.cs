using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Leases;

public enum InspectionType { MoveIn = 0, MoveOut = 1 }

public enum ItemCondition { Good = 0, Fair = 1, Poor = 2, Damaged = 3, Missing = 4 }

public sealed record InspectionItemInput(string Area, ItemCondition Condition, string? Notes, decimal? ProposedDeduction);

public sealed record InspectionDetails(
    InspectionType Type, DateOnly InspectedOn, bool TenantPresent, bool TenantAgreed, string? Summary,
    IReadOnlyList<InspectionItemInput> Items);

/// <summary>
/// A move-in or move-out condition report for a lease (design 6.5). Move-out items can carry a proposed deduction
/// from the caution deposit; that is a proposal only, and Phase 4 finance decides what is actually deducted.
/// One report of each type per lease; recording it again replaces the items.
/// </summary>
public class LeaseInspection : IOrganizationScoped
{
    public const int MaxItems = 100;
    private readonly List<InspectionItem> _items = [];

    private LeaseInspection() { }

    public LeaseInspection(Guid organizationId, Guid leaseId, InspectionDetails details, Guid userId, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        LeaseId = leaseId;
        Guard.Defined(details.Type, "Inspection type");
        Type = details.Type;
        Apply(details, userId, nowUtc);
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid LeaseId { get; private set; }
    public InspectionType Type { get; private set; }
    public DateOnly InspectedOn { get; private set; }
    public bool TenantPresent { get; private set; }
    /// <summary>The tenant agreed with the report, for example by signing it.</summary>
    public bool TenantAgreed { get; private set; }
    public string? Summary { get; private set; }
    public Guid InspectedByUserId { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public IReadOnlyList<InspectionItem> Items => _items;

    public decimal TotalProposedDeduction => _items.Sum(i => i.ProposedDeduction ?? 0);

    public void Update(InspectionDetails details, Guid userId, DateTime nowUtc)
    {
        if (details.Type != Type) throw new DomainException("An inspection's type can't be changed.");
        Apply(details, userId, nowUtc);
    }

    private void Apply(InspectionDetails d, Guid userId, DateTime nowUtc)
    {
        if (d.Items.Count == 0) throw new DomainException("Add at least one area to the report.");
        if (d.Items.Count > MaxItems) throw new DomainException($"A report can have at most {MaxItems} areas.");
        if (d.TenantAgreed && !d.TenantPresent) throw new DomainException("The tenant can only agree the report if they were present.");
        var items = d.Items.Select((x, i) => new InspectionItem(OrganizationId, Id, i, x, Type)).ToList();
        InspectedOn = d.InspectedOn;
        TenantPresent = d.TenantPresent;
        TenantAgreed = d.TenantAgreed;
        Summary = Guard.Optional(d.Summary, "Summary", 2000);
        InspectedByUserId = userId;
        UpdatedAtUtc = nowUtc;
        _items.Clear();
        _items.AddRange(items);
    }
}

public class InspectionItem : IOrganizationScoped
{
    private InspectionItem() { }

    internal InspectionItem(Guid organizationId, Guid inspectionId, int position, InspectionItemInput input, InspectionType type)
    {
        Guard.Defined(input.Condition, "Condition");
        if (input.ProposedDeduction is { } amount)
        {
            if (type != InspectionType.MoveOut) throw new DomainException("Deductions can only be proposed at move-out.");
            if (amount <= 0 || decimal.Round(amount, 2) != amount)
                throw new DomainException("A proposed deduction must be more than zero with at most two decimal places.");
        }
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        InspectionId = inspectionId;
        Position = position;
        Area = Guard.Required(input.Area, "Area", 100);
        Condition = input.Condition;
        Notes = Guard.Optional(input.Notes, "Notes", 1000);
        ProposedDeduction = input.ProposedDeduction;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid InspectionId { get; private set; }
    public int Position { get; private set; }
    /// <summary>Room or fixture, such as "Kitchen" or "Master bedroom wardrobe".</summary>
    public string Area { get; private set; } = "";
    public ItemCondition Condition { get; private set; }
    public string? Notes { get; private set; }
    public decimal? ProposedDeduction { get; private set; }
}
