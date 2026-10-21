using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PropertyManagement.Api.Infrastructure;

namespace PropertyManagement.IntegrationTests;

/// <summary>Phase 16: notice periods per organization and state, reminder days, and reminders to tenants (design 8.9).</summary>
public sealed class LeaseSettingsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private static async Task<Guid> PropertyInAsync(TestClient client, string state) =>
        Id(await client.PostAsync("/api/v1/properties", new
        {
            code = $"P-{Guid.NewGuid().ToString()[..6]}", name = $"Home in {state}", type = "Residential",
            addressLine = "3 Market Road", city = "Town", state, localGovernmentArea = "Central",
        }));

    /// <summary>A lease body with its own notice period replaced, null meaning none agreed.</summary>
    private static Dictionary<string, object?> Body(Guid unit, Guid tenant, DateOnly start, DateOnly end, int? noticeDays)
    {
        var json = JsonSerializer.Serialize(TestClient.LeaseBody(unit, [tenant], start, end), TestClient.Json);
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, TestClient.Json)!;
        body["noticePeriodDays"] = noticeDays;
        return body;
    }

    private static async Task<Guid> LeaseAsync(TestClient client, Guid property, DateOnly start, DateOnly end, string? tenantEmail = null,
        int? noticeDays = null)
    {
        var unit = await client.CreateUnitAsync(property, Guid.NewGuid().ToString()[..6]);
        var tenant = await client.CreateTenantAsync($"Tenant {Guid.NewGuid().ToString()[..4]}", tenantEmail);
        var lease = await client.PostAsync("/api/v1/leases", Body(unit, tenant, start, end, noticeDays));
        await client.PostAsync($"/api/v1/leases/{Id(lease)}/activate", new { });
        return Id(lease);
    }

    [Fact]
    public async Task Defaults_are_shown_until_the_organization_saves_its_own()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Default Lets");
        var settings = await admin.GetAsync("/api/v1/lease-settings");
        Assert.Equal([90, 60, 30], settings.GetProperty("staffReminderDays").EnumerateArray().Select(d => d.GetInt32()));
        Assert.False(settings.GetProperty("tenantRemindersEnabled").GetBoolean());
        Assert.Equal(0, settings.GetProperty("noticePeriods").GetArrayLength());
        Assert.Contains(settings.GetProperty("builtInNoticePeriods").EnumerateArray(),
            p => p.GetProperty("type").GetString() == "NoticeToQuit" && p.GetProperty("frequency").GetString() == "Monthly"
                 && p.GetProperty("days").GetInt32() == 30);

        var saved = await admin.PutAsync("/api/v1/lease-settings",
            new { staffReminderDays = new[] { 30, 120, 30 }, tenantRemindersEnabled = true, tenantReminderDays = new[] { 14 } });
        Assert.Equal([120, 30], saved.GetProperty("staffReminderDays").EnumerateArray().Select(d => d.GetInt32()));
        Assert.Equal([14], saved.GetProperty("tenantReminderDays").EnumerateArray().Select(d => d.GetInt32()));

        var invalid = await admin.Http.PutAsJsonAsync("/api/v1/lease-settings",
            new { staffReminderDays = new[] { 400 }, tenantRemindersEnabled = false }, TestClient.Json);
        invalid.ShouldBe(HttpStatusCode.BadRequest);
        var none = await admin.Http.PutAsJsonAsync("/api/v1/lease-settings",
            new { staffReminderDays = Array.Empty<int>(), tenantRemindersEnabled = false }, TestClient.Json);
        none.ShouldBe(HttpStatusCode.BadRequest);

        var audit = await admin.GetAsync("/api/v1/audit-logs?pageSize=100");
        Assert.Contains(audit.GetProperty("items").EnumerateArray(), a => a.GetProperty("action").GetString() == "lease_settings.updated");
    }

    [Fact]
    public async Task The_most_specific_notice_period_is_suggested_and_a_lease_period_still_wins()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Abeokuta Lets");
        var ogun = await PropertyInAsync(admin, "Ogun");
        var lagos = await PropertyInAsync(admin, "Lagos");
        var ogunLease = await LeaseAsync(admin, ogun, Today.AddDays(-100), Today.AddDays(265));
        var lagosLease = await LeaseAsync(admin, lagos, Today.AddDays(-100), Today.AddDays(265));

        await admin.PostAsync("/api/v1/lease-settings/notice-periods", new { type = "NoticeToQuit", days = 120 });
        var stateRule = await admin.PostAsync("/api/v1/lease-settings/notice-periods",
            new { state = "ogun", type = "NoticeToQuit", frequency = "Annual", days = 150, basis = "Ogun tenancy rules, per Barr. Bello" });

        var duplicate = await admin.Http.PostAsJsonAsync("/api/v1/lease-settings/notice-periods",
            new { state = "OGUN", type = "NoticeToQuit", frequency = "Annual", days = 100 }, TestClient.Json);
        duplicate.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal("notice_period_exists", await duplicate.ProblemCodeAsync());
        var tooLong = await admin.Http.PostAsJsonAsync("/api/v1/lease-settings/notice-periods",
            new { type = "RentReview", days = 1000 }, TestClient.Json);
        tooLong.ShouldBe(HttpStatusCode.BadRequest);

        var inOgun = await admin.GetAsync($"/api/v1/leases/{ogunLease}/notices/default?type=NoticeToQuit&servedOn={Today:yyyy-MM-dd}");
        Assert.Equal(150, inOgun.GetProperty("days").GetInt32());
        Assert.Contains("Barr. Bello", inOgun.GetProperty("basis").GetString());
        var inLagos = await admin.GetAsync($"/api/v1/leases/{lagosLease}/notices/default?type=NoticeToQuit&servedOn={Today:yyyy-MM-dd}");
        Assert.Equal(120, inLagos.GetProperty("days").GetInt32());

        // A notice served without an expiry date uses the same suggestion.
        var served = await admin.PostAsync($"/api/v1/leases/{ogunLease}/notices",
            new { type = "NoticeToQuit", servedOn = Today, method = "PersonalService" });
        Assert.Equal(Today.AddDays(150), DateOnly.Parse(served.GetProperty("expiresOn").GetString()!));

        await admin.PutAsync($"/api/v1/lease-settings/notice-periods/{Id(stateRule)}",
            new { state = "Ogun", type = "NoticeToQuit", frequency = "Annual", days = 160 });
        Assert.Equal(160, (await admin.GetAsync($"/api/v1/leases/{ogunLease}/notices/default?type=NoticeToQuit"))
            .GetProperty("days").GetInt32());

        var delete = await admin.Http.DeleteAsync($"/api/v1/lease-settings/notice-periods/{Id(stateRule)}");
        delete.ShouldBe(HttpStatusCode.NoContent);
        Assert.Equal(120, (await admin.GetAsync($"/api/v1/leases/{ogunLease}/notices/default?type=NoticeToQuit"))
            .GetProperty("days").GetInt32());

        // A lease with its own agreed notice period keeps it.
        var agreed = await LeaseAsync(admin, ogun, Today.AddDays(-10), Today.AddDays(355), noticeDays: 45);
        Assert.Equal(45, (await admin.GetAsync($"/api/v1/leases/{agreed}/notices/default?type=NoticeToQuit"))
            .GetProperty("days").GetInt32());
    }

    [Fact]
    public async Task Only_settings_managers_can_change_notice_periods()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Rules Lets");
        var manager = await admin.AddMemberAsync(factory, "PropertyManager");
        Assert.Equal(JsonValueKind.Object, (await manager.GetAsync("/api/v1/lease-settings")).ValueKind);
        var change = await manager.Http.PostAsJsonAsync("/api/v1/lease-settings/notice-periods",
            new { type = "NoticeToQuit", days = 10 }, TestClient.Json);
        change.ShouldBe(HttpStatusCode.Forbidden);
        var update = await manager.Http.PutAsJsonAsync("/api/v1/lease-settings",
            new { staffReminderDays = new[] { 10 }, tenantRemindersEnabled = true }, TestClient.Json);
        update.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reminders_follow_the_chosen_days_and_reach_tenants_when_turned_on()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Ibadan Lets");
        var property = await PropertyInAsync(admin, "Oyo");
        var tenantEmail = $"tenant{Guid.NewGuid():N}@tenants.example.com";
        var lease = await LeaseAsync(admin, property, Today.AddDays(-320), Today.AddDays(45), tenantEmail);
        var reference = (await admin.GetAsync($"/api/v1/leases/{lease}")).GetProperty("reference").GetString()!;
        var job = factory.Services.GetRequiredService<LeaseScheduleJob>();

        // Staff only reminded 30 days out, and tenants are off: nothing yet at 45 days.
        await admin.PutAsync("/api/v1/lease-settings", new { staffReminderDays = new[] { 30 }, tenantRemindersEnabled = false });
        await job.RunOnceAsync(CancellationToken.None);
        Assert.DoesNotContain(factory.Emails.Messages, m => m.Subject.Contains(reference) || m.To == tenantEmail);

        await admin.PutAsync("/api/v1/lease-settings",
            new { staffReminderDays = new[] { 50 }, tenantRemindersEnabled = true, tenantReminderDays = new[] { 60, 30 } });
        await job.RunOnceAsync(CancellationToken.None);
        Assert.Contains(factory.Emails.Messages, m => m.To == admin.Email && m.Subject.EndsWith($"Lease {reference} ends in 45 days"));
        var toTenant = Assert.Single(factory.Emails.Messages, m => m.To == tenantEmail);
        Assert.StartsWith("Ibadan Lets: Your tenancy ends on", toTenant.Subject);
        Assert.Contains("in 45 days", toTenant.Body);
        Assert.DoesNotContain("Lease ", toTenant.Subject);

        await job.RunOnceAsync(CancellationToken.None);
        Assert.Single(factory.Emails.Messages, m => m.To == tenantEmail);
    }
}
