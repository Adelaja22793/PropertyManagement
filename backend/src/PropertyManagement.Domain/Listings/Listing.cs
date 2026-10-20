using System.Security.Cryptography;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Properties;

namespace PropertyManagement.Domain.Listings;

public enum ListingStatus { Draft, Published, Closed }

public enum ListingCloseReason { Let, Withdrawn }

public sealed record ListingDetails(
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
    Guid? AgentUserId);

/// <summary>
/// A vacant unit advertised to the public (design 6.12). Only what is on this record, the unit's size and the photos
/// staff picked are ever shown publicly; the public sees a listing only while it is published.
/// </summary>
public class Listing : IOrganizationScoped
{
    public const int MaxPhotos = 20;
    private const string CodeAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    private readonly List<ListingPhoto> _photos = [];

    private Listing() { }

    public Listing(Guid organizationId, Guid propertyId, Guid unitId, ListingDetails details, Guid userId, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        PropertyId = propertyId;
        UnitId = unitId;
        PublicCode = NewCode();
        Status = ListingStatus.Draft;
        CreatedByUserId = userId;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        Apply(details);
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid PropertyId { get; private set; }
    public Guid UnitId { get; private set; }
    /// <summary>The short code in the public address. Random, so listings can't be enumerated.</summary>
    public string PublicCode { get; private set; } = "";
    public string Headline { get; private set; } = "";
    public string? Description { get; private set; }
    public decimal AskingRent { get; private set; }
    public string Currency { get; private set; } = "NGN";
    public BillingFrequency BillingFrequency { get; private set; }
    public DateOnly? AvailableFrom { get; private set; }
    /// <summary>One feature per line, such as "Prepaid meter" or "24-hour security".</summary>
    public string? Features { get; private set; }
    /// <summary>When false the public sees only the area, city and state.</summary>
    public bool ShowStreetAddress { get; private set; }
    public string? ContactName { get; private set; }
    public string? ContactPhone { get; private set; }
    /// <summary>The agent who handles enquiries. Agents see only the listings assigned to them.</summary>
    public Guid? AgentUserId { get; private set; }
    public ListingStatus Status { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public ListingCloseReason? CloseReason { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public IReadOnlyList<ListingPhoto> Photos => _photos;

    public bool IsLive => Status == ListingStatus.Published;

    public void Update(ListingDetails details, DateTime nowUtc)
    {
        if (Status == ListingStatus.Closed) throw new DomainException("This listing is closed. Reopen it to make changes.", "listing_status");
        Apply(details);
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Replaces the photos with these documents, in this order. The caller checks they are the property's photos.</summary>
    public void SetPhotos(IReadOnlyList<Guid> documentIds, DateTime nowUtc)
    {
        if (Status == ListingStatus.Closed) throw new DomainException("This listing is closed. Reopen it to make changes.", "listing_status");
        if (documentIds.Count > MaxPhotos) throw new DomainException($"A listing can have up to {MaxPhotos} photos.");
        if (documentIds.Distinct().Count() != documentIds.Count) throw new DomainException("A photo is listed twice.");
        _photos.Clear();
        for (var i = 0; i < documentIds.Count; i++) _photos.Add(new ListingPhoto(OrganizationId, Id, documentIds[i], i + 1));
        UpdatedAtUtc = nowUtc;
    }

    public void Publish(DateTime nowUtc)
    {
        if (Status == ListingStatus.Published) throw new DomainException("This listing is already published.", "listing_status");
        if (Status == ListingStatus.Closed) throw new DomainException("This listing is closed. Reopen it first.", "listing_status");
        if (Description is null) throw new DomainException("Add a description before publishing.");
        if (_photos.Count == 0) throw new DomainException("Add at least one photo before publishing.");
        Status = ListingStatus.Published;
        PublishedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Takes the listing off the public site but keeps it ready to publish again.</summary>
    public void Unpublish(DateTime nowUtc)
    {
        if (Status != ListingStatus.Published) throw new DomainException("This listing isn't published.", "listing_status");
        Status = ListingStatus.Draft;
        UpdatedAtUtc = nowUtc;
    }

    public void Close(ListingCloseReason reason, DateTime nowUtc)
    {
        Guard.Defined(reason, "Reason");
        if (Status == ListingStatus.Closed) throw new DomainException("This listing is already closed.", "listing_status");
        Status = ListingStatus.Closed;
        CloseReason = reason;
        ClosedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public void Reopen(DateTime nowUtc)
    {
        if (Status != ListingStatus.Closed) throw new DomainException("This listing isn't closed.", "listing_status");
        Status = ListingStatus.Draft;
        CloseReason = null;
        ClosedAtUtc = null;
        UpdatedAtUtc = nowUtc;
    }

    private void Apply(ListingDetails d)
    {
        Guard.Defined(d.BillingFrequency, "Billing frequency");
        Headline = Guard.Required(d.Headline, "Headline", 120);
        Description = Guard.Optional(d.Description, "Description", 4000);
        if (d.AskingRent <= 0) throw new DomainException("Asking rent must be more than zero.");
        AskingRent = d.AskingRent;
        Currency = Guard.Currency(d.Currency);
        BillingFrequency = d.BillingFrequency;
        AvailableFrom = d.AvailableFrom;
        Features = Guard.Optional(d.Features, "Features", 2000);
        ShowStreetAddress = d.ShowStreetAddress;
        ContactName = Guard.Optional(d.ContactName, "Contact name", 200);
        ContactPhone = Guard.Optional(d.ContactPhone, "Contact phone", 30);
        AgentUserId = d.AgentUserId;
    }

    private static string NewCode() =>
        new(Enumerable.Range(0, 10).Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]).ToArray());
}

/// <summary>A property photo chosen for a listing, in display order.</summary>
public class ListingPhoto : IOrganizationScoped
{
    private ListingPhoto() { }

    internal ListingPhoto(Guid organizationId, Guid listingId, Guid documentId, int sortOrder)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        ListingId = listingId;
        DocumentId = documentId;
        SortOrder = sortOrder;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ListingId { get; private set; }
    public Guid DocumentId { get; private set; }
    public int SortOrder { get; private set; }
}
