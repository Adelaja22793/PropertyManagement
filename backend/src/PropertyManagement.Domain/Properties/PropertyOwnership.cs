using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Properties;

/// <summary>
/// One owner's share of a property for a date range (design 6.3). Shares are always replaced as a set
/// that totals exactly 100%, so the total is 100% on every date from the first set onwards.
/// </summary>
public class PropertyOwnership : IOrganizationScoped
{
    private PropertyOwnership() { }

    public PropertyOwnership(Guid organizationId, Guid propertyId, Guid ownerId, decimal sharePercent, DateOnly effectiveFrom)
    {
        if (sharePercent is <= 0 or > 100) throw new DomainException("Each share must be more than 0 and at most 100 percent.");
        if (decimal.Round(sharePercent, 2) != sharePercent) throw new DomainException("Shares can have at most two decimal places.");
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        PropertyId = propertyId;
        OwnerId = ownerId;
        SharePercent = sharePercent;
        EffectiveFrom = effectiveFrom;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid PropertyId { get; private set; }
    public Guid OwnerId { get; private set; }
    public decimal SharePercent { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    /// <summary>Last day the share applies; null while it is current.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    public bool IsCurrentOn(DateOnly date) => EffectiveFrom <= date && (EffectiveTo is null || EffectiveTo >= date);

    public void EndOn(DateOnly lastDay)
    {
        if (lastDay < EffectiveFrom) throw new DomainException("A share can't end before it starts.");
        EffectiveTo = lastDay;
    }

    /// <summary>Checks a proposed set of shares: distinct owners totalling exactly 100%.</summary>
    public static void ValidateSet(IReadOnlyCollection<(Guid OwnerId, decimal SharePercent)> shares)
    {
        if (shares.Count == 0) throw new DomainException("Add at least one owner.", "ownership_invalid");
        if (shares.Select(s => s.OwnerId).Distinct().Count() != shares.Count)
            throw new DomainException("Each owner can appear only once.", "ownership_invalid");
        var total = shares.Sum(s => s.SharePercent);
        if (total != 100m)
            throw new DomainException($"Ownership shares must total 100%. They total {total:0.##}%.", "ownership_invalid");
    }
}
