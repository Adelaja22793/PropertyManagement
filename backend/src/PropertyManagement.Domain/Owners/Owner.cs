using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Owners;

public enum PartyType { Individual = 0, Company = 1 }

public enum ContactChannel { Email = 0, Sms = 1, WhatsApp = 2, Phone = 3 }

public sealed record OwnerDetails(
    PartyType Type,
    string Name,
    string? Email,
    string? Phone,
    string? Address,
    string? TaxIdentificationNumber,
    ContactChannel PreferredChannel,
    string? Notes);

/// <summary>A landlord whose properties the organization manages (design 6.3).</summary>
public class Owner : IOrganizationScoped
{
    private Owner() { }

    public Owner(Guid organizationId, OwnerDetails details)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        CreatedAtUtc = DateTime.UtcNow;
        Apply(details);
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public PartyType Type { get; private set; }
    public string Name { get; private set; } = "";
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Address { get; private set; }
    public string? TaxIdentificationNumber { get; private set; }
    public ContactChannel PreferredChannel { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Update(OwnerDetails details) => Apply(details);

    private void Apply(OwnerDetails d)
    {
        Guard.Defined(d.Type, "Owner type");
        Guard.Defined(d.PreferredChannel, "Preferred channel");
        Type = d.Type;
        Name = Guard.Required(d.Name, "Name", 200);
        Email = Contact.Email(d.Email);
        Phone = Contact.Phone(d.Phone);
        Address = Guard.Optional(d.Address, "Address", 500);
        TaxIdentificationNumber = Guard.Optional(d.TaxIdentificationNumber, "TIN", 30);
        PreferredChannel = d.PreferredChannel;
        Notes = Guard.Optional(d.Notes, "Notes", 2000);
        if (Email is null && Phone is null) throw new DomainException("Enter an email address or a phone number.");
    }
}
