using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Properties;

public enum UnitType { Flat = 0, Room = 1, Shop = 2, Office = 3, Warehouse = 4, Duplex = 5, House = 6, Other = 7 }

public enum BillingFrequency { Annual = 0, Biannual = 1, Quarterly = 2, Monthly = 3 }

public enum OccupancyStatus { Vacant = 0, Occupied = 1, Reserved = 2, Unavailable = 3 }

public sealed record UnitDetails(
    string Code,
    UnitType Type,
    string? Floor,
    int? Bedrooms,
    int? Bathrooms,
    decimal? SizeSquareMetres,
    decimal DefaultRent,
    string? Currency,
    BillingFrequency BillingFrequency,
    decimal? ServiceChargeSharePercent,
    string? Amenities,
    string? Notes,
    int? SharedOccupancyLimit = null);

/// <summary>A rentable space inside a property (design 6.2). Its code is unique within the property.</summary>
public class PropertyUnit : IOrganizationScoped
{
    private PropertyUnit() { }

    public PropertyUnit(Guid organizationId, Guid propertyId, UnitDetails details)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        PropertyId = propertyId;
        OccupancyStatus = OccupancyStatus.Vacant;
        CreatedAtUtc = DateTime.UtcNow;
        Apply(details);
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid PropertyId { get; private set; }
    public string Code { get; private set; } = "";
    public UnitType Type { get; private set; }
    public string? Floor { get; private set; }
    public int? Bedrooms { get; private set; }
    public int? Bathrooms { get; private set; }
    public decimal? SizeSquareMetres { get; private set; }
    /// <summary>Asking rent per billing period, in <see cref="Currency"/>. Leases record the agreed rent.</summary>
    public decimal DefaultRent { get; private set; }
    public string Currency { get; private set; } = "NGN";
    public BillingFrequency BillingFrequency { get; private set; }
    /// <summary>This unit's share of the property's service charge budget, as a percentage.</summary>
    public decimal? ServiceChargeSharePercent { get; private set; }
    public OccupancyStatus OccupancyStatus { get; private set; }
    public string? Amenities { get; private set; }
    public string? Notes { get; private set; }
    public bool IsArchived { get; private set; }
    /// <summary>
    /// How many separate leases may hold the unit at once, for a unit let room by room (design 6.5). Null means the
    /// unit isn't shared, so it takes one lease at a time.
    /// </summary>
    public int? SharedOccupancyLimit { get; private set; }
    public int MaxConcurrentLeases => SharedOccupancyLimit ?? 1;
    public DateTime CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>
    /// How many leases have been activated on this unit. Bumped by <see cref="RecordLeaseActivation"/> so every
    /// activation writes the unit row: of two activations racing on the same unit, the second fails its row-version
    /// check instead of both passing the overlap check.
    /// </summary>
    public int LeaseActivationCount { get; private set; }

    public void RecordLeaseActivation() => LeaseActivationCount++;

    public void Update(UnitDetails details, OccupancyStatus occupancy)
    {
        if (IsArchived) throw new DomainException("This unit is archived and can't be changed.", "archived");
        Guard.Defined(occupancy, "Occupancy status");
        Apply(details);
        OccupancyStatus = occupancy;
    }

    /// <summary>
    /// Sets occupancy from the unit's leases. A lease that holds the unit today makes it occupied, and one that starts
    /// later makes it reserved. With neither, a unit that leases had marked occupied or reserved becomes vacant;
    /// a status staff set by hand (such as unavailable) is left alone.
    /// </summary>
    public bool ApplyLeaseOccupancy(bool heldToday, bool heldLater)
    {
        var next = heldToday ? OccupancyStatus.Occupied
            : heldLater ? OccupancyStatus.Reserved
            : OccupancyStatus is OccupancyStatus.Occupied or OccupancyStatus.Reserved ? OccupancyStatus.Vacant
            : OccupancyStatus;
        if (next == OccupancyStatus) return false;
        OccupancyStatus = next;
        return true;
    }

    public void Archive()
    {
        if (IsArchived) throw new DomainException("This unit is already archived.", "archived");
        if (OccupancyStatus == OccupancyStatus.Occupied)
            throw new DomainException("An occupied unit can't be archived.", "unit_occupied");
        IsArchived = true;
        OccupancyStatus = OccupancyStatus.Unavailable;
    }

    private void Apply(UnitDetails d)
    {
        Guard.Defined(d.Type, "Unit type");
        Guard.Defined(d.BillingFrequency, "Billing frequency");
        if (d.ServiceChargeSharePercent is < 0 or > 100)
            throw new DomainException("Service charge share must be between 0 and 100 percent.");
        Code = Guard.Required(d.Code, "Unit code", 30).ToUpperInvariant();
        Type = d.Type;
        Floor = Guard.Optional(d.Floor, "Floor", 20);
        Bedrooms = Guard.NonNegative(d.Bedrooms, "Bedrooms");
        Bathrooms = Guard.NonNegative(d.Bathrooms, "Bathrooms");
        SizeSquareMetres = Guard.NonNegative(d.SizeSquareMetres, "Size");
        DefaultRent = Guard.NonNegative(d.DefaultRent, "Rent");
        Currency = Guard.Currency(d.Currency);
        BillingFrequency = d.BillingFrequency;
        ServiceChargeSharePercent = d.ServiceChargeSharePercent;
        Amenities = Guard.Optional(d.Amenities, "Amenities", 1000);
        Notes = Guard.Optional(d.Notes, "Notes", 2000);
        if (d.SharedOccupancyLimit is < MinSharedLeases or > MaxSharedLeases)
            throw new DomainException($"A shared unit takes between {MinSharedLeases} and {MaxSharedLeases} leases at once.");
        SharedOccupancyLimit = d.SharedOccupancyLimit;
    }

    public const int MinSharedLeases = 2;
    public const int MaxSharedLeases = 50;
}
