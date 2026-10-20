using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PropertyManagement.IntegrationTests;

/// <summary>Phase 6 owner and tenant portals: each person reaches only their own records.</summary>
public sealed class PortalTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));
    private TestClient _admin = null!;
    private Guid _property;
    private Guid _otherProperty;
    private Guid _unit;
    private Guid _otherUnit;
    private Guid _tenantId;
    private Guid _lease;
    private TestClient _owner = null!;
    private TestClient _tenant = null!;

    public async Task InitializeAsync()
    {
        _admin = await TestClient.RegisterWithOrganizationAsync(factory, "Portal Homes");
        _property = await _admin.CreatePropertyAsync($"PH-{Guid.NewGuid().ToString()[..4]}");
        _otherProperty = await _admin.CreatePropertyAsync($"PX-{Guid.NewGuid().ToString()[..4]}");
        _unit = await _admin.CreateUnitAsync(_property, "1A");
        _otherUnit = await _admin.CreateUnitAsync(_otherProperty, "9Z");

        var ownerEmail = $"{Guid.NewGuid():N}@owner.example.com";
        var ownerId = await _admin.CreateOwnerAsync("Mrs Funmi Adebayo", ownerEmail);
        await _admin.PutAsync($"/api/v1/properties/{_property}/owners", new
        {
            effectiveFrom = Today.AddYears(-1), shares = new[] { new { ownerId, sharePercent = 100m } },
        });
        await _admin.PostAsync($"/api/v1/owners/{ownerId}/agreements", new
        {
            propertyId = _property, feeBasis = "PercentOfRentCollected", feeValue = 10m, payoutFrequency = "Monthly",
            advanceRentRelease = "OnReceipt", reserveAmount = 0m, startDate = Today.AddYears(-1), repairApprovalLimit = 50_000m,
        });
        var ownerInvite = await _admin.PostAsync($"/api/v1/owners/{ownerId}/portal-invite", new { });
        _owner = await TestClient.RegisterWithInvitationAsync(factory, ownerEmail, ownerInvite.GetProperty("acceptToken").GetString()!);

        var tenantEmail = $"{Guid.NewGuid():N}@tenant.example.com";
        _tenantId = await _admin.CreateTenantAsync("Tolu Bakare", tenantEmail);
        var lease = await _admin.CreateLeaseAsync(_unit, _tenantId, Today.AddMonths(-2));
        _lease = lease.GetProperty("id").GetGuid();
        await _admin.PostAsync($"/api/v1/leases/{_lease}/notices", new { type = "RentReview", servedOn = Today, method = "Email" });
        var tenantInvite = await _admin.PostAsync($"/api/v1/tenants/{_tenantId}/portal-invite", new { });
        _tenant = await TestClient.RegisterWithInvitationAsync(factory, tenantEmail, tenantInvite.GetProperty("acceptToken").GetString()!);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    [Fact]
    public async Task Owner_sees_only_their_properties_with_occupancy()
    {
        var home = await _owner.GetAsync("/api/v1/portal/owner");
        Assert.Equal("Mrs Funmi Adebayo", home.GetProperty("ownerName").GetString());
        var property = Assert.Single(home.GetProperty("properties").EnumerateArray());
        Assert.Equal(_property, Id(property));
        Assert.Equal(100m, property.GetProperty("sharePercent").GetDecimal());
        var unit = Assert.Single(property.GetProperty("units").EnumerateArray());
        Assert.Equal("Tolu Bakare", unit.GetProperty("lease").GetProperty("tenantName").GetString());

        // Staff screens and the tenant portal stay closed.
        (await _owner.Http.GetAsync("/api/v1/maintenance")).ShouldBe(HttpStatusCode.Forbidden);
        (await _owner.Http.GetAsync("/api/v1/portal/tenant")).ShouldBe(HttpStatusCode.Forbidden);
        (await _tenant.Http.GetAsync("/api/v1/portal/owner")).ShouldBe(HttpStatusCode.Forbidden);
        (await _admin.Http.GetAsync("/api/v1/portal/owner")).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Tenant_sees_their_lease_and_notices_and_reports_a_repair()
    {
        var home = await _tenant.GetAsync("/api/v1/portal/tenant");
        var lease = Assert.Single(home.GetProperty("leases").EnumerateArray());
        Assert.Equal(_lease, Id(lease));
        Assert.Equal("1A", lease.GetProperty("unitCode").GetString());
        Assert.Single(lease.GetProperty("notices").EnumerateArray());

        var before = factory.Emails.Messages.Count;
        var report = await _tenant.PostAsync("/api/v1/portal/tenant/maintenance", new
        {
            leaseId = _lease, title = "No water in the bathroom", category = "Plumbing", urgency = "Emergency",
        });
        Assert.Equal("Submitted", report.GetProperty("status").GetString());
        Assert.Contains(factory.Emails.Messages.Skip(before), m => m.To == _admin.Email && m.Subject.Contains("EMERGENCY"));

        // Staff see it linked to the tenant and unit; a staff comment stays internal.
        var staffView = await _admin.GetAsync($"/api/v1/maintenance/{Id(report)}");
        Assert.Equal(_tenantId, staffView.GetProperty("tenantId").GetGuid());
        Assert.Equal(_unit, staffView.GetProperty("unitId").GetGuid());
        await _admin.PostAsync($"/api/v1/maintenance/{Id(report)}/comments", new { text = "Internal: tenant owes rent" });
        await _admin.PostAsync($"/api/v1/maintenance/{Id(report)}/review", new { });
        var mine = await _tenant.PostAsync($"/api/v1/portal/tenant/maintenance/{Id(report)}/comments", new { text = "Still no water" });
        var texts = mine.GetProperty("updates").EnumerateArray().Select(u => u.GetProperty("text").GetString()).ToList();
        Assert.Contains("Still no water", texts);
        Assert.DoesNotContain("Internal: tenant owes rent", texts);
        Assert.Equal("UnderReview", mine.GetProperty("status").GetString());

        // A repair on someone else's unit is not theirs to see or report.
        var other = await _admin.PostAsync("/api/v1/maintenance", new
        {
            propertyId = _otherProperty, unitId = _otherUnit, title = "Gate broken", category = "Security", urgency = "High",
        });
        (await _tenant.Http.GetAsync($"/api/v1/portal/tenant/maintenance/{Id(other)}")).ShouldBe(HttpStatusCode.NotFound);
        Assert.Single((await _tenant.GetAsync("/api/v1/portal/tenant/maintenance")).EnumerateArray());
        (await _tenant.Http.PostAsJsonAsync("/api/v1/portal/tenant/maintenance", new
        {
            leaseId = Guid.NewGuid(), title = "x", category = "Other", urgency = "Low",
        }, TestClient.Json)).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Owner_approves_or_declines_estimates_above_their_limit()
    {
        var request = await _admin.PostAsync("/api/v1/maintenance", new
        {
            propertyId = _property, unitId = _unit, title = "Replace water heater", category = "Plumbing", urgency = "Normal",
        });
        var id = Id(request);
        var hidden = await _admin.PostAsync("/api/v1/maintenance", new
        {
            propertyId = _otherProperty, title = "Not the owner's", category = "Other", urgency = "Low",
        });
        await _admin.PostAsync($"/api/v1/maintenance/{id}/estimate", new { amount = 120_000m });

        var list = await _owner.GetAsync("/api/v1/portal/owner/maintenance");
        var item = Assert.Single(list.EnumerateArray());
        Assert.True(item.GetProperty("awaitingOwnerApproval").GetBoolean());
        Assert.Equal(1, (await _owner.GetAsync("/api/v1/portal/owner")).GetProperty("awaitingYourApproval").GetInt32());
        (await _owner.Http.GetAsync($"/api/v1/portal/owner/maintenance/{Id(hidden)}")).ShouldBe(HttpStatusCode.NotFound);

        var declined = await _owner.PostAsync($"/api/v1/portal/owner/maintenance/{id}/decline", new { reason = "Get a second quote" });
        Assert.True(declined.GetProperty("awaitingOwnerApproval").GetBoolean());

        var approved = await _owner.PostAsync($"/api/v1/portal/owner/maintenance/{id}/approve", new { note = "Second quote matched" });
        Assert.False(approved.GetProperty("awaitingOwnerApproval").GetBoolean());
        Assert.Contains("owner portal", approved.GetProperty("ownerApprovalNote").GetString());
        var again = await _owner.Http.PostAsJsonAsync($"/api/v1/portal/owner/maintenance/{id}/approve", new { }, TestClient.Json);
        again.ShouldBe(HttpStatusCode.Conflict);

        // Staff can now approve the work.
        Assert.Equal("Approved", (await _admin.PostAsync($"/api/v1/maintenance/{id}/approve", new { })).GetProperty("status").GetString());
    }
}
