using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Organizations;
using PropertyManagement.Domain.Platform;

namespace PropertyManagement.Application.Platform;

/// <summary>One company as platform staff see it: its plan and lifecycle, and counts for plan limits. No company data.</summary>
public sealed record PlatformOrganizationDto(
    Guid Id,
    string Name,
    string Slug,
    OrganizationType Type,
    OrganizationStatus Status,
    DateTime CreatedAtUtc,
    string PlanCode,
    string PlanName,
    int MaxUnits,
    int MaxUsers,
    SubscriptionSummaryDto Subscription,
    int Units,
    int Staff);

public sealed record ExtendTrialRequest(DateTime Until, string? Note);
public sealed record ActivateSubscriptionRequest(string PlanCode, DateTime Until, string? Note);
public sealed record ChangePlanRequest(string PlanCode, string? Note);
public sealed record SubscriptionReasonRequest(string Reason);

/// <summary>
/// The platform administrator's side of subscriptions (design 2, "Platform administration"). Platform staff see each
/// company's plan, lifecycle and size, never its records, and every change lands in that company's audit log.
/// </summary>
public sealed class PlatformOrganizationService(
    IAppDbContext db,
    ICurrentOrganization currentOrganization,
    ICrossOrganizationAccess crossOrganization,
    IAuditLogger audit,
    IClock clock)
{
    public async Task<IReadOnlyList<PlatformOrganizationDto>> ListAsync(string? search, CancellationToken ct)
    {
        using var _ = crossOrganization.Allow();
        var organizations = db.Organizations.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            organizations = organizations.Where(o => o.Name.Contains(term) || o.Slug.Contains(term));
        }
        var rows = await organizations.OrderBy(o => o.Name).Take(200).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<PlatformOrganizationDto> GetAsync(Guid id, CancellationToken ct)
    {
        using var _ = crossOrganization.Allow();
        var organization = await db.Organizations.SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException("Organization");
        return (await ToDtosAsync([organization], ct)).Single();
    }

    public Task<PlatformOrganizationDto> ExtendTrialAsync(Guid id, ExtendTrialRequest request, CancellationToken ct) =>
        ChangeAsync(id, "platform.trial_extended", (s, _) =>
        {
            s.ExtendTrial(AsUtc(request.Until), clock.UtcNow);
            return Task.FromResult<object>(new { until = AsUtc(request.Until), request.Note });
        }, ct);

    public Task<PlatformOrganizationDto> ActivateAsync(Guid id, ActivateSubscriptionRequest request, CancellationToken ct) =>
        ChangeAsync(id, "platform.subscription_activated", async (s, token) =>
        {
            var plan = await RequirePlanAsync(request.PlanCode, token);
            s.Activate(plan, AsUtc(request.Until), clock.UtcNow);
            return new { plan = plan.Code, until = AsUtc(request.Until), request.Note };
        }, ct);

    public Task<PlatformOrganizationDto> ChangePlanAsync(Guid id, ChangePlanRequest request, CancellationToken ct) =>
        ChangeAsync(id, "platform.plan_changed", async (s, token) =>
        {
            var plan = await RequirePlanAsync(request.PlanCode, token);
            s.ChangePlan(plan);
            return new { plan = plan.Code, request.Note };
        }, ct);

    public Task<PlatformOrganizationDto> SuspendAsync(Guid id, SubscriptionReasonRequest request, CancellationToken ct) =>
        ChangeAsync(id, "platform.subscription_suspended", (s, _) =>
        {
            s.Suspend(clock.UtcNow);
            return Task.FromResult<object>(new { reason = RequireReason(request.Reason) });
        }, ct);

    public Task<PlatformOrganizationDto> CancelAsync(Guid id, SubscriptionReasonRequest request, CancellationToken ct) =>
        ChangeAsync(id, "platform.subscription_cancelled", (s, _) =>
        {
            s.Cancel(clock.UtcNow);
            return Task.FromResult<object>(new { reason = RequireReason(request.Reason), exportUntil = s.ExportUntilUtc });
        }, ct);

    /// <summary>Acts as the company for the write, so the usual isolation checks apply to it, then records it in its audit log.</summary>
    private async Task<PlatformOrganizationDto> ChangeAsync(Guid id, string action,
        Func<OrganizationSubscription, CancellationToken, Task<object>> change, CancellationToken ct)
    {
        Organization organization;
        using (crossOrganization.Allow())
            organization = await db.Organizations.SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException("Organization");
        currentOrganization.Set(id, null, [], []);
        var subscription = await db.Subscriptions.OrderByDescending(s => s.StartedAtUtc).FirstOrDefaultAsync(ct)
                           ?? throw new NotFoundException("Subscription");
        var before = new { subscription.Status, OrganizationStatus = organization.Status };
        var details = await change(subscription, ct);
        organization.SetStatus(subscription.OrganizationStatus);
        audit.Record(action, nameof(OrganizationSubscription), subscription.Id.ToString(),
            details: new { before, after = new { subscription.Status, OrganizationStatus = organization.Status }, change = details },
            organizationId: id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task<IReadOnlyList<PlatformOrganizationDto>> ToDtosAsync(IReadOnlyList<Organization> rows, CancellationToken ct)
    {
        var ids = rows.Select(o => o.Id).ToList();
        var staffRoles = Roles.StaffRoles.ToArray();
        var subscriptions = (await db.Subscriptions.IgnoreQueryFilters().Where(s => ids.Contains(s.OrganizationId)).ToListAsync(ct))
            .GroupBy(s => s.OrganizationId).ToDictionary(g => g.Key, g => g.MaxBy(s => s.StartedAtUtc)!);
        var plans = await db.SubscriptionPlans.ToDictionaryAsync(p => p.Id, ct);
        var units = await db.Units.IgnoreQueryFilters().Where(u => ids.Contains(u.OrganizationId))
            .GroupBy(u => u.OrganizationId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var staff = await db.Memberships.IgnoreQueryFilters()
            .Where(m => ids.Contains(m.OrganizationId) && m.Status == MembershipStatus.Active
                        && m.Roles.Any(r => staffRoles.Contains(r.RoleName) || r.RoleName.StartsWith(CustomRole.KeyPrefix)))
            .GroupBy(m => m.OrganizationId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);

        return rows.Where(o => subscriptions.ContainsKey(o.Id)).Select(o =>
        {
            var s = subscriptions[o.Id];
            var plan = plans[s.PlanId];
            return new PlatformOrganizationDto(o.Id, o.Name, o.Slug, o.Type, o.Status, DateTime.SpecifyKind(o.CreatedAtUtc, DateTimeKind.Utc),
                plan.Code, plan.Name, plan.MaxUnits, plan.MaxUsers, SubscriptionSummaryDto.From(s),
                units.GetValueOrDefault(o.Id), staff.GetValueOrDefault(o.Id));
        }).ToList();
    }

    private async Task<SubscriptionPlan> RequirePlanAsync(string code, CancellationToken ct) =>
        await db.SubscriptionPlans.SingleOrDefaultAsync(p => p.Code == code && p.IsActive, ct)
        ?? throw new DomainException("Choose one of the plans.", "plan_not_found");

    private static string RequireReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? throw new DomainException("Say why, for the company's audit log.", "reason_required") : reason.Trim();

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
