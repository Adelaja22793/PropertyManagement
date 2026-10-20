using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Common;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.IntegrationTests;

/// <summary>
/// Cross-organization attack tests (design 13.4). Two companies, ABC and XYZ, each with their own
/// admin and branch. Every test tries to reach the other company's data and must be refused.
/// </summary>
public sealed class IsolationTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private TestClient _abc = null!;
    private TestClient _xyz = null!;
    private Guid _abcBranchId;
    private Guid _xyzBranchId;

    public async Task InitializeAsync()
    {
        _abc = await TestClient.RegisterWithOrganizationAsync(factory, "ABC Property Managers");
        _xyz = await TestClient.RegisterWithOrganizationAsync(factory, "XYZ Estate Services");
        _abcBranchId = (await _abc.CreateBranchAsync("ABC Ikeja")).GetProperty("id").GetGuid();
        _xyzBranchId = (await _xyz.CreateBranchAsync("XYZ Lekki")).GetProperty("id").GetGuid();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Cannot_read_another_organizations_branch_by_changing_the_id()
    {
        var response = await _abc.Http.GetAsync($"/api/v1/branches/{_xyzBranchId}");
        response.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cannot_update_another_organizations_branch()
    {
        var response = await _abc.Http.PutAsJsonAsync($"/api/v1/branches/{_xyzBranchId}", new { name = "Hijacked" });
        response.ShouldBe(HttpStatusCode.NotFound);

        var branch = await _xyz.Http.GetFromJsonAsync<JsonElement>($"/api/v1/branches/{_xyzBranchId}", TestClient.Json);
        Assert.Equal("XYZ Lekki", branch.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Branch_list_contains_only_the_active_organizations_branches()
    {
        var branches = await _abc.Http.GetFromJsonAsync<JsonElement[]>("/api/v1/branches", TestClient.Json);
        Assert.NotNull(branches);
        Assert.Contains(branches, b => b.GetProperty("id").GetGuid() == _abcBranchId);
        Assert.DoesNotContain(branches, b => b.GetProperty("id").GetGuid() == _xyzBranchId);
    }

    [Fact]
    public async Task Cannot_select_an_organization_without_a_membership()
    {
        _abc.UseOrganization(_xyz.OrganizationId);
        try
        {
            var response = await _abc.Http.GetAsync("/api/v1/branches");
            response.ShouldBe(HttpStatusCode.Forbidden);
            Assert.Equal("organization_forbidden", await response.ProblemCodeAsync());
        }
        finally
        {
            _abc.UseOwnOrganization();
        }
    }

    [Fact]
    public async Task Unknown_and_foreign_organizations_get_the_same_answer()
    {
        var foreign = TestClient.Anonymous(factory);
        await LoginAsync(foreign, _abc.Email);

        foreign.UseOrganization(_xyz.OrganizationId);
        var toForeign = await foreign.Http.GetAsync("/api/v1/branches");
        foreign.UseOrganization(Guid.NewGuid());
        var toUnknown = await foreign.Http.GetAsync("/api/v1/branches");

        toForeign.ShouldBe(HttpStatusCode.Forbidden);
        toUnknown.ShouldBe(HttpStatusCode.Forbidden);
        Assert.Equal(await toForeign.ProblemCodeAsync(), await toUnknown.ProblemCodeAsync());
    }

    [Fact]
    public async Task Cannot_list_or_invite_members_of_another_organization_through_the_url()
    {
        _abc.ClearOrganization();
        try
        {
            var members = await _abc.Http.GetAsync($"/api/v1/organizations/{_xyz.OrganizationId}/members");
            members.ShouldBe(HttpStatusCode.Forbidden);

            var invite = await _abc.Http.PostAsJsonAsync($"/api/v1/organizations/{_xyz.OrganizationId}/invitations",
                new { email = "intruder@example.com", role = "OrganizationAdmin" });
            invite.ShouldBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            _abc.UseOwnOrganization();
        }
    }

    [Fact]
    public async Task Url_and_header_must_name_the_same_organization()
    {
        // ABC's header is set; the URL names XYZ.
        var response = await _abc.Http.GetAsync($"/api/v1/organizations/{_xyz.OrganizationId}/members");
        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Equal("organization_mismatch", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Audit_logs_of_another_organization_are_not_visible()
    {
        var logs = await _abc.Http.GetFromJsonAsync<JsonElement>("/api/v1/audit-logs?pageSize=100", TestClient.Json);
        var ids = logs.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("resourceId").GetString()).ToList();
        Assert.Contains(_abcBranchId.ToString(), ids);
        Assert.DoesNotContain(_xyzBranchId.ToString(), ids);
        Assert.DoesNotContain(_xyz.OrganizationId.ToString(), ids);
    }

    [Fact]
    public async Task Revoked_membership_is_denied_on_the_next_request()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var (_, token) = await _abc.InviteAsync(email, "PropertyManager");
        var member = await TestClient.RegisterWithInvitationAsync(factory, email, token);
        (await member.Http.GetAsync("/api/v1/branches")).ShouldBe(HttpStatusCode.OK);

        var members = await _abc.Http.GetFromJsonAsync<JsonElement[]>(
            $"/api/v1/organizations/{_abc.OrganizationId}/members", TestClient.Json);
        var membershipId = members!.Single(m => m.GetProperty("email").GetString() == email)
            .GetProperty("membershipId").GetGuid();
        (await _abc.Http.DeleteAsync($"/api/v1/organizations/{_abc.OrganizationId}/members/{membershipId}"))
            .ShouldBe(HttpStatusCode.NoContent);

        // Same session cookie, very next request.
        (await member.Http.GetAsync("/api/v1/branches")).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_person_in_two_organizations_sees_each_organizations_data_separately()
    {
        var (_, token) = await _xyz.InviteAsync(_abc.Email, "PropertyManager");
        (await _abc.Http.PostAsJsonAsync("/api/v1/invitations/accept", new { token })).ShouldBe(HttpStatusCode.OK);

        _abc.UseOrganization(_xyz.OrganizationId);
        try
        {
            var xyzBranches = await _abc.Http.GetFromJsonAsync<JsonElement[]>("/api/v1/branches", TestClient.Json);
            Assert.Contains(xyzBranches!, b => b.GetProperty("id").GetGuid() == _xyzBranchId);
            Assert.DoesNotContain(xyzBranches!, b => b.GetProperty("id").GetGuid() == _abcBranchId);

            // In XYZ this person is only a Property Manager, so XYZ's audit log stays closed to them.
            (await _abc.Http.GetAsync("/api/v1/audit-logs")).ShouldBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            _abc.UseOwnOrganization();
        }
        var abcBranches = await _abc.Http.GetFromJsonAsync<JsonElement[]>("/api/v1/branches", TestClient.Json);
        Assert.DoesNotContain(abcBranches!, b => b.GetProperty("id").GetGuid() == _xyzBranchId);
    }

    [Fact]
    public async Task Cannot_scope_a_member_to_another_organizations_branch()
    {
        var response = await _abc.Http.PostAsJsonAsync($"/api/v1/organizations/{_abc.OrganizationId}/invitations",
            new { email = $"{Guid.NewGuid():N}@example.com", role = "PropertyManager", branchIds = new[] { _xyzBranchId } });
        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Equal("branch_not_found", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Invitation_can_only_be_accepted_by_the_invited_email()
    {
        var (_, token) = await _abc.InviteAsync($"{Guid.NewGuid():N}@example.com", "OrganizationAdmin");
        var response = await _xyz.Http.PostAsJsonAsync("/api/v1/invitations/accept", new { token });
        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Equal("invitation_email_mismatch", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Database_context_refuses_to_save_a_change_to_another_organizations_row()
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<ICurrentOrganization>().Set(_abc.OrganizationId, null, ["OrganizationAdmin"], []);
        var db = services.GetRequiredService<AppDbContext>();

        // Even a query that deliberately bypasses the filter cannot be used to write.
        var foreignBranch = await db.Branches.IgnoreQueryFilters().SingleAsync(b => b.Id == _xyzBranchId);
        foreignBranch.Update("Hijacked", null, false);

        await Assert.ThrowsAsync<CrossOrganizationWriteException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Queries_without_a_selected_organization_return_nothing()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Branches.CountAsync());
        Assert.Equal(0, await db.Memberships.CountAsync());
    }

    [Fact]
    public async Task Composite_foreign_key_blocks_linking_rows_across_organizations_in_the_database()
    {
        // Bypass the application completely: the database itself must refuse an ABC membership scoped to an XYZ branch.
        await using var connection = new SqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var find = connection.CreateCommand();
        find.CommandText = "SELECT TOP 1 Id FROM OrganizationMemberships WHERE OrganizationId = @org";
        find.Parameters.AddWithValue("@org", _abc.OrganizationId);
        var abcMembershipId = (Guid)(await find.ExecuteScalarAsync())!;

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO MembershipBranchScopes (Id, OrganizationId, MembershipId, BranchId)
            VALUES (NEWID(), @org, @membership, @branch)
            """;
        insert.Parameters.AddWithValue("@org", _abc.OrganizationId);
        insert.Parameters.AddWithValue("@membership", abcMembershipId);
        insert.Parameters.AddWithValue("@branch", _xyzBranchId);

        var error = await Assert.ThrowsAsync<SqlException>(() => insert.ExecuteNonQueryAsync());
        Assert.Contains("FOREIGN KEY", error.Message);
    }

    private static async Task LoginAsync(TestClient client, string email)
    {
        var response = await client.Http.PostAsJsonAsync("/api/v1/auth/login", new { email, password = TestClient.Password });
        await response.EnsureSuccessAsync();
    }
}
