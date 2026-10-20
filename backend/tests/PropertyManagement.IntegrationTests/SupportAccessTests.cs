using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace PropertyManagement.IntegrationTests;

/// <summary>Platform support access to a company's data only through a grant its admin approves (design 4.7).</summary>
public sealed class SupportAccessTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>A platform support person, with two-step sign-in on.</summary>
    private async Task<TestClient> SupportAsync()
    {
        var support = await TestClient.RegisterWithOrganizationAsync(factory, "Platform HQ");
        await using (var connection = new SqlConnection(factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE AspNetUsers SET IsPlatformAdmin = 1 WHERE Id = @user";
            command.Parameters.AddWithValue("@user", support.UserId);
            await command.ExecuteNonQueryAsync();
        }
        var setup = await support.PostAsync("/api/v1/auth/two-factor/setup", new { });
        await support.PostAsync("/api/v1/auth/two-factor/enable", new { code = TwoFactorTests.Totp(setup.GetProperty("sharedKey").GetString()!) });
        return support;
    }

    private static async Task<string> SlugAsync(TestClient admin) =>
        (await admin.GetAsync($"/api/v1/organizations/{admin.OrganizationId}")).GetProperty("slug").GetString()!;

    private static async Task<HttpResponseMessage> AsOrganizationAsync(TestClient client, Guid organizationId, Func<HttpClient, Task<HttpResponseMessage>> send)
    {
        client.UseOrganization(organizationId);
        try { return await send(client.Http); }
        finally { client.UseOwnOrganization(); }
    }

    [Fact]
    public async Task Support_sees_nothing_until_the_admin_approves_and_then_only_reads_until_it_ends()
    {
        var company = await TestClient.RegisterWithOrganizationAsync(factory, "Victoria Island Lets");
        var property = await company.CreatePropertyAsync($"VI-{Guid.NewGuid().ToString()[..4]}");
        var support = await SupportAsync();
        var properties = (HttpClient h) => h.GetAsync("/api/v1/properties");

        // No standing access.
        (await AsOrganizationAsync(support, company.OrganizationId, properties)).ShouldBe(HttpStatusCode.Forbidden);

        var before = factory.Emails.Messages.Count;
        var request = await support.PostAsync("/api/v1/platform/support-grants",
            new { organization = await SlugAsync(company), reason = "Ticket 4411: rent roll looks wrong", hours = 2 });
        Assert.Equal("Pending", request.GetProperty("status").GetString());
        Assert.Contains(factory.Emails.Messages.Skip(before), m => m.To == company.Email && m.Subject.StartsWith("Support has asked"));
        // Asking twice while one is open is refused.
        (await support.Http.PostAsJsonAsync("/api/v1/platform/support-grants",
            new { organization = await SlugAsync(company), reason = "Again", hours = 2 }, TestClient.Json)).ShouldBe(HttpStatusCode.Conflict);
        // A pending request still opens nothing.
        (await AsOrganizationAsync(support, company.OrganizationId, properties)).ShouldBe(HttpStatusCode.Forbidden);

        var id = request.GetProperty("id").GetGuid();
        var pending = await company.GetAsync("/api/v1/support-grants");
        Assert.Equal("Ticket 4411: rent roll looks wrong", Assert.Single(pending.EnumerateArray()).GetProperty("reason").GetString());
        var approved = await company.PostAsync($"/api/v1/support-grants/{id}/approve", new { });
        Assert.Equal("Active", approved.GetProperty("status").GetString());
        var until = approved.GetProperty("expiresAtUtc").GetDateTime();
        Assert.InRange(until, DateTime.UtcNow.AddHours(1.9), DateTime.UtcNow.AddHours(2.1));

        // Now support can read, but not change anything or see identity numbers.
        (await AsOrganizationAsync(support, company.OrganizationId, properties)).ShouldBe(HttpStatusCode.OK);
        (await AsOrganizationAsync(support, company.OrganizationId, h => h.GetAsync($"/api/v1/properties/{property}"))).ShouldBe(HttpStatusCode.OK);
        var write = await AsOrganizationAsync(support, company.OrganizationId,
            h => h.PostAsJsonAsync("/api/v1/branches", new { name = "Support branch", address = "Lagos" }, TestClient.Json));
        write.ShouldBe(HttpStatusCode.Forbidden);
        Assert.Equal("support_read_only", await write.ProblemCodeAsync());
        var me = await support.GetAsync("/api/v1/auth/me");
        var viaSupport = me.GetProperty("organizations").EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == company.OrganizationId);
        Assert.Equal(["PlatformSupport"], viaSupport.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.NotEqual(JsonValueKind.Null, viaSupport.GetProperty("supportAccessUntilUtc").ValueKind);
        Assert.DoesNotContain(viaSupport.GetProperty("permissions").EnumerateArray(), p => p.GetString() == "Tenants.ViewKyc");

        // The company sees every step and every look in its audit log.
        var audit = (await company.GetAsync("/api/v1/audit-logs?pageSize=100")).GetProperty("items").EnumerateArray()
            .Select(a => a.GetProperty("action").GetString()).ToList();
        Assert.Contains("support.requested", audit);
        Assert.Contains("support.approved", audit);
        Assert.Contains("support.viewed", audit);
        Assert.Contains("support.change_blocked", audit);

        // The admin ends it, and access stops at once.
        await company.PostAsync($"/api/v1/support-grants/{id}/revoke", new { });
        (await AsOrganizationAsync(support, company.OrganizationId, properties)).ShouldBe(HttpStatusCode.Forbidden);
        var mine = await support.GetAsync("/api/v1/platform/support-grants");
        Assert.Equal("Revoked", mine.EnumerateArray().Single(g => g.GetProperty("id").GetGuid() == id).GetProperty("status").GetString());
        // Their account still loads with the ended organization selected, and no longer lists it.
        var after = await AsOrganizationAsync(support, company.OrganizationId, h => h.GetAsync("/api/v1/auth/me"));
        after.ShouldBe(HttpStatusCode.OK);
        Assert.DoesNotContain((await after.Content.ReadFromJsonAsync<JsonElement>(TestClient.Json)).GetProperty("organizations").EnumerateArray(),
            o => o.GetProperty("id").GetGuid() == company.OrganizationId);
    }

    [Fact]
    public async Task Grants_expire_and_only_the_right_people_can_ask_or_answer()
    {
        var company = await TestClient.RegisterWithOrganizationAsync(factory, "Ikeja Residences");
        var slug = await SlugAsync(company);

        // Ordinary users can't ask for access.
        var stranger = await TestClient.RegisterWithOrganizationAsync(factory, "Some Landlord");
        (await stranger.Http.PostAsJsonAsync("/api/v1/platform/support-grants", new { organization = slug, reason = "Curious", hours = 1 }, TestClient.Json))
            .ShouldBe(HttpStatusCode.Forbidden);
        (await stranger.Http.GetAsync("/api/v1/platform/support-grants")).ShouldBe(HttpStatusCode.Forbidden);

        var support = await SupportAsync();
        (await support.Http.PostAsJsonAsync("/api/v1/platform/support-grants", new { organization = slug, reason = "Too long", hours = 25 }, TestClient.Json))
            .ShouldBe(HttpStatusCode.BadRequest);
        (await support.Http.PostAsJsonAsync("/api/v1/platform/support-grants", new { organization = "no-such-company", reason = "x", hours = 1 }, TestClient.Json))
            .ShouldBe(HttpStatusCode.NotFound);
        var grant = await support.PostAsync("/api/v1/platform/support-grants", new { organization = slug, reason = "Import failed", hours = 1 });
        var id = grant.GetProperty("id").GetGuid();

        // Only this company's admins answer it: an agent can't, and another company's admin doesn't even see it.
        var agent = await company.AddMemberAsync(factory, "Agent");
        (await agent.Http.PostAsJsonAsync($"/api/v1/support-grants/{id}/approve", new { }, TestClient.Json)).ShouldBe(HttpStatusCode.Forbidden);
        Assert.Empty((await stranger.GetAsync("/api/v1/support-grants")).EnumerateArray());
        (await stranger.Http.PostAsJsonAsync($"/api/v1/support-grants/{id}/approve", new { }, TestClient.Json)).ShouldBe(HttpStatusCode.NotFound);

        await company.PostAsync($"/api/v1/support-grants/{id}/approve", new { });
        (await company.Http.PostAsJsonAsync($"/api/v1/support-grants/{id}/decline", new { }, TestClient.Json)).ShouldBe(HttpStatusCode.Conflict);
        (await AsOrganizationAsync(support, company.OrganizationId, h => h.GetAsync("/api/v1/properties"))).ShouldBe(HttpStatusCode.OK);

        // Once the hour is up, the grant has run out.
        await using (var connection = new SqlConnection(factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE SupportGrants SET ExpiresAtUtc = DATEADD(hour, -1, SYSUTCDATETIME()) WHERE Id = @id";
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync();
        }
        (await AsOrganizationAsync(support, company.OrganizationId, h => h.GetAsync("/api/v1/properties"))).ShouldBe(HttpStatusCode.Forbidden);
        Assert.Equal("Expired", Assert.Single((await company.GetAsync("/api/v1/support-grants")).EnumerateArray()).GetProperty("status").GetString());

        // Support can withdraw a request nobody has answered.
        var second = await support.PostAsync("/api/v1/platform/support-grants", new { organization = slug, reason = "Follow-up", hours = 1 });
        var ended = await support.PostAsync($"/api/v1/platform/support-grants/{second.GetProperty("id").GetGuid()}/end", new { });
        Assert.Equal("Revoked", ended.GetProperty("status").GetString());
    }
}
