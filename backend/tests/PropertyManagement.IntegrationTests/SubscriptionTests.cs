using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using PropertyManagement.Api.Infrastructure;

namespace PropertyManagement.IntegrationTests;

/// <summary>Trials, lapse, suspension, cancellation and plan limits (design 4.8), and platform staff managing them.</summary>
public sealed class SubscriptionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task SqlAsync(string sql, Guid organizationId)
    {
        await using var connection = new SqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@org", organizationId);
        await command.ExecuteNonQueryAsync();
    }

    private Task RunJobAsync() => factory.Services.GetRequiredService<SubscriptionJob>().RunOnceAsync(CancellationToken.None);

    private static async Task<JsonElement> SubscriptionOfAsync(TestClient client) =>
        (await client.GetAsync("/api/v1/auth/me")).GetProperty("organizations").EnumerateArray()
        .Single(o => o.GetProperty("id").GetGuid() == client.OrganizationId).GetProperty("subscription");

    private static Task<HttpResponseMessage> NewPropertyAsync(TestClient client) =>
        client.Http.PostAsJsonAsync("/api/v1/properties", new
        {
            code = $"P-{Guid.NewGuid().ToString()[..6]}", name = "Block", type = "Residential", addressLine = "1 Road", city = "Yaba",
            state = "Lagos", localGovernmentArea = "Yaba",
        });

    private async Task<TestClient> PlatformStaffAsync()
    {
        var staff = await TestClient.RegisterWithOrganizationAsync(factory, "Platform HQ");
        await using (var connection = new SqlConnection(factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE AspNetUsers SET IsPlatformAdmin = 1 WHERE Id = @user";
            command.Parameters.AddWithValue("@user", staff.UserId);
            await command.ExecuteNonQueryAsync();
        }
        var setup = await staff.PostAsync("/api/v1/auth/two-factor/setup", new { });
        await staff.PostAsync("/api/v1/auth/two-factor/enable", new { code = TwoFactorTests.Totp(setup.GetProperty("sharedKey").GetString()!) });
        return staff;
    }

    [Fact]
    public async Task A_trial_that_ends_lapses_with_reminders_and_then_turns_read_only()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Lekki Gardens");
        var trial = await SubscriptionOfAsync(admin);
        Assert.Equal("Trial", trial.GetProperty("status").GetString());

        // A week before the trial ends, the admins hear about it, once.
        await SqlAsync("UPDATE OrganizationSubscriptions SET TrialEndsAtUtc = DATEADD(day, 3, SYSUTCDATETIME()) WHERE OrganizationId = @org",
            admin.OrganizationId);
        var before = factory.Emails.Messages.Count;
        await RunJobAsync();
        await RunJobAsync();
        Assert.Single(factory.Emails.Messages.Skip(before), m => m.To == admin.Email && m.Subject.Contains("trial ends"));

        // When it ends: past due, still fully working, with the date it turns read-only.
        await SqlAsync("UPDATE OrganizationSubscriptions SET TrialEndsAtUtc = DATEADD(day, -1, SYSUTCDATETIME()) WHERE OrganizationId = @org",
            admin.OrganizationId);
        await RunJobAsync();
        var lapsed = await SubscriptionOfAsync(admin);
        Assert.Equal("PastDue", lapsed.GetProperty("status").GetString());
        var suspends = lapsed.GetProperty("suspendsAtUtc").GetDateTime();
        Assert.InRange(suspends, DateTime.UtcNow.AddDays(12), DateTime.UtcNow.AddDays(14));
        Assert.Contains(factory.Emails.Messages, m => m.To == admin.Email && m.Subject.Contains("lapsed"));
        (await NewPropertyAsync(admin)).ShouldBe(HttpStatusCode.Created);

        // After the grace period: read-only for staff, but people can still look and manage their own account.
        await SqlAsync("UPDATE OrganizationSubscriptions SET PastDueSinceUtc = DATEADD(day, -15, SYSUTCDATETIME()) WHERE OrganizationId = @org",
            admin.OrganizationId);
        await RunJobAsync();
        Assert.Equal("Suspended", (await SubscriptionOfAsync(admin)).GetProperty("status").GetString());
        var blocked = await NewPropertyAsync(admin);
        blocked.ShouldBe(HttpStatusCode.Forbidden);
        Assert.Equal("organization_suspended", await blocked.ProblemCodeAsync());
        (await admin.Http.GetAsync("/api/v1/properties")).ShouldBe(HttpStatusCode.OK);
        (await admin.Http.PostAsJsonAsync("/api/v1/auth/two-factor/setup", new { })).ShouldBe(HttpStatusCode.OK);
        Assert.Contains(factory.Emails.Messages, m => m.To == admin.Email && m.Subject.Contains("read-only"));

        var audit = await admin.GetAsync("/api/v1/audit-logs?pageSize=100");
        var actions = audit.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("action").GetString()).ToList();
        Assert.Contains("subscription.past_due", actions);
        Assert.Contains("subscription.suspended", actions);
    }

    [Fact]
    public async Task The_plan_limits_staff_and_units_but_never_owner_or_tenant_access()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Gbagada Flats");
        var plan = await admin.GetAsync("/api/v1/subscription");
        Assert.Equal("starter", plan.GetProperty("planCode").GetString());
        Assert.Equal(5, plan.GetProperty("maxUsers").GetInt32());

        // The admin and four invitations fill the five places on the Starter plan.
        for (var i = 0; i < 4; i++) await admin.InviteAsync($"{Guid.NewGuid():N}@example.com", "Agent");
        Assert.Equal(5, (await admin.GetAsync("/api/v1/subscription")).GetProperty("staffUsed").GetInt32());
        var full = await admin.Http.PostAsJsonAsync($"/api/v1/organizations/{admin.OrganizationId}/invitations",
            new { email = $"{Guid.NewGuid():N}@example.com", role = "Agent" });
        full.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Equal("plan_limit_users", await full.ProblemCodeAsync());

        // Tenants can still be given portal access.
        var tenant = await admin.CreateTenantAsync("Kemi Ade", $"{Guid.NewGuid():N}@example.com");
        (await admin.Http.PostAsJsonAsync($"/api/v1/tenants/{tenant}/portal-invite", new { })).ShouldBe(HttpStatusCode.Created);

        // Units stop at the plan's limit.
        var property = await admin.CreatePropertyAsync($"GF-{Guid.NewGuid().ToString()[..4]}");
        for (var i = 0; i < 50; i++) await admin.CreateUnitAsync(property, $"U{i}");
        var tooMany = await admin.Http.PostAsJsonAsync($"/api/v1/properties/{property}/units",
            new { code = "EXTRA", type = "Flat", defaultRent = 1_000_000m, currency = "NGN", billingFrequency = "Annual", occupancy = "Vacant" });
        tooMany.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Equal("plan_limit_units", await tooMany.ProblemCodeAsync());
    }

    [Fact]
    public async Task Platform_staff_manage_a_companys_subscription_and_a_cancelled_company_can_only_export()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Ojota Homes");
        await admin.CreatePropertyAsync($"OH-{Guid.NewGuid().ToString()[..4]}");
        var staff = await PlatformStaffAsync();
        var path = $"/api/v1/platform/organizations/{admin.OrganizationId}";

        // Only platform staff.
        (await admin.Http.GetAsync("/api/v1/platform/organizations")).ShouldBe(HttpStatusCode.Forbidden);
        var listed = (await staff.GetAsync("/api/v1/platform/organizations?search=Ojota")).EnumerateArray()
            .Single(o => o.GetProperty("id").GetGuid() == admin.OrganizationId);
        Assert.Equal(1, listed.GetProperty("staff").GetInt32());

        var until = DateTime.UtcNow.AddDays(60);
        var extended = await staff.PostAsync($"{path}/extend-trial", new { until, note = "Demo next week" });
        Assert.Equal("Trial", extended.GetProperty("subscription").GetProperty("status").GetString());
        var active = await staff.PostAsync($"{path}/activate", new { planCode = "growth", until = DateTime.UtcNow.AddYears(1), note = "Paid by transfer" });
        Assert.Equal("Active", active.GetProperty("subscription").GetProperty("status").GetString());
        Assert.Equal(25, (await admin.GetAsync("/api/v1/subscription")).GetProperty("maxUsers").GetInt32());

        var noReason = await staff.Http.PostAsJsonAsync($"{path}/suspend", new { reason = " " });
        Assert.Equal("reason_required", await noReason.ProblemCodeAsync());
        await staff.PostAsync($"{path}/suspend", new { reason = "Chargeback" });
        (await NewPropertyAsync(admin)).ShouldBe(HttpStatusCode.Forbidden);
        var audit = await admin.GetAsync("/api/v1/audit-logs?pageSize=100");
        var actions = audit.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("action").GetString()).ToList();
        Assert.Contains("platform.trial_extended", actions);
        Assert.Contains("platform.subscription_activated", actions);
        Assert.Contains("platform.subscription_suspended", actions);

        // Cancelled: only the data export and the subscription stay open, for 90 days.
        await staff.PostAsync($"{path}/cancel", new { reason = "Customer left" });
        var closed = await admin.Http.GetAsync("/api/v1/properties");
        closed.ShouldBe(HttpStatusCode.Forbidden);
        Assert.Equal("organization_cancelled", await closed.ProblemCodeAsync());
        (await admin.Http.GetAsync("/api/v1/subscription")).ShouldBe(HttpStatusCode.OK);
        (await admin.Http.GetAsync("/api/v1/exports/organization")).ShouldBe(HttpStatusCode.OK);
        var exportUntil = (await SubscriptionOfAsync(admin)).GetProperty("exportUntilUtc").GetDateTime();
        Assert.InRange(exportUntil, DateTime.UtcNow.AddDays(89), DateTime.UtcNow.AddDays(91));
        var extendCancelled = await staff.Http.PostAsJsonAsync($"{path}/extend-trial", new { until });
        Assert.Equal("subscription_cancelled", await extendCancelled.ProblemCodeAsync());

        // Reinstating with a paid-up date opens it again.
        await staff.PostAsync($"{path}/activate", new { planCode = "starter", until = DateTime.UtcNow.AddMonths(1) });
        (await NewPropertyAsync(admin)).ShouldBe(HttpStatusCode.Created);
    }
}
