using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Domain.Properties;

namespace PropertyManagement.Application.Properties;

/// <summary>
/// Which properties the caller may see (design 5, "default scope"). Organization-wide roles see all of them;
/// members limited to branches see those branches' properties; a member whose only property role is
/// Property Manager sees just the properties assigned to them. Units follow their property.
/// </summary>
public sealed class PropertyAccess(IAppDbContext db, ICurrentOrganization currentOrganization, ICurrentUser currentUser)
{
    public IQueryable<Property> VisibleProperties()
    {
        IQueryable<Property> query = db.Properties;
        var roles = currentOrganization.Roles;
        if (roles.Overlaps(Roles.OrganizationWideRoles)) return query;

        var branches = currentOrganization.BranchScope;
        if (branches.Count > 0)
            query = query.Where(p => p.BranchId != null && branches.Contains(p.BranchId.Value));

        var widerRoles = roles.Where(r => !Roles.AssignedPropertyRoles.Contains(r))
            .Any(r => Roles.PermissionsFor([r]).Contains(Permissions.Properties.View));
        if (!widerRoles)
        {
            var userId = currentUser.RequireUserId();
            query = query.Where(p => p.ManagerUserId == userId);
        }
        return query;
    }

    public IQueryable<PropertyUnit> VisibleUnits()
    {
        var properties = VisibleProperties();
        return db.Units.Where(u => properties.Any(p => p.Id == u.PropertyId));
    }

    /// <summary>True when the caller is limited to some branches, so new properties must go in one of them.</summary>
    public bool IsBranchLimited(out IReadOnlySet<Guid> branches)
    {
        branches = currentOrganization.BranchScope;
        return branches.Count > 0 && !currentOrganization.Roles.Overlaps(Roles.OrganizationWideRoles);
    }
}
