using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PropertyManagement.Api.Infrastructure;

namespace PropertyManagement.IntegrationTests;

/// <summary>Phase 4 lease records: notices, move-in and move-out inspections, and lease reminders.</summary>
public sealed class LeaseRecordsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));
    private TestClient _admin = null!;
    private Guid _property;

    public async Task InitializeAsync()
    {
        _admin = await TestClient.RegisterWithOrganizationAsync(factory, "Surulere Homes");
        _property = await _admin.CreatePropertyAsync($"SH-{Guid.NewGuid().ToString()[..4]}");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private async Task<Guid> LeaseAsync(DateOnly start, DateOnly? end = null, bool activate = true, object[]? charges = null)
    {
        var unit = await _admin.CreateUnitAsync(_property, Guid.NewGuid().ToString()[..6]);
        var tenant = await _admin.CreateTenantAsync($"Tenant {Guid.NewGuid().ToString()[..4]}");
        var lease = await _admin.PostAsync("/api/v1/leases", TestClient.LeaseBody(unit, [tenant], start, end, charges: charges));
        if (activate) await _admin.PostAsync($"/api/v1/leases/{Id(lease)}/activate", new { });
        return Id(lease);
    }

    [Fact]
    public async Task Notices_are_recorded_with_a_default_expiry_and_can_be_withdrawn()
    {
        var lease = await LeaseAsync(Today.AddDays(-100));

        var suggestion = await _admin.GetAsync($"/api/v1/leases/{lease}/notices/default?type=NoticeToQuit&servedOn={Today:yyyy-MM-dd}");
        Assert.Equal(180, suggestion.GetProperty("days").GetInt32());
        Assert.Equal(Today.AddDays(180), DateOnly.Parse(suggestion.GetProperty("expiresOn").GetString()!));

        var notice = await _admin.PostAsync($"/api/v1/leases/{lease}/notices", new
        {
            type = "NoticeToQuit", servedOn = Today, method = "PersonalService", servedBy = "Barr. Adeyemi",
        });
        Assert.Equal(Today.AddDays(180), DateOnly.Parse(notice.GetProperty("expiresOn").GetString()!));
        Assert.Equal("Served", notice.GetProperty("status").GetString());
        Assert.False(notice.GetProperty("hasExpired").GetBoolean());

        var future = await _admin.Http.PostAsJsonAsync($"/api/v1/leases/{lease}/notices",
            new { type = "RentReview", servedOn = Today.AddDays(1), method = "Email" }, TestClient.Json);
        future.ShouldBe(HttpStatusCode.BadRequest);

        var withdrawn = await _admin.PostAsync($"/api/v1/leases/{lease}/notices/{Id(notice)}/withdraw", new { reason = "Tenant agreed to renew" });
        Assert.Equal("Withdrawn", withdrawn.GetProperty("status").GetString());
        var again = await _admin.Http.PostAsJsonAsync($"/api/v1/leases/{lease}/notices/{Id(notice)}/withdraw", new { }, TestClient.Json);
        again.ShouldBe(HttpStatusCode.Conflict);

        var list = await _admin.GetAsync($"/api/v1/leases/{lease}/notices");
        Assert.Equal(1, list.GetArrayLength());
    }

    [Fact]
    public async Task Owners_intention_notice_needs_an_expired_notice_to_quit_first()
    {
        var lease = await LeaseAsync(Today.AddYears(-1).AddDays(-30), Today.AddDays(-30));
        var early = await _admin.Http.PostAsJsonAsync($"/api/v1/leases/{lease}/notices",
            new { type = "OwnersIntentionToRecoverPossession", servedOn = Today, method = "PastedOnPremises" }, TestClient.Json);
        early.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal("notice_sequence", await early.ProblemCodeAsync());

        await _admin.PostAsync($"/api/v1/leases/{lease}/notices", new
        {
            type = "NoticeToQuit", servedOn = Today.AddDays(-20), method = "PersonalService", expiresOn = Today.AddDays(-2),
        });
        var intention = await _admin.PostAsync($"/api/v1/leases/{lease}/notices",
            new { type = "OwnersIntentionToRecoverPossession", servedOn = Today, method = "PastedOnPremises" });
        Assert.Equal(Today.AddDays(7), DateOnly.Parse(intention.GetProperty("expiresOn").GetString()!));
    }

    [Fact]
    public async Task Notices_need_an_active_lease_and_the_notice_permission()
    {
        var draft = await LeaseAsync(Today, activate: false);
        var onDraft = await _admin.Http.PostAsJsonAsync($"/api/v1/leases/{draft}/notices",
            new { type = "RentReview", servedOn = Today, method = "Email" }, TestClient.Json);
        onDraft.ShouldBe(HttpStatusCode.Conflict);

        var lease = await LeaseAsync(Today.AddDays(-5));
        var accountant = await _admin.AddMemberAsync(factory, "Accountant");
        (await accountant.Http.PostAsJsonAsync($"/api/v1/leases/{lease}/notices",
            new { type = "RentReview", servedOn = Today, method = "Email" }, TestClient.Json)).ShouldBe(HttpStatusCode.Forbidden);
        (await accountant.Http.GetAsync($"/api/v1/leases/{lease}/notices")).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Move_in_and_move_out_reports_are_kept_one_of_each()
    {
        var lease = await LeaseAsync(Today.AddDays(-200), charges: [new { type = "CautionDeposit", amount = 400_000m, recurrence = "OneOff" }]);

        var deductionAtMoveIn = await _admin.Http.PutAsJsonAsync($"/api/v1/leases/{lease}/inspections/MoveIn", new
        {
            inspectedOn = Today.AddDays(-200), tenantPresent = true, tenantAgreed = true,
            items = new[] { new { area = "Kitchen", condition = "Good", proposedDeduction = 10m } },
        }, TestClient.Json);
        deductionAtMoveIn.ShouldBe(HttpStatusCode.BadRequest);

        var moveIn = await _admin.PutAsync($"/api/v1/leases/{lease}/inspections/MoveIn", new
        {
            inspectedOn = Today.AddDays(-200), tenantPresent = true, tenantAgreed = true, summary = "Freshly painted",
            items = new object[] { new { area = "Kitchen", condition = "Good" }, new { area = "Bathroom", condition = "Fair", notes = "Cracked tile" } },
        });
        Assert.Equal(2, moveIn.GetProperty("items").GetArrayLength());

        await _admin.PostAsync($"/api/v1/leases/{lease}/end", new { moveOutDate = Today, reason = "Leaving Lagos" });
        await _admin.PutAsync($"/api/v1/leases/{lease}/inspections/MoveOut", new
        {
            inspectedOn = Today, tenantPresent = false, tenantAgreed = false,
            items = new object[] { new { area = "Kitchen", condition = "Damaged", notes = "Broken cabinet door", proposedDeduction = 45_000m } },
        });
        var moveOut = await _admin.PutAsync($"/api/v1/leases/{lease}/inspections/MoveOut", new
        {
            inspectedOn = Today, tenantPresent = true, tenantAgreed = false,
            items = new object[]
            {
                new { area = "Kitchen", condition = "Damaged", notes = "Broken cabinet door", proposedDeduction = 45_000m },
                new { area = "Living room", condition = "Poor", proposedDeduction = 30_000.50m },
            },
        });
        Assert.Equal(75_000.50m, moveOut.GetProperty("totalProposedDeduction").GetDecimal());

        var both = await _admin.GetAsync($"/api/v1/leases/{lease}/inspections");
        Assert.Equal("Freshly painted", both.GetProperty("moveIn").GetProperty("summary").GetString());
        Assert.Equal(2, both.GetProperty("moveOut").GetProperty("items").GetArrayLength());
        Assert.Equal(400_000m, both.GetProperty("cautionDeposit").GetDecimal());

        var agreedButAbsent = await _admin.Http.PutAsJsonAsync($"/api/v1/leases/{lease}/inspections/MoveOut", new
        {
            inspectedOn = Today, tenantPresent = false, tenantAgreed = true,
            items = new[] { new { area = "Kitchen", condition = "Good" } },
        }, TestClient.Json);
        agreedButAbsent.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reminders_go_out_once_for_expiry_holding_over_and_expired_notices()
    {
        var ending = await LeaseAsync(Today.AddDays(-345), Today.AddDays(20));
        var overdue = await LeaseAsync(Today.AddYears(-1).AddDays(-3), Today.AddDays(-3));
        await _admin.PostAsync($"/api/v1/leases/{overdue}/notices", new
        {
            type = "NoticeToQuit", servedOn = Today.AddDays(-40), method = "PersonalService", expiresOn = Today.AddDays(-1),
        });
        var renewing = await LeaseAsync(Today.AddDays(-340), Today.AddDays(25));
        await _admin.PostAsync($"/api/v1/leases/{renewing}/renew", new { endDate = Today.AddDays(400), rentAmount = 3_300_000m });

        var job = factory.Services.GetRequiredService<LeaseScheduleJob>();
        await job.RunOnceAsync(CancellationToken.None);
        var mine = factory.Emails.Messages.Where(m => m.To == _admin.Email).ToList();
        var endingRef = (await _admin.GetAsync($"/api/v1/leases/{ending}")).GetProperty("reference").GetString();
        var overdueRef = (await _admin.GetAsync($"/api/v1/leases/{overdue}")).GetProperty("reference").GetString();
        var renewingRef = (await _admin.GetAsync($"/api/v1/leases/{renewing}")).GetProperty("reference").GetString();

        Assert.Contains(mine, m => m.Subject.EndsWith($"Lease {endingRef} ends in 20 days"));
        Assert.Contains(mine, m => m.Subject.EndsWith($"Lease {overdueRef} has passed its end date"));
        Assert.Contains(mine, m => m.Subject.EndsWith($"Notice on lease {overdueRef} has expired") && m.Body.Contains("7-day notice"));
        Assert.DoesNotContain(mine, m => m.Subject.Contains(renewingRef!));
        Assert.All(mine, m => Assert.StartsWith("Surulere Homes: ", m.Subject));

        var count = mine.Count;
        await job.RunOnceAsync(CancellationToken.None);
        Assert.Equal(count, factory.Emails.Messages.Count(m => m.To == _admin.Email));
    }

    [Fact]
    public async Task Reminders_go_to_the_property_manager_when_there_is_one()
    {
        var manager = await _admin.AddMemberAsync(factory, "PropertyManager");
        var managed = await _admin.CreatePropertyAsync($"MG-{Guid.NewGuid().ToString()[..4]}", managerUserId: manager.UserId);
        var unit = await _admin.CreateUnitAsync(managed, "M1");
        var lease = await _admin.CreateLeaseAsync(unit, await _admin.CreateTenantAsync("Managed Tenant"), Today.AddDays(-300), Today.AddDays(60));
        var reference = lease.GetProperty("reference").GetString();

        await factory.Services.GetRequiredService<LeaseScheduleJob>().RunOnceAsync(CancellationToken.None);
        var about = factory.Emails.Messages.Where(m => m.Subject.Contains(reference!)).ToList();
        Assert.Contains(about, m => m.To == manager.Email && m.Subject.EndsWith("ends in 60 days"));
        Assert.DoesNotContain(about, m => m.To == _admin.Email);
    }
}
