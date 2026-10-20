using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Properties;

public enum PropertyType { Residential = 0, Commercial = 1, Mixed = 2 }

public enum PropertyStatus { Active = 0, UnderRenovation = 1, Archived = 2 }

public sealed record PropertyDetails(
    string Code,
    string Name,
    PropertyType Type,
    string? Description,
    string AddressLine,
    string? City,
    string State,
    string? LocalGovernmentArea,
    Guid? BranchId,
    Guid? ManagerUserId,
    string? Amenities);

/// <summary>A building or estate that holds rentable units (design 6.2).</summary>
public class Property : IOrganizationScoped
{
    private Property() { }

    public Property(Guid organizationId, PropertyDetails details)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        Status = PropertyStatus.Active;
        CreatedAtUtc = DateTime.UtcNow;
        Apply(details);
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public PropertyType Type { get; private set; }
    public string? Description { get; private set; }
    public string AddressLine { get; private set; } = "";
    public string? City { get; private set; }
    /// <summary>Nigerian state, e.g. Lagos. Legal rules such as advance-rent caps vary by state.</summary>
    public string State { get; private set; } = "";
    public string? LocalGovernmentArea { get; private set; }
    public Guid? BranchId { get; private set; }
    /// <summary>The staff member responsible. Property Managers see only the properties assigned to them.</summary>
    public Guid? ManagerUserId { get; private set; }
    /// <summary>Comma-separated amenities, e.g. "Borehole, Generator, Security".</summary>
    public string? Amenities { get; private set; }
    public PropertyStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ArchivedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsArchived => Status == PropertyStatus.Archived;

    public void Update(PropertyDetails details, PropertyStatus status)
    {
        EnsureNotArchived();
        if (status == PropertyStatus.Archived) throw new DomainException("Use archive to archive a property.");
        Guard.Defined(status, "Status");
        Apply(details);
        Status = status;
    }

    public void Archive(DateTime nowUtc)
    {
        EnsureNotArchived();
        Status = PropertyStatus.Archived;
        ArchivedAtUtc = nowUtc;
    }

    public void EnsureNotArchived()
    {
        if (IsArchived) throw new DomainException("This property is archived and can't be changed.", "archived");
    }

    private void Apply(PropertyDetails d)
    {
        Guard.Defined(d.Type, "Property type");
        Code = Guard.Required(d.Code, "Code", 30).ToUpperInvariant();
        Name = Guard.Required(d.Name, "Name", 200);
        Type = d.Type;
        Description = Guard.Optional(d.Description, "Description", 2000);
        AddressLine = Guard.Required(d.AddressLine, "Address", 300);
        City = Guard.Optional(d.City, "City", 100);
        State = Guard.Required(d.State, "State", 50);
        LocalGovernmentArea = Guard.Optional(d.LocalGovernmentArea, "Local government area", 100);
        BranchId = d.BranchId;
        ManagerUserId = d.ManagerUserId;
        Amenities = Guard.Optional(d.Amenities, "Amenities", 1000);
    }
}
