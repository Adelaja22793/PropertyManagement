using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PropertyManagement.IntegrationTests;

public sealed class MembershipAndAuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Registration_creates_the_organization_with_the_founder_as_admin_and_a_trial()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Ikoyi Lettings");
        var me = await admin.Http.GetFromJsonAsync<JsonElement>("/api/v1/auth/me", TestClient.Json);
        var organization = me.GetProperty("organizations")[0];
        Assert.Equal("Ikoyi Lettings", organization.GetProperty("name").GetString());
        Assert.Contains("OrganizationAdmin", organization.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

        var settings = await admin.Http.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{admin.OrganizationId}", TestClient.Json);
        Assert.Equal("NGN", settings.GetProperty("baseCurrency").GetString());
        Assert.Equal("Africa/Lagos", settings.GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task Requests_without_a_session_are_refused()
    {
        var anonymous = TestClient.Anonymous(factory);
        (await anonymous.Http.GetAsync("/api/v1/auth/me")).ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.Http.GetAsync("/api/v1/organizations")).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task State_changing_requests_need_the_csrf_header()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Surulere Homes");
        admin.Http.DefaultRequestHeaders.Remove("X-Requested-With");
        var response = await admin.Http.PostAsJsonAsync("/api/v1/branches", new { name = "No header" });
        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Equal("csrf_header_missing", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Weak_passwords_are_rejected()
    {
        var client = TestClient.Anonymous(factory);
        var response = await client.Http.PostAsJsonAsync("/api/v1/auth/register", new
        {
            fullName = "Weak", email = $"{Guid.NewGuid():N}@example.com", password = "short", organizationName = "Weak Ltd",
        });
        response.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Account_locks_after_five_wrong_passwords()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Yaba Rentals");
        var client = TestClient.Anonymous(factory);
        for (var i = 0; i < 5; i++)
            await client.Http.PostAsJsonAsync("/api/v1/auth/login", new { email = admin.Email, password = "wrong-password-1" });

        var response = await client.Http.PostAsJsonAsync("/api/v1/auth/login",
            new { email = admin.Email, password = TestClient.Password });
        response.ShouldBe((HttpStatusCode)423);
    }

    [Fact]
    public async Task Invited_person_joins_with_the_invited_role_and_branch_scope()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Ajah Estates");
        var ikeja = (await admin.CreateBranchAsync("Ikeja")).GetProperty("id").GetGuid();
        var lekki = (await admin.CreateBranchAsync("Lekki")).GetProperty("id").GetGuid();

        var email = $"{Guid.NewGuid():N}@example.com";
        var (_, token) = await admin.InviteAsync(email, "PropertyManager", lekki);
        var manager = await TestClient.RegisterWithInvitationAsync(factory, email, token);
        Assert.Equal(admin.OrganizationId, manager.OrganizationId);

        // Scoped to Lekki, so Ikeja is invisible.
        var branches = await manager.Http.GetFromJsonAsync<JsonElement[]>("/api/v1/branches", TestClient.Json);
        Assert.Equal([lekki], branches!.Select(b => b.GetProperty("id").GetGuid()));
        (await manager.Http.GetAsync($"/api/v1/branches/{ikeja}")).ShouldBe(HttpStatusCode.NotFound);

        // A Property Manager cannot invite or manage members.
        var invite = await manager.Http.PostAsJsonAsync($"/api/v1/organizations/{admin.OrganizationId}/invitations",
            new { email = "x@example.com", role = "OrganizationAdmin" });
        invite.ShouldBe(HttpStatusCode.Forbidden);

        // The token is single-use.
        var reuse = await admin.Http.PostAsJsonAsync("/api/v1/invitations/accept", new { token });
        reuse.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_last_organization_admin_cannot_be_removed_or_demoted()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Gbagada Homes");
        var members = await admin.Http.GetFromJsonAsync<JsonElement[]>(
            $"/api/v1/organizations/{admin.OrganizationId}/members", TestClient.Json);
        var self = members!.Single().GetProperty("membershipId").GetGuid();

        var demote = await admin.Http.PatchAsJsonAsync($"/api/v1/organizations/{admin.OrganizationId}/members/{self}",
            new { roles = new[] { "Accountant" } });
        demote.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal("last_admin", await demote.ProblemCodeAsync());

        var revoke = await admin.Http.DeleteAsync($"/api/v1/organizations/{admin.OrganizationId}/members/{self}");
        revoke.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Portal_roles_cannot_be_given_through_staff_invitations()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Magodo Lettings");
        var response = await admin.Http.PostAsJsonAsync($"/api/v1/organizations/{admin.OrganizationId}/invitations",
            new { email = $"{Guid.NewGuid():N}@example.com", role = "Tenant" });
        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Equal("invalid_role", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Sensitive_actions_are_written_to_the_audit_log()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Festac Properties");
        await admin.CreateBranchAsync("Festac Town");
        await admin.InviteAsync($"{Guid.NewGuid():N}@example.com", "Accountant");

        var logs = await admin.Http.GetFromJsonAsync<JsonElement>("/api/v1/audit-logs", TestClient.Json);
        var actions = logs.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("action").GetString()).ToList();
        Assert.Contains("organization.created", actions);
        Assert.Contains("branch.created", actions);
        Assert.Contains("member.invited", actions);
        Assert.All(logs.GetProperty("items").EnumerateArray(),
            i => Assert.False(string.IsNullOrEmpty(i.GetProperty("action").GetString())));
    }
}
