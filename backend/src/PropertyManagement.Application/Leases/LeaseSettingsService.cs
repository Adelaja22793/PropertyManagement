using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Leases;
using PropertyManagement.Domain.Properties;

namespace PropertyManagement.Application.Leases;

public sealed record NoticePeriodRuleDto(Guid Id, string? State, NoticeType Type, BillingFrequency? Frequency, int Days, string? Basis);

/// <summary>A built-in default used when none of the organization's rules match.</summary>
public sealed record BuiltInNoticePeriodDto(NoticeType Type, BillingFrequency? Frequency, int Days);

public sealed record LeaseSettingsDto(
    IReadOnlyList<int> StaffReminderDays,
    bool TenantRemindersEnabled,
    IReadOnlyList<int> TenantReminderDays,
    IReadOnlyList<NoticePeriodRuleDto> NoticePeriods,
    IReadOnlyList<BuiltInNoticePeriodDto> BuiltInNoticePeriods);

public sealed record UpdateLeaseSettingsRequest(IReadOnlyList<int>? StaffReminderDays, bool TenantRemindersEnabled,
    IReadOnlyList<int>? TenantReminderDays);

public sealed record SaveNoticePeriodRequest(string? State, NoticeType Type, BillingFrequency? Frequency, int Days, string? Basis);

/// <summary>
/// An organization's lease settings (design 8.9): reminder days for staff and tenants, and its own notice periods
/// per notice type, state and rent frequency. Notice periods replace the built-in Lagos defaults where they match.
/// </summary>
public sealed class LeaseSettingsService(IAppDbContext db, ICurrentOrganization currentOrganization, IAuditLogger audit, IClock clock)
{
    private static readonly IReadOnlyList<BuiltInNoticePeriodDto> BuiltIn =
    [
        .. Enum.GetValues<BillingFrequency>().Select(f =>
            new BuiltInNoticePeriodDto(NoticeType.NoticeToQuit, f, NoticePeriods.DefaultDays(NoticeType.NoticeToQuit, f, null))),
        new(NoticeType.OwnersIntentionToRecoverPossession, null, 7),
        new(NoticeType.RentReview, null, 30),
        new(NoticeType.Other, null, 30),
    ];

    /// <summary>The organization's saved settings, or the defaults when it hasn't saved any. Shared with the reminder job.</summary>
    public static async Task<LeaseSettings> LoadAsync(IAppDbContext db, Guid organizationId, CancellationToken ct) =>
        await db.LeaseSettings.SingleOrDefaultAsync(ct) ?? LeaseSettings.Defaults(organizationId);

    public async Task<LeaseSettingsDto> GetAsync(CancellationToken ct)
    {
        var settings = await LoadAsync(db, currentOrganization.RequireOrganizationId(), ct);
        return await ToDtoAsync(settings, ct);
    }

    public async Task<LeaseSettingsDto> UpdateAsync(UpdateLeaseSettingsRequest request, CancellationToken ct)
    {
        var organizationId = currentOrganization.RequireOrganizationId();
        var settings = await db.LeaseSettings.SingleOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new LeaseSettings(organizationId);
            db.LeaseSettings.Add(settings);
        }
        settings.Update(request.StaffReminderDays ?? [], request.TenantRemindersEnabled, request.TenantReminderDays ?? [], clock.UtcNow);
        audit.Record("lease_settings.updated", nameof(LeaseSettings), settings.Id.ToString(), details: new
        {
            StaffReminderDays = settings.GetStaffDays(), settings.TenantRemindersEnabled, TenantReminderDays = settings.GetTenantDays(),
        });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(settings, ct);
    }

    public async Task<NoticePeriodRuleDto> CreateRuleAsync(SaveNoticePeriodRequest request, CancellationToken ct)
    {
        var rule = new NoticePeriodRule(currentOrganization.RequireOrganizationId(), Details(request), clock.UtcNow);
        await EnsureUniqueAsync(rule, ct);
        db.NoticePeriodRules.Add(rule);
        audit.Record("notice_period.created", nameof(NoticePeriodRule), rule.Id.ToString(), details: ToDto(rule));
        await db.SaveChangesAsync(ct);
        return ToDto(rule);
    }

    public async Task<NoticePeriodRuleDto> UpdateRuleAsync(Guid id, SaveNoticePeriodRequest request, CancellationToken ct)
    {
        var rule = await FindAsync(id, ct);
        var before = ToDto(rule);
        rule.Update(Details(request), clock.UtcNow);
        await EnsureUniqueAsync(rule, ct);
        audit.Record("notice_period.updated", nameof(NoticePeriodRule), rule.Id.ToString(), details: new { Before = before, After = ToDto(rule) });
        await db.SaveChangesAsync(ct);
        return ToDto(rule);
    }

    public async Task DeleteRuleAsync(Guid id, CancellationToken ct)
    {
        var rule = await FindAsync(id, ct);
        db.NoticePeriodRules.Remove(rule);
        audit.Record("notice_period.deleted", nameof(NoticePeriodRule), rule.Id.ToString(), details: ToDto(rule));
        await db.SaveChangesAsync(ct);
    }

    private async Task<NoticePeriodRule> FindAsync(Guid id, CancellationToken ct) =>
        await db.NoticePeriodRules.SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Notice period");

    private async Task EnsureUniqueAsync(NoticePeriodRule rule, CancellationToken ct)
    {
        var others = await db.NoticePeriodRules.Where(r => r.Id != rule.Id && r.Type == rule.Type && r.Frequency == rule.Frequency)
            .Select(r => r.State).ToListAsync(ct);
        if (others.Any(s => string.Equals(s, rule.State, StringComparison.OrdinalIgnoreCase)))
            throw new DomainException("There is already a notice period for this notice, state and rent frequency. Change that one instead.",
                "notice_period_exists");
    }

    private static NoticePeriodRuleDetails Details(SaveNoticePeriodRequest r) => new(r.State, r.Type, r.Frequency, r.Days, r.Basis);

    private async Task<LeaseSettingsDto> ToDtoAsync(LeaseSettings settings, CancellationToken ct)
    {
        var rules = await db.NoticePeriodRules.ToListAsync(ct);
        var ordered = rules.OrderBy(r => r.Type).ThenBy(r => r.State is null ? 0 : 1).ThenBy(r => r.State)
            .ThenBy(r => r.Frequency is null ? -1 : (int)r.Frequency).Select(ToDto).ToList();
        return new LeaseSettingsDto(settings.GetStaffDays(), settings.TenantRemindersEnabled, settings.GetTenantDays(), ordered, BuiltIn);
    }

    private static NoticePeriodRuleDto ToDto(NoticePeriodRule r) => new(r.Id, r.State, r.Type, r.Frequency, r.Days, r.Basis);
}
