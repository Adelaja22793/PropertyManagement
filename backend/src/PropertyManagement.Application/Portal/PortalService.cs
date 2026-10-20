using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Leases;
using PropertyManagement.Domain.Maintenance;
using PropertyManagement.Domain.Organizations;

namespace PropertyManagement.Application.Portal;

/// <summary>
/// The owner and tenant portals (design 4.6, 10.3 and 10.4). Every query starts from the caller's own owner or tenant
/// link: an owner reaches only properties they currently hold a share of, and a tenant only leases they are party to
/// and the repairs reported for them. Staff-only notes on maintenance are never shown; a portal user sees the status
/// steps and their own messages.
/// </summary>
public sealed class PortalService(
    IAppDbContext db,
    ICurrentOrganization currentOrganization,
    ICurrentUser currentUser,
    IUserDirectory users,
    IEmailSender email,
    IAuditLogger audit,
    IClock clock,
    PortalScope scope)
{
    // Owner portal

    public async Task<OwnerPortalDto> OwnerHomeAsync(CancellationToken ct)
    {
        var ownerIds = await OwnerIdsAsync(ct);
        var today = clock.TodayInNigeria();
        var shares = await db.PropertyOwnerships
            .Where(o => ownerIds.Contains(o.OwnerId) && o.EffectiveFrom <= today && (o.EffectiveTo == null || o.EffectiveTo >= today))
            .GroupBy(o => o.PropertyId).Select(g => new { PropertyId = g.Key, Share = g.Sum(o => o.SharePercent) })
            .ToListAsync(ct);
        var propertyIds = shares.Select(s => s.PropertyId).ToList();
        var properties = await db.Properties.Where(p => propertyIds.Contains(p.Id)).OrderBy(p => p.Name).ToListAsync(ct);
        var units = await db.Units.Where(u => propertyIds.Contains(u.PropertyId) && !u.IsArchived).OrderBy(u => u.Code).ToListAsync(ct);
        var leases = await db.Leases.Where(l => propertyIds.Contains(l.PropertyId) && l.Status == LeaseStatus.Active)
            .Select(l => new
            {
                Lease = l,
                TenantName = db.LeaseTenants.Where(t => t.LeaseId == l.Id && t.IsPrimary)
                    .Join(db.Tenants, lt => lt.TenantId, t => t.Id, (lt, t) => t.Name).FirstOrDefault(),
            }).ToListAsync(ct);

        var result = properties.Select(p => new PortalPropertyDto(p.Id, p.Code, p.Name, p.AddressLine, p.City, p.State,
            shares.First(s => s.PropertyId == p.Id).Share,
            units.Where(u => u.PropertyId == p.Id).Select(u =>
            {
                var current = leases.Where(x => x.Lease.UnitId == u.Id).OrderBy(x => x.Lease.StartDate > today)
                    .ThenByDescending(x => x.Lease.StartDate).FirstOrDefault();
                return new PortalUnitDto(u.Code, u.Type, u.Bedrooms, u.OccupancyStatus, current is null ? null
                    : new PortalLeaseBriefDto(current.Lease.Reference, current.TenantName, current.Lease.RentAmount,
                        current.Lease.Currency, current.Lease.BillingFrequency, current.Lease.StartDate, current.Lease.EndDate,
                        current.Lease.StageOn(today)));
            }).ToList())).ToList();

        var requests = OwnerRequests(propertyIds);
        var open = await requests.CountAsync(r => r.Status != MaintenanceStatus.Completed && r.Status != MaintenanceStatus.Rejected
                                                  && r.Status != MaintenanceStatus.Cancelled, ct);
        var awaiting = (await requests.Where(r => r.Status != MaintenanceStatus.Completed && r.Status != MaintenanceStatus.Rejected
                                                  && r.Status != MaintenanceStatus.Cancelled && r.EstimateAmount != null
                                                  && r.ApprovalLimit != null && r.OwnerApprovalRecordedAtUtc == null)
            .ToListAsync(ct)).Count(r => r.NeedsOwnerApproval);
        var ownerName = await db.Owners.Where(o => o.Id == ownerIds[0]).Select(o => o.Name).SingleAsync(ct);
        return new OwnerPortalDto(ownerName, await OrganizationNameAsync(ct), result, open, awaiting);
    }

    public async Task<IReadOnlyList<PortalMaintenanceSummaryDto>> OwnerMaintenanceAsync(CancellationToken ct) =>
        await SummariesAsync(OwnerRequests(await OwnerPropertyIdsAsync(ct)), ct);

    public async Task<PortalMaintenanceDetailDto> OwnerMaintenanceDetailAsync(Guid id, CancellationToken ct) =>
        ToDetail(await LoadOwnerRequestAsync(id, ct));

    /// <summary>Records the owner's approval of an estimate above their repair limit, from the portal.</summary>
    public async Task<PortalMaintenanceDetailDto> OwnerApproveAsync(Guid id, PortalApprovalRequest request, CancellationToken ct)
    {
        var item = await LoadOwnerRequestAsync(id, ct);
        var name = await MyNameAsync(ct);
        var note = string.IsNullOrWhiteSpace(request.Note) ? "" : $": {request.Note.Trim()}";
        item.RecordOwnerApproval($"Approved by {name} in the owner portal{note}", currentUser.RequireUserId(), clock.UtcNow);
        audit.Record("maintenance.owner_approved", nameof(MaintenanceRequest), item.Id.ToString(),
            details: new { item.Reference, item.EstimateAmount, Via = "owner portal" });
        await db.SaveChangesAsync(ct);
        await NotifyStaffAsync(item.PropertyId, $"{item.Reference}: owner approved the estimate",
            $"{name} approved the estimate of {item.EstimateAmount:N2} for {item.Reference} ({item.Title}) in the owner portal{note}.", ct);
        return ToDetail(item);
    }

    /// <summary>Tells the managers the owner doesn't approve the estimate. Staff decide the next step.</summary>
    public async Task<PortalMaintenanceDetailDto> OwnerDeclineAsync(Guid id, PortalDeclineRequest request, CancellationToken ct)
    {
        var item = await LoadOwnerRequestAsync(id, ct);
        if (!(item.IsOpen && item.NeedsOwnerApproval && item.OwnerApprovalRecordedAtUtc is null))
            throw new DomainException("This request isn't waiting for your approval.", "maintenance_status");
        var reason = Guard.Required(request.Reason, "Reason", 1000);
        var name = await MyNameAsync(ct);
        item.Comment($"Owner declined the estimate: {reason}", currentUser.RequireUserId(), clock.UtcNow);
        audit.Record("maintenance.owner_declined", nameof(MaintenanceRequest), item.Id.ToString(), details: new { item.Reference, reason });
        await db.SaveChangesAsync(ct);
        await NotifyStaffAsync(item.PropertyId, $"{item.Reference}: owner declined the estimate",
            $"{name} declined the estimate of {item.EstimateAmount:N2} for {item.Reference} ({item.Title}).\n\nReason: {reason}", ct);
        return ToDetail(item);
    }

    // Tenant portal

    public async Task<TenantPortalDto> TenantHomeAsync(CancellationToken ct)
    {
        var tenantIds = await TenantIdsAsync(ct);
        var today = clock.TodayInNigeria();
        var leases = await TenantLeases(tenantIds).OrderByDescending(l => l.StartDate).ToListAsync(ct);
        var leaseIds = leases.Select(l => l.Id).ToList();
        var places = await db.Units.Where(u => leases.Select(l => l.UnitId).Contains(u.Id))
            .Select(u => new { u.Id, u.Code, Property = db.Properties.Where(p => p.Id == u.PropertyId)
                .Select(p => new { p.Name, p.AddressLine, p.City, p.State }).First() })
            .ToListAsync(ct);
        var parties = await db.LeaseTenants.Where(t => leaseIds.Contains(t.LeaseId))
            .Join(db.Tenants, lt => lt.TenantId, t => t.Id, (lt, t) => new { lt.LeaseId, lt.IsPrimary, t.Name })
            .ToListAsync(ct);
        var charges = await db.LeaseCharges.Where(c => leaseIds.Contains(c.LeaseId)).ToListAsync(ct);
        var notices = await db.LeaseNotices.Where(n => leaseIds.Contains(n.LeaseId) && n.Status == NoticeStatus.Served)
            .OrderByDescending(n => n.ServedOn).ToListAsync(ct);

        var result = leases.Select(l =>
        {
            var place = places.First(p => p.Id == l.UnitId);
            return new PortalLeaseDto(l.Id, l.Reference, place.Property.Name, place.Property.AddressLine, place.Property.City,
                place.Property.State, l.UnitId, place.Code, l.Status, l.StageOn(today), l.StartDate, l.EndDate, l.RentAmount,
                l.Currency, l.BillingFrequency, l.DueDaysBeforePeriod, l.GraceDays, l.NoticePeriodDays, l.RenewalTerms, l.MoveOutDate,
                parties.Where(p => p.LeaseId == l.Id).OrderByDescending(p => p.IsPrimary).ThenBy(p => p.Name).Select(p => p.Name).ToList(),
                charges.Where(c => c.LeaseId == l.Id).Select(c => new PortalChargeDto(c.Type, c.Description, c.Amount, c.Recurrence)).ToList(),
                notices.Where(n => n.LeaseId == l.Id).Select(n => new PortalNoticeDto(n.Type, n.ServedOn, n.ExpiresOn, n.Notes)).ToList());
        }).ToList();
        var tenantName = await db.Tenants.Where(t => t.Id == tenantIds[0]).Select(t => t.Name).SingleAsync(ct);
        return new TenantPortalDto(tenantName, await OrganizationNameAsync(ct), result);
    }

    public async Task<IReadOnlyList<PortalMaintenanceSummaryDto>> TenantMaintenanceAsync(CancellationToken ct) =>
        await SummariesAsync(TenantRequests(await TenantIdsAsync(ct)), ct);

    public async Task<PortalMaintenanceDetailDto> TenantMaintenanceDetailAsync(Guid id, CancellationToken ct) =>
        ToDetail(await LoadTenantRequestAsync(id, ct));

    /// <summary>A tenant reports a repair on a unit they currently rent. The property's managers are emailed.</summary>
    public async Task<PortalMaintenanceDetailDto> TenantReportAsync(PortalRepairRequest request, CancellationToken ct)
    {
        var tenantIds = await TenantIdsAsync(ct);
        var lease = await TenantLeases(tenantIds).SingleOrDefaultAsync(l => l.Id == request.LeaseId, ct)
                    ?? throw new NotFoundException("Lease");
        if (lease.Status != LeaseStatus.Active)
            throw new DomainException("Repairs can be reported on your current lease only.", "lease_not_active");
        var tenantId = await db.LeaseTenants.Where(t => t.LeaseId == lease.Id && tenantIds.Contains(t.TenantId))
            .Select(t => t.TenantId).FirstAsync(ct);
        var name = await MyNameAsync(ct);
        var number = (await db.MaintenanceRequests.MaxAsync(r => (int?)r.Number, ct) ?? 0) + 1;
        var item = new MaintenanceRequest(currentOrganization.RequireOrganizationId(), number, lease.PropertyId,
            new MaintenanceDetails(lease.UnitId, tenantId, $"{name} (tenant portal)", request.Title, request.Description,
                request.Category, request.Urgency),
            currentUser.RequireUserId(), clock.UtcNow);
        db.MaintenanceRequests.Add(item);
        audit.Record("maintenance.created", nameof(MaintenanceRequest), item.Id.ToString(),
            details: new { item.Reference, item.Title, item.Category, item.Urgency, Via = "tenant portal" });
        await db.SaveChangesAsync(ct);
        var unit = await db.Units.Where(u => u.Id == lease.UnitId).Select(u => u.Code).FirstAsync(ct);
        await NotifyStaffAsync(item.PropertyId,
            $"{(item.Urgency == MaintenanceUrgency.Emergency ? "EMERGENCY " : "")}{item.Reference} reported by a tenant",
            $"{name} reported a repair on unit {unit} through the tenant portal.\n\n{item.Title}\nUrgency: {item.Urgency}\n" +
            (item.Description is null ? "" : $"\n{item.Description}\n"), ct);
        return ToDetail(item);
    }

    public async Task<PortalMaintenanceDetailDto> TenantCommentAsync(Guid id, PortalCommentRequest request, CancellationToken ct)
    {
        var item = await LoadTenantRequestAsync(id, ct);
        item.Comment(request.Text, currentUser.RequireUserId(), clock.UtcNow);
        audit.Record("maintenance.commented", nameof(MaintenanceRequest), item.Id.ToString(), details: new { item.Reference, Via = "tenant portal" });
        await db.SaveChangesAsync(ct);
        return ToDetail(item);
    }

    // Shared

    private Task<List<Guid>> OwnerIdsAsync(CancellationToken ct) => scope.OwnerIdsAsync(ct);

    private Task<List<Guid>> TenantIdsAsync(CancellationToken ct) => scope.TenantIdsAsync(ct);

    private async Task<List<Guid>> OwnerPropertyIdsAsync(CancellationToken ct) => await scope.OwnerPropertyIdsAsync(await OwnerIdsAsync(ct), ct);

    private IQueryable<MaintenanceRequest> OwnerRequests(List<Guid> propertyIds) => scope.OwnerRequests(propertyIds);

    private IQueryable<MaintenanceRequest> TenantRequests(List<Guid> tenantIds) => scope.TenantRequests(tenantIds);

    private IQueryable<Lease> TenantLeases(List<Guid> tenantIds) => scope.TenantLeases(tenantIds);

    private async Task<MaintenanceRequest> LoadOwnerRequestAsync(Guid id, CancellationToken ct) =>
        await OwnerRequests(await OwnerPropertyIdsAsync(ct)).Include(r => r.Updates).SingleOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new NotFoundException("Maintenance request");

    private async Task<MaintenanceRequest> LoadTenantRequestAsync(Guid id, CancellationToken ct) =>
        await TenantRequests(await TenantIdsAsync(ct)).Include(r => r.Updates).SingleOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new NotFoundException("Maintenance request");

    private async Task<IReadOnlyList<PortalMaintenanceSummaryDto>> SummariesAsync(IQueryable<MaintenanceRequest> query, CancellationToken ct)
    {
        var rows = await query.OrderByDescending(r => r.Number).Take(200).Select(r => new
        {
            Request = r,
            PropertyName = db.Properties.Where(p => p.Id == r.PropertyId).Select(p => p.Name).First(),
            UnitCode = db.Units.Where(u => u.Id == r.UnitId).Select(u => u.Code).FirstOrDefault(),
        }).ToListAsync(ct);
        return rows.Select(x => new PortalMaintenanceSummaryDto(x.Request.Id, x.Request.Reference, x.Request.Title,
            x.Request.Category, x.Request.Urgency, x.Request.Status, x.PropertyName, x.UnitCode, x.Request.EstimateAmount,
            AwaitingOwner(x.Request), x.Request.CreatedAtUtc, x.Request.UpdatedAtUtc)).ToList();
    }

    private PortalMaintenanceDetailDto ToDetail(MaintenanceRequest r)
    {
        var place = db.Properties.Where(p => p.Id == r.PropertyId).Select(p => new
        {
            p.Name,
            Unit = db.Units.Where(u => u.Id == r.UnitId).Select(u => u.Code).FirstOrDefault(),
            Vendor = db.Vendors.Where(v => v.Id == r.VendorId).Select(v => v.Name).FirstOrDefault(),
        }).Single();
        var me = currentUser.UserId;
        // Staff comments are internal; portal users see the steps and their own messages.
        var updates = r.Updates.Where(u => u.Kind != MaintenanceUpdateKind.Comment || u.ByUserId == me)
            .OrderBy(u => u.Sequence)
            .Select(u => new PortalMaintenanceUpdateDto(u.Kind, u.Kind == MaintenanceUpdateKind.Assigned ? null : u.Text, u.Status,
                u.ByUserId == me, u.AtUtc))
            .ToList();
        return new PortalMaintenanceDetailDto(r.Id, r.Reference, r.Title, r.Description, r.Category, r.Urgency, r.Status,
            place.Name, place.Unit, place.Vendor, r.EstimateAmount, r.EstimateNotes, r.ApprovalLimit, AwaitingOwner(r),
            r.OwnerApprovalNote, r.CompletedOn, r.ActualCost, r.CompletionNotes, r.ClosedReason, r.CreatedAtUtc, updates);
    }

    private static bool AwaitingOwner(MaintenanceRequest r) => r.IsOpen && r.NeedsOwnerApproval && r.OwnerApprovalRecordedAtUtc is null;

    private async Task<string> OrganizationNameAsync(CancellationToken ct)
    {
        var id = currentOrganization.RequireOrganizationId();
        return await db.Organizations.Where(o => o.Id == id).Select(o => o.Name).SingleAsync(ct);
    }

    private async Task<string> MyNameAsync(CancellationToken ct)
    {
        var id = currentUser.RequireUserId();
        return (await users.GetAsync([id], ct)).GetValueOrDefault(id)?.FullName ?? "The client";
    }

    /// <summary>Emails the property's manager, or the organization admins when it has no active manager.</summary>
    private async Task NotifyStaffAsync(Guid propertyId, string subject, string body, CancellationToken ct)
    {
        var managerId = await db.Properties.Where(p => p.Id == propertyId).Select(p => p.ManagerUserId).SingleAsync(ct);
        var members = await db.Memberships.Where(m => m.Status == MembershipStatus.Active)
            .Select(m => new { m.UserId, Roles = m.Roles.Select(x => x.RoleName).ToList() }).ToListAsync(ct);
        var ids = managerId is { } manager && members.Any(m => m.UserId == manager)
            ? [manager]
            : members.Where(m => m.Roles.Contains(Roles.OrganizationAdmin)).Select(m => m.UserId).ToList();
        var people = await users.GetAsync(ids, ct);
        var organization = await OrganizationNameAsync(ct);
        foreach (var address in people.Values.Select(p => p.Email).Distinct())
            await email.SendAsync(address, $"{organization}: {subject}", $"{body}\n\n{organization}", ct);
    }
}
