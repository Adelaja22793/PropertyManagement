namespace PropertyManagement.Domain.Common;

/// <summary>
/// Marks an entity that belongs to exactly one organization. Such entities are
/// automatically filtered to the active organization and cannot be written
/// for any other organization.
/// </summary>
public interface IOrganizationScoped
{
    Guid OrganizationId { get; }
}
