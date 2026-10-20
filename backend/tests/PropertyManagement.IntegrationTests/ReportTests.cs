using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace PropertyManagement.IntegrationTests;

/// <summary>Operational reports and their CSV downloads.</summary>
public sealed class ReportTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));

    private TestClient _admin = null!;
    private Guid _property;
    private Guid _occupied;
    private Guid _lease;

    public async Task InitializeAsync()
    {
        _admin = await TestClient.RegisterWithOrganizationAsync(factory, "Surulere Estates");
        _property = await _admin.CreatePropertyAsync($"SU-{Guid.NewGuid().ToString()[..4]}");
        _occupied = await _admin.CreateUnitAsync(_property, "A1");
        await _admin.CreateUnitAsync(_property, "A2");
        await _admin.CreateUnitAsync(_property, "A3", "Unavailable");
        // A tenant whose name a spreadsheet would treat as a formula.
        var tenant = await _admin.CreateTenantAsync("=HYPERLINK(\"http://evil.example\")", $"{Guid.NewGuid():N}@example.com");
        var lease = await _admin.CreateLeaseAsync(_occupied, tenant, Today.AddDays(-300), Today.AddDays(45));
        _lease = lease.GetProperty("id").GetGuid();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static List<List<JsonElement>> Rows(JsonElement report) =>
        report.GetProperty("rows").EnumerateArray().Select(r => r.EnumerateArray().ToList()).ToList();

    private static int Column(JsonElement report, string key) =>
        report.GetProperty("columns").EnumerateArray().Select((c, i) => (c, i)).Single(x => x.c.GetProperty("key").GetString() == key).i;

    private static string? Cell(JsonElement report, List<JsonElement> row, string key) =>
        row[Column(report, key)] is { ValueKind: not JsonValueKind.Null } v ? v.ToString() : null;

    [Fact]
    public async Task Occupancy_and_the_unit_register_count_let_vacant_and_unavailable_units()
    {
        var occupancy = await _admin.GetAsync($"/api/v1/reports/occupancy?propertyId={_property}");
        var row = Assert.Single(Rows(occupancy));
        Assert.Equal("3", Cell(occupancy, row, "units"));
        Assert.Equal("1", Cell(occupancy, row, "occupied"));
        Assert.Equal("1", Cell(occupancy, row, "vacant"));
        // Unavailable units don't count against occupancy: one of two lettable units is let.
        Assert.Equal(50m, row[Column(occupancy, "occupancy")].GetDecimal());

        var units = await _admin.GetAsync($"/api/v1/reports/units?propertyId={_property}");
        var a1 = Rows(units).Single(r => Cell(units, r, "unit") == "A1");
        Assert.Equal("LSE-", Cell(units, a1, "lease")![..4]);
        Assert.Equal(Today.AddDays(45).ToString("yyyy-MM-dd"), Cell(units, a1, "leaseEnds"));
        var a2 = Rows(units).Single(r => Cell(units, r, "unit") == "A2");
        Assert.Equal("Vacant", Cell(units, a2, "occupancy"));
        Assert.Equal("0", Cell(units, a2, "daysVacant"));
    }

    [Fact]
    public async Task Lease_expiries_and_notices_show_in_their_period_and_download_safely_as_csv()
    {
        await _admin.PostAsync($"/api/v1/leases/{_lease}/notices", new { type = "NoticeToQuit", servedOn = Today, method = "PersonalService" });

        var expiries = await _admin.GetAsync($"/api/v1/reports/lease-expiries?propertyId={_property}");
        var row = Assert.Single(Rows(expiries));
        Assert.Equal("45", Cell(expiries, row, "daysLeft"));
        Assert.Equal("Not started", Cell(expiries, row, "renewal"));
        Assert.StartsWith("Notice to quit", Cell(expiries, row, "notice"));
        // Outside the chosen period it isn't listed.
        var later = await _admin.GetAsync($"/api/v1/reports/lease-expiries?propertyId={_property}&from={Today.AddDays(60):yyyy-MM-dd}&to={Today.AddDays(120):yyyy-MM-dd}");
        Assert.Empty(Rows(later));

        var notices = await _admin.GetAsync($"/api/v1/reports/notices?propertyId={_property}");
        Assert.Equal("Notice to quit", Cell(notices, Assert.Single(Rows(notices)), "type"));

        (await _admin.Http.GetAsync($"/api/v1/reports/notices?from={Today:yyyy-MM-dd}&to={Today.AddDays(-1):yyyy-MM-dd}"))
            .ShouldBe(HttpStatusCode.BadRequest);
        (await _admin.Http.GetAsync($"/api/v1/reports/notices?from={Today.AddDays(-400):yyyy-MM-dd}&to={Today:yyyy-MM-dd}"))
            .ShouldBe(HttpStatusCode.BadRequest);

        var csv = await _admin.Http.GetAsync($"/api/v1/reports/lease-expiries?propertyId={_property}&format=csv");
        csv.ShouldBe(HttpStatusCode.OK);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.Contains("lease-expiries-", csv.Content.Headers.ContentDisposition!.FileName);
        var text = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync()).TrimStart('﻿');
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Lease,Property,Unit,Tenant,Ends,Days left", lines[0]);
        // The formula-like tenant name is kept as text.
        Assert.Contains("\"'=HYPERLINK(\"\"http://evil.example\"\")\"", lines[1]);

        var audit = await _admin.GetAsync("/api/v1/audit-logs?pageSize=50");
        Assert.Contains(audit.GetProperty("items").EnumerateArray(),
            a => a.GetProperty("action").GetString() == "report.exported" && a.GetProperty("resourceId").GetString() == "lease-expiries");
    }

    [Fact]
    public async Task Maintenance_letting_and_activity_summarise_the_period()
    {
        await _admin.PostAsync("/api/v1/maintenance", new
        {
            propertyId = _property, title = "Generator not starting", category = "Generator", urgency = "Emergency",
        });
        var maintenance = await _admin.GetAsync($"/api/v1/reports/maintenance?propertyId={_property}");
        var generator = Rows(maintenance).Single(r => Cell(maintenance, r, "category") == "Generator");
        Assert.Equal("1", Cell(maintenance, generator, "opened"));
        Assert.Equal("1", Cell(maintenance, generator, "openNow"));
        Assert.Contains(maintenance.GetProperty("facts").EnumerateArray(),
            f => f.GetProperty("label").GetString() == "Open emergencies" && f.GetProperty("value").GetString() == "1");

        var vacant = await _admin.CreateUnitAsync(_property, "B1");
        var listing = await _admin.PostAsync("/api/v1/listings", new
        {
            unitId = vacant, headline = "Shop on the main road", askingRent = 900_000m, currency = "NGN", billingFrequency = "Annual",
            showStreetAddress = false,
        });
        await _admin.PostAsync("/api/v1/leads", new
        {
            listingId = listing.GetProperty("id").GetGuid(), name = "Bayo", phone = "08030000001", source = "WalkIn",
        });
        var letting = await _admin.GetAsync($"/api/v1/reports/letting?propertyId={_property}");
        var shop = Rows(letting).Single(r => Cell(letting, r, "listing") == "Shop on the main road");
        Assert.Equal("1", Cell(letting, shop, "enquiries"));
        Assert.Contains(letting.GetProperty("facts").EnumerateArray(), f => f.GetProperty("label").GetString() == "From walk in");

        var activity = await _admin.GetAsync("/api/v1/reports/activity");
        var me = Rows(activity).Single(r => Cell(activity, r, "email") == _admin.Email);
        Assert.True(int.Parse(Cell(activity, me, "changes")!) > 5);
    }

    [Fact]
    public async Task Reports_follow_the_same_access_as_the_screens()
    {
        // Maintenance staff work only on jobs given to them: no reports.
        var staff = await _admin.AddMemberAsync(factory, "MaintenanceStaff");
        Assert.Empty((await staff.GetAsync("/api/v1/reports")).EnumerateArray());
        (await staff.Http.GetAsync("/api/v1/reports/occupancy")).ShouldBe(HttpStatusCode.Forbidden);

        // An agent sees property reports and letting, but not leases or the audit trail.
        var agent = await _admin.AddMemberAsync(factory, "Agent");
        var keys = (await agent.GetAsync("/api/v1/reports")).EnumerateArray().Select(r => r.GetProperty("key").GetString()).ToList();
        Assert.Contains("occupancy", keys);
        Assert.Contains("letting", keys);
        Assert.DoesNotContain("lease-expiries", keys);
        Assert.DoesNotContain("activity", keys);
        (await agent.Http.GetAsync("/api/v1/reports/activity?format=csv")).ShouldBe(HttpStatusCode.Forbidden);
        // Without lease access the unit register leaves out who is in each unit.
        var units = await agent.GetAsync("/api/v1/reports/units");
        Assert.DoesNotContain(units.GetProperty("columns").EnumerateArray(), c => c.GetProperty("key").GetString() == "tenant");

        // A property manager sees only their own properties.
        var manager = await _admin.AddMemberAsync(factory, "PropertyManager");
        var own = await _admin.CreatePropertyAsync($"PM-{Guid.NewGuid().ToString()[..4]}", managerUserId: manager.UserId);
        await _admin.CreateUnitAsync(own, "M1");
        var occupancy = await manager.GetAsync("/api/v1/reports/occupancy");
        var ownCode = (await _admin.GetAsync($"/api/v1/properties/{own}")).GetProperty("code").GetString();
        Assert.Equal([ownCode], Rows(occupancy).Select(r => Cell(occupancy, r, "code")));

        // Another company sees nothing of this one.
        var outsider = await TestClient.RegisterWithOrganizationAsync(factory, "Other Estates");
        Assert.Empty(Rows(await outsider.GetAsync("/api/v1/reports/occupancy")));
        Assert.Empty(Rows(await outsider.GetAsync("/api/v1/reports/lease-expiries")));
        Assert.DoesNotContain(Rows(await outsider.GetAsync("/api/v1/reports/activity")), r => r.Any(c => c.ToString() == _admin.Email));
        (await outsider.Http.GetAsync("/api/v1/reports/nonsense")).ShouldBe(HttpStatusCode.NotFound);
    }
}
