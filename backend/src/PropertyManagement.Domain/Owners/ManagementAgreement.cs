using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Owners;

public enum ManagementFeeBasis { PercentOfRentCollected = 0, PercentOfRentInvoiced = 1, FixedAmountPerMonth = 2 }

public enum PayoutFrequency { OnReceipt = 0, Monthly = 1, Quarterly = 2 }

/// <summary>When rent paid in advance for future periods is passed to the owner (design decision 18.6).</summary>
public enum AdvanceRentRelease { OnReceipt = 0, ByPeriod = 1 }

public sealed record AgreementTerms(
    Guid? PropertyId,
    ManagementFeeBasis FeeBasis,
    decimal FeeValue,
    decimal? LettingFeePercent,
    PayoutFrequency PayoutFrequency,
    AdvanceRentRelease AdvanceRentRelease,
    decimal ReserveAmount,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Notes,
    decimal? RepairApprovalLimit = null);

/// <summary>
/// The fee and payout terms agreed with an owner (design 6.3), for one property or, when PropertyId is null,
/// all of the owner's properties. Phase 4 finance reads these terms; this phase records them.
/// Defaults follow decisions 18.6 and 18.7: advance rent released on receipt, fee on rent collected.
/// </summary>
public class ManagementAgreement : IOrganizationScoped
{
    private ManagementAgreement() { }

    public ManagementAgreement(Guid organizationId, Guid ownerId, AgreementTerms terms)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        OwnerId = ownerId;
        CreatedAtUtc = DateTime.UtcNow;
        Apply(terms);
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid? PropertyId { get; private set; }
    public ManagementFeeBasis FeeBasis { get; private set; }
    /// <summary>A percentage for the percent bases, or an amount in the organization's currency for a fixed fee.</summary>
    public decimal FeeValue { get; private set; }
    /// <summary>Fee on a new letting, as a percentage of the first year's rent.</summary>
    public decimal? LettingFeePercent { get; private set; }
    public PayoutFrequency PayoutFrequency { get; private set; }
    public AdvanceRentRelease AdvanceRentRelease { get; private set; }
    /// <summary>Amount held back from payouts for repairs.</summary>
    public decimal ReserveAmount { get; private set; }
    /// <summary>Repairs estimated above this need the owner's approval first (design 6.11). Null means no limit.</summary>
    public decimal? RepairApprovalLimit { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool OverlapsWith(DateOnly start, DateOnly? end) =>
        StartDate <= (end ?? DateOnly.MaxValue) && start <= (EndDate ?? DateOnly.MaxValue);

    public void Update(AgreementTerms terms)
    {
        if (terms.PropertyId != PropertyId) throw new DomainException("An agreement's property can't be changed.");
        Apply(terms);
    }

    private void Apply(AgreementTerms t)
    {
        Guard.Defined(t.FeeBasis, "Fee basis");
        Guard.Defined(t.PayoutFrequency, "Payout frequency");
        Guard.Defined(t.AdvanceRentRelease, "Advance rent release");
        Guard.NonNegative(t.FeeValue, "Fee");
        if (t.FeeBasis != ManagementFeeBasis.FixedAmountPerMonth && t.FeeValue > 100)
            throw new DomainException("A percentage fee can't be more than 100%.");
        if (t.LettingFeePercent is < 0 or > 100) throw new DomainException("Letting fee must be between 0 and 100 percent.");
        if (t.EndDate < t.StartDate) throw new DomainException("End date can't be before start date.");
        PropertyId = t.PropertyId;
        FeeBasis = t.FeeBasis;
        FeeValue = t.FeeValue;
        LettingFeePercent = t.LettingFeePercent;
        PayoutFrequency = t.PayoutFrequency;
        AdvanceRentRelease = t.AdvanceRentRelease;
        ReserveAmount = Guard.NonNegative(t.ReserveAmount, "Reserve");
        RepairApprovalLimit = Guard.NonNegative(t.RepairApprovalLimit, "Repair approval limit");
        StartDate = t.StartDate;
        EndDate = t.EndDate;
        Notes = Guard.Optional(t.Notes, "Notes", 2000);
    }
}
