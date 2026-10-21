using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Domain.Leases;
using PropertyManagement.Domain.Organizations;

namespace PropertyManagement.Application.Leases;

/// <summary>
/// Emails staff about leases that need action (design 6.5): on the organization's chosen days before a lease ends
/// (90, 60 and 30 by default), when a lease starts holding over, and when a notice served on the tenant expires.
/// Staff reminders go to the property's manager, or to the organization admins when the property has no manager.
/// When the organization turns tenant reminders on, a lease's tenants are also emailed on their own chosen days
/// before it ends. Each reminder goes out once, recorded in <see cref="LeaseReminder"/>, however often the job runs.
/// </summary>
public sealed class LeaseReminderService(
    IAppDbContext db, ICurrentOrganization currentOrganization, IUserDirectory users, IEmailSender email, IClock clock)
{
    private sealed record Due(Lease Lease, string Key, string Subject, string Body, bool ToTenants = false);

    /// <summary>Sends every reminder now due in the current organization. Returns how many reminders were sent.</summary>
    public async Task<int> SendDueAsync(CancellationToken ct)
    {
        var organizationId = currentOrganization.RequireOrganizationId();
        var settings = await LeaseSettingsService.LoadAsync(db, organizationId, ct);
        var staffDays = settings.GetStaffDays();
        var tenantDays = settings.TenantRemindersEnabled ? settings.GetTenantDays() : [];
        var today = clock.TodayInNigeria();
        var horizon = today.AddDays(staffDays.Concat(tenantDays).Max());
        var active = await db.Leases.Where(l => l.Status == LeaseStatus.Active && l.StartDate <= today && l.EndDate <= horizon)
            .ToListAsync(ct);
        var leaseIds = active.Select(l => l.Id).ToList();
        var renewed = (await db.Leases.Where(l => l.PreviousLeaseId != null && leaseIds.Contains(l.PreviousLeaseId.Value)
                                                  && l.Status != LeaseStatus.Cancelled)
            .Select(l => l.PreviousLeaseId!.Value).ToListAsync(ct)).ToHashSet();

        var due = new List<Due>();
        foreach (var lease in active.Where(l => !renewed.Contains(l.Id)))
        {
            var days = lease.EndDate.DayNumber - today.DayNumber;
            if (days < 0)
            {
                due.Add(new Due(lease, "holding-over", $"Lease {lease.Reference} has passed its end date",
                    $"Lease {lease.Reference} ended on {lease.EndDate:d MMM yyyy}, but it hasn't been renewed or ended. " +
                    "If the tenant is still in the unit, renew the lease or serve a notice. If they have left, record the move-out."));
                continue;
            }
            if (Threshold(staffDays, days) is { } threshold)
                due.Add(new Due(lease, $"expiry-{threshold}", $"Lease {lease.Reference} ends in {days} days",
                    $"Lease {lease.Reference} ends on {lease.EndDate:d MMM yyyy}. Agree a renewal with the tenant and owner, " +
                    "or serve notice if it won't be renewed."));
            if (Threshold(tenantDays, days) is { } tenantThreshold)
                due.Add(new Due(lease, $"tenant-expiry-{tenantThreshold}", $"Your tenancy ends on {lease.EndDate:d MMMM yyyy}",
                    $"Your tenancy ends on {lease.EndDate:d MMMM yyyy}, in {days} days. If you would like to renew, or you plan " +
                    "to move out, please let us know soon so we can arrange it.", ToTenants: true));
        }

        var expired = await db.LeaseNotices
            .Where(n => n.Status == NoticeStatus.Served && n.ExpiresOn < today
                        && db.Leases.Any(l => l.Id == n.LeaseId && l.Status == LeaseStatus.Active))
            .ToListAsync(ct);
        if (expired.Count > 0)
        {
            var noticeLeaseIds = expired.Select(n => n.LeaseId).Distinct().ToList();
            var noticeLeases = await db.Leases.Where(l => noticeLeaseIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
            foreach (var notice in expired)
            {
                var lease = noticeLeases[notice.LeaseId];
                var next = notice.Type == NoticeType.NoticeToQuit
                    ? "If the tenant hasn't left, the next step is usually the 7-day notice of owner's intention to recover possession. Check with your lawyer."
                    : "Decide the next step with your lawyer.";
                due.Add(new Due(lease, $"notice-expired-{notice.Id:N}", $"Notice on lease {lease.Reference} has expired",
                    $"The {Describe(notice.Type)} served on {notice.ServedOn:d MMM yyyy} for lease {lease.Reference} expired on " +
                    $"{notice.ExpiresOn:d MMM yyyy}. {next}"));
            }
        }
        if (due.Count == 0) return 0;

        var dueLeaseIds = due.Select(d => d.Lease.Id).Distinct().ToList();
        var sent = (await db.LeaseReminders.Where(r => dueLeaseIds.Contains(r.LeaseId))
            .Select(r => new { r.LeaseId, r.Key }).ToListAsync(ct)).Select(r => (r.LeaseId, r.Key)).ToHashSet();
        var toSend = due.Where(d => !sent.Contains((d.Lease.Id, d.Key))).ToList();
        if (toSend.Count == 0) return 0;

        var recipients = await RecipientsAsync(toSend.Where(d => !d.ToTenants).Select(d => d.Lease).ToList(), ct);
        var tenantEmails = await TenantEmailsAsync(toSend.Where(d => d.ToTenants).Select(d => d.Lease.Id).ToList(), ct);
        var org = await db.Organizations.Where(o => o.Id == organizationId)
            .Select(o => new { o.Name, o.ContactEmail, o.ContactPhone }).SingleAsync(ct);
        var organization = org.Name;
        var contact = string.Join(" or ", new[] { org.ContactPhone, org.ContactEmail }.Where(c => !string.IsNullOrWhiteSpace(c)));
        foreach (var item in toSend)
        {
            var to = item.ToTenants
                ? tenantEmails.GetValueOrDefault(item.Lease.Id) ?? []
                : recipients.GetValueOrDefault(item.Lease.PropertyId) ?? [];
            var unit = await db.Units.Where(u => u.Id == item.Lease.UnitId)
                .Select(u => new { u.Code, Property = db.Properties.Where(p => p.Id == u.PropertyId).Select(p => p.Name).First() })
                .FirstAsync(ct);
            var body = item.ToTenants
                ? $"{item.Body}\n\nHome: {unit.Property}, unit {unit.Code}.\n\n{organization}" + (contact.Length > 0 ? $"\n{contact}" : "")
                : $"{item.Body}\n\nProperty: {unit.Property}, unit {unit.Code}.\n\n{organization}";
            foreach (var address in to) await email.SendAsync(address, $"{organization}: {item.Subject}", body, ct);
            db.LeaseReminders.Add(new LeaseReminder(item.Lease.OrganizationId, item.Lease.Id, item.Key, to.Count, clock.UtcNow));
        }
        await db.SaveChangesAsync(ct);
        return toSend.Count;
    }

    /// <summary>The smallest chosen day that a lease <paramref name="daysLeft"/> days from its end has reached, if any.</summary>
    private static int? Threshold(int[] days, int daysLeft)
    {
        var reached = days.Where(t => daysLeft <= t).ToList();
        return reached.Count == 0 ? null : reached.Min();
    }

    /// <summary>The email addresses on file for each lease's tenants.</summary>
    private async Task<Dictionary<Guid, List<string>>> TenantEmailsAsync(IReadOnlyList<Guid> leaseIds, CancellationToken ct)
    {
        if (leaseIds.Count == 0) return [];
        var rows = await db.LeaseTenants.Where(lt => leaseIds.Contains(lt.LeaseId))
            .Join(db.Tenants, lt => lt.TenantId, t => t.Id, (lt, t) => new { lt.LeaseId, t.Email })
            .Where(x => x.Email != null).ToListAsync(ct);
        return rows.GroupBy(x => x.LeaseId).ToDictionary(g => g.Key, g => g.Select(x => x.Email!).Distinct().ToList());
    }

    /// <summary>Email addresses per property: its manager if it has an active one, otherwise the organization admins.</summary>
    private async Task<Dictionary<Guid, List<string>>> RecipientsAsync(IReadOnlyList<Lease> leases, CancellationToken ct)
    {
        var propertyIds = leases.Select(l => l.PropertyId).Distinct().ToList();
        var managers = await db.Properties.Where(p => propertyIds.Contains(p.Id))
            .Select(p => new { p.Id, p.ManagerUserId }).ToListAsync(ct);
        var activeMembers = await db.Memberships.Where(m => m.Status == MembershipStatus.Active)
            .Select(m => new { m.UserId, Roles = m.Roles.Select(r => r.RoleName).ToList() }).ToListAsync(ct);
        var activeIds = activeMembers.Select(m => m.UserId).ToHashSet();
        var admins = activeMembers.Where(m => m.Roles.Contains(Roles.OrganizationAdmin)).Select(m => m.UserId).ToList();
        var people = await users.GetAsync(activeIds, ct);

        return managers.ToDictionary(p => p.Id, p =>
        {
            var ids = p.ManagerUserId is { } manager && activeIds.Contains(manager) ? [manager] : admins;
            return ids.Select(id => people.GetValueOrDefault(id)?.Email).OfType<string>().Distinct().ToList();
        });
    }

    private static string Describe(NoticeType type) => type switch
    {
        NoticeType.NoticeToQuit => "notice to quit",
        NoticeType.OwnersIntentionToRecoverPossession => "notice of owner's intention to recover possession",
        NoticeType.RentReview => "rent review notice",
        _ => "notice",
    };
}
