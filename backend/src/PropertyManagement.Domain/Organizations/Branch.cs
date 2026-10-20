using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Organizations;

/// <summary>An office within an organization. Staff access can be limited to branches.</summary>
public class Branch : IOrganizationScoped
{
    private Branch() { }

    public Branch(Guid organizationId, string name, string? address)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Branch name is required.");
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        Name = name.Trim();
        Address = address?.Trim();
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = "";
    public string? Address { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Update(string name, string? address, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Branch name is required.");
        Name = name.Trim();
        Address = address?.Trim();
        IsActive = isActive;
    }
}
