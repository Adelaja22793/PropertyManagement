using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Properties;

namespace PropertyManagement.Domain.Leases;

/// <summary>What is stored. Expiring, upcoming and holding over are worked out from the dates; see <see cref="Lease.StageOn"/>.</summary>
public enum LeaseStatus { Draft = 0, Active = 1, Renewed = 2, Ended = 3, Terminated = 4, Cancelled = 5 }

/// <summary>Where a lease is in its life on a given day, as staff see it (design 6.5).</summary>
public enum LeaseStage { Draft = 0, Upcoming = 1, Active = 2, Expiring = 3, HoldingOver = 4, Renewed = 5, Ended = 6, Terminated = 7, Cancelled = 8 }

public sealed record LeaseTerms(
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
    string? Notes);

public sealed record LeaseChargeInput(LeaseChargeType Type, string? Description, decimal Amount, ChargeRecurrence Recurrence);

/// <summary>
/// An agreement to let one unit to one or more tenants for a fixed term (design 6.5). A lease is edited freely
/// while it is a draft. Once active its terms are fixed: changes come through a renewal, which is a new lease
/// linked to this one. This phase records the terms only; Phase 4 finance turns them into invoices.
/// </summary>
public class Lease : IOrganizationScoped
{
    /// <summary>Default warning window for "expiring" (design 6.5 reminders at 90, 60 and 30 days).</summary>
    public const int ExpiringWithinDays = 90;
    public const int MaxTenants = 10;

    private readonly List<LeaseTenant> _tenants = [];
    private readonly List<LeaseGuarantor> _guarantors = [];
    private readonly List<LeaseCharge> _charges = [];

    private Lease() { }

    public Lease(Guid organizationId, int number, Guid propertyId, Guid unitId, LeaseTerms terms, Guid? previousLeaseId = null)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        Number = number;
        PropertyId = propertyId;
        UnitId = unitId;
        PreviousLeaseId = previousLeaseId;
        Status = LeaseStatus.Draft;
        CreatedAtUtc = DateTime.UtcNow;
        Apply(terms);
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    /// <summary>Sequence number within the organization. Shown as <see cref="Reference"/>.</summary>
    public int Number { get; private set; }
    public string Reference => FormatReference(Number);
    public Guid PropertyId { get; private set; }
    public Guid UnitId { get; private set; }
    /// <summary>The lease this one renews, if any.</summary>
    public Guid? PreviousLeaseId { get; private set; }
    public LeaseStatus Status { get; private set; }
    public DateOnly StartDate { get; private set; }
    /// <summary>Last day of the term.</summary>
    public DateOnly EndDate { get; private set; }
    /// <summary>Rent per billing period.</summary>
    public decimal RentAmount { get; private set; }
    public string Currency { get; private set; } = "NGN";
    public BillingFrequency BillingFrequency { get; private set; }
    /// <summary>How many days before each period starts its rent falls due. 0 means on the first day.</summary>
    public int DueDaysBeforePeriod { get; private set; }
    /// <summary>Days after the due date before rent counts as overdue.</summary>
    public int GraceDays { get; private set; }
    public int? NoticePeriodDays { get; private set; }
    /// <summary>Rent per billing period if the tenant stays on after the end date without a renewal.</summary>
    public decimal? HoldoverRentAmount { get; private set; }
    public string? RenewalTerms { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ActivatedAtUtc { get; private set; }
    public Guid? ActivatedByUserId { get; private set; }
    /// <summary>The day the tenant handed the unit back, for an ended or terminated lease. The unit is free from this day.</summary>
    public DateOnly? MoveOutDate { get; private set; }
    public string? EndReason { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<LeaseTenant> Tenants => _tenants;
    public IReadOnlyList<LeaseGuarantor> Guarantors => _guarantors;
    public IReadOnlyList<LeaseCharge> Charges => _charges;

    public static string FormatReference(int number) => $"LSE-{number:D5}";

    /// <summary>
    /// The last day this lease holds the unit, for overlap checks. An active lease past its end date is holding over
    /// and keeps the unit until it is ended, so it has no last day yet.
    /// </summary>
    public DateOnly? HoldsUnitUntil(DateOnly today) => Status switch
    {
        LeaseStatus.Active when EndDate < today => null,
        LeaseStatus.Active or LeaseStatus.Renewed => EndDate,
        // The unit is free again on the move-out day itself, so the next tenant can move in that day.
        LeaseStatus.Ended or LeaseStatus.Terminated => MoveOutDate?.AddDays(-1),
        _ => DateOnly.MinValue,
    };

    /// <summary>True when this lease takes up the unit on some day in [start, end].</summary>
    public bool BlocksUnitBetween(DateOnly start, DateOnly end, DateOnly today)
    {
        if (Status is LeaseStatus.Draft or LeaseStatus.Cancelled) return false;
        var until = HoldsUnitUntil(today) ?? DateOnly.MaxValue;
        return StartDate <= end && start <= until;
    }

    public LeaseStage StageOn(DateOnly today) => Status switch
    {
        LeaseStatus.Draft => LeaseStage.Draft,
        LeaseStatus.Renewed => LeaseStage.Renewed,
        LeaseStatus.Ended => LeaseStage.Ended,
        LeaseStatus.Terminated => LeaseStage.Terminated,
        LeaseStatus.Cancelled => LeaseStage.Cancelled,
        _ when StartDate > today => LeaseStage.Upcoming,
        _ when EndDate < today => LeaseStage.HoldingOver,
        _ when EndDate.DayNumber - today.DayNumber < ExpiringWithinDays => LeaseStage.Expiring,
        _ => LeaseStage.Active,
    };

    public void Update(LeaseTerms terms)
    {
        RequireDraft();
        Apply(terms);
    }

    /// <summary>Replaces the tenants (the first is the primary tenant) and guarantors of a draft.</summary>
    public void SetParties(IReadOnlyList<Guid> tenantIds, IReadOnlyList<Guid> guarantorIds)
    {
        RequireDraft();
        if (tenantIds.Count == 0) throw new DomainException("Add at least one tenant.");
        if (tenantIds.Count > MaxTenants) throw new DomainException($"A lease can have at most {MaxTenants} tenants.");
        if (tenantIds.Distinct().Count() != tenantIds.Count) throw new DomainException("Each tenant can appear only once.");
        if (guarantorIds.Distinct().Count() != guarantorIds.Count) throw new DomainException("Each guarantor can appear only once.");

        _tenants.RemoveAll(t => !tenantIds.Contains(t.TenantId));
        for (var i = 0; i < tenantIds.Count; i++)
        {
            var existing = _tenants.Find(t => t.TenantId == tenantIds[i]);
            if (existing is null) _tenants.Add(new LeaseTenant(OrganizationId, Id, tenantIds[i], i == 0));
            else existing.SetPrimary(i == 0);
        }

        _guarantors.RemoveAll(g => !guarantorIds.Contains(g.GuarantorId));
        foreach (var id in guarantorIds.Where(id => _guarantors.TrueForAll(g => g.GuarantorId != id)))
            _guarantors.Add(new LeaseGuarantor(OrganizationId, Id, id));
    }

    public void SetCharges(IReadOnlyList<LeaseChargeInput> charges)
    {
        RequireDraft();
        if (charges.Count > 30) throw new DomainException("A lease can have at most 30 charges.");
        var built = charges.Select(c => new LeaseCharge(OrganizationId, Id, c)).ToList();
        if (built.Count(c => c.Type == LeaseChargeType.CautionDeposit) > 1)
            throw new DomainException("A lease can have only one caution deposit.");
        _charges.Clear();
        _charges.AddRange(built);
    }

    public void Activate(Guid userId, DateTime nowUtc)
    {
        RequireDraft();
        if (_tenants.Count == 0) throw new DomainException("Add at least one tenant before activating the lease.");
        Status = LeaseStatus.Active;
        ActivatedAtUtc = nowUtc;
        ActivatedByUserId = userId;
    }

    public void Cancel(string? reason, DateTime nowUtc)
    {
        RequireDraft();
        Status = LeaseStatus.Cancelled;
        EndReason = Guard.Optional(reason, "Reason", 500);
        ClosedAtUtc = nowUtc;
    }

    /// <summary>
    /// Records that the tenant has left. Before the end date this is an early termination and needs a reason;
    /// on or after it the lease has simply ended. Invoices, payments and documents are never deleted.
    /// </summary>
    public void End(DateOnly moveOutDate, string? reason, DateTime nowUtc)
    {
        if (Status != LeaseStatus.Active) throw new DomainException("Only an active lease can be ended.", "lease_not_active");
        if (moveOutDate < StartDate) throw new DomainException("The move-out date can't be before the lease starts.");
        var early = moveOutDate < EndDate;
        if (early && string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Give a reason for ending the lease before its end date.");
        Status = early ? LeaseStatus.Terminated : LeaseStatus.Ended;
        MoveOutDate = moveOutDate;
        EndReason = Guard.Optional(reason, "Reason", 500);
        ClosedAtUtc = nowUtc;
    }

    /// <summary>Called when the renewal that follows this lease is activated.</summary>
    public void MarkRenewed(DateTime nowUtc)
    {
        if (Status != LeaseStatus.Active) throw new DomainException("Only an active lease can be renewed.", "lease_not_active");
        Status = LeaseStatus.Renewed;
        ClosedAtUtc = nowUtc;
    }

    public void UpdateNotes(string? notes) => Notes = Guard.Optional(notes, "Notes", 2000);

    private void RequireDraft()
    {
        if (Status != LeaseStatus.Draft)
            throw new DomainException("Only a draft lease can be changed. Renew the lease to change its terms.", "lease_not_draft");
    }

    private void Apply(LeaseTerms t)
    {
        Guard.Defined(t.BillingFrequency, "Billing frequency");
        if (t.EndDate <= t.StartDate) throw new DomainException("The end date must be after the start date.");
        if (t.EndDate > t.StartDate.AddYears(25)) throw new DomainException("A lease can be at most 25 years long.");
        if (t.RentAmount <= 0) throw new DomainException("Rent must be more than zero.");
        if (decimal.Round(t.RentAmount, 2) != t.RentAmount) throw new DomainException("Rent can have at most two decimal places.");
        if (t.DueDaysBeforePeriod is < 0 or > 90) throw new DomainException("Due days before the period must be between 0 and 90.");
        if (t.GraceDays is < 0 or > 90) throw new DomainException("Grace period must be between 0 and 90 days.");
        if (t.NoticePeriodDays is < 0 or > 730) throw new DomainException("Notice period must be between 0 and 730 days.");
        if (t.HoldoverRentAmount is <= 0) throw new DomainException("Holdover rent must be more than zero.");
        StartDate = t.StartDate;
        EndDate = t.EndDate;
        RentAmount = t.RentAmount;
        Currency = Guard.Currency(t.Currency);
        BillingFrequency = t.BillingFrequency;
        DueDaysBeforePeriod = t.DueDaysBeforePeriod;
        GraceDays = t.GraceDays;
        NoticePeriodDays = t.NoticePeriodDays;
        HoldoverRentAmount = t.HoldoverRentAmount;
        RenewalTerms = Guard.Optional(t.RenewalTerms, "Renewal terms", 1000);
        Notes = Guard.Optional(t.Notes, "Notes", 2000);
    }
}
