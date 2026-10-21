using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Organizations;
using PropertyManagement.Domain.Platform;

namespace PropertyManagement.Application.Platform;

/// <summary>The organization's plan, where it is in the lifecycle, and how much of the plan is used.</summary>
public sealed record SubscriptionDto(
    string PlanCode,
    string PlanName,
    int MaxUnits,
    int MaxUsers,
    int UnitsUsed,
    int StaffUsed,
    SubscriptionStatus Status,
    DateTime? TrialEndsAtUtc,
    DateTime? CurrentPeriodEndsAtUtc,
    DateTime? SuspendsAtUtc,
    DateTime? ExportUntilUtc);

/// <summary>The lifecycle dates shown in banners, carried with each organization the user can open.</summary>
public sealed record SubscriptionSummaryDto(
    SubscriptionStatus Status,
    DateTime? TrialEndsAtUtc,
    DateTime? CurrentPeriodEndsAtUtc,
    DateTime? SuspendsAtUtc,
    DateTime? ExportUntilUtc)
{
    public static SubscriptionSummaryDto From(OrganizationSubscription s) => new(s.Status, Utc(s.TrialEndsAtUtc),
        Utc(s.CurrentPeriodEndsAtUtc), Utc(s.SuspendsAtUtc), Utc(s.ExportUntilUtc));

    internal static DateTime? Utc(DateTime? value) => value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
}

/// <summary>
/// Subscriptions, lapse and offboarding for the active organization (design 4.8). A trial or paid period that ends
/// leaves full access for a grace period with reminders, then the company becomes read-only. Plan limits stop new units
/// and staff beyond the plan, never anything an owner or tenant needs, and never weaken isolation.
/// </summary>
public sealed class SubscriptionService(
    IAppDbContext db,
    ICurrentOrganization currentOrganization,
    IUserDirectory users,
    IEmailSender email,
    IAuditLogger audit,
    IClock clock)
{
    /// <summary>Days before the trial ends when admins are reminded.</summary>
    public const int TrialReminderDays = 7;

    public async Task<SubscriptionDto> GetAsync(CancellationToken ct)
    {
        var subscription = await LoadAsync(ct);
        var plan = await db.SubscriptionPlans.SingleAsync(p => p.Id == subscription.PlanId, ct);
        return new SubscriptionDto(plan.Code, plan.Name, plan.MaxUnits, plan.MaxUsers, await db.Units.CountAsync(ct),
            await StaffUsedAsync(ct), subscription.Status, SubscriptionSummaryDto.Utc(subscription.TrialEndsAtUtc),
            SubscriptionSummaryDto.Utc(subscription.CurrentPeriodEndsAtUtc), SubscriptionSummaryDto.Utc(subscription.SuspendsAtUtc),
            SubscriptionSummaryDto.Utc(subscription.ExportUntilUtc));
    }

    /// <summary>Refuses a new unit beyond the plan's limit.</summary>
    public async Task EnsureRoomForUnitsAsync(int adding, CancellationToken ct)
    {
        var plan = await PlanAsync(ct);
        if (await db.Units.CountAsync(ct) + adding > plan.MaxUnits)
            throw new DomainException($"Your {plan.Name} plan allows {plan.MaxUnits} units. Ask us about a bigger plan to add more.",
                "plan_limit_units");
    }

    /// <summary>Refuses a new staff member beyond the plan's limit. Pending invitations hold a place; portal users don't count.</summary>
    public async Task EnsureRoomForStaffAsync(CancellationToken ct)
    {
        var plan = await PlanAsync(ct);
        if (await StaffUsedAsync(ct) >= plan.MaxUsers)
            throw new DomainException($"Your {plan.Name} plan allows {plan.MaxUsers} team members, including pending invitations. "
                                      + "Remove someone or ask us about a bigger plan.", "plan_limit_users");
    }

    /// <summary>
    /// Moves the subscription on when a date has passed, keeps the organization's status in step, and emails the
    /// admins once at each stage. Run by the subscription job for one organization at a time. Safe to repeat.
    /// </summary>
    public async Task<bool> RefreshAsync(CancellationToken ct)
    {
        var subscription = await db.Subscriptions.OrderByDescending(s => s.StartedAtUtc).FirstOrDefaultAsync(ct);
        if (subscription is null) return false;
        var organization = await db.Organizations.SingleAsync(o => o.Id == subscription.OrganizationId, ct);
        var now = clock.UtcNow;
        var changed = false;
        while (subscription.Advance(now))
        {
            changed = true;
            audit.Record($"subscription.{(subscription.Status == SubscriptionStatus.PastDue ? "past_due" : "suspended")}",
                nameof(OrganizationSubscription), subscription.Id.ToString(),
                details: new { subscription.Status, subscription.PastDueSinceUtc, subscription.SuspendsAtUtc, job = "subscriptions" });
        }
        if (organization.Status != subscription.OrganizationStatus)
        {
            organization.SetStatus(subscription.OrganizationStatus);
            changed = true;
        }

        var notice = Notice(subscription, organization.Name, now);
        if (notice is not null && subscription.TakeNotice(notice.Value.Key))
        {
            await db.SaveChangesAsync(ct);
            await EmailAdminsAsync(notice.Value.Subject, notice.Value.Body, ct);
            return true;
        }
        if (changed) await db.SaveChangesAsync(ct);
        return changed;
    }

    private static (string Key, string Subject, string Body)? Notice(OrganizationSubscription s, string name, DateTime now)
    {
        static string Day(DateTime? d) => d?.ToString("d MMMM yyyy") ?? "";
        return s.Status switch
        {
            SubscriptionStatus.Trial when s.TrialEndsAtUtc is { } ends && ends - now <= TimeSpan.FromDays(TrialReminderDays) =>
                ($"trial-ending:{ends:yyyyMMdd}", $"Your {name} trial ends on {Day(ends)}",
                    $"Your free trial of Property Management for {name} ends on {Day(ends)}. Contact us to choose a plan and keep full access."),
            SubscriptionStatus.PastDue =>
                ($"past-due:{s.PastDueSinceUtc:yyyyMMdd}", $"{name}: your subscription has lapsed",
                    $"Your subscription for {name} has lapsed. Everything keeps working until {Day(s.SuspendsAtUtc)}; after that the company "
                    + "becomes read-only until it is renewed. Contact us to renew."),
            SubscriptionStatus.Suspended =>
                ($"suspended:{s.SuspendedAtUtc:yyyyMMddHHmm}", $"{name} is now read-only",
                    $"Your subscription for {name} is suspended, so your team can view records but not change them. "
                    + "Owners and tenants can still see their portals. Contact us to renew."),
            _ => null,
        };
    }

    private async Task EmailAdminsAsync(string subject, string body, CancellationToken ct)
    {
        var adminIds = await db.Memberships
            .Where(m => m.Status == MembershipStatus.Active && m.Roles.Any(r => r.RoleName == Roles.OrganizationAdmin))
            .Select(m => m.UserId).ToListAsync(ct);
        foreach (var admin in (await users.GetAsync(adminIds, ct)).Values)
            await email.SendAsync(admin.Email, subject, body, ct);
    }

    private async Task<int> StaffUsedAsync(CancellationToken ct)
    {
        var staffRoles = Roles.StaffRoles.ToArray();
        var now = clock.UtcNow;
        var members = await db.Memberships.CountAsync(m => m.Status == MembershipStatus.Active
            && m.Roles.Any(r => staffRoles.Contains(r.RoleName) || r.RoleName.StartsWith(CustomRole.KeyPrefix)), ct);
        var pending = await db.Invitations.CountAsync(i => i.Status == InvitationStatus.Pending && i.ExpiresAtUtc > now
            && i.OwnerId == null && i.TenantId == null, ct);
        return members + pending;
    }

    private async Task<SubscriptionPlan> PlanAsync(CancellationToken ct)
    {
        var subscription = await LoadAsync(ct);
        return await db.SubscriptionPlans.SingleAsync(p => p.Id == subscription.PlanId, ct);
    }

    private async Task<OrganizationSubscription> LoadAsync(CancellationToken ct)
    {
        currentOrganization.RequireOrganizationId();
        return await db.Subscriptions.OrderByDescending(s => s.StartedAtUtc).FirstOrDefaultAsync(ct)
               ?? throw new NotFoundException("Subscription");
    }
}
