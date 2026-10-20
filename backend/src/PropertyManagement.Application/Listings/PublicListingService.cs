using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Documents;
using PropertyManagement.Domain.Listings;
using PropertyManagement.Domain.Organizations;

namespace PropertyManagement.Application.Listings;

/// <summary>
/// The public site (design 6.12): published listings, their chosen photos and the enquiry form, for anyone, without
/// signing in. Each call first finds the organization from the public code or slug, then works inside that one
/// organization exactly as a signed-in request would, so the usual isolation rules still hold. Only fields built
/// into the public DTOs ever leave; drafts, closed listings and suspended companies answer "not found".
/// </summary>
public sealed class PublicListingService(
    IAppDbContext db,
    ICurrentOrganization currentOrganization,
    IUserDirectory users,
    IEmailSender email,
    IFileStore files,
    IAuditLogger audit,
    IClock clock)
{
    public async Task<PublicOrganizationDto> OrganizationAsync(string slug, CancellationToken ct)
    {
        var organization = await db.Organizations
            .SingleOrDefaultAsync(o => o.Slug == slug && o.Status == OrganizationStatus.Active, ct) ?? throw new NotFoundException("Page");
        currentOrganization.Set(organization.Id, null, [], []);
        var listings = await db.Listings.Where(l => l.Status == ListingStatus.Published)
            .OrderByDescending(l => l.PublishedAtUtc).Take(200).ToListAsync(ct);
        return new PublicOrganizationDto(organization.Name, organization.Slug, organization.ContactPhone, organization.ContactEmail,
            await SummariesAsync(listings, ct));
    }

    public async Task<PublicListingDto> ListingAsync(string code, CancellationToken ct)
    {
        var listing = await EnterAsync(code, ct);
        var place = await PlaceAsync(listing, ct);
        var organization = await db.Organizations.Where(o => o.Id == listing.OrganizationId)
            .Select(o => new { o.Name, o.Slug, o.ContactPhone }).SingleAsync(ct);
        var photos = await LivePhotoIdsAsync(listing, ct);
        return new PublicListingDto(listing.PublicCode, listing.Headline, listing.Description, listing.AskingRent, listing.Currency,
            listing.BillingFrequency, listing.AvailableFrom, place.Unit.Type, place.Unit.Bedrooms, place.Unit.Bathrooms,
            place.Unit.SizeSquareMetres,
            (listing.Features ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            listing.ShowStreetAddress ? place.AddressLine : null, place.Area,
            photos.Select(id => PhotoUrl(listing.PublicCode, id)).ToList(),
            organization.Name, organization.Slug, listing.ContactName, listing.ContactPhone ?? organization.ContactPhone);
    }

    /// <summary>A photo chosen for a published listing. Nothing else in storage can be reached this way.</summary>
    public async Task<(Stream Content, string ContentType)> PhotoAsync(string code, Guid documentId, CancellationToken ct)
    {
        var listing = await EnterAsync(code, ct);
        if (!(await LivePhotoIdsAsync(listing, ct)).Contains(documentId)) throw new NotFoundException("Photo");
        var document = await db.Documents.SingleAsync(d => d.Id == documentId, ct);
        var stream = await files.OpenReadAsync(document.StorageKey, ct) ?? throw new NotFoundException("Photo");
        return (stream, document.ContentType);
    }

    /// <summary>
    /// Records an enquiry and emails whoever handles the listing. Returns nothing about the organization's staff.
    /// A filled-in hidden field marks a bot: it is accepted and quietly dropped.
    /// </summary>
    public async Task EnquireAsync(string code, PublicEnquiryRequest request, CancellationToken ct)
    {
        var listing = await EnterAsync(code, ct);
        if (!string.IsNullOrWhiteSpace(request.Website)) return;
        var lead = new Lead(listing.OrganizationId, listing.Id, await LeadService.NextNumberAsync(db, ct), request.Name, request.Phone,
            request.Email, request.Message, LeadSource.Website, null, clock.UtcNow);
        db.Leads.Add(lead);
        audit.Record("lead.created", nameof(Lead), lead.Id.ToString(), details: new { lead.Reference, lead.ListingId, lead.Source });
        await db.SaveChangesAsync(ct);
        await NotifyAsync(listing, lead, ct);
    }

    private async Task<Listing> EnterAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 20) throw new NotFoundException("Listing");
        // The one query that looks across organizations: it finds the listing's owner, then everything else is scoped to it.
        var listing = await db.Listings.IgnoreQueryFilters().Include(l => l.Photos)
            .Where(l => l.PublicCode == code && l.Status == ListingStatus.Published)
            .Where(l => db.Organizations.Any(o => o.Id == l.OrganizationId && o.Status == OrganizationStatus.Active))
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Listing");
        currentOrganization.Set(listing.OrganizationId, null, [], []);
        return listing;
    }

    private async Task<List<Guid>> LivePhotoIdsAsync(Listing listing, CancellationToken ct)
    {
        var ids = listing.Photos.OrderBy(p => p.SortOrder).Select(p => p.DocumentId).ToList();
        var live = await LivePhotos(listing.PropertyId).Where(d => ids.Contains(d.Id)).Select(d => d.Id).ToListAsync(ct);
        return ids.Where(live.Contains).ToList();
    }

    /// <summary>Photos still filed on the property, so a photo deleted by staff disappears from the public site too.</summary>
    private IQueryable<Document> LivePhotos(Guid propertyId) =>
        db.Documents.Where(d => d.DeletedAtUtc == null && d.Subject == DocumentSubject.Property && d.SubjectId == propertyId
                                && d.ContentType.StartsWith("image/") && d.Category != DocumentCategory.Identity);

    private async Task<IReadOnlyList<PublicListingSummaryDto>> SummariesAsync(List<Listing> listings, CancellationToken ct)
    {
        var ids = listings.Select(l => l.Id).ToList();
        var photos = await db.Listings.Where(l => ids.Contains(l.Id)).SelectMany(l => l.Photos)
            .Join(db.Documents.Where(d => d.DeletedAtUtc == null && d.ContentType.StartsWith("image/") && d.Category != DocumentCategory.Identity),
                p => p.DocumentId, d => d.Id, (p, d) => new { p.ListingId, p.DocumentId, p.SortOrder, d.SubjectId, d.Subject })
            .ToListAsync(ct);
        var result = new List<PublicListingSummaryDto>();
        foreach (var l in listings)
        {
            var place = await PlaceAsync(l, ct);
            var first = photos.Where(p => p.ListingId == l.Id && p.Subject == DocumentSubject.Property && p.SubjectId == l.PropertyId)
                .OrderBy(p => p.SortOrder).FirstOrDefault();
            result.Add(new PublicListingSummaryDto(l.PublicCode, l.Headline, l.AskingRent, l.Currency, l.BillingFrequency, place.Unit.Type,
                place.Unit.Bedrooms, place.Unit.Bathrooms, place.Area, l.AvailableFrom,
                first is null ? null : PhotoUrl(l.PublicCode, first.DocumentId)));
        }
        return result;
    }

    private sealed record Place(Domain.Properties.PropertyUnit Unit, string AddressLine, string Area);

    private async Task<Place> PlaceAsync(Listing listing, CancellationToken ct)
    {
        var unit = await db.Units.SingleAsync(u => u.Id == listing.UnitId, ct);
        var property = await db.Properties.Where(p => p.Id == listing.PropertyId)
            .Select(p => new { p.AddressLine, p.City, p.LocalGovernmentArea, p.State }).SingleAsync(ct);
        var area = string.Join(", ", new[] { property.LocalGovernmentArea ?? property.City, property.State }.Where(x => !string.IsNullOrEmpty(x)));
        return new Place(unit, property.AddressLine, area);
    }

    private static string PhotoUrl(string code, Guid documentId) => $"/api/v1/public/listings/{code}/photos/{documentId}";

    /// <summary>Emails the listing's agent, else the property's manager, else the organization admins.</summary>
    private async Task NotifyAsync(Listing listing, Lead lead, CancellationToken ct)
    {
        var members = await db.Memberships.Where(m => m.Status == MembershipStatus.Active)
            .Select(m => new { m.UserId, Roles = m.Roles.Select(r => r.RoleName).ToList() }).ToListAsync(ct);
        var manager = await db.Properties.Where(p => p.Id == listing.PropertyId).Select(p => p.ManagerUserId).SingleAsync(ct);
        List<Guid> ids =
            listing.AgentUserId is { } agent && members.Any(m => m.UserId == agent) ? [agent]
            : manager is { } m1 && members.Any(m => m.UserId == m1) ? [m1]
            : members.Where(m => m.Roles.Contains(Roles.OrganizationAdmin)).Select(m => m.UserId).ToList();
        var people = await users.GetAsync(ids, ct);
        var organization = await db.Organizations.Where(o => o.Id == listing.OrganizationId).Select(o => o.Name).SingleAsync(ct);
        var contact = string.Join(" · ", new[] { lead.Phone, lead.Email }.OfType<string>());
        var body = $"New enquiry {lead.Reference} about \"{listing.Headline}\".\n\nFrom: {lead.Name}\nContact: {contact}\n" +
                   (lead.Message is null ? "" : $"\n{lead.Message}\n") + $"\n{organization}";
        foreach (var address in people.Values.Select(p => p.Email).Distinct())
            await email.SendAsync(address, $"{organization}: new enquiry for {listing.Headline}", body, ct);
    }
}
