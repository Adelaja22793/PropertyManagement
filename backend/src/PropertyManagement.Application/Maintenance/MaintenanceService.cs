using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Application.Properties;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Maintenance;
using PropertyManagement.Domain.Organizations;

namespace PropertyManagement.Application.Maintenance;

/// <summary>
/// Maintenance requests (design 6.11). Members with Maintenance.View see requests on the properties they can see;
/// members with only Maintenance.UpdateAssigned (maintenance staff) see and work on just the jobs assigned to them.
/// Reviewing, estimating, approving, assigning, rejecting and cancelling need Maintenance.Manage.
/// </summary>
public sealed partial class MaintenanceService(
    IAppDbContext db,
    PropertyAccess access,
    ICurrentOrganization currentOrganization,
    ICurrentUser currentUser,
    IUserDirectory users,
    IEmailSender email,
    IAuditLogger audit,
    IClock clock)
{
    private bool CanManage => currentOrganization.HasPermission(Permissions.Maintenance.Manage);

    public IQueryable<MaintenanceRequest> VisibleRequests()
    {
        var userId = currentUser.RequireUserId();
        if (!currentOrganization.HasPermission(Permissions.Maintenance.View))
            return db.MaintenanceRequests.Where(r => r.AssignedUserId == userId);
        var properties = access.VisibleProperties();
        return db.MaintenanceRequests.Where(r => properties.Any(p => p.Id == r.PropertyId) || r.AssignedUserId == userId);
    }

    public async Task<PagedResult<MaintenanceSummaryDto>> SearchAsync(MaintenanceSearch search, PageRequest page, CancellationToken ct)
    {
        var query = VisibleRequests();
        if (search.AssignedToMe)
        {
            var userId = currentUser.RequireUserId();
            query = query.Where(r => r.AssignedUserId == userId);
        }
        if (search.Status is { } status) query = query.Where(r => r.Status == status);
        if (search.OpenOnly == true)
            query = query.Where(r => r.Status != MaintenanceStatus.Completed && r.Status != MaintenanceStatus.Rejected
                                     && r.Status != MaintenanceStatus.Cancelled);
        if (search.Urgency is { } urgency) query = query.Where(r => r.Urgency == urgency);
        if (search.PropertyId is { } propertyId) query = query.Where(r => r.PropertyId == propertyId);
        if (search.UnitId is { } unitId) query = query.Where(r => r.UnitId == unitId);
        if (search.VendorId is { } vendorId) query = query.Where(r => r.VendorId == vendorId);
        if (!string.IsNullOrWhiteSpace(search.Search))
        {
            var term = search.Search.Trim();
            var match = ReferencePattern().Match(term);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var number))
                query = query.Where(r => r.Number == number);
            else
                query = query.Where(r => r.Title.Contains(term) || (r.Description != null && r.Description.Contains(term)));
        }

        var total = await query.CountAsync(ct);
        var ordered = search.OpenOnly == true
            ? query.OrderBy(r => r.Urgency).ThenBy(r => r.CreatedAtUtc)
            : query.OrderByDescending(r => r.Number);
        var rows = await ordered.Skip(page.Skip).Take(page.SafePageSize).Select(r => new
        {
            Request = r,
            Property = db.Properties.Where(p => p.Id == r.PropertyId).Select(p => new { p.Code, p.Name }).First(),
            UnitCode = db.Units.Where(u => u.Id == r.UnitId).Select(u => u.Code).FirstOrDefault(),
            VendorName = db.Vendors.Where(v => v.Id == r.VendorId).Select(v => v.Name).FirstOrDefault(),
        }).ToListAsync(ct);
        var people = await users.GetAsync(rows.Select(x => x.Request.AssignedUserId).OfType<Guid>().Distinct(), ct);
        var items = rows.Select(x => new MaintenanceSummaryDto(x.Request.Id, x.Request.Reference, x.Request.Title,
            x.Request.Category, x.Request.Urgency, x.Request.Status, x.Request.PropertyId, x.Property.Code, x.Property.Name,
            x.Request.UnitId, x.UnitCode,
            x.Request.AssignedUserId is { } a ? people.GetValueOrDefault(a)?.FullName : null, x.VendorName,
            x.Request.EstimateAmount, AwaitingOwner(x.Request), x.Request.CreatedAtUtc, x.Request.UpdatedAtUtc)).ToList();
        return new PagedResult<MaintenanceSummaryDto>(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<MaintenanceDetailDto> GetAsync(Guid id, CancellationToken ct) => await ToDetailAsync(await LoadAsync(id, ct), ct);

    public async Task<MaintenanceDetailDto> CreateAsync(SaveMaintenanceRequest request, CancellationToken ct)
    {
        RequireManage();
        await CheckLinksAsync(request, ct);
        var number = (await db.MaintenanceRequests.MaxAsync(r => (int?)r.Number, ct) ?? 0) + 1;
        var item = new MaintenanceRequest(currentOrganization.RequireOrganizationId(), number, request.PropertyId,
            request.ToDetails(), currentUser.RequireUserId(), clock.UtcNow);
        db.MaintenanceRequests.Add(item);
        audit.Record("maintenance.created", nameof(MaintenanceRequest), item.Id.ToString(),
            details: new { item.Reference, item.Title, item.Category, item.Urgency, item.PropertyId, item.UnitId });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(item, ct);
    }

    public async Task<MaintenanceDetailDto> UpdateAsync(Guid id, SaveMaintenanceRequest request, CancellationToken ct)
    {
        var item = await LoadAsync(id, ct);
        RequireManage();
        if (request.PropertyId != item.PropertyId) throw new DomainException("A request's property can't be changed.");
        await CheckLinksAsync(request, ct);
        item.Update(request.ToDetails(), currentUser.RequireUserId(), clock.UtcNow);
        audit.Record("maintenance.updated", nameof(MaintenanceRequest), item.Id.ToString(),
            details: new { item.Reference, item.Title, item.Category, item.Urgency, item.UnitId });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(item, ct);
    }

    public Task<MaintenanceDetailDto> CommentAsync(Guid id, CommentRequest request, CancellationToken ct) =>
        ActAsync(id, work: true, "maintenance.commented", (r, user, now) => r.Comment(request.Text, user, now), ct);

    public Task<MaintenanceDetailDto> ReviewAsync(Guid id, CancellationToken ct) =>
        ActAsync(id, work: false, "maintenance.reviewing", (r, user, now) => r.Review(user, now), ct);

    public async Task<MaintenanceDetailDto> EstimateAsync(Guid id, EstimateRequest request, CancellationToken ct)
    {
        var item = await LoadAsync(id, ct);
        RequireManage();
        var limit = await ApprovalLimitForAsync(item.PropertyId, ct);
        item.Estimate(request.Amount, request.Notes, limit, request.OwnerApprovalNote, currentUser.RequireUserId(), clock.UtcNow);
        audit.Record("maintenance.estimated", nameof(MaintenanceRequest), item.Id.ToString(), details: new
        {
            item.Reference, item.EstimateAmount, item.ApprovalLimit, item.NeedsOwnerApproval, OwnerApproved = item.OwnerApprovalNote is not null,
        });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(item, ct);
    }

    public Task<MaintenanceDetailDto> RecordOwnerApprovalAsync(Guid id, OwnerApprovalRequest request, CancellationToken ct) =>
        ActAsync(id, work: false, "maintenance.owner_approved", (r, user, now) => r.RecordOwnerApproval(request.Note, user, now), ct);

    public Task<MaintenanceDetailDto> ApproveAsync(Guid id, CancellationToken ct) =>
        ActAsync(id, work: false, "maintenance.approved", (r, user, now) => r.Approve(user, now), ct);

    public async Task<MaintenanceDetailDto> AssignAsync(Guid id, AssignRequest request, CancellationToken ct)
    {
        var item = await LoadAsync(id, ct);
        RequireManage();
        UserSummary? person = null;
        if (request.AssignedUserId is { } assigneeId)
        {
            var assignees = await AssigneesAsync(ct);
            if (assignees.All(a => a.UserId != assigneeId))
                throw new DomainException("Assign the job to an active member who handles maintenance.");
            person = (await users.GetAsync([assigneeId], ct)).GetValueOrDefault(assigneeId);
        }
        string? vendorName = null;
        if (request.VendorId is { } vendorId)
        {
            var vendor = await db.Vendors.SingleOrDefaultAsync(v => v.Id == vendorId, ct) ?? throw new NotFoundException("Vendor");
            if (!vendor.IsActive) throw new DomainException("This vendor is inactive. Reactivate them first.");
            vendorName = vendor.Name;
        }
        var previousAssignee = item.AssignedUserId;
        var description = string.Join(" and ", new[] { person?.FullName, vendorName }.OfType<string>());
        item.Assign(request.AssignedUserId, request.VendorId, $"Assigned to {description}", currentUser.RequireUserId(), clock.UtcNow);
        audit.Record("maintenance.assigned", nameof(MaintenanceRequest), item.Id.ToString(),
            details: new { item.Reference, item.AssignedUserId, item.VendorId });
        await db.SaveChangesAsync(ct);

        if (person is not null && person.Id != previousAssignee && person.Id != currentUser.UserId)
            await NotifyAssigneeAsync(item, person, ct);
        return await ToDetailAsync(item, ct);
    }

    public Task<MaintenanceDetailDto> StartAsync(Guid id, CancellationToken ct) =>
        ActAsync(id, work: true, "maintenance.started", (r, user, now) => r.Start(user, now), ct);

    public Task<MaintenanceDetailDto> AwaitPartsAsync(Guid id, NoteRequest request, CancellationToken ct) =>
        ActAsync(id, work: true, "maintenance.awaiting_parts", (r, user, now) => r.AwaitParts(request.Note, user, now), ct);

    public Task<MaintenanceDetailDto> CompleteAsync(Guid id, CompleteRequest request, CancellationToken ct)
    {
        if (request.CompletedOn > clock.TodayInNigeria()) throw new DomainException("The completion date can't be in the future.");
        return ActAsync(id, work: true, "maintenance.completed",
            (r, user, now) => r.Complete(request.CompletedOn, request.ActualCost, request.Notes, user, now), ct);
    }

    public Task<MaintenanceDetailDto> RejectAsync(Guid id, ReasonRequest request, CancellationToken ct) =>
        ActAsync(id, work: false, "maintenance.rejected", (r, user, now) => r.Reject(request.Reason, user, now), ct);

    public Task<MaintenanceDetailDto> CancelAsync(Guid id, ReasonRequest request, CancellationToken ct) =>
        ActAsync(id, work: false, "maintenance.cancelled", (r, user, now) => r.Cancel(request.Reason, user, now), ct);

    public Task<MaintenanceDetailDto> FeedbackAsync(Guid id, FeedbackRequest request, CancellationToken ct) =>
        ActAsync(id, work: false, "maintenance.feedback", (r, user, now) => r.RecordFeedback(request.Rating, request.Comment, user, now), ct);

    /// <summary>Active members who can be given maintenance jobs.</summary>
    public async Task<IReadOnlyList<AssigneeDto>> AssigneesAsync(CancellationToken ct)
    {
        var members = await db.Memberships.Where(m => m.Status == MembershipStatus.Active)
            .Select(m => new { m.UserId, Roles = m.Roles.Select(r => r.RoleName).ToList() }).ToListAsync(ct);
        var ids = members.Where(m =>
        {
            var granted = Roles.PermissionsFor(m.Roles);
            return granted.Contains(Permissions.Maintenance.Manage) || granted.Contains(Permissions.Maintenance.UpdateAssigned);
        }).Select(m => m.UserId).ToList();
        var people = await users.GetAsync(ids, ct);
        return people.Values.OrderBy(p => p.FullName).Select(p => new AssigneeDto(p.Id, p.FullName, p.Email)).ToList();
    }

    public async Task<ApprovalLimitDto> ApprovalLimitAsync(Guid propertyId, CancellationToken ct)
    {
        if (!await access.VisibleProperties().AnyAsync(p => p.Id == propertyId, ct)) throw new NotFoundException("Property");
        return new ApprovalLimitDto(await ApprovalLimitForAsync(propertyId, ct));
    }

    /// <summary>The lowest repair approval limit among the agreements of the property's current owners, or null for none.</summary>
    private async Task<decimal?> ApprovalLimitForAsync(Guid propertyId, CancellationToken ct)
    {
        var today = clock.TodayInNigeria();
        var owners = await db.PropertyOwnerships
            .Where(o => o.PropertyId == propertyId && o.EffectiveFrom <= today && (o.EffectiveTo == null || o.EffectiveTo >= today))
            .Select(o => o.OwnerId).ToListAsync(ct);
        if (owners.Count == 0) return null;
        return await db.ManagementAgreements
            .Where(a => owners.Contains(a.OwnerId) && (a.PropertyId == null || a.PropertyId == propertyId)
                        && a.StartDate <= today && (a.EndDate == null || a.EndDate >= today) && a.RepairApprovalLimit != null)
            .MinAsync(a => a.RepairApprovalLimit, ct);
    }

    private async Task<MaintenanceDetailDto> ActAsync(Guid id, bool work, string action,
        Action<MaintenanceRequest, Guid, DateTime> change, CancellationToken ct)
    {
        var item = await LoadAsync(id, ct);
        if (work) RequireWork(item); else RequireManage();
        var before = item.Status;
        change(item, currentUser.RequireUserId(), clock.UtcNow);
        audit.Record(action, nameof(MaintenanceRequest), item.Id.ToString(),
            details: new { item.Reference, From = before, To = item.Status });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(item, ct);
    }

    private async Task CheckLinksAsync(SaveMaintenanceRequest request, CancellationToken ct)
    {
        var property = await access.VisibleProperties().SingleOrDefaultAsync(p => p.Id == request.PropertyId, ct)
                       ?? throw new NotFoundException("Property");
        if (property.IsArchived) throw new DomainException("This property is archived.", "archived");
        if (request.UnitId is { } unitId && !await db.Units.AnyAsync(u => u.Id == unitId && u.PropertyId == property.Id, ct))
            throw new DomainException("The unit isn't part of this property.");
        if (request.TenantId is { } tenantId && !await db.Tenants.AnyAsync(t => t.Id == tenantId, ct))
            throw new NotFoundException("Tenant");
    }

    private async Task<MaintenanceRequest> LoadAsync(Guid id, CancellationToken ct) =>
        await VisibleRequests().Include(r => r.Updates).SingleOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new NotFoundException("Maintenance request");

    private void RequireManage()
    {
        if (!CanManage) throw new ForbiddenException("You can't manage maintenance requests.");
    }

    private bool CanWork(MaintenanceRequest item) =>
        CanManage || (currentOrganization.HasPermission(Permissions.Maintenance.UpdateAssigned)
                      && item.AssignedUserId == currentUser.UserId);

    private void RequireWork(MaintenanceRequest item)
    {
        if (!CanWork(item)) throw new ForbiddenException("Only the person assigned to this job, or a maintenance manager, can update it.");
    }

    private static bool AwaitingOwner(MaintenanceRequest r) => r.IsOpen && r.NeedsOwnerApproval && r.OwnerApprovalRecordedAtUtc is null;

    private async Task NotifyAssigneeAsync(MaintenanceRequest item, UserSummary person, CancellationToken ct)
    {
        var details = await db.Properties.Where(p => p.Id == item.PropertyId).Select(p => new
        {
            p.Name,
            Unit = db.Units.Where(u => u.Id == item.UnitId).Select(u => u.Code).FirstOrDefault(),
            Organization = db.Organizations.Where(o => o.Id == p.OrganizationId).Select(o => o.Name).First(),
        }).SingleAsync(ct);
        var place = details.Unit is null ? details.Name : $"{details.Name}, unit {details.Unit}";
        var body = $"You have been assigned maintenance request {item.Reference}: {item.Title}.\n\n" +
                   $"Property: {place}\nUrgency: {item.Urgency}\n" +
                   (item.Description is null ? "" : $"\n{item.Description}\n") +
                   $"\n{details.Organization}";
        await email.SendAsync(person.Email, $"{details.Organization}: {item.Reference} assigned to you", body, ct);
    }

    private async Task<MaintenanceDetailDto> ToDetailAsync(MaintenanceRequest r, CancellationToken ct)
    {
        var links = await db.Properties.Where(p => p.Id == r.PropertyId).Select(p => new
        {
            p.Code,
            p.Name,
            UnitCode = db.Units.Where(u => u.Id == r.UnitId).Select(u => u.Code).FirstOrDefault(),
            TenantName = db.Tenants.Where(t => t.Id == r.TenantId).Select(t => t.Name).FirstOrDefault(),
            Vendor = db.Vendors.Where(v => v.Id == r.VendorId).Select(v => new { v.Name, v.Phone }).FirstOrDefault(),
        }).SingleAsync(ct);
        var ids = r.Updates.Select(u => u.ByUserId)
            .Concat(new[] { r.AssignedUserId, r.ApprovedByUserId, (Guid?)r.CreatedByUserId }.OfType<Guid>()).Distinct();
        var people = await users.GetAsync(ids, ct);
        string? Name(Guid? id) => id is { } x ? people.GetValueOrDefault(x)?.FullName : null;

        return new MaintenanceDetailDto(r.Id, r.Reference, r.Title, r.Description, r.Category, r.Urgency, r.Status,
            r.PropertyId, links.Code, links.Name, r.UnitId, links.UnitCode, r.TenantId, links.TenantName, r.ReportedBy,
            r.AssignedUserId, Name(r.AssignedUserId), r.VendorId, links.Vendor?.Name, links.Vendor?.Phone,
            r.EstimateAmount, r.EstimateNotes, r.ApprovalLimit, r.NeedsOwnerApproval, r.OwnerApprovalNote, r.OwnerApprovalRecordedAtUtc,
            Name(r.ApprovedByUserId), r.ApprovedAtUtc, r.CompletedOn, r.ActualCost, r.CompletionNotes, r.ClosedReason,
            r.TenantRating, r.TenantFeedback, Name(r.CreatedByUserId), r.CreatedAtUtc, r.UpdatedAtUtc,
            r.Updates.OrderBy(u => u.Sequence).Select(u => new MaintenanceUpdateDto(u.Id, u.Kind, u.Text, u.FromStatus, u.Status,
                Name(u.ByUserId), u.AtUtc)).ToList(),
            new MaintenanceActionsDto(CanManage, CanWork(r)));
    }

    [GeneratedRegex(@"^(?:MNT-?)?0*(\d{1,9})$", RegexOptions.IgnoreCase)]
    private static partial Regex ReferencePattern();
}
