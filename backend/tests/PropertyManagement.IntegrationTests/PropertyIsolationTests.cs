using System.Net;
using System.Net.Http.Json;

namespace PropertyManagement.IntegrationTests;

/// <summary>
/// Cross-organization attack tests for Phase 3 records (design 13.4). ABC tries to read, change or link to XYZ's
/// properties, units, owners and tenants by guessing IDs, and must always get "not found".
/// </summary>
public sealed class PropertyIsolationTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private TestClient _abc = null!;
    private TestClient _xyz = null!;
    private Guid _xyzProperty;
    private Guid _xyzUnit;
    private Guid _xyzOwner;
    private Guid _xyzTenant;
    private Guid _abcProperty;

    public async Task InitializeAsync()
    {
        _abc = await TestClient.RegisterWithOrganizationAsync(factory, "ABC Property Managers");
        _xyz = await TestClient.RegisterWithOrganizationAsync(factory, "XYZ Estate Services");
        _xyzProperty = await _xyz.CreatePropertyAsync("XYZ-1");
        _xyzUnit = await _xyz.CreateUnitAsync(_xyzProperty, "1");
        _xyzOwner = await _xyz.CreateOwnerAsync("XYZ Landlord");
        _xyzTenant = await _xyz.CreateTenantAsync("XYZ Tenant");
        await _xyz.PutAsync($"/api/v1/tenants/{_xyzTenant}/kyc", new { idType = "Nin", idNumber = "98765432109" });
        _abcProperty = await _abc.CreatePropertyAsync("ABC-1");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("/api/v1/properties/{p}")]
    [InlineData("/api/v1/properties/{p}/units")]
    [InlineData("/api/v1/properties/{p}/owners")]
    [InlineData("/api/v1/units/{u}")]
    [InlineData("/api/v1/owners/{o}")]
    [InlineData("/api/v1/owners/{o}/bank-accounts")]
    [InlineData("/api/v1/owners/{o}/agreements")]
    [InlineData("/api/v1/tenants/{t}")]
    [InlineData("/api/v1/tenants/{t}/kyc")]
    [InlineData("/api/v1/tenants/{t}/guarantors")]
    [InlineData("/api/v1/tenants/{t}/screening")]
    public async Task Cannot_read_another_organizations_records(string template)
    {
        var response = await _abc.Http.GetAsync(Fill(template));
        response.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cannot_change_another_organizations_records()
    {
        (await _abc.Http.PutAsJsonAsync($"/api/v1/properties/{_xyzProperty}", new
        {
            code = "HIJACK", name = "Hijacked", type = "Residential", addressLine = "x", state = "Lagos",
        })).ShouldBe(HttpStatusCode.NotFound);
        (await _abc.Http.PostAsync($"/api/v1/properties/{_xyzProperty}/archive", null)).ShouldBe(HttpStatusCode.NotFound);
        (await _abc.Http.PostAsJsonAsync($"/api/v1/properties/{_xyzProperty}/units", new
        {
            code = "X", type = "Flat", defaultRent = 1m, billingFrequency = "Annual",
        })).ShouldBe(HttpStatusCode.NotFound);
        (await _abc.Http.PostAsync($"/api/v1/units/{_xyzUnit}/archive", null)).ShouldBe(HttpStatusCode.NotFound);
        (await _abc.Http.PutAsJsonAsync($"/api/v1/owners/{_xyzOwner}", new
        {
            type = "Individual", name = "Hijacked", phone = "08030000000",
        })).ShouldBe(HttpStatusCode.NotFound);
        (await _abc.Http.PostAsJsonAsync($"/api/v1/owners/{_xyzOwner}/bank-accounts", new
        {
            bankName = "Fraud Bank", accountNumber = "1111111111", accountName = "Fraudster",
        })).ShouldBe(HttpStatusCode.NotFound);
        (await _abc.Http.PutAsJsonAsync($"/api/v1/tenants/{_xyzTenant}/kyc", new { idType = "Nin", idNumber = "11111111111" }))
            .ShouldBe(HttpStatusCode.NotFound);
        (await _abc.Http.PostAsync($"/api/v1/tenants/{_xyzTenant}/portal-invite", null)).ShouldBe(HttpStatusCode.NotFound);

        var property = await _xyz.GetAsync($"/api/v1/properties/{_xyzProperty}");
        Assert.Equal("XYZ-1", property.GetProperty("code").GetString());
        Assert.Empty((await _xyz.GetAsync($"/api/v1/owners/{_xyzOwner}/bank-accounts")).EnumerateArray());
    }

    [Fact]
    public async Task Cannot_make_another_organizations_owner_an_owner_of_my_property()
    {
        var response = await _abc.Http.PutAsJsonAsync($"/api/v1/properties/{_abcProperty}/owners", new
        {
            effectiveFrom = "2026-01-01", shares = new[] { new { ownerId = _xyzOwner, sharePercent = 100m } },
        });
        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Equal("owner_not_found", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Cannot_tie_an_agreement_to_another_organizations_property()
    {
        var ownerId = await _abc.CreateOwnerAsync("ABC Landlord");
        var response = await _abc.Http.PostAsJsonAsync($"/api/v1/owners/{ownerId}/agreements", new
        {
            propertyId = _xyzProperty, feeBasis = "PercentOfRentCollected", feeValue = 10m, payoutFrequency = "Monthly",
            advanceRentRelease = "OnReceipt", reserveAmount = 0m, startDate = "2026-01-01",
        });
        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Equal("property_not_found", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Lists_and_searches_contain_only_the_active_organizations_records()
    {
        await _abc.CreateOwnerAsync("ABC Owner");
        await _abc.CreateTenantAsync("ABC Tenant");
        var properties = await _abc.GetAsync("/api/v1/properties?pageSize=100&search=XYZ");
        var owners = await _abc.GetAsync("/api/v1/owners?pageSize=100&search=XYZ");
        var tenants = await _abc.GetAsync("/api/v1/tenants?pageSize=100&search=XYZ");
        Assert.Equal(0, properties.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, owners.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, tenants.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Same_property_code_can_be_used_by_two_organizations()
    {
        var id = await _abc.CreatePropertyAsync("XYZ-1");
        Assert.NotEqual(_xyzProperty, id);
    }

    [Fact]
    public async Task Cannot_read_change_or_link_to_another_organizations_leases()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var xyzUnit = await _xyz.CreateUnitAsync(_xyzProperty, $"L{Guid.NewGuid().ToString()[..4]}");
        var xyzLease = (await _xyz.CreateLeaseAsync(xyzUnit, _xyzTenant, today)).GetProperty("id").GetGuid();

        (await _abc.Http.GetAsync($"/api/v1/leases/{xyzLease}")).ShouldBe(HttpStatusCode.NotFound);
        Assert.Equal(0, (await _abc.GetAsync($"/api/v1/leases?unitId={xyzUnit}")).GetProperty("totalCount").GetInt32());
        (await _abc.Http.PostAsync($"/api/v1/leases/{xyzLease}/activate", null)).ShouldBe(HttpStatusCode.NotFound);
        (await _abc.Http.PostAsJsonAsync($"/api/v1/leases/{xyzLease}/end", new { moveOutDate = today, reason = "x" }))
            .ShouldBe(HttpStatusCode.NotFound);
        (await _abc.Http.PostAsJsonAsync($"/api/v1/leases/{xyzLease}/renew", new { endDate = today.AddYears(3), rentAmount = 1m }))
            .ShouldBe(HttpStatusCode.NotFound);

        // A lease on XYZ's unit, or naming XYZ's tenant on ABC's own unit, is refused as not found.
        var abcTenant = await _abc.CreateTenantAsync("ABC Tenant");
        var abcUnit = await _abc.CreateUnitAsync(_abcProperty, $"L{Guid.NewGuid().ToString()[..4]}");
        (await _abc.Http.PostAsJsonAsync("/api/v1/leases", TestClient.LeaseBody(xyzUnit, [abcTenant], today), TestClient.Json))
            .ShouldBe(HttpStatusCode.NotFound);
        (await _abc.Http.PostAsJsonAsync("/api/v1/leases", TestClient.LeaseBody(abcUnit, [_xyzTenant], today), TestClient.Json))
            .ShouldBe(HttpStatusCode.NotFound);
    }

    private string Fill(string template) => template
        .Replace("{p}", _xyzProperty.ToString()).Replace("{u}", _xyzUnit.ToString())
        .Replace("{o}", _xyzOwner.ToString()).Replace("{t}", _xyzTenant.ToString());
}
