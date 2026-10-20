using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PropertyManagement.IntegrationTests;

/// <summary>Roles an organization defines itself from the permission list (design 5).</summary>
public sealed class CustomRoleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> CreateRoleAsync(TestClient client, string name, params string[] permissions) =>
        await client.PostAsync("/api/v1/roles", new { name, description = $"{name} staff", permissions });

    private static JsonElement Role(JsonElement roles, string key) =>
        roles.EnumerateArray().Single(r => r.GetProperty("key").GetString() == key);

    private static async Task<JsonElement> MyOrganizationAsync(TestClient client) =>
        (await client.GetAsync("/api/v1/auth/me")).GetProperty("organizations").EnumerateArray()
        .Single(o => o.GetProperty("id").GetGuid() == client.OrganizationId);

    [Fact]
    public async Task A_member_with_a_custom_role_can_do_exactly_what_the_role_allows()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Surulere Lettings");
        var role = await CreateRoleAsync(admin, "Front desk", "Organization.View", "Properties.View", "Tenants.View", "Tenants.Create");
        var key = role.GetProperty("key").GetString()!;
        Assert.StartsWith("custom:", key);
        Assert.False(role.GetProperty("requiresTwoFactor").GetBoolean());

        var desk = await admin.AddMemberAsync(factory, key);
        await desk.CreateTenantAsync("Ngozi Obi", $"{Guid.NewGuid():N}@example.com");
        (await desk.Http.GetAsync("/api/v1/tenants")).ShouldBe(HttpStatusCode.OK);
        (await desk.Http.GetAsync("/api/v1/properties")).ShouldBe(HttpStatusCode.OK);
        (await desk.Http.GetAsync("/api/v1/leases")).ShouldBe(HttpStatusCode.Forbidden);
        (await desk.Http.PostAsJsonAsync("/api/v1/properties", new { code = "X1" })).ShouldBe(HttpStatusCode.Forbidden);

        var mine = await MyOrganizationAsync(desk);
        Assert.Equal("Front desk", mine.GetProperty("roleNames").GetProperty(key).GetString());
        Assert.Contains("Tenants.Create", mine.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(1, Role(await admin.GetAsync("/api/v1/roles"), key).GetProperty("memberCount").GetInt32());
        var team = await admin.GetAsync($"/api/v1/organizations/{admin.OrganizationId}/members");
        Assert.Contains(team.EnumerateArray(), m => m.GetProperty("userId").GetGuid() == desk.UserId);

        // A change to the role applies on the member's next request.
        var id = role.GetProperty("id").GetGuid();
        await admin.PutAsync($"/api/v1/roles/{id}", new
        {
            name = "Front desk", permissions = new[] { "Organization.View", "Properties.View", "Tenants.View", "Tenants.Create", "Leases.View" },
        });
        (await desk.Http.GetAsync("/api/v1/leases")).ShouldBe(HttpStatusCode.OK);
        Assert.Equal(JsonValueKind.Null, (await MyOrganizationAsync(desk)).GetProperty("twoFactorRequiredSinceUtc").ValueKind);

        // Admin powers bring the two-step sign-in requirement with them.
        var updated = await admin.PutAsync($"/api/v1/roles/{id}", new { name = "Front desk", permissions = new[] { "Users.View", "Users.Manage" } });
        Assert.True(updated.GetProperty("requiresTwoFactor").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, (await MyOrganizationAsync(desk)).GetProperty("twoFactorRequiredSinceUtc").ValueKind);
        Assert.True((await desk.GetAsync("/api/v1/auth/two-factor")).GetProperty("required").GetBoolean());

        var audit = await admin.GetAsync("/api/v1/audit-logs?pageSize=100");
        var actions = audit.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("action").GetString()).ToList();
        Assert.Contains("role.created", actions);
        Assert.Contains("role.updated", actions);
    }

    [Fact]
    public async Task Nobody_can_give_a_role_or_a_member_more_than_they_hold_themselves()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Ajah Homes");
        var lead = await CreateRoleAsync(admin, "Team lead", "Organization.View", "Users.View", "Users.Manage", "Properties.View");
        var leadKey = lead.GetProperty("key").GetString()!;
        var teamLead = await admin.AddMemberAsync(factory, leadKey);

        var tooMuch = await teamLead.Http.PostAsJsonAsync("/api/v1/roles", new { name = "Builder", permissions = new[] { "Properties.Create" } });
        tooMuch.ShouldBe(HttpStatusCode.Forbidden);
        var asAdmin = await teamLead.Http.PostAsJsonAsync($"/api/v1/organizations/{admin.OrganizationId}/invitations",
            new { email = $"{Guid.NewGuid():N}@example.com", role = "OrganizationAdmin" });
        asAdmin.ShouldBe(HttpStatusCode.Forbidden);

        // Within what they hold, they can.
        var viewer = await teamLead.PostAsync("/api/v1/roles", new { name = "Viewer", permissions = new[] { "Organization.View", "Properties.View" } });
        await teamLead.InviteAsync($"{Guid.NewGuid():N}@example.com", viewer.GetProperty("key").GetString()!);
    }

    [Fact]
    public async Task Roles_are_checked_named_and_removed_safely_and_stay_inside_their_organization()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Ikorodu Estates");
        var unknown = await admin.Http.PostAsJsonAsync("/api/v1/roles", new { name = "Odd", permissions = new[] { "Rockets.Launch" } });
        unknown.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Equal("unknown_permission", await unknown.ProblemCodeAsync());
        var builtInName = await admin.Http.PostAsJsonAsync("/api/v1/roles", new { name = "Accountant", permissions = new[] { "Organization.View" } });
        Assert.Equal("role_name_taken", await builtInName.ProblemCodeAsync());

        var role = await CreateRoleAsync(admin, "Caretaker", "Organization.View", "Maintenance.UpdateAssigned");
        var key = role.GetProperty("key").GetString()!;
        var id = role.GetProperty("id").GetGuid();
        var duplicate = await admin.Http.PostAsJsonAsync("/api/v1/roles", new { name = "Caretaker", permissions = new[] { "Organization.View" } });
        Assert.Equal("role_name_taken", await duplicate.ProblemCodeAsync());

        // Another company can neither see the role nor hand it out.
        var other = await TestClient.RegisterWithOrganizationAsync(factory, "Other Estates");
        (await other.Http.GetAsync($"/api/v1/roles/{id}")).ShouldBe(HttpStatusCode.NotFound);
        Assert.DoesNotContain((await other.GetAsync("/api/v1/roles")).EnumerateArray(), r => r.GetProperty("key").GetString() == key);
        var stolen = await other.Http.PostAsJsonAsync($"/api/v1/organizations/{other.OrganizationId}/invitations",
            new { email = $"{Guid.NewGuid():N}@example.com", role = key });
        Assert.Equal("invalid_role", await stolen.ProblemCodeAsync());

        // A role in use can't be deleted until its members have another role.
        var caretaker = await admin.AddMemberAsync(factory, key);
        var inUse = await admin.Http.DeleteAsync($"/api/v1/roles/{id}");
        inUse.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Equal("role_in_use", await inUse.ProblemCodeAsync());
        var members = await admin.GetAsync($"/api/v1/organizations/{admin.OrganizationId}/members");
        var membershipId = members.EnumerateArray().Single(m => m.GetProperty("userId").GetGuid() == caretaker.UserId).GetProperty("membershipId").GetGuid();
        (await admin.Http.PatchAsJsonAsync($"/api/v1/organizations/{admin.OrganizationId}/members/{membershipId}",
            new { roles = new[] { "MaintenanceStaff" } })).ShouldBe(HttpStatusCode.OK);
        (await admin.Http.DeleteAsync($"/api/v1/roles/{id}")).ShouldBe(HttpStatusCode.NoContent);
        (await admin.Http.GetAsync($"/api/v1/roles/{id}")).ShouldBe(HttpStatusCode.NotFound);

        var builtIn = Role(await admin.GetAsync("/api/v1/roles"), "OrganizationAdmin");
        Assert.False(builtIn.GetProperty("isCustom").GetBoolean());
        Assert.True(builtIn.GetProperty("requiresTwoFactor").GetBoolean());
        var groups = await admin.GetAsync("/api/v1/roles/permissions");
        Assert.Contains(groups.EnumerateArray(), g => g.GetProperty("group").GetString() == "Tenants"
            && g.GetProperty("permissions").EnumerateArray().Any(p => p.GetString() == "Tenants.ViewKyc"));
    }
}
