using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PropertyManagement.IntegrationTests;

/// <summary>Phase 5 maintenance requests and vendors.</summary>
public sealed class MaintenanceTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));
    private TestClient _admin = null!;
    private Guid _property;
    private Guid _unit;

    public async Task InitializeAsync()
    {
        _admin = await TestClient.RegisterWithOrganizationAsync(factory, "Lekki Repairs Ltd");
        _property = await _admin.CreatePropertyAsync($"LR-{Guid.NewGuid().ToString()[..4]}");
        _unit = await _admin.CreateUnitAsync(_property, "A1");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Status(JsonElement e) => e.GetProperty("status").GetString()!;

    private Task<JsonElement> VendorAsync(string name = "Bayo Plumbing Works") =>
        _admin.PostAsync("/api/v1/vendors", new { name, trade = "Plumbing", phone = "08030000000" });

    private Task<JsonElement> RequestAsync(TestClient? client = null, Guid? property = null, string urgency = "High") =>
        (client ?? _admin).PostAsync("/api/v1/maintenance", new
        {
            propertyId = property ?? _property, unitId = property is null ? _unit : (Guid?)null, reportedBy = "Tenant by phone",
            title = "Kitchen sink leaking", description = "Water under the sink cabinet.", category = "Plumbing", urgency,
        });

    private async Task SetOwnerLimitAsync(decimal limit)
    {
        var owner = await _admin.CreateOwnerAsync($"Owner {Guid.NewGuid().ToString()[..4]}");
        await _admin.PutAsync($"/api/v1/properties/{_property}/owners", new
        {
            effectiveFrom = Today.AddYears(-1), shares = new[] { new { ownerId = owner, sharePercent = 100m } },
        });
        await _admin.PostAsync($"/api/v1/owners/{owner}/agreements", new
        {
            propertyId = _property, feeBasis = "PercentOfRentCollected", feeValue = 10m, payoutFrequency = "Monthly",
            advanceRentRelease = "OnReceipt", reserveAmount = 0m, startDate = Today.AddYears(-1), repairApprovalLimit = limit,
        });
    }

    [Fact]
    public async Task A_request_runs_from_report_to_completion_with_owner_approval_above_the_limit()
    {
        await SetOwnerLimitAsync(100_000m);
        var staff = await _admin.AddMemberAsync(factory, "MaintenanceStaff");
        var vendor = await VendorAsync();
        var request = await RequestAsync();
        Assert.Equal("Submitted", Status(request));
        Assert.StartsWith("MNT-", request.GetProperty("reference").GetString());
        var id = Id(request);

        await _admin.PostAsync($"/api/v1/maintenance/{id}/review", new { });
        var limit = await _admin.GetAsync($"/api/v1/maintenance/approval-limit?propertyId={_property}");
        Assert.Equal(100_000m, limit.GetProperty("limit").GetDecimal());

        var estimated = await _admin.PostAsync($"/api/v1/maintenance/{id}/estimate", new { amount = 250_000m, notes = "Replace trap and waste pipe" });
        Assert.True(estimated.GetProperty("needsOwnerApproval").GetBoolean());

        var blocked = await _admin.Http.PostAsJsonAsync($"/api/v1/maintenance/{id}/approve", new { }, TestClient.Json);
        blocked.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal("owner_approval_required", await blocked.ProblemCodeAsync());

        await _admin.PostAsync($"/api/v1/maintenance/{id}/owner-approval", new { note = "Mrs Adebayo approved by WhatsApp" });
        Assert.Equal("Approved", Status(await _admin.PostAsync($"/api/v1/maintenance/{id}/approve", new { })));

        var before = factory.Emails.Messages.Count;
        var assigned = await _admin.PostAsync($"/api/v1/maintenance/{id}/assign", new { assignedUserId = staff.UserId, vendorId = Id(vendor) });
        Assert.Equal("Assigned", Status(assigned));
        Assert.Equal("Bayo Plumbing Works", assigned.GetProperty("vendorName").GetString());
        Assert.Contains(factory.Emails.Messages.Skip(before), m => m.To == staff.Email && m.Subject.Contains("assigned to you"));

        // The assigned staff member does the work.
        await staff.PostAsync($"/api/v1/maintenance/{id}/start", new { });
        await staff.PostAsync($"/api/v1/maintenance/{id}/awaiting-parts", new { note = "Waiting for a 1.5 inch trap" });
        await staff.PostAsync($"/api/v1/maintenance/{id}/start", new { });
        await staff.PostAsync($"/api/v1/maintenance/{id}/comments", new { text = "Trap fitted, testing for leaks." });
        var done = await staff.PostAsync($"/api/v1/maintenance/{id}/complete", new { completedOn = Today, actualCost = 240_000m, notes = "Fixed" });
        Assert.Equal("Completed", Status(done));
        Assert.Equal(240_000m, done.GetProperty("actualCost").GetDecimal());

        var rated = await _admin.PostAsync($"/api/v1/maintenance/{id}/feedback", new { rating = 4, comment = "Quick and tidy" });
        Assert.Equal(4, rated.GetProperty("tenantRating").GetInt32());
        var twice = await _admin.Http.PostAsJsonAsync($"/api/v1/maintenance/{id}/feedback", new { rating = 5 }, TestClient.Json);
        twice.ShouldBe(HttpStatusCode.Conflict);

        var kinds = rated.GetProperty("updates").EnumerateArray().Select(u => u.GetProperty("kind").GetString()).ToList();
        Assert.Equal("Created", kinds[0]);
        Assert.Contains("OwnerApproved", kinds);
        Assert.Contains("Comment", kinds);
        Assert.Equal("Feedback", kinds[^1]);

        var vendorAfter = await _admin.GetAsync($"/api/v1/vendors/{Id(vendor)}");
        Assert.Equal(0, vendorAfter.GetProperty("openJobs").GetInt32());
    }

    [Fact]
    public async Task Maintenance_staff_see_and_update_only_jobs_assigned_to_them()
    {
        var staff = await _admin.AddMemberAsync(factory, "MaintenanceStaff");
        var other = await _admin.AddMemberAsync(factory, "MaintenanceStaff");
        var mine = Id(await RequestAsync());
        var notMine = Id(await RequestAsync());
        foreach (var id in new[] { mine, notMine })
            await _admin.PostAsync($"/api/v1/maintenance/{id}/approve", new { });
        await _admin.PostAsync($"/api/v1/maintenance/{mine}/assign", new { assignedUserId = staff.UserId });
        await _admin.PostAsync($"/api/v1/maintenance/{notMine}/assign", new { assignedUserId = other.UserId });

        var list = await staff.GetAsync("/api/v1/maintenance");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(mine, Id(list.GetProperty("items")[0]));
        Assert.False((await staff.GetAsync($"/api/v1/maintenance/{mine}")).GetProperty("can").GetProperty("manage").GetBoolean());

        (await staff.Http.GetAsync($"/api/v1/maintenance/{notMine}")).ShouldBe(HttpStatusCode.NotFound);
        (await staff.Http.PostAsJsonAsync($"/api/v1/maintenance/{notMine}/start", new { }, TestClient.Json)).ShouldBe(HttpStatusCode.NotFound);
        (await staff.Http.PostAsJsonAsync($"/api/v1/maintenance/{mine}/cancel", new { reason = "x" }, TestClient.Json)).ShouldBe(HttpStatusCode.Forbidden);
        (await staff.Http.PostAsJsonAsync("/api/v1/vendors", new { name = "X", phone = "1" }, TestClient.Json)).ShouldBe(HttpStatusCode.Forbidden);

        // Reassigned away: the job disappears from their list.
        await _admin.PostAsync($"/api/v1/maintenance/{mine}/assign", new { assignedUserId = other.UserId });
        (await staff.Http.PostAsJsonAsync($"/api/v1/maintenance/{mine}/start", new { }, TestClient.Json)).ShouldBe(HttpStatusCode.NotFound);

        // Only members who handle maintenance can be assigned.
        var accountant = await _admin.AddMemberAsync(factory, "Accountant");
        var wrong = await _admin.Http.PostAsJsonAsync($"/api/v1/maintenance/{mine}/assign", new { assignedUserId = accountant.UserId }, TestClient.Json);
        wrong.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Steps_must_follow_the_workflow()
    {
        var id = Id(await RequestAsync());
        var early = await _admin.Http.PostAsJsonAsync($"/api/v1/maintenance/{id}/complete", new { completedOn = Today }, TestClient.Json);
        early.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal("maintenance_status", await early.ProblemCodeAsync());

        var vendor = await VendorAsync("Sunny Electricals");
        await _admin.PutAsync($"/api/v1/vendors/{Id(vendor)}/active", new { isActive = false });
        await _admin.PostAsync($"/api/v1/maintenance/{id}/approve", new { });
        var inactive = await _admin.Http.PostAsJsonAsync($"/api/v1/maintenance/{id}/assign", new { vendorId = Id(vendor) }, TestClient.Json);
        inactive.ShouldBe(HttpStatusCode.BadRequest);

        (await _admin.Http.PostAsJsonAsync($"/api/v1/maintenance/{id}/reject", new { reason = "Duplicate" }, TestClient.Json))
            .ShouldBe(HttpStatusCode.Conflict);
        (await _admin.Http.PostAsJsonAsync($"/api/v1/maintenance/{id}/cancel", new { reason = "" }, TestClient.Json))
            .ShouldBe(HttpStatusCode.BadRequest);
        var cancelled = await _admin.PostAsync($"/api/v1/maintenance/{id}/cancel", new { reason = "Tenant fixed it themselves" });
        Assert.Equal("Cancelled", Status(cancelled));
        (await _admin.Http.PostAsJsonAsync($"/api/v1/maintenance/{id}/comments", new { text = "late" }, TestClient.Json))
            .ShouldBe(HttpStatusCode.OK);
        (await _admin.Http.PutAsJsonAsync($"/api/v1/maintenance/{id}", new
        {
            propertyId = _property, title = "Changed", category = "Plumbing", urgency = "Low",
        }, TestClient.Json)).ShouldBe(HttpStatusCode.Conflict);

        var open = await _admin.GetAsync($"/api/v1/maintenance?open=true&propertyId={_property}");
        Assert.DoesNotContain(open.GetProperty("items").EnumerateArray(), i => Id(i) == id);
        var byReference = await _admin.GetAsync($"/api/v1/maintenance?search={cancelled.GetProperty("reference").GetString()}");
        Assert.Equal(id, Id(byReference.GetProperty("items")[0]));
    }

    [Fact]
    public async Task Requests_and_vendors_stay_inside_their_organization()
    {
        var request = Id(await RequestAsync());
        var vendor = Id(await VendorAsync("Private Vendor"));
        var other = await TestClient.RegisterWithOrganizationAsync(factory, "Other Estates");
        var otherProperty = await other.CreatePropertyAsync("OE-1");

        (await other.Http.GetAsync($"/api/v1/maintenance/{request}")).ShouldBe(HttpStatusCode.NotFound);
        (await other.Http.GetAsync($"/api/v1/vendors/{vendor}")).ShouldBe(HttpStatusCode.NotFound);
        Assert.Equal(0, (await other.GetAsync("/api/v1/maintenance")).GetProperty("totalCount").GetInt32());

        var theirs = Id(await RequestAsync(other, otherProperty));
        await other.PostAsync($"/api/v1/maintenance/{theirs}/approve", new { });
        (await other.Http.PostAsJsonAsync($"/api/v1/maintenance/{theirs}/assign", new { vendorId = vendor }, TestClient.Json))
            .ShouldBe(HttpStatusCode.NotFound);
        (await other.Http.PostAsJsonAsync("/api/v1/maintenance", new
        {
            propertyId = _property, title = "Sneaky", category = "Other", urgency = "Low",
        }, TestClient.Json)).ShouldBe(HttpStatusCode.NotFound);
    }
}
