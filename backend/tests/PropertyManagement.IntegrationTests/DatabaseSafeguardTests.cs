using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Leases;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.IntegrationTests;

/// <summary>Safeguards held by the database itself, so they hold even if application code gets them wrong.</summary>
public sealed class DatabaseSafeguardTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));

    [Fact]
    public async Task Audit_records_cannot_be_changed_or_deleted_with_sql()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Audit Safe Homes");
        await admin.CreatePropertyAsync($"AS-{Guid.NewGuid().ToString()[..4]}");

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentOrganization>().Set(admin.OrganizationId, null, ["OrganizationAdmin"], []);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.AuditLogs.Where(a => a.OrganizationId == admin.OrganizationId).Select(a => a.Id).FirstAsync();

        var update = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AuditLogs SET Action = 'tampered' WHERE Id = {id}"));
        Assert.Contains("append-only", update.Message);
        var delete = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AuditLogs WHERE Id = {id}"));
        Assert.Contains("append-only", delete.Message);
        Assert.NotEqual("tampered", await db.AuditLogs.Where(a => a.Id == id).Select(a => a.Action).SingleAsync());
    }

    /// <summary>
    /// Reproduces two activations racing: the second one read the unit and the other lease before the first one
    /// saved, so its overlap check passes on stale data. The unit's row version must still stop it. The unit starts
    /// out reserved, which is what both activations would set, so without the forced unit write nothing would collide.
    /// </summary>
    [Fact]
    public async Task A_lease_activated_on_stale_data_cannot_overlap_one_activated_at_the_same_moment()
    {
        var admin = await TestClient.RegisterWithOrganizationAsync(factory, "Race Homes");
        var property = await admin.CreatePropertyAsync($"RH-{Guid.NewGuid().ToString()[..4]}");
        var unit = await admin.CreateUnitAsync(property, "R1", occupancy: "Reserved");
        var first = Id(await admin.CreateLeaseAsync(unit, await admin.CreateTenantAsync("First Tenant"), Today.AddDays(5), activate: false));
        var second = Id(await admin.CreateLeaseAsync(unit, await admin.CreateTenantAsync("Second Tenant"), Today.AddDays(20), activate: false));

        var accessor = factory.Services.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, admin.UserId.ToString())], "test")),
        };
        try
        {
            using var late = Scope(admin);
            var lateDb = late.ServiceProvider.GetRequiredService<AppDbContext>();
            // The late request has read everything before the early one saves.
            await lateDb.Units.SingleAsync(u => u.Id == unit);
            await lateDb.Leases.Where(l => l.UnitId == unit).ToListAsync();

            using (var early = Scope(admin))
                await early.ServiceProvider.GetRequiredService<LeaseService>().ActivateAsync(first, CancellationToken.None);

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                late.ServiceProvider.GetRequiredService<LeaseService>().ActivateAsync(second, CancellationToken.None));
        }
        finally
        {
            accessor.HttpContext = null;
        }

        var leases = await admin.GetAsync($"/api/v1/leases?unitId={unit}&pageSize=10");
        Assert.Single(leases.GetProperty("items").EnumerateArray(), l => l.GetProperty("status").GetString() == "Active");
    }

    private IServiceScope Scope(TestClient admin)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentOrganization>().Set(admin.OrganizationId, null, ["OrganizationAdmin"], []);
        return scope;
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

}
