using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Tenants;

/// <summary>Links a portal login to the tenant record it may see (design 4.6).</summary>
public class TenantUserLink : IOrganizationScoped
{
    private TenantUserLink() { }

    public TenantUserLink(Guid organizationId, Guid tenantId, Guid userId)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        TenantId = tenantId;
        UserId = userId;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}
