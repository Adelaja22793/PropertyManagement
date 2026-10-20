using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Tenants;

public sealed record GuarantorDetails(
    string FullName,
    string? Phone,
    string? Email,
    string? Address,
    string? Relationship,
    string? Employer,
    string? Occupation,
    bool SignedFormReceived);

/// <summary>
/// Someone who guarantees a tenant's obligations (design 6.4). Recorded against the tenant now;
/// Phase 4 links guarantors to specific leases.
/// </summary>
public class Guarantor : IOrganizationScoped
{
    private Guarantor() { }

    public Guarantor(Guid organizationId, Guid tenantId, GuarantorDetails details)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        TenantId = tenantId;
        CreatedAtUtc = DateTime.UtcNow;
        Apply(details);
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid TenantId { get; private set; }
    public string FullName { get; private set; } = "";
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }
    public string? Relationship { get; private set; }
    public string? Employer { get; private set; }
    public string? Occupation { get; private set; }
    public bool SignedFormReceived { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public void Update(GuarantorDetails details) => Apply(details);

    private void Apply(GuarantorDetails d)
    {
        FullName = Guard.Required(d.FullName, "Full name", 200);
        Phone = Contact.Phone(d.Phone);
        Email = Contact.Email(d.Email);
        Address = Guard.Optional(d.Address, "Address", 500);
        Relationship = Guard.Optional(d.Relationship, "Relationship", 100);
        Employer = Guard.Optional(d.Employer, "Employer", 200);
        Occupation = Guard.Optional(d.Occupation, "Occupation", 100);
        SignedFormReceived = d.SignedFormReceived;
        if (Phone is null && Email is null) throw new DomainException("Enter a phone number or email for the guarantor.");
    }
}
