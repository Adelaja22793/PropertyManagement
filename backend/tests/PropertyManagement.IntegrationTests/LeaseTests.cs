using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using PropertyManagement.Api.Infrastructure;

namespace PropertyManagement.IntegrationTests;

/// <summary>Phase 4 leases: drafts, activation, overlap, renewal, move-out and lease-driven occupancy. No money.</summary>
public sealed class LeaseTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));
    private TestClient _admin = null!;
    private Guid _property;

    public async Task InitializeAsync()
    {
        _admin = await TestClient.RegisterWithOrganizationAsync(factory, "Lekki Lettings");
        _property = await _admin.CreatePropertyAsync($"LL-{Guid.NewGuid().ToString()[..4]}");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<Guid> NewUnitAsync() => _admin.CreateUnitAsync(_property, Guid.NewGuid().ToString()[..6]);

    private async Task<string> OccupancyAsync(Guid unitId) =>
        (await _admin.GetAsync($"/api/v1/units/{unitId}")).GetProperty("occupancyStatus").GetString()!;

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    [Fact]
    public async Task Draft_holds_parties_and_charges_and_activation_occupies_the_unit()
    {
        var unit = await NewUnitAsync();
        var tenant = await _admin.CreateTenantAsync("Chidi Okafor");
        var spouse = await _admin.CreateTenantAsync("Ada Okafor");
        var guarantor = await _admin.PostAsync($"/api/v1/tenants/{tenant}/guarantors",
            new { fullName = "Emeka Obi", phone = "08031112222", signedFormReceived = true });

        var draft = await _admin.PostAsync("/api/v1/leases", TestClient.LeaseBody(unit, [tenant, spouse], Today.AddDays(-10),
            guarantorIds: [Id(guarantor)],
            charges:
            [
                new { type = "CautionDeposit", amount = 300_000m, recurrence = "OneOff" },
                new { type = "AgencyFee", amount = 300_000m, recurrence = "OneOff" },
                new { type = "ServiceCharge", amount = 250_000m, recurrence = "EachPeriod" },
            ]));
        Assert.Matches(@"^LSE-\d{5}$", draft.GetProperty("reference").GetString());
        Assert.Equal("Draft", draft.GetProperty("stage").GetString());
        Assert.Equal("Chidi Okafor", draft.GetProperty("tenants")[0].GetProperty("name").GetString());
        Assert.True(draft.GetProperty("tenants")[0].GetProperty("isPrimary").GetBoolean());
        Assert.Equal(1, draft.GetProperty("guarantors").GetArrayLength());
        Assert.Equal(3, draft.GetProperty("charges").GetArrayLength());
        Assert.Equal("Vacant", await OccupancyAsync(unit));

        // Swap the primary tenant while still a draft.
        var updated = await _admin.PutAsync($"/api/v1/leases/{Id(draft)}", TestClient.LeaseBody(unit, [spouse, tenant],
            Today.AddDays(-10), guarantorIds: [Id(guarantor)]));
        Assert.Equal("Ada Okafor", updated.GetProperty("tenants")[0].GetProperty("name").GetString());
        Assert.Equal(0, updated.GetProperty("charges").GetArrayLength());

        var active = await _admin.PostAsync($"/api/v1/leases/{Id(draft)}/activate", new { });
        Assert.Equal("Active", active.GetProperty("status").GetString());
        Assert.NotNull(active.GetProperty("activatedByName").GetString());
        Assert.Equal("Occupied", await OccupancyAsync(unit));

        var forTenant = await _admin.GetAsync($"/api/v1/leases?tenantId={tenant}");
        Assert.Equal(1, forTenant.GetProperty("totalCount").GetInt32());
        Assert.Equal("Ada Okafor", forTenant.GetProperty("items")[0].GetProperty("primaryTenantName").GetString());

        // Active terms are fixed.
        var edit = await _admin.Http.PutAsJsonAsync($"/api/v1/leases/{Id(draft)}",
            TestClient.LeaseBody(unit, [tenant], Today.AddDays(-10)), TestClient.Json);
        edit.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal("lease_not_draft", await edit.ProblemCodeAsync());
    }

    [Fact]
    public async Task A_unit_cannot_have_overlapping_leases()
    {
        var unit = await NewUnitAsync();
        await _admin.CreateLeaseAsync(unit, await _admin.CreateTenantAsync("First Tenant"), Today.AddDays(-30));

        var clash = await _admin.Http.PostAsJsonAsync("/api/v1/leases",
            TestClient.LeaseBody(unit, [await _admin.CreateTenantAsync("Second Tenant")], Today.AddMonths(3)), TestClient.Json);
        clash.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal("lease_overlap", await clash.ProblemCodeAsync());

        // Two drafts can overlap each other, but only the first one activated wins.
        var other = await NewUnitAsync();
        var a = await _admin.CreateLeaseAsync(other, await _admin.CreateTenantAsync("Draft A"), Today.AddMonths(1), activate: false);
        var b = await _admin.CreateLeaseAsync(other, await _admin.CreateTenantAsync("Draft B"), Today.AddMonths(2), activate: false);
        await _admin.PostAsync($"/api/v1/leases/{Id(a)}/activate", new { });
        var second = await _admin.Http.PostAsync($"/api/v1/leases/{Id(b)}/activate", null);
        second.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal("lease_overlap", await second.ProblemCodeAsync());
    }

    [Fact]
    public async Task A_lease_starting_later_reserves_the_unit()
    {
        var unit = await NewUnitAsync();
        var lease = await _admin.CreateLeaseAsync(unit, await _admin.CreateTenantAsync("Future Tenant"), Today.AddDays(14));
        Assert.Equal("Upcoming", lease.GetProperty("stage").GetString());
        Assert.Equal("Reserved", await OccupancyAsync(unit));
    }

    [Fact]
    public async Task Holding_over_keeps_the_unit_until_the_tenant_moves_out()
    {
        var unit = await NewUnitAsync();
        var lease = await _admin.CreateLeaseAsync(unit, await _admin.CreateTenantAsync("Overstayer"),
            Today.AddYears(-1).AddDays(-5), Today.AddDays(-5));
        Assert.Equal("HoldingOver", lease.GetProperty("stage").GetString());
        Assert.Equal("Occupied", await OccupancyAsync(unit));

        var next = await _admin.Http.PostAsJsonAsync("/api/v1/leases",
            TestClient.LeaseBody(unit, [await _admin.CreateTenantAsync("Next Tenant")], Today.AddDays(30)), TestClient.Json);
        next.ShouldBe(HttpStatusCode.Conflict);
        Assert.Contains("passed its end date", (await next.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());

        var ended = await _admin.PostAsync($"/api/v1/leases/{Id(lease)}/end", new { moveOutDate = Today });
        Assert.Equal("Ended", ended.GetProperty("status").GetString());
        Assert.Equal("Vacant", await OccupancyAsync(unit));
    }

    [Fact]
    public async Task Ending_early_is_a_termination_and_needs_a_reason()
    {
        var unit = await NewUnitAsync();
        var lease = await _admin.CreateLeaseAsync(unit, await _admin.CreateTenantAsync("Early Leaver"), Today.AddDays(-60));

        var noReason = await _admin.Http.PostAsJsonAsync($"/api/v1/leases/{Id(lease)}/end", new { moveOutDate = Today }, TestClient.Json);
        noReason.ShouldBe(HttpStatusCode.BadRequest);

        var terminated = await _admin.PostAsync($"/api/v1/leases/{Id(lease)}/end",
            new { moveOutDate = Today, reason = "Relocating to Abuja" });
        Assert.Equal("Terminated", terminated.GetProperty("stage").GetString());
        Assert.Equal("Vacant", await OccupancyAsync(unit));

        // The unit is free again from the day after the move-out.
        await _admin.CreateLeaseAsync(unit, await _admin.CreateTenantAsync("New Arrival"), Today.AddDays(1));
    }

    [Fact]
    public async Task Renewal_drafts_the_next_lease_and_activating_it_closes_the_current_one()
    {
        var unit = await NewUnitAsync();
        var tenant = await _admin.CreateTenantAsync("Renewing Tenant");
        var start = Today.AddMonths(-11);
        var draft = await _admin.PostAsync("/api/v1/leases", TestClient.LeaseBody(unit, [tenant], start, charges:
        [
            new { type = "CautionDeposit", amount = 300_000m, recurrence = "OneOff" },
            new { type = "ServiceCharge", amount = 250_000m, recurrence = "EachPeriod" },
        ]));
        var current = await _admin.PostAsync($"/api/v1/leases/{Id(draft)}/activate", new { });
        Assert.Equal("Expiring", current.GetProperty("stage").GetString());

        var expiring = await _admin.GetAsync($"/api/v1/leases?endingWithinDays=60&propertyId={_property}");
        Assert.Contains(expiring.GetProperty("items").EnumerateArray(), l => Id(l) == Id(current));

        var early = await _admin.Http.PostAsJsonAsync($"/api/v1/leases/{Id(current)}/renew",
            new { startDate = Today, endDate = Today.AddYears(1), rentAmount = 3_600_000m }, TestClient.Json);
        early.ShouldBe(HttpStatusCode.BadRequest);

        var renewal = await _admin.PostAsync($"/api/v1/leases/{Id(current)}/renew",
            new { endDate = start.AddYears(2).AddDays(-1), rentAmount = 3_600_000m, notes = "20% review" });
        Assert.Equal("Draft", renewal.GetProperty("status").GetString());
        Assert.Equal(start.AddYears(1), DateOnly.Parse(renewal.GetProperty("startDate").GetString()!));
        Assert.Equal(3_000_000m, renewal.GetProperty("previousLease").GetProperty("rentAmount").GetDecimal());
        var charge = Assert.Single(renewal.GetProperty("charges").EnumerateArray());
        Assert.Equal("ServiceCharge", charge.GetProperty("type").GetString());
        Assert.Equal(tenant, renewal.GetProperty("tenants")[0].GetProperty("tenantId").GetGuid());

        var twice = await _admin.Http.PostAsJsonAsync($"/api/v1/leases/{Id(current)}/renew",
            new { endDate = start.AddYears(2), rentAmount = 3_600_000m }, TestClient.Json);
        twice.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal("renewal_exists", await twice.ProblemCodeAsync());

        await _admin.PostAsync($"/api/v1/leases/{Id(renewal)}/activate", new { });
        var old = await _admin.GetAsync($"/api/v1/leases/{Id(current)}");
        Assert.Equal("Renewed", old.GetProperty("status").GetString());
        Assert.Equal(Id(renewal), old.GetProperty("renewalLease").GetProperty("id").GetGuid());
        Assert.Equal("Occupied", await OccupancyAsync(unit));
    }

    [Fact]
    public async Task Guarantors_must_belong_to_a_lease_tenant_and_stay_while_named()
    {
        var unit = await NewUnitAsync();
        var tenant = await _admin.CreateTenantAsync("Guaranteed Tenant");
        var stranger = await _admin.CreateTenantAsync("Someone Else");
        var theirs = await _admin.PostAsync($"/api/v1/tenants/{stranger}/guarantors", new { fullName = "Not Mine", phone = "08030001111" });
        var mine = await _admin.PostAsync($"/api/v1/tenants/{tenant}/guarantors", new { fullName = "Mine", phone = "08030002222" });

        var wrong = await _admin.Http.PostAsJsonAsync("/api/v1/leases",
            TestClient.LeaseBody(unit, [tenant], Today, guarantorIds: [Id(theirs)]), TestClient.Json);
        wrong.ShouldBe(HttpStatusCode.BadRequest);

        await _admin.PostAsync("/api/v1/leases", TestClient.LeaseBody(unit, [tenant], Today, guarantorIds: [Id(mine)]));
        var remove = await _admin.Http.DeleteAsync($"/api/v1/tenants/{tenant}/guarantors/{Id(mine)}");
        remove.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal("guarantor_in_use", await remove.ProblemCodeAsync());
    }

    [Fact]
    public async Task Occupancy_of_a_leased_unit_cannot_be_set_by_hand_or_archived()
    {
        var unit = await NewUnitAsync();
        await _admin.CreateLeaseAsync(unit, await _admin.CreateTenantAsync("Sitting Tenant"), Today.AddDays(-1));
        var current = await _admin.GetAsync($"/api/v1/units/{unit}");

        var manual = await _admin.Http.PutAsJsonAsync($"/api/v1/units/{unit}", new
        {
            code = current.GetProperty("code").GetString(), type = "Flat", defaultRent = 1m, billingFrequency = "Annual",
            occupancyStatus = "Vacant",
        });
        manual.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal("unit_has_lease", await manual.ProblemCodeAsync());

        var archive = await _admin.Http.PostAsync($"/api/v1/units/{unit}/archive", null);
        archive.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Daily_refresh_brings_occupancy_back_in_line_with_leases()
    {
        var unit = await NewUnitAsync();
        await _admin.CreateLeaseAsync(unit, await _admin.CreateTenantAsync("Refresh Tenant"), Today.AddDays(-1));
        await using (var connection = new SqlConnection(factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE PropertyUnits SET OccupancyStatus = 'Vacant' WHERE Id = @id";
            command.Parameters.AddWithValue("@id", unit);
            await command.ExecuteNonQueryAsync();
        }
        Assert.Equal("Vacant", await OccupancyAsync(unit));

        await factory.Services.GetRequiredService<LeaseScheduleJob>().RunOnceAsync(CancellationToken.None);
        Assert.Equal("Occupied", await OccupancyAsync(unit));
    }

    [Fact]
    public async Task Permissions_decide_who_can_end_leases()
    {
        var unit = await NewUnitAsync();
        var manager = await _admin.AddMemberAsync(factory, "PropertyManager");
        var accountant = await _admin.AddMemberAsync(factory, "Accountant");
        var lease = await _admin.CreateLeaseAsync(unit, await _admin.CreateTenantAsync("Permission Tenant"), Today.AddDays(-1));

        // A property manager sees only leases on properties assigned to them.
        (await manager.Http.GetAsync($"/api/v1/leases/{Id(lease)}")).ShouldBe(HttpStatusCode.NotFound);
        (await accountant.Http.GetAsync($"/api/v1/leases/{Id(lease)}")).ShouldBe(HttpStatusCode.OK);
        (await accountant.Http.PostAsJsonAsync($"/api/v1/leases/{Id(lease)}/end",
            new { moveOutDate = Today, reason = "x" }, TestClient.Json)).ShouldBe(HttpStatusCode.Forbidden);

        var managed = await _admin.CreatePropertyAsync($"PM-{Guid.NewGuid().ToString()[..4]}", managerUserId: manager.UserId);
        var managedUnit = await _admin.CreateUnitAsync(managed, "M1");
        var theirs = await manager.CreateLeaseAsync(managedUnit, await _admin.CreateTenantAsync("Managed Tenant"), Today);
        Assert.Equal("Active", theirs.GetProperty("status").GetString());
        (await manager.Http.PostAsJsonAsync($"/api/v1/leases/{Id(theirs)}/end",
            new { moveOutDate = Today, reason = "x" }, TestClient.Json)).ShouldBe(HttpStatusCode.Forbidden);
    }
}
