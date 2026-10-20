using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Listings;

namespace PropertyManagement.Application.Listings;

/// <summary>
/// Enquiries and viewings (design 6.12). A member sees the enquiries on the listings they can see, so an agent sees
/// only those on their own listings. Every action needs Leads.Manage.
/// </summary>
public sealed class LeadService(
    IAppDbContext db,
    ListingService listings,
    ICurrentOrganization currentOrganization,
    ICurrentUser currentUser,
    IUserDirectory users,
    IAuditLogger audit,
    IClock clock)
{
    private IQueryable<Lead> VisibleLeads()
    {
        var visible = listings.VisibleListings();
        return db.Leads.Where(l => visible.Any(x => x.Id == l.ListingId));
    }

    public async Task<IReadOnlyList<LeadSummaryDto>> SearchAsync(Guid? listingId, LeadStatus? status, bool openOnly, CancellationToken ct)
    {
        RequireLeads();
        var query = VisibleLeads();
        if (listingId is { } id) query = query.Where(l => l.ListingId == id);
        if (status is { } s) query = query.Where(l => l.Status == s);
        if (openOnly) query = query.Where(l => l.Status != LeadStatus.Won && l.Status != LeadStatus.Lost);
        var now = clock.UtcNow;
        var rows = await query.OrderByDescending(l => l.CreatedAtUtc).Take(500).Select(l => new
        {
            Lead = l,
            Listing = db.Listings.Where(x => x.Id == l.ListingId).Select(x => new
            {
                x.Headline,
                Property = db.Properties.Where(p => p.Id == x.PropertyId).Select(p => p.Name).First(),
                Unit = db.Units.Where(u => u.Id == x.UnitId).Select(u => u.Code).First(),
            }).First(),
            Next = l.Viewings.Where(v => v.Status == ViewingStatus.Scheduled && v.ScheduledAtUtc >= now.AddHours(-12))
                .OrderBy(v => v.ScheduledAtUtc).Select(v => (DateTime?)v.ScheduledAtUtc).FirstOrDefault(),
        }).ToListAsync(ct);
        return rows.Select(r => new LeadSummaryDto(r.Lead.Id, r.Lead.Reference, r.Lead.Name, r.Lead.Phone, r.Lead.Email, r.Lead.Source,
            r.Lead.Status, r.Lead.ListingId, r.Listing.Headline, r.Listing.Property, r.Listing.Unit, r.Next, r.Lead.CreatedAtUtc,
            r.Lead.UpdatedAtUtc)).ToList();
    }

    public async Task<LeadDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        RequireLeads();
        return await ToDetailAsync(await LoadAsync(id, ct), ct);
    }

    /// <summary>An enquiry taken by phone, walk-in or referral.</summary>
    public async Task<LeadDetailDto> CreateAsync(CreateLeadRequest request, CancellationToken ct)
    {
        RequireLeads();
        var listing = await listings.VisibleListings().SingleOrDefaultAsync(l => l.Id == request.ListingId, ct)
                      ?? throw new NotFoundException("Listing");
        if (listing.Status == ListingStatus.Closed) throw new DomainException("This listing is closed.", "listing_status");
        var lead = new Lead(currentOrganization.RequireOrganizationId(), listing.Id, await NextNumberAsync(db, ct), request.Name,
            request.Phone, request.Email, request.Message, request.Source, currentUser.RequireUserId(), clock.UtcNow);
        db.Leads.Add(lead);
        audit.Record("lead.created", nameof(Lead), lead.Id.ToString(), details: new { lead.Reference, lead.ListingId, lead.Source });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(lead, ct);
    }

    public async Task<LeadDetailDto> SetStatusAsync(Guid id, LeadStatusRequest request, CancellationToken ct)
    {
        RequireLeads();
        var lead = await LoadAsync(id, ct);
        var before = lead.Status;
        lead.SetStatus(request.Status, request.Notes, clock.UtcNow);
        audit.Record("lead.status_changed", nameof(Lead), lead.Id.ToString(), details: new { lead.Reference, From = before, To = lead.Status });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(lead, ct);
    }

    public async Task<LeadDetailDto> ScheduleViewingAsync(Guid id, ScheduleViewingRequest request, CancellationToken ct)
    {
        RequireLeads();
        var lead = await LoadAsync(id, ct);
        if (request.AgentUserId is { } agent && (await listings.AgentsAsync(ct)).All(a => a.UserId != agent))
            throw new DomainException("Choose an active member who handles listings.");
        var at = DateTime.SpecifyKind(request.ScheduledAtUtc, DateTimeKind.Utc);
        var viewing = lead.ScheduleViewing(at, request.AgentUserId ?? currentUser.RequireUserId(), request.Notes, clock.UtcNow);
        audit.Record("lead.viewing_booked", nameof(Lead), lead.Id.ToString(),
            details: new { lead.Reference, ViewingId = viewing.Id, viewing.ScheduledAtUtc, viewing.AgentUserId });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(lead, ct);
    }

    public async Task<LeadDetailDto> RecordViewingAsync(Guid id, Guid viewingId, RecordViewingRequest request, CancellationToken ct)
    {
        RequireLeads();
        var lead = await LoadAsync(id, ct);
        var viewing = lead.RecordViewing(viewingId, request.Status, request.Outcome, clock.UtcNow);
        audit.Record("lead.viewing_recorded", nameof(Lead), lead.Id.ToString(),
            details: new { lead.Reference, ViewingId = viewing.Id, viewing.Status });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(lead, ct);
    }

    public static async Task<int> NextNumberAsync(IAppDbContext db, CancellationToken ct) =>
        (await db.Leads.MaxAsync(l => (int?)l.Number, ct) ?? 0) + 1;

    private void RequireLeads()
    {
        if (!currentOrganization.HasPermission(Permissions.Listings.Leads)) throw new ForbiddenException("You can't manage enquiries.");
    }

    private async Task<Lead> LoadAsync(Guid id, CancellationToken ct) =>
        await VisibleLeads().Include(l => l.Viewings).SingleOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Enquiry");

    private async Task<LeadDetailDto> ToDetailAsync(Lead l, CancellationToken ct)
    {
        var listing = await db.Listings.Where(x => x.Id == l.ListingId).Select(x => new
        {
            x.Headline,
            Property = db.Properties.Where(p => p.Id == x.PropertyId).Select(p => p.Name).First(),
            Unit = db.Units.Where(u => u.Id == x.UnitId).Select(u => u.Code).First(),
        }).SingleAsync(ct);
        var people = await users.GetAsync(l.Viewings.Select(v => v.AgentUserId).OfType<Guid>().Distinct(), ct);
        return new LeadDetailDto(l.Id, l.Reference, l.Name, l.Phone, l.Email, l.Message, l.Source, l.Status, l.Notes, l.ListingId,
            listing.Headline, listing.Property, listing.Unit,
            l.Viewings.OrderBy(v => v.ScheduledAtUtc).Select(v => new ViewingDto(v.Id, v.ScheduledAtUtc, v.AgentUserId,
                v.AgentUserId is { } a ? people.GetValueOrDefault(a)?.FullName : null, v.Notes, v.Status, v.Outcome)).ToList(),
            l.CreatedAtUtc, l.UpdatedAtUtc);
    }
}
