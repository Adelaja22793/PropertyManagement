using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Owners;

/// <summary>Links a portal login to the owner record it may see (design 4.6).</summary>
public class OwnerUserLink : IOrganizationScoped
{
    private OwnerUserLink() { }

    public OwnerUserLink(Guid organizationId, Guid ownerId, Guid userId)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        OwnerId = ownerId;
        UserId = userId;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}
