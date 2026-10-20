using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Owners;

namespace PropertyManagement.Domain.Tenants;

public sealed record TenantDetails(
    PartyType Type,
    string Name,
    string? Email,
    string? Phone,
    string? AlternatePhone,
    string? Address,
    string? Employer,
    string? Occupation,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? CompanyRegistrationNumber,
    string? TaxIdentificationNumber,
    string? Notes);

/// <summary>A person or company that rents, or applies to rent, a unit (design 6.4).</summary>
public class Tenant : IOrganizationScoped
{
    private Tenant() { }

    public Tenant(Guid organizationId, TenantDetails details)
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
    public string? AlternatePhone { get; private set; }
    public string? Address { get; private set; }
    public string? Employer { get; private set; }
    public string? Occupation { get; private set; }
    public string? EmergencyContactName { get; private set; }
    public string? EmergencyContactPhone { get; private set; }
    /// <summary>CAC RC number for company tenants.</summary>
    public string? CompanyRegistrationNumber { get; private set; }
    public string? TaxIdentificationNumber { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Update(TenantDetails details) => Apply(details);

    private void Apply(TenantDetails d)
    {
        Guard.Defined(d.Type, "Tenant type");
        Type = d.Type;
        Name = Guard.Required(d.Name, "Name", 200);
        Email = Contact.Email(d.Email);
        Phone = Contact.Phone(d.Phone);
        AlternatePhone = Contact.Phone(d.AlternatePhone, "Alternate phone");
        Address = Guard.Optional(d.Address, "Address", 500);
        Employer = Guard.Optional(d.Employer, "Employer", 200);
        Occupation = Guard.Optional(d.Occupation, "Occupation", 100);
        EmergencyContactName = Guard.Optional(d.EmergencyContactName, "Emergency contact name", 200);
        EmergencyContactPhone = Contact.Phone(d.EmergencyContactPhone, "Emergency contact phone");
        CompanyRegistrationNumber = Guard.Optional(d.CompanyRegistrationNumber, "RC number", 30);
        TaxIdentificationNumber = Guard.Optional(d.TaxIdentificationNumber, "TIN", 30);
        Notes = Guard.Optional(d.Notes, "Notes", 2000);
        if (Email is null && Phone is null) throw new DomainException("Enter an email address or a phone number.");
    }
}
