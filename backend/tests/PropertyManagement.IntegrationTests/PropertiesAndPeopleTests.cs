using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace PropertyManagement.IntegrationTests;

/// <summary>Phase 3: properties, units, ownership, owners and tenants, within one organization.</summary>
public sealed class PropertiesAndPeopleTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private TestClient _admin = null!;

    public async Task InitializeAsync() => _admin = await TestClient.RegisterWithOrganizationAsync(factory, "Lekki Homes");

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Property_and_units_can_be_created_listed_and_updated()
    {
        var propertyId = await _admin.CreatePropertyAsync("lkh-01");
        await _admin.CreateUnitAsync(propertyId, "A1", "Occupied");
        await _admin.CreateUnitAsync(propertyId, "A2");

        var detail = await _admin.GetAsync($"/api/v1/properties/{propertyId}");
        Assert.Equal("LKH-01", detail.GetProperty("code").GetString());
        Assert.Equal(2, detail.GetProperty("unitCount").GetInt32());
        Assert.Equal(1, detail.GetProperty("occupiedUnitCount").GetInt32());

        var list = await _admin.GetAsync("/api/v1/properties?search=LKH-01");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());

        var units = await _admin.GetAsync($"/api/v1/properties/{propertyId}/units");
        Assert.Equal(["A1", "A2"], units.EnumerateArray().Select(u => u.GetProperty("code").GetString()));
        Assert.Equal(4_500_000m, units[0].GetProperty("defaultRent").GetDecimal());
    }

    [Fact]
    public async Task Duplicate_property_and_unit_codes_are_refused()
    {
        var propertyId = await _admin.CreatePropertyAsync("DUP-1");
        var again = await _admin.Http.PostAsJsonAsync("/api/v1/properties", new
        {
            code = "dup-1", name = "Another", type = "Residential", addressLine = "1 Road", state = "Lagos",
        });
        again.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal("duplicate_code", await again.ProblemCodeAsync());

        await _admin.CreateUnitAsync(propertyId, "B1");
        var unit = await _admin.Http.PostAsJsonAsync($"/api/v1/properties/{propertyId}/units", new
        {
            code = "b1", type = "Flat", defaultRent = 1m, billingFrequency = "Annual",
        });
        unit.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Property_with_an_occupied_unit_cannot_be_archived()
    {
        var propertyId = await _admin.CreatePropertyAsync("ARC-1");
        var unitId = await _admin.CreateUnitAsync(propertyId, "1", "Occupied");

        var response = await _admin.Http.PostAsync($"/api/v1/properties/{propertyId}/archive", null);
        response.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal("unit_occupied", await response.ProblemCodeAsync());

        var unit = await _admin.GetAsync($"/api/v1/units/{unitId}");
        await _admin.PutAsync($"/api/v1/units/{unitId}", new
        {
            code = "1", type = "Flat", defaultRent = unit.GetProperty("defaultRent").GetDecimal(),
            billingFrequency = "Annual", occupancyStatus = "Vacant",
        });
        var archived = await _admin.PostAsync($"/api/v1/properties/{propertyId}/archive", new { });
        Assert.Equal("Archived", archived.GetProperty("status").GetString());

        var hidden = await _admin.GetAsync("/api/v1/properties?search=ARC-1");
        Assert.Equal(0, hidden.GetProperty("totalCount").GetInt32());
        var addUnit = await _admin.Http.PostAsJsonAsync($"/api/v1/properties/{propertyId}/units", new
        {
            code = "2", type = "Flat", defaultRent = 1m, billingFrequency = "Annual",
        });
        addUnit.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Ownership_shares_must_total_100_percent()
    {
        var propertyId = await _admin.CreatePropertyAsync("OWN-1");
        var mrs = await _admin.CreateOwnerAsync("Mrs Adebayo");
        var mr = await _admin.CreateOwnerAsync("Mr Okafor");

        var bad = await _admin.Http.PutAsJsonAsync($"/api/v1/properties/{propertyId}/owners", new
        {
            effectiveFrom = "2026-01-01",
            shares = new[] { new { ownerId = mrs, sharePercent = 60m }, new { ownerId = mr, sharePercent = 30m } },
        });
        bad.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Equal("ownership_invalid", await bad.ProblemCodeAsync());

        var ok = await _admin.PutAsync($"/api/v1/properties/{propertyId}/owners", new
        {
            effectiveFrom = "2026-01-01",
            shares = new[] { new { ownerId = mrs, sharePercent = 60m }, new { ownerId = mr, sharePercent = 40m } },
        });
        Assert.Equal(2, ok.GetProperty("current").GetArrayLength());
    }

    [Fact]
    public async Task Changing_owners_from_a_date_ends_the_previous_shares_the_day_before()
    {
        var propertyId = await _admin.CreatePropertyAsync("OWN-2");
        var seller = await _admin.CreateOwnerAsync("Seller");
        var buyer = await _admin.CreateOwnerAsync("Buyer");
        await _admin.PutAsync($"/api/v1/properties/{propertyId}/owners", new
        {
            effectiveFrom = "2025-01-01", shares = new[] { new { ownerId = seller, sharePercent = 100m } },
        });
        var after = await _admin.PutAsync($"/api/v1/properties/{propertyId}/owners", new
        {
            effectiveFrom = "2026-03-01", shares = new[] { new { ownerId = buyer, sharePercent = 100m } },
        });

        var current = after.GetProperty("current").EnumerateArray().ToList();
        Assert.Single(current);
        Assert.Equal(buyer, current[0].GetProperty("ownerId").GetGuid());
        var old = after.GetProperty("history").EnumerateArray().Single(h => h.GetProperty("ownerId").GetGuid() == seller);
        Assert.Equal("2026-02-28", old.GetProperty("effectiveTo").GetString());

        var owner = await _admin.GetAsync($"/api/v1/owners/{buyer}");
        Assert.Equal("OWN-2", owner.GetProperty("properties")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Bank_account_needs_a_second_person_to_approve_it()
    {
        var ownerId = await _admin.CreateOwnerAsync("Chief Eze");
        var account = await _admin.PostAsync($"/api/v1/owners/{ownerId}/bank-accounts", new
        {
            bankName = "GTBank", accountNumber = "0123456789", accountName = "Chief Eze",
        });
        Assert.Equal("PendingApproval", account.GetProperty("status").GetString());
        var accountId = account.GetProperty("id").GetGuid();

        var self = await _admin.Http.PostAsJsonAsync($"/api/v1/owners/{ownerId}/bank-accounts/{accountId}/decision", new { approve = true });
        self.ShouldBe(HttpStatusCode.Forbidden);
        Assert.Equal("maker_checker", await self.ProblemCodeAsync());

        var accountant = await _admin.AddMemberAsync(factory, "Accountant");
        var noRight = await accountant.Http.PostAsJsonAsync($"/api/v1/owners/{ownerId}/bank-accounts/{accountId}/decision", new { approve = true });
        noRight.ShouldBe(HttpStatusCode.Forbidden);

        var approver = await _admin.AddMemberAsync(factory, "FinanceApprover");
        var approved = await approver.PostAsync($"/api/v1/owners/{ownerId}/bank-accounts/{accountId}/decision", new { approve = true });
        Assert.Equal("Approved", approved.GetProperty("status").GetString());

        // A replacement account, once approved, supersedes the first.
        var second = await accountant.PostAsync($"/api/v1/owners/{ownerId}/bank-accounts", new
        {
            bankName = "Access Bank", accountNumber = "9876543210", accountName = "Chief Eze",
        });
        await approver.PostAsync($"/api/v1/owners/{ownerId}/bank-accounts/{second.GetProperty("id").GetGuid()}/decision", new { approve = true });
        var accounts = await _admin.GetAsync($"/api/v1/owners/{ownerId}/bank-accounts");
        Assert.Equal(["Approved", "Replaced"], accounts.EnumerateArray().Select(a => a.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Bank_account_numbers_must_be_ten_digits()
    {
        var ownerId = await _admin.CreateOwnerAsync("Short Number");
        var response = await _admin.Http.PostAsJsonAsync($"/api/v1/owners/{ownerId}/bank-accounts", new
        {
            bankName = "UBA", accountNumber = "12345", accountName = "Short Number",
        });
        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Equal("invalid_account_number", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Overlapping_management_agreements_are_refused()
    {
        var ownerId = await _admin.CreateOwnerAsync("Agreement Owner");
        var terms = new
        {
            feeBasis = "PercentOfRentCollected", feeValue = 10m, payoutFrequency = "Monthly",
            advanceRentRelease = "OnReceipt", reserveAmount = 50_000m, startDate = "2026-01-01", endDate = (string?)"2026-12-31",
        };
        await _admin.PostAsync($"/api/v1/owners/{ownerId}/agreements", terms);
        var overlap = await _admin.Http.PostAsJsonAsync($"/api/v1/owners/{ownerId}/agreements", terms with { startDate = "2026-06-01", endDate = null });
        overlap.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal("agreement_overlap", await overlap.ProblemCodeAsync());
    }

    [Fact]
    public async Task Identity_numbers_are_encrypted_and_shown_only_with_ViewKyc()
    {
        var tenantId = await _admin.CreateTenantAsync("Ngozi Obi");
        var summary = await _admin.PutAsync($"/api/v1/tenants/{tenantId}/kyc", new
        {
            idType = "Nin", idNumber = "12345678901", bvn = "22233344455",
        });
        Assert.Equal("8901", summary.GetProperty("idNumberLast4").GetString());

        // Stored encrypted: the plain number appears nowhere in the table.
        await using (var connection = new SqlConnection(factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM TenantKyc WHERE IdNumberEncrypted LIKE '%12345678901%' OR BvnEncrypted LIKE '%22233344455%'";
            Assert.Equal(0, (int)(await command.ExecuteScalarAsync())!);
        }

        var manager = await _admin.AddMemberAsync(factory, "PropertyManager");
        var detail = await manager.GetAsync($"/api/v1/tenants/{tenantId}");
        Assert.Equal("8901", detail.GetProperty("kyc").GetProperty("idNumberLast4").GetString());
        Assert.DoesNotContain("12345678901", detail.GetRawText());
        (await manager.Http.GetAsync($"/api/v1/tenants/{tenantId}/kyc")).ShouldBe(HttpStatusCode.Forbidden);

        var full = await _admin.GetAsync($"/api/v1/tenants/{tenantId}/kyc");
        Assert.Equal("12345678901", full.GetProperty("idNumber").GetString());
        Assert.Equal("22233344455", full.GetProperty("bvn").GetString());

        var audit = await _admin.GetAsync("/api/v1/audit-logs?pageSize=100");
        Assert.Contains(audit.GetProperty("items").EnumerateArray(), a => a.GetProperty("action").GetString() == "tenant.kyc_viewed");
        Assert.DoesNotContain("12345678901", audit.GetRawText());
    }

    [Fact]
    public async Task A_NIN_must_be_eleven_digits()
    {
        var tenantId = await _admin.CreateTenantAsync("Bad Nin");
        var response = await _admin.Http.PutAsJsonAsync($"/api/v1/tenants/{tenantId}/kyc", new { idType = "Nin", idNumber = "1234" });
        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Equal("invalid_nin", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Guarantors_and_screening_checks_are_recorded_against_the_tenant()
    {
        var tenantId = await _admin.CreateTenantAsync("Tunde Bakare");
        var guarantor = await _admin.PostAsync($"/api/v1/tenants/{tenantId}/guarantors", new
        {
            fullName = "Alhaji Bakare", phone = "08031234567", relationship = "Father", signedFormReceived = true,
        });
        await _admin.PostAsync($"/api/v1/tenants/{tenantId}/screening", new { type = "Employer", outcome = "Passed", notes = "Confirmed by HR" });

        var guarantors = await _admin.GetAsync($"/api/v1/tenants/{tenantId}/guarantors");
        Assert.Equal("Alhaji Bakare", guarantors[0].GetProperty("fullName").GetString());
        var checks = await _admin.GetAsync($"/api/v1/tenants/{tenantId}/screening");
        Assert.Equal("Passed", checks[0].GetProperty("outcome").GetString());
        Assert.False(string.IsNullOrEmpty(checks[0].GetProperty("checkedByName").GetString()));

        var delete = await _admin.Http.DeleteAsync($"/api/v1/tenants/{tenantId}/guarantors/{guarantor.GetProperty("id").GetGuid()}");
        delete.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Duplicate_tenant_email_is_refused()
    {
        await _admin.CreateTenantAsync("First", "same@tenants.example.com");
        var response = await _admin.Http.PostAsJsonAsync("/api/v1/tenants", new
        {
            type = "Individual", name = "Second", email = "SAME@tenants.example.com",
        });
        response.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal("duplicate_tenant", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Branch_managers_see_and_create_properties_only_in_their_branches()
    {
        var ikeja = (await _admin.CreateBranchAsync("Ikeja")).GetProperty("id").GetGuid();
        var lekki = (await _admin.CreateBranchAsync("Lekki")).GetProperty("id").GetGuid();
        var inIkeja = await _admin.CreatePropertyAsync("IKJ-1", ikeja);
        var inLekki = await _admin.CreatePropertyAsync("LEK-1", lekki);
        var lekkiUnit = await _admin.CreateUnitAsync(inLekki, "1");

        var manager = await _admin.AddMemberAsync(factory, "BranchManager", ikeja);
        var list = await manager.GetAsync("/api/v1/properties?pageSize=100");
        var codes = list.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("code").GetString()).ToList();
        Assert.Contains("IKJ-1", codes);
        Assert.DoesNotContain("LEK-1", codes);

        (await manager.Http.GetAsync($"/api/v1/properties/{inLekki}")).ShouldBe(HttpStatusCode.NotFound);
        (await manager.Http.GetAsync($"/api/v1/units/{lekkiUnit}")).ShouldBe(HttpStatusCode.NotFound);
        (await manager.Http.GetAsync($"/api/v1/properties/{inIkeja}")).ShouldBe(HttpStatusCode.OK);

        var wrongBranch = await manager.Http.PostAsJsonAsync("/api/v1/properties", new
        {
            code = "LEK-2", name = "Not mine", type = "Residential", addressLine = "1 Road", state = "Lagos", branchId = lekki,
        });
        wrongBranch.ShouldBe(HttpStatusCode.Forbidden);

        var noBranch = await manager.Http.PostAsJsonAsync("/api/v1/properties", new
        {
            code = "IKJ-2", name = "No branch", type = "Residential", addressLine = "1 Road", state = "Lagos",
        });
        noBranch.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Equal("branch_required", await noBranch.ProblemCodeAsync());
    }

    [Fact]
    public async Task Property_managers_see_only_properties_assigned_to_them()
    {
        var manager = await _admin.AddMemberAsync(factory, "PropertyManager");
        var assigned = await _admin.CreatePropertyAsync("PM-MINE", managerUserId: manager.UserId);
        var other = await _admin.CreatePropertyAsync("PM-OTHER");

        var list = await manager.GetAsync("/api/v1/properties?pageSize=100");
        Assert.Equal(["PM-MINE"], list.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("code").GetString()));
        (await manager.Http.GetAsync($"/api/v1/properties/{other}")).ShouldBe(HttpStatusCode.NotFound);
        await manager.CreateUnitAsync(assigned, "1");
        var create = await manager.Http.PostAsJsonAsync("/api/v1/properties", new
        {
            code = "PM-NEW", name = "New", type = "Residential", addressLine = "1 Road", state = "Lagos",
        });
        create.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Manager_must_be_a_staff_member_of_the_organization()
    {
        var stranger = await TestClient.RegisterWithOrganizationAsync(factory, "Someone Else Ltd");
        var response = await _admin.Http.PostAsJsonAsync("/api/v1/properties", new
        {
            code = "MGR-1", name = "Managed", type = "Residential", addressLine = "1 Road", state = "Lagos",
            managerUserId = stranger.UserId,
        });
        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Equal("manager_not_found", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Owner_portal_invitation_links_the_new_login_to_the_owner()
    {
        var email = $"{Guid.NewGuid():N}@landlord.example.com";
        var ownerId = await _admin.CreateOwnerAsync("Portal Owner", email);
        var invite = await _admin.PostAsync($"/api/v1/owners/{ownerId}/portal-invite", new { });
        Assert.Equal("Owner", invite.GetProperty("invitation").GetProperty("role").GetString());

        var owner = await TestClient.RegisterWithInvitationAsync(factory, email, invite.GetProperty("acceptToken").GetString()!);
        // Portal users have no staff permissions.
        (await owner.Http.GetAsync("/api/v1/properties")).ShouldBe(HttpStatusCode.Forbidden);
        (await owner.Http.GetAsync("/api/v1/tenants")).ShouldBe(HttpStatusCode.Forbidden);

        var detail = await _admin.GetAsync($"/api/v1/owners/{ownerId}");
        Assert.True(detail.GetProperty("hasPortalAccess").GetBoolean());
        var members = await _admin.GetAsync($"/api/v1/organizations/{_admin.OrganizationId}/members");
        Assert.DoesNotContain(members.EnumerateArray(), m => m.GetProperty("userId").GetGuid() == owner.UserId);

        var again = await _admin.Http.PostAsync($"/api/v1/owners/{ownerId}/portal-invite", null);
        again.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal("portal_exists", await again.ProblemCodeAsync());
    }

    [Fact]
    public async Task Staff_member_who_is_also_a_tenant_keeps_staff_roles_and_gains_the_portal_link()
    {
        var accountant = await _admin.AddMemberAsync(factory, "Accountant");
        var tenantId = await _admin.CreateTenantAsync("Staff Tenant", accountant.Email);
        var invite = await _admin.PostAsync($"/api/v1/tenants/{tenantId}/portal-invite", new { });

        var accept = await accountant.Http.PostAsJsonAsync("/api/v1/invitations/accept",
            new { token = invite.GetProperty("acceptToken").GetString() });
        await accept.EnsureSuccessAsync();

        var members = await _admin.GetAsync($"/api/v1/organizations/{_admin.OrganizationId}/members");
        var me = members.EnumerateArray().Single(m => m.GetProperty("userId").GetGuid() == accountant.UserId);
        Assert.Equal(["Accountant", "Tenant"], me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.True((await _admin.GetAsync($"/api/v1/tenants/{tenantId}")).GetProperty("hasPortalAccess").GetBoolean());
    }

    [Fact]
    public async Task Changes_are_written_to_the_audit_log()
    {
        var propertyId = await _admin.CreatePropertyAsync("AUD-1");
        await _admin.CreateUnitAsync(propertyId, "1");
        await _admin.CreateOwnerAsync("Audit Owner");

        var audit = await _admin.GetAsync("/api/v1/audit-logs?pageSize=100");
        var actions = audit.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("action").GetString()).ToList();
        Assert.Contains("property.created", actions);
        Assert.Contains("unit.created", actions);
        Assert.Contains("owner.created", actions);
    }

    [Fact]
    public async Task Adding_a_role_to_an_existing_member_saves()
    {
        // Regression: a role added to an already-saved membership used to be sent as an UPDATE and fail.
        var agent = await _admin.AddMemberAsync(factory, "Agent");
        var members = await _admin.GetAsync($"/api/v1/organizations/{_admin.OrganizationId}/members");
        var membershipId = members.EnumerateArray().Single(m => m.GetProperty("userId").GetGuid() == agent.UserId)
            .GetProperty("membershipId").GetGuid();
        var response = await _admin.Http.PatchAsJsonAsync(
            $"/api/v1/organizations/{_admin.OrganizationId}/members/{membershipId}", new { roles = new[] { "Agent", "PropertyManager" } });
        await response.EnsureSuccessAsync();
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>(TestClient.Json);
        Assert.Equal(["Agent", "PropertyManager"], updated.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Manager_options_list_staff_but_not_portal_users()
    {
        var manager = await _admin.AddMemberAsync(factory, "PropertyManager");
        var ownerEmail = $"{Guid.NewGuid():N}@landlord.example.com";
        var ownerId = await _admin.CreateOwnerAsync("Option Owner", ownerEmail);
        var invite = await _admin.PostAsync($"/api/v1/owners/{ownerId}/portal-invite", new { });
        var owner = await TestClient.RegisterWithInvitationAsync(factory, ownerEmail, invite.GetProperty("acceptToken").GetString()!);

        // A property manager can't list members, but can still see who may manage a property.
        var options = await manager.GetAsync("/api/v1/properties/manager-options");
        var ids = options.EnumerateArray().Select(o => o.GetProperty("userId").GetGuid()).ToList();
        Assert.Contains(manager.UserId, ids);
        Assert.Contains(_admin.UserId, ids);
        Assert.DoesNotContain(owner.UserId, ids);
    }
}
