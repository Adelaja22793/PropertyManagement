using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PropertyManagement.IntegrationTests;

/// <summary>Phase 18: a unit let room by room takes several leases at once, up to its limit (design 6.5).</summary>
public sealed class SharedOccupancyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private static object UnitBody(int? limit, string occupancy = "Vacant") => new
    {
        code = "SH1", type = "Flat", bedrooms = 3, bathrooms = 2, defaultRent = 600_000m, currency = "NGN",
        billingFrequency = "Annual", sharedOccupancyLimit = limit, occupancyStatus = occupancy,
    };

    [Fact]
    public async Task A_shared_unit_takes_leases_up_to_its_limit_and_stays_advertised_until_full()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Shared Rooms Ltd");
        var property = await admin.CreatePropertyAsync($"SR-{Guid.NewGuid().ToString()[..4]}");
        var unit = await admin.PostAsync($"/api/v1/properties/{property}/units", UnitBody(2));
        Assert.Equal(2, unit.GetProperty("sharedOccupancyLimit").GetInt32());
        var unitId = Id(unit);
        var listing = await admin.PostAsync("/api/v1/listings", new
        {
            unitId, headline = "Room in a shared flat", description = "Ensuite room in a three-bedroom flat.", askingRent = 600_000m,
            currency = "NGN", billingFrequency = "Annual", showStreetAddress = false,
        });

        var first = await admin.CreateLeaseAsync(unitId, await admin.CreateTenantAsync("Room One"), Today.AddDays(-10));
        Assert.Equal("Active", first.GetProperty("status").GetString());
        Assert.NotEqual("Closed", (await admin.GetAsync($"/api/v1/listings/{Id(listing)}")).GetProperty("status").GetString());

        var second = await admin.CreateLeaseAsync(unitId, await admin.CreateTenantAsync("Room Two"), Today);
        Assert.Equal("Active", second.GetProperty("status").GetString());
        Assert.Equal("Closed", (await admin.GetAsync($"/api/v1/listings/{Id(listing)}")).GetProperty("status").GetString());

        var units = await admin.GetAsync($"/api/v1/properties/{property}/units");
        var row = units.EnumerateArray().Single(u => Id(u) == unitId);
        Assert.Equal(2, row.GetProperty("activeLeaseCount").GetInt32());
        Assert.Equal("Occupied", row.GetProperty("occupancyStatus").GetString());

        var roomThree = await admin.CreateTenantAsync("Room Three");
        var refused = await admin.Http.PostAsJsonAsync("/api/v1/leases",
            TestClient.LeaseBody(unitId, [roomThree], Today.AddDays(5)), TestClient.Json);
        refused.ShouldBe(HttpStatusCode.Conflict);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("lease_overlap", problem.GetProperty("code").GetString());
        Assert.Contains("takes 2 leases at once", problem.GetProperty("title").GetString());

        // The limit can't drop below the leases already holding the unit together, but can rise.
        var tooLow = await admin.Http.PutAsJsonAsync($"/api/v1/units/{unitId}", UnitBody(null, "Occupied"), TestClient.Json);
        tooLow.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal("shared_occupancy_in_use", await tooLow.ProblemCodeAsync());
        await admin.PutAsync($"/api/v1/units/{unitId}", UnitBody(3, "Occupied"));
        var third = await admin.CreateLeaseAsync(unitId, roomThree, Today.AddDays(5));
        Assert.Equal("Active", third.GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_ordinary_unit_still_takes_one_lease_at_a_time()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Single Lets Ltd");
        var property = await admin.CreatePropertyAsync($"SL-{Guid.NewGuid().ToString()[..4]}");
        var unitId = Id(await admin.PostAsync($"/api/v1/properties/{property}/units", UnitBody(null)));
        await admin.CreateLeaseAsync(unitId, await admin.CreateTenantAsync("Only Tenant"), Today);
        var refused = await admin.Http.PostAsJsonAsync("/api/v1/leases",
            TestClient.LeaseBody(unitId, [await admin.CreateTenantAsync("Late Tenant")], Today.AddDays(30)), TestClient.Json);
        refused.ShouldBe(HttpStatusCode.Conflict);
        Assert.Contains("overlap lease", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());

        var invalid = await admin.Http.PutAsJsonAsync($"/api/v1/units/{unitId}", UnitBody(1, "Occupied"), TestClient.Json);
        invalid.ShouldBe(HttpStatusCode.BadRequest);
    }
}
