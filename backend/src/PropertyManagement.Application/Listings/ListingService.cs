using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Application.Properties;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Documents;
using PropertyManagement.Domain.Listings;
using PropertyManagement.Domain.Organizations;

namespace PropertyManagement.Application.Listings;

/// <summary>
/// Listings of vacant units (design 6.12). Staff see listings on the properties they can see; a member whose only
/// listings role is Agent sees just the listings assigned to them (design 5, "assigned listings"). Photos come from
/// the property's own photos, so nothing reaches the public site that wasn't already filed and virus-scanned.
/// </summary>
public sealed class ListingService(
    IAppDbContext db,
    PropertyAccess access,
    ICurrentOrganization currentOrganization,
    ICurrentUser currentUser,
    IUserDirectory users,
    IAuditLogger audit,
    IClock clock)
{
    /// <summary>True when the member's listings rights come only from the Agent role.</summary>
    private bool AgentOnly =>
        !currentOrganization.Roles.Where(r => r != Roles.Agent).Any(r =>
        {
            var granted = Roles.PermissionsFor([r]);
            return granted.Contains(Permissions.Listings.Manage) || granted.Contains(Permissions.Listings.Leads);
        });

    public IQueryable<Listing> VisibleListings()
    {
        var properties = access.VisibleProperties();
        var query = db.Listings.Where(l => properties.Any(p => p.Id == l.PropertyId));
        if (AgentOnly)
        {
            var userId = currentUser.RequireUserId();
            query = query.Where(l => l.AgentUserId == userId);
        }
        return query;
    }

    public async Task<IReadOnlyList<ListingSummaryDto>> SearchAsync(ListingStatus? status, Guid? propertyId, Guid? unitId, CancellationToken ct)
    {
        var query = VisibleListings();
        if (status is { } s) query = query.Where(l => l.Status == s);
        if (propertyId is { } p) query = query.Where(l => l.PropertyId == p);
        if (unitId is { } u) query = query.Where(l => l.UnitId == u);
        var rows = await query.OrderBy(l => l.Status).ThenByDescending(l => l.UpdatedAtUtc).Take(500).Select(l => new
        {
            Listing = l,
            PhotoCount = l.Photos.Count,
            Property = db.Properties.Where(p => p.Id == l.PropertyId).Select(p => p.Name).First(),
            Unit = db.Units.Where(x => x.Id == l.UnitId).Select(x => x.Code).First(),
            OpenLeads = db.Leads.Count(x => x.ListingId == l.Id && x.Status != LeadStatus.Won && x.Status != LeadStatus.Lost),
        }).ToListAsync(ct);
        var people = await users.GetAsync(rows.Select(r => r.Listing.AgentUserId).OfType<Guid>().Distinct(), ct);
        return rows.Select(r => new ListingSummaryDto(r.Listing.Id, r.Listing.PublicCode, r.Listing.Headline, r.Listing.Status,
            r.Listing.CloseReason, r.Listing.PropertyId, r.Property, r.Listing.UnitId, r.Unit, r.Listing.AskingRent, r.Listing.Currency,
            r.Listing.BillingFrequency, r.PhotoCount, r.OpenLeads,
            r.Listing.AgentUserId is { } a ? people.GetValueOrDefault(a)?.FullName : null, r.Listing.PublishedAtUtc,
            r.Listing.UpdatedAtUtc)).ToList();
    }

    public async Task<ListingDetailDto> GetAsync(Guid id, CancellationToken ct) => await ToDetailAsync(await LoadAsync(id, ct), ct);

    public async Task<ListingDetailDto> CreateAsync(SaveListingRequest request, CancellationToken ct)
    {
        RequireManage();
        var unit = await access.VisibleUnits().SingleOrDefaultAsync(u => u.Id == request.UnitId, ct) ?? throw new NotFoundException("Unit");
        if (unit.IsArchived) throw new DomainException("This unit is archived.", "archived");
        await EnsureNoOpenListingAsync(unit.Id, null, ct);
        var details = request.ToDetails();
        // An agent's own listing is theirs to handle unless they name someone else.
        if (details.AgentUserId is null && AgentOnly) details = details with { AgentUserId = currentUser.RequireUserId() };
        await CheckAgentAsync(details.AgentUserId, ct);
        var listing = new Listing(currentOrganization.RequireOrganizationId(), unit.PropertyId, unit.Id, details,
            currentUser.RequireUserId(), clock.UtcNow);
        db.Listings.Add(listing);
        audit.Record("listing.created", nameof(Listing), listing.Id.ToString(),
            details: new { listing.Headline, listing.UnitId, listing.AskingRent, listing.AgentUserId });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(listing, ct);
    }

    public async Task<ListingDetailDto> UpdateAsync(Guid id, SaveListingRequest request, CancellationToken ct)
    {
        var listing = await LoadAsync(id, ct);
        RequireManage();
        if (request.UnitId != listing.UnitId) throw new DomainException("A listing's unit can't be changed. Create a new listing instead.");
        if (AgentOnly && request.AgentUserId != listing.AgentUserId)
            throw new ForbiddenException("Only a manager can reassign a listing.");
        await CheckAgentAsync(request.AgentUserId, ct);
        listing.Update(request.ToDetails(), clock.UtcNow);
        audit.Record("listing.updated", nameof(Listing), listing.Id.ToString(),
            details: new { listing.Headline, listing.AskingRent, listing.AgentUserId, listing.ShowStreetAddress });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(listing, ct);
    }

    /// <summary>The property's photos that can go on the listing.</summary>
    public async Task<IReadOnlyList<ListingPhotoOptionDto>> PhotoOptionsAsync(Guid id, CancellationToken ct)
    {
        var listing = await LoadAsync(id, ct);
        return await PropertyPhotos(listing.PropertyId).OrderByDescending(d => d.CreatedAtUtc)
            .Select(d => new ListingPhotoOptionDto(d.Id, d.Title, d.CreatedAtUtc)).ToListAsync(ct);
    }

    public async Task<ListingDetailDto> SetPhotosAsync(Guid id, ListingPhotosRequest request, CancellationToken ct)
    {
        var listing = await LoadAsync(id, ct);
        RequireManage();
        var ids = request.DocumentIds ?? [];
        var allowed = await PropertyPhotos(listing.PropertyId).Where(d => ids.Contains(d.Id)).Select(d => d.Id).ToListAsync(ct);
        if (ids.Any(i => !allowed.Contains(i)))
            throw new DomainException("Choose photos filed on this property. Add them under the property's documents first.");
        listing.SetPhotos(ids, clock.UtcNow);
        if (listing.IsLive && ids.Count == 0) listing.Unpublish(clock.UtcNow);
        audit.Record("listing.photos_changed", nameof(Listing), listing.Id.ToString(), details: new { Photos = ids });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(listing, ct);
    }

    public Task<ListingDetailDto> PublishAsync(Guid id, CancellationToken ct) =>
        ActAsync(id, "listing.published", l => l.Publish(clock.UtcNow), ct);

    public Task<ListingDetailDto> UnpublishAsync(Guid id, CancellationToken ct) =>
        ActAsync(id, "listing.unpublished", l => l.Unpublish(clock.UtcNow), ct);

    public Task<ListingDetailDto> CloseAsync(Guid id, CloseListingRequest request, CancellationToken ct) =>
        ActAsync(id, "listing.closed", l => l.Close(request.Reason, clock.UtcNow), ct);

    public async Task<ListingDetailDto> ReopenAsync(Guid id, CancellationToken ct)
    {
        var listing = await LoadAsync(id, ct);
        RequireManage();
        await EnsureNoOpenListingAsync(listing.UnitId, listing.Id, ct);
        listing.Reopen(clock.UtcNow);
        audit.Record("listing.reopened", nameof(Listing), listing.Id.ToString(), details: new { listing.Headline });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(listing, ct);
    }

    /// <summary>Active members who can be put in charge of a listing.</summary>
    public async Task<IReadOnlyList<ListingAgentDto>> AgentsAsync(CancellationToken ct)
    {
        var members = await db.Memberships.Where(m => m.Status == MembershipStatus.Active)
            .Select(m => new { m.UserId, Roles = m.Roles.Select(r => r.RoleName).ToList() }).ToListAsync(ct);
        var ids = members.Where(m => Roles.PermissionsFor(m.Roles).Contains(Permissions.Listings.Leads)).Select(m => m.UserId).ToList();
        var people = await users.GetAsync(ids, ct);
        return people.Values.OrderBy(p => p.FullName).Select(p => new ListingAgentDto(p.Id, p.FullName, p.Email)).ToList();
    }

    /// <summary>Called when a lease takes a unit: open listings for it close as let.</summary>
    public static async Task CloseForLetUnitAsync(IAppDbContext db, Guid unitId, DateTime nowUtc, CancellationToken ct)
    {
        var open = await db.Listings.Where(l => l.UnitId == unitId && l.Status != ListingStatus.Closed).ToListAsync(ct);
        foreach (var listing in open) listing.Close(ListingCloseReason.Let, nowUtc);
    }

    private IQueryable<Document> PropertyPhotos(Guid propertyId) =>
        db.Documents.Where(d => d.DeletedAtUtc == null && d.Subject == DocumentSubject.Property && d.SubjectId == propertyId
                                && d.ContentType.StartsWith("image/") && d.Category != DocumentCategory.Identity);

    private async Task<ListingDetailDto> ActAsync(Guid id, string action, Action<Listing> change, CancellationToken ct)
    {
        var listing = await LoadAsync(id, ct);
        RequireManage();
        var before = listing.Status;
        change(listing);
        audit.Record(action, nameof(Listing), listing.Id.ToString(),
            details: new { listing.Headline, From = before, To = listing.Status, listing.CloseReason });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(listing, ct);
    }

    private async Task EnsureNoOpenListingAsync(Guid unitId, Guid? except, CancellationToken ct)
    {
        if (await db.Listings.AnyAsync(l => l.UnitId == unitId && l.Status != ListingStatus.Closed && l.Id != except, ct))
            throw new DomainException("This unit already has an open listing.", "listing_exists");
    }

    private async Task CheckAgentAsync(Guid? agentUserId, CancellationToken ct)
    {
        if (agentUserId is not { } id) return;
        if ((await AgentsAsync(ct)).All(a => a.UserId != id))
            throw new DomainException("Choose an active member who handles listings.");
    }

    private void RequireManage()
    {
        if (!currentOrganization.HasPermission(Permissions.Listings.Manage)) throw new ForbiddenException("You can't manage listings.");
    }

    private async Task<Listing> LoadAsync(Guid id, CancellationToken ct) =>
        await VisibleListings().Include(l => l.Photos).SingleOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Listing");

    private async Task<ListingDetailDto> ToDetailAsync(Listing l, CancellationToken ct)
    {
        var place = await db.Units.Where(u => u.Id == l.UnitId).Select(u => new
        {
            Unit = u,
            Property = db.Properties.Where(p => p.Id == u.PropertyId).Select(p => new { p.Name, p.AddressLine, p.City, p.State }).First(),
            Slug = db.Organizations.Where(o => o.Id == u.OrganizationId).Select(o => o.Slug).First(),
        }).SingleAsync(ct);
        var photoIds = l.Photos.OrderBy(p => p.SortOrder).Select(p => p.DocumentId).ToList();
        var titles = await db.Documents.Where(d => photoIds.Contains(d.Id) && d.DeletedAtUtc == null).ToDictionaryAsync(d => d.Id, d => d.Title, ct);
        var openLeads = await db.Leads.CountAsync(x => x.ListingId == l.Id && x.Status != LeadStatus.Won && x.Status != LeadStatus.Lost, ct);
        var agent = l.AgentUserId is { } a ? (await users.GetAsync([a], ct)).GetValueOrDefault(a)?.FullName : null;
        var address = string.Join(", ", new[] { place.Property.AddressLine, place.Property.City, place.Property.State }.Where(x => !string.IsNullOrEmpty(x)));
        return new ListingDetailDto(l.Id, l.PublicCode, place.Slug, l.Headline, l.Description, l.AskingRent, l.Currency, l.BillingFrequency,
            l.AvailableFrom, l.Features, l.ShowStreetAddress, l.ContactName, l.ContactPhone, l.AgentUserId, agent, l.Status, l.CloseReason,
            l.PublishedAtUtc, l.ClosedAtUtc, l.PropertyId, place.Property.Name, address, l.UnitId, place.Unit.Code, place.Unit.Type,
            place.Unit.Bedrooms, place.Unit.Bathrooms, place.Unit.OccupancyStatus,
            photoIds.Where(titles.ContainsKey).Select(id => new ListingPhotoDto(id, titles[id])).ToList(),
            openLeads, l.CreatedAtUtc, l.UpdatedAtUtc);
    }
}
