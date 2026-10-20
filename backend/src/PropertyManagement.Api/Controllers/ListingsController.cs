using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using PropertyManagement.Api.Authorization;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Listings;
using PropertyManagement.Domain.Listings;

namespace PropertyManagement.Api.Controllers;

/// <summary>Listings of vacant units (design 6.12).</summary>
[ApiController]
[Route("api/v1/listings")]
[RequireAnyPermission(Permissions.Listings.Manage, Permissions.Listings.Leads)]
public sealed class ListingsController(ListingService listings) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ListingSummaryDto>> Search([FromQuery] ListingStatus? status, [FromQuery] Guid? propertyId,
        [FromQuery] Guid? unitId, CancellationToken ct) => listings.SearchAsync(status, propertyId, unitId, ct);

    [HttpGet("agents")]
    public Task<IReadOnlyList<ListingAgentDto>> Agents(CancellationToken ct) => listings.AgentsAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<ListingDetailDto> Get(Guid id, CancellationToken ct) => listings.GetAsync(id, ct);

    [HttpPost]
    [RequirePermission(Permissions.Listings.Manage)]
    public async Task<ActionResult<ListingDetailDto>> Create(SaveListingRequest request, CancellationToken ct)
    {
        var created = await listings.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Listings.Manage)]
    public Task<ListingDetailDto> Update(Guid id, SaveListingRequest request, CancellationToken ct) => listings.UpdateAsync(id, request, ct);

    [HttpGet("{id:guid}/photo-options")]
    public Task<IReadOnlyList<ListingPhotoOptionDto>> PhotoOptions(Guid id, CancellationToken ct) => listings.PhotoOptionsAsync(id, ct);

    [HttpPut("{id:guid}/photos")]
    [RequirePermission(Permissions.Listings.Manage)]
    public Task<ListingDetailDto> Photos(Guid id, ListingPhotosRequest request, CancellationToken ct) => listings.SetPhotosAsync(id, request, ct);

    [HttpPost("{id:guid}/publish")]
    [RequirePermission(Permissions.Listings.Manage)]
    public Task<ListingDetailDto> Publish(Guid id, CancellationToken ct) => listings.PublishAsync(id, ct);

    [HttpPost("{id:guid}/unpublish")]
    [RequirePermission(Permissions.Listings.Manage)]
    public Task<ListingDetailDto> Unpublish(Guid id, CancellationToken ct) => listings.UnpublishAsync(id, ct);

    [HttpPost("{id:guid}/close")]
    [RequirePermission(Permissions.Listings.Manage)]
    public Task<ListingDetailDto> Close(Guid id, CloseListingRequest request, CancellationToken ct) => listings.CloseAsync(id, request, ct);

    [HttpPost("{id:guid}/reopen")]
    [RequirePermission(Permissions.Listings.Manage)]
    public Task<ListingDetailDto> Reopen(Guid id, CancellationToken ct) => listings.ReopenAsync(id, ct);
}

/// <summary>Enquiries and viewings (design 6.12).</summary>
[ApiController]
[Route("api/v1/leads")]
[RequirePermission(Permissions.Listings.Leads)]
public sealed class LeadsController(LeadService leads) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<LeadSummaryDto>> Search([FromQuery] Guid? listingId, [FromQuery] LeadStatus? status,
        [FromQuery] bool open = false, CancellationToken ct = default) => leads.SearchAsync(listingId, status, open, ct);

    [HttpGet("{id:guid}")]
    public Task<LeadDetailDto> Get(Guid id, CancellationToken ct) => leads.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<LeadDetailDto>> Create(CreateLeadRequest request, CancellationToken ct)
    {
        var created = await leads.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}/status")]
    public Task<LeadDetailDto> Status(Guid id, LeadStatusRequest request, CancellationToken ct) => leads.SetStatusAsync(id, request, ct);

    [HttpPost("{id:guid}/viewings")]
    public Task<LeadDetailDto> Schedule(Guid id, ScheduleViewingRequest request, CancellationToken ct) =>
        leads.ScheduleViewingAsync(id, request, ct);

    [HttpPost("{id:guid}/viewings/{viewingId:guid}/outcome")]
    public Task<LeadDetailDto> Outcome(Guid id, Guid viewingId, RecordViewingRequest request, CancellationToken ct) =>
        leads.RecordViewingAsync(id, viewingId, request, ct);
}

/// <summary>
/// The public site: no sign-in, rate-limited per client address, and only the fields in the public DTOs. Nothing here
/// takes an organization header; the listing code or company slug decides which organization is read.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/public")]
[EnableRateLimiting(Api.RateLimitPolicies.Public)]
public sealed class PublicListingsController(PublicListingService site) : ControllerBase
{
    [HttpGet("organizations/{slug}/listings")]
    public Task<PublicOrganizationDto> Organization(string slug, CancellationToken ct) => site.OrganizationAsync(slug, ct);

    [HttpGet("listings/{code}")]
    public Task<PublicListingDto> Listing(string code, CancellationToken ct) => site.ListingAsync(code, ct);

    [HttpGet("listings/{code}/photos/{documentId:guid}")]
    public async Task<IActionResult> Photo(string code, Guid documentId, CancellationToken ct)
    {
        var (content, contentType) = await site.PhotoAsync(code, documentId, ct);
        Response.Headers[HeaderNames.CacheControl] = "public, max-age=300";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        return File(content, contentType);
    }

    [HttpPost("listings/{code}/enquiries")]
    [EnableRateLimiting(Api.RateLimitPolicies.Enquiry)]
    public async Task<IActionResult> Enquire(string code, PublicEnquiryRequest request, CancellationToken ct)
    {
        await site.EnquireAsync(code, request, ct);
        return Accepted();
    }
}
