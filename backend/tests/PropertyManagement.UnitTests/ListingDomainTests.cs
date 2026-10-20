using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Listings;
using PropertyManagement.Domain.Properties;

namespace PropertyManagement.UnitTests;

public class ListingDomainTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    private static ListingDetails Details(string? description = "Two bedrooms, newly painted.", decimal rent = 3_000_000m) =>
        new("2-bed flat in Yaba", description, rent, "NGN", BillingFrequency.Annual, null, "Prepaid meter", false, null, null, null);

    private static Listing New(ListingDetails? details = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), details ?? Details(), Guid.NewGuid(), Now);

    private static Lead NewLead(string? phone = "08030000000", string? email = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 7, "Ada", phone, email, null, LeadSource.Website, null, Now);

    [Fact]
    public void Public_codes_are_random_and_short()
    {
        var codes = Enumerable.Range(0, 50).Select(_ => New().PublicCode).ToList();
        Assert.All(codes, c => Assert.Matches("^[a-z2-9]{10}$", c));
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void Publishing_needs_a_description_and_a_photo()
    {
        Assert.Throws<DomainException>(() => New(Details(description: null)).Publish(Now));
        var listing = New();
        Assert.Throws<DomainException>(() => listing.Publish(Now));
        listing.SetPhotos([Guid.NewGuid()], Now);
        listing.Publish(Now);
        Assert.Equal(ListingStatus.Published, listing.Status);
        Assert.Equal("listing_status", Assert.Throws<DomainException>(() => listing.Publish(Now)).Code);
    }

    [Fact]
    public void Photos_keep_their_order_and_reject_duplicates_and_too_many()
    {
        var listing = New();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        listing.SetPhotos([b, a], Now);
        Assert.Equal([b, a], listing.Photos.OrderBy(p => p.SortOrder).Select(p => p.DocumentId));
        Assert.Throws<DomainException>(() => listing.SetPhotos([a, a], Now));
        Assert.Throws<DomainException>(() => listing.SetPhotos(Enumerable.Range(0, Listing.MaxPhotos + 1).Select(_ => Guid.NewGuid()).ToList(), Now));
    }

    [Fact]
    public void A_closed_listing_must_be_reopened_before_changes()
    {
        var listing = New();
        listing.Close(ListingCloseReason.Let, Now);
        Assert.Equal(ListingCloseReason.Let, listing.CloseReason);
        Assert.Throws<DomainException>(() => listing.Update(Details(), Now));
        Assert.Throws<DomainException>(() => listing.SetPhotos([], Now));
        Assert.Throws<DomainException>(() => listing.Close(ListingCloseReason.Withdrawn, Now));
        listing.Reopen(Now);
        Assert.Equal(ListingStatus.Draft, listing.Status);
        Assert.Null(listing.CloseReason);
    }

    [Fact]
    public void Asking_rent_must_be_positive() =>
        Assert.Throws<DomainException>(() => New(Details(rent: 0)));

    [Fact]
    public void A_lead_needs_a_phone_or_an_email()
    {
        Assert.Throws<DomainException>(() => NewLead(phone: null));
        Assert.Throws<DomainException>(() => NewLead(phone: null, email: "not-an-email"));
        Assert.Equal("ENQ-00007", NewLead(phone: null, email: "ada@example.com").Reference);
    }

    [Fact]
    public void Booking_a_viewing_moves_the_lead_on_and_refuses_the_past()
    {
        var lead = NewLead();
        Assert.Throws<DomainException>(() => lead.ScheduleViewing(Now.AddDays(-1), null, null, Now));
        lead.ScheduleViewing(Now.AddDays(1), null, null, Now);
        Assert.Equal(LeadStatus.ViewingBooked, lead.Status);

        lead.SetStatus(LeadStatus.Lost, "Found elsewhere", Now);
        Assert.False(lead.IsOpen);
        Assert.Equal("lead_closed", Assert.Throws<DomainException>(() => lead.ScheduleViewing(Now.AddDays(2), null, null, Now)).Code);
    }

    [Fact]
    public void A_viewing_outcome_is_recorded_once_and_only_after_it_happens()
    {
        var lead = NewLead();
        var viewing = lead.ScheduleViewing(Now.AddDays(1), null, null, Now);
        Assert.Throws<DomainException>(() => lead.RecordViewing(viewing.Id, ViewingStatus.Completed, null, Now));
        Assert.Throws<DomainException>(() => lead.RecordViewing(viewing.Id, ViewingStatus.Scheduled, null, Now.AddDays(2)));
        lead.RecordViewing(viewing.Id, ViewingStatus.NoShow, "Didn't arrive", Now.AddDays(2));
        Assert.Equal(ViewingStatus.NoShow, viewing.Status);
        Assert.Equal("already_decided",
            Assert.Throws<DomainException>(() => lead.RecordViewing(viewing.Id, ViewingStatus.Completed, null, Now.AddDays(2))).Code);
    }
}
