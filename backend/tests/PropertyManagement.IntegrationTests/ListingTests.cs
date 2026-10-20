using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace PropertyManagement.IntegrationTests;

/// <summary>Listings, the public site, enquiries and viewings.</summary>
public sealed class ListingTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n");

    private TestClient _admin = null!;
    private Guid _property;
    private Guid _otherProperty;
    private Guid _photo;
    private Guid _otherPhoto;
    private Guid _pdf;

    public async Task InitializeAsync()
    {
        _admin = await TestClient.RegisterWithOrganizationAsync(factory, "Lekki Lettings");
        _property = await _admin.CreatePropertyAsync($"LK-{Guid.NewGuid().ToString()[..4]}");
        _otherProperty = await _admin.CreatePropertyAsync($"LX-{Guid.NewGuid().ToString()[..4]}");
        _photo = await UploadAsync("Property", _property, Png, "front.png", "Photo");
        _otherPhoto = await UploadAsync("Property", _otherProperty, Png, "other.png", "Photo");
        _pdf = await UploadAsync("Property", _property, Pdf, "plan.pdf", "Other");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private async Task<Guid> UploadAsync(string subject, Guid subjectId, byte[] bytes, string name, string category)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", name);
        form.Add(new StringContent(subject), "subject");
        form.Add(new StringContent(subjectId.ToString()), "subjectId");
        form.Add(new StringContent(category), "category");
        var response = await _admin.Http.PostAsync("/api/v1/documents", form);
        await response.EnsureSuccessAsync();
        return Id(await response.Content.ReadFromJsonAsync<JsonElement>(TestClient.Json));
    }

    private static object ListingBody(Guid unitId, Guid? agentUserId = null) => new
    {
        unitId, headline = "Bright 2-bedroom flat in Lekki Phase 1", description = "Newly renovated, close to the expressway.",
        askingRent = 4_500_000m, currency = "NGN", billingFrequency = "Annual", availableFrom = Today.AddDays(14),
        features = "Prepaid meter\n24-hour security\n", showStreetAddress = false, contactName = (string?)null,
        contactPhone = "08031234567", agentUserId,
    };

    private async Task<JsonElement> NewListingAsync(Guid? agentUserId = null)
    {
        var unit = await _admin.CreateUnitAsync(_property, $"U{Guid.NewGuid().ToString()[..4]}");
        return await _admin.PostAsync("/api/v1/listings", ListingBody(unit, agentUserId));
    }

    [Fact]
    public async Task A_published_listing_shows_only_public_fields_and_chosen_photos()
    {
        var listing = await NewListingAsync();
        var id = Id(listing);
        var code = listing.GetProperty("publicCode").GetString()!;
        var slug = listing.GetProperty("organizationSlug").GetString()!;
        var anonymous = TestClient.Anonymous(factory);

        // One open listing per unit.
        var again = await _admin.Http.PostAsJsonAsync("/api/v1/listings", ListingBody(listing.GetProperty("unitId").GetGuid()), TestClient.Json);
        again.ShouldBe(HttpStatusCode.Conflict);

        // It needs a photo, and only this property's photos qualify.
        (await _admin.Http.PostAsync($"/api/v1/listings/{id}/publish", null)).ShouldBe(HttpStatusCode.BadRequest);
        (await _admin.Http.PutAsJsonAsync($"/api/v1/listings/{id}/photos", new { documentIds = new[] { _pdf } }, TestClient.Json))
            .ShouldBe(HttpStatusCode.BadRequest);
        (await _admin.Http.PutAsJsonAsync($"/api/v1/listings/{id}/photos", new { documentIds = new[] { _otherPhoto } }, TestClient.Json))
            .ShouldBe(HttpStatusCode.BadRequest);
        var options = await _admin.GetAsync($"/api/v1/listings/{id}/photo-options");
        Assert.Equal([_photo], options.EnumerateArray().Select(o => o.GetProperty("documentId").GetGuid()));
        await _admin.PutAsync($"/api/v1/listings/{id}/photos", new { documentIds = new[] { _photo } });

        (await anonymous.Http.GetAsync($"/api/v1/public/listings/{code}")).ShouldBe(HttpStatusCode.NotFound);
        Assert.Equal("Published", (await _admin.PostAsync($"/api/v1/listings/{id}/publish", new { })).GetProperty("status").GetString());

        var page = await anonymous.GetAsync($"/api/v1/public/listings/{code}");
        Assert.Equal("Bright 2-bedroom flat in Lekki Phase 1", page.GetProperty("headline").GetString());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("streetAddress").ValueKind);
        Assert.Equal(["Prepaid meter", "24-hour security"], page.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal("08031234567", page.GetProperty("contactPhone").GetString());
        foreach (var hidden in new[] { "id", "propertyId", "unitId", "agentUserId", "status", "organizationId" })
            Assert.False(page.TryGetProperty(hidden, out _), $"{hidden} must not be public");

        var photoUrl = Assert.Single(page.GetProperty("photoUrls").EnumerateArray()).GetString()!;
        var photo = await anonymous.Http.GetAsync(photoUrl);
        photo.ShouldBe(HttpStatusCode.OK);
        Assert.Equal(Png, await photo.Content.ReadAsByteArrayAsync());
        // No other file can be fetched through a listing's address.
        (await anonymous.Http.GetAsync($"/api/v1/public/listings/{code}/photos/{_pdf}")).ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.Http.GetAsync($"/api/v1/public/listings/{code}/photos/{_otherPhoto}")).ShouldBe(HttpStatusCode.NotFound);

        var company = await anonymous.GetAsync($"/api/v1/public/organizations/{slug}/listings");
        Assert.Contains(company.GetProperty("listings").EnumerateArray(), l => l.GetProperty("code").GetString() == code);

        // Unpublishing takes it off the public site straight away.
        await _admin.PostAsync($"/api/v1/listings/{id}/unpublish", new { });
        (await anonymous.Http.GetAsync($"/api/v1/public/listings/{code}")).ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.Http.GetAsync(photoUrl)).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Enquiries_reach_staff_and_move_through_viewings_and_a_let_closes_the_listing()
    {
        var listing = await NewListingAsync();
        var id = Id(listing);
        var code = listing.GetProperty("publicCode").GetString()!;
        await _admin.PutAsync($"/api/v1/listings/{id}/photos", new { documentIds = new[] { _photo } });
        await _admin.PostAsync($"/api/v1/listings/{id}/publish", new { });
        var anonymous = TestClient.Anonymous(factory);

        var before = factory.Emails.Messages.Count;
        var sent = await anonymous.Http.PostAsJsonAsync($"/api/v1/public/listings/{code}/enquiries",
            new { name = "Kemi Adeyemi", phone = "08099998888", message = "Can I view on Saturday?" }, TestClient.Json);
        sent.ShouldBe(HttpStatusCode.Accepted);
        Assert.Contains(factory.Emails.Messages.Skip(before), m => m.To == _admin.Email && m.Subject.Contains("new enquiry"));
        (await anonymous.Http.PostAsJsonAsync($"/api/v1/public/listings/{code}/enquiries",
            new { name = "No contact" }, TestClient.Json)).ShouldBe(HttpStatusCode.BadRequest);
        // A bot filling the hidden field gets the same answer but nothing is saved.
        (await anonymous.Http.PostAsJsonAsync($"/api/v1/public/listings/{code}/enquiries",
            new { name = "Bot", email = "bot@example.com", website = "http://spam.example" }, TestClient.Json)).ShouldBe(HttpStatusCode.Accepted);

        var leads = await _admin.GetAsync($"/api/v1/leads?listingId={id}");
        var lead = Assert.Single(leads.EnumerateArray());
        Assert.Equal("Website", lead.GetProperty("source").GetString());
        var leadId = Id(lead);

        var booked = await _admin.PostAsync($"/api/v1/leads/{leadId}/viewings", new { scheduledAtUtc = DateTime.UtcNow.AddDays(2) });
        Assert.Equal("ViewingBooked", booked.GetProperty("status").GetString());
        var viewingId = Id(Assert.Single(booked.GetProperty("viewings").EnumerateArray()));
        (await _admin.Http.PostAsJsonAsync($"/api/v1/leads/{leadId}/viewings/{viewingId}/outcome", new { status = "Completed" }, TestClient.Json))
            .ShouldBe(HttpStatusCode.BadRequest);
        var cancelled = await _admin.PostAsync($"/api/v1/leads/{leadId}/viewings/{viewingId}/outcome", new { status = "Cancelled", outcome = "Moved to Sunday" });
        Assert.Equal("Cancelled", cancelled.GetProperty("viewings")[0].GetProperty("status").GetString());
        await _admin.PutAsync($"/api/v1/leads/{leadId}/status", new { status = "Won", notes = "Signing next week" });

        // The tenancy starts: the listing closes as let and leaves the public site.
        var tenant = await _admin.CreateTenantAsync("Kemi Adeyemi", $"{Guid.NewGuid():N}@example.com");
        await _admin.CreateLeaseAsync(listing.GetProperty("unitId").GetGuid(), tenant, Today);
        var closed = await _admin.GetAsync($"/api/v1/listings/{id}");
        Assert.Equal("Closed", closed.GetProperty("status").GetString());
        Assert.Equal("Let", closed.GetProperty("closeReason").GetString());
        (await anonymous.Http.GetAsync($"/api/v1/public/listings/{code}")).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Agents_see_only_their_listings_and_other_companies_see_none()
    {
        var agent = await _admin.AddMemberAsync(factory, "Agent");
        var unassigned = Id(await NewListingAsync());
        var assigned = Id(await NewListingAsync(agent.UserId));

        var list = await agent.GetAsync("/api/v1/listings");
        Assert.Equal([assigned], list.EnumerateArray().Select(Id));
        (await agent.Http.GetAsync($"/api/v1/listings/{unassigned}")).ShouldBe(HttpStatusCode.NotFound);
        Assert.Contains(agent.UserId, (await _admin.GetAsync("/api/v1/listings/agents")).EnumerateArray().Select(a => a.GetProperty("userId").GetGuid()));

        // An agent's own new listing is assigned to them, and they can't hand it to someone else.
        var unit = await _admin.CreateUnitAsync(_property, $"A{Guid.NewGuid().ToString()[..4]}");
        var own = await agent.PostAsync("/api/v1/listings", ListingBody(unit));
        Assert.Equal(agent.UserId, own.GetProperty("agentUserId").GetGuid());
        (await agent.Http.PutAsJsonAsync($"/api/v1/listings/{Id(own)}", ListingBody(unit, _admin.UserId), TestClient.Json))
            .ShouldBe(HttpStatusCode.Forbidden);

        var outsider = await TestClient.RegisterWithOrganizationAsync(factory, "Other Agency");
        (await outsider.Http.GetAsync($"/api/v1/listings/{assigned}")).ShouldBe(HttpStatusCode.NotFound);
        Assert.Empty((await outsider.GetAsync("/api/v1/listings")).EnumerateArray());
        Assert.Empty((await outsider.GetAsync("/api/v1/leads")).EnumerateArray());

        // Portal users and members without listing rights are kept out.
        var accountant = await _admin.AddMemberAsync(factory, "Accountant");
        (await accountant.Http.GetAsync("/api/v1/listings")).ShouldBe(HttpStatusCode.Forbidden);
    }
}
