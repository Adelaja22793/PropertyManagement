using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Leases;
using PropertyManagement.Domain.Maintenance;

namespace PropertyManagement.Application.Portal;

/// <summary>
/// What a portal user is linked to: the owner or tenant records their login was invited for, and from those the
/// properties, leases and repairs they may see. Every portal query starts here.
/// </summary>
public sealed class PortalScope(IAppDbContext db, ICurrentOrganization currentOrganization, ICurrentUser currentUser, IClock clock)
{
    public bool IsOwner => currentOrganization.Roles.Contains(Roles.Owner);
    public bool IsTenant => currentOrganization.Roles.Contains(Roles.Tenant);

    public async Task<List<Guid>> OwnerIdsAsync(CancellationToken ct)
    {
        RequireRole(Roles.Owner);
        var userId = currentUser.RequireUserId();
        var ids = await db.OwnerUserLinks.Where(l => l.UserId == userId).Select(l => l.OwnerId).ToListAsync(ct);
        return ids.Count > 0 ? ids : throw new ForbiddenException("Your login isn't linked to an owner record. Ask your property manager.");
    }

    public async Task<List<Guid>> TenantIdsAsync(CancellationToken ct)
    {
        RequireRole(Roles.Tenant);
        var userId = currentUser.RequireUserId();
        var ids = await db.TenantUserLinks.Where(l => l.UserId == userId).Select(l => l.TenantId).ToListAsync(ct);
        return ids.Count > 0 ? ids : throw new ForbiddenException("Your login isn't linked to a tenant record. Ask your property manager.");
    }

    /// <summary>Properties the owners currently hold a share of.</summary>
    public async Task<List<Guid>> OwnerPropertyIdsAsync(List<Guid> ownerIds, CancellationToken ct)
    {
        var today = clock.TodayInNigeria();
        return await db.PropertyOwnerships
            .Where(o => ownerIds.Contains(o.OwnerId) && o.EffectiveFrom <= today && (o.EffectiveTo == null || o.EffectiveTo >= today))
            .Select(o => o.PropertyId).Distinct().ToListAsync(ct);
    }

    public IQueryable<MaintenanceRequest> OwnerRequests(List<Guid> propertyIds) =>
        db.MaintenanceRequests.Where(r => propertyIds.Contains(r.PropertyId));

    public IQueryable<MaintenanceRequest> TenantRequests(List<Guid> tenantIds) =>
        db.MaintenanceRequests.Where(r => r.TenantId != null && tenantIds.Contains(r.TenantId.Value));

    /// <summary>Leases the tenant is party to, leaving out drafts and cancelled drafts that never took effect.</summary>
    public IQueryable<Lease> TenantLeases(List<Guid> tenantIds) =>
        db.Leases.Where(l => l.Status != LeaseStatus.Draft && l.Status != LeaseStatus.Cancelled
                             && db.LeaseTenants.Any(t => t.LeaseId == l.Id && tenantIds.Contains(t.TenantId)));

    private void RequireRole(string role)
    {
        if (!currentOrganization.Roles.Contains(role)) throw new ForbiddenException("This portal isn't available to you.");
    }
}
