using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Maintenance;

public sealed record VendorDetails(string Name, string? Trade, string? ContactName, string? Phone, string? Email, string? Address, string? Notes);

/// <summary>A supplier or contractor the organization uses for repairs (design 6.10: one list for both since v1.1).</summary>
public class Vendor : IOrganizationScoped
{
    private Vendor() { }

    public Vendor(Guid organizationId, VendorDetails details, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        IsActive = true;
        CreatedAtUtc = nowUtc;
        Apply(details);
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = "";
    /// <summary>What they do, such as plumbing or electrical, in the organization's own words.</summary>
    public string? Trade { get; private set; }
    public string? ContactName { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }
    public string? Notes { get; private set; }
    /// <summary>Inactive vendors keep their history but can't be given new work.</summary>
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Update(VendorDetails details) => Apply(details);

    public void SetActive(bool active) => IsActive = active;

    private void Apply(VendorDetails d)
    {
        Name = Guard.Required(d.Name, "Name", 200);
        Trade = Guard.Optional(d.Trade, "Trade", 100);
        ContactName = Guard.Optional(d.ContactName, "Contact name", 200);
        Phone = Guard.Optional(d.Phone, "Phone", 30);
        Email = Guard.Optional(d.Email, "Email", 256);
        if (Email is not null && !Email.Contains('@')) throw new DomainException("Email is not valid.");
        Address = Guard.Optional(d.Address, "Address", 500);
        Notes = Guard.Optional(d.Notes, "Notes", 2000);
        if (Phone is null && Email is null) throw new DomainException("Add a phone number or email for the vendor.");
    }
}
