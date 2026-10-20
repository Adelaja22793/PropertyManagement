using PropertyManagement.Domain.Listings;
using PropertyManagement.Domain.Properties;

namespace PropertyManagement.Application.Listings;

public sealed record ListingSummaryDto(
    Guid Id,
    string PublicCode,
    string Headline,
    ListingStatus Status,
    ListingCloseReason? CloseReason,
    Guid PropertyId,
    string PropertyName,
    Guid UnitId,
    string UnitCode,
    decimal AskingRent,
    string Currency,
    BillingFrequency BillingFrequency,
    int PhotoCount,
    int OpenLeads,
    string? AgentName,
    DateTime? PublishedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ListingPhotoDto(Guid DocumentId, string Title);

public sealed record ListingDetailDto(
    Guid Id,
    string PublicCode,
    string OrganizationSlug,
    string Headline,
    string? Description,
    decimal AskingRent,
    string Currency,
    BillingFrequency BillingFrequency,
    DateOnly? AvailableFrom,
    string? Features,
    bool ShowStreetAddress,
    string? ContactName,
    string? ContactPhone,
    Guid? AgentUserId,
    string? AgentName,
    ListingStatus Status,
    ListingCloseReason? CloseReason,
    DateTime? PublishedAtUtc,
    DateTime? ClosedAtUtc,
    Guid PropertyId,
    string PropertyName,
    string PropertyAddress,
    Guid UnitId,
    string UnitCode,
    UnitType UnitType,
    int? Bedrooms,
    int? Bathrooms,
    OccupancyStatus UnitOccupancy,
    IReadOnlyList<ListingPhotoDto> Photos,
    int OpenLeads,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record SaveListingRequest(
    Guid UnitId,
    string Headline,
    string? Description,
    decimal AskingRent,
    string? Currency,
    BillingFrequency BillingFrequency,
    DateOnly? AvailableFrom,
    string? Features,
    bool ShowStreetAddress,
    string? ContactName,
    string? ContactPhone,
    Guid? AgentUserId)
{
    public ListingDetails ToDetails() => new(Headline, Description, AskingRent, Currency, BillingFrequency, AvailableFrom, Features,
        ShowStreetAddress, ContactName, ContactPhone, AgentUserId);
}

public sealed record ListingPhotosRequest(IReadOnlyList<Guid> DocumentIds);

public sealed record CloseListingRequest(ListingCloseReason Reason);

/// <summary>A photo of the property that can be added to a listing.</summary>
public sealed record ListingPhotoOptionDto(Guid DocumentId, string Title, DateTime CreatedAtUtc);

public sealed record ListingAgentDto(Guid UserId, string FullName, string Email);

// Leads

public sealed record LeadSummaryDto(
    Guid Id,
    string Reference,
    string Name,
    string? Phone,
    string? Email,
    LeadSource Source,
    LeadStatus Status,
    Guid ListingId,
    string ListingHeadline,
    string PropertyName,
    string UnitCode,
    DateTime? NextViewingAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ViewingDto(
    Guid Id, DateTime ScheduledAtUtc, Guid? AgentUserId, string? AgentName, string? Notes, ViewingStatus Status, string? Outcome);

public sealed record LeadDetailDto(
    Guid Id,
    string Reference,
    string Name,
    string? Phone,
    string? Email,
    string? Message,
    LeadSource Source,
    LeadStatus Status,
    string? Notes,
    Guid ListingId,
    string ListingHeadline,
    string PropertyName,
    string UnitCode,
    IReadOnlyList<ViewingDto> Viewings,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateLeadRequest(Guid ListingId, string Name, string? Phone, string? Email, string? Message, LeadSource Source);

public sealed record LeadStatusRequest(LeadStatus Status, string? Notes);

public sealed record ScheduleViewingRequest(DateTime ScheduledAtUtc, Guid? AgentUserId, string? Notes);

public sealed record RecordViewingRequest(ViewingStatus Status, string? Outcome);

// Public site: only fields meant for the public, built explicitly so nothing private leaks by accident.

public sealed record PublicListingSummaryDto(
    string Code,
    string Headline,
    decimal AskingRent,
    string Currency,
    BillingFrequency BillingFrequency,
    UnitType UnitType,
    int? Bedrooms,
    int? Bathrooms,
    string Area,
    DateOnly? AvailableFrom,
    string? PhotoUrl);

public sealed record PublicOrganizationDto(string Name, string Slug, string? ContactPhone, string? ContactEmail, IReadOnlyList<PublicListingSummaryDto> Listings);

public sealed record PublicListingDto(
    string Code,
    string Headline,
    string? Description,
    decimal AskingRent,
    string Currency,
    BillingFrequency BillingFrequency,
    DateOnly? AvailableFrom,
    UnitType UnitType,
    int? Bedrooms,
    int? Bathrooms,
    decimal? SizeSquareMetres,
    IReadOnlyList<string> Features,
    string? StreetAddress,
    string Area,
    IReadOnlyList<string> PhotoUrls,
    string OrganizationName,
    string OrganizationSlug,
    string? ContactName,
    string? ContactPhone);

/// <summary>The public enquiry form. <see cref="Website"/> is a hidden field people never fill in; bots do.</summary>
public sealed record PublicEnquiryRequest(string Name, string? Phone, string? Email, string? Message, string? Website);
