using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Domain.Leases;
using PropertyManagement.Domain.Organizations;

namespace PropertyManagement.Application.Leases;

/// <summary>
/// Emails staff about leases that need action (design 6.5): 90, 60 and 30 days before a lease ends, when a lease
/// starts holding over, and when a notice served on the tenant expires. Each reminder goes out once, recorded in
/// <see cref="LeaseReminder"/>, however often the job runs. It goes to the property's manager, or to the
/// organization admins when the property has no manager.
/// </summary>
public sealed class LeaseReminderService(IAppDbContext db, IUserDirectory users, IEmailSender email, IClock clock)
{
    public static readonly int[] ExpiryDays = [90, 60, 30];

    private sealed record Due(Lease Lease, string Key, string Subject, string Body);

    /// <summary>Sends every reminder now due in the current organization. Returns how many reminders were sent.</summary>
    public async Task<int> SendDueAsync(CancellationToken ct)
    {
        var today = clock.TodayInNigeria();
        var horizon = today.AddDays(ExpiryDays.Max());
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
            var threshold = ExpiryDays.Where(t => days <= t).Min();
            due.Add(new Due(lease, $"expiry-{threshold}", $"Lease {lease.Reference} ends in {days} days",
                $"Lease {lease.Reference} ends on {lease.EndDate:d MMM yyyy}. Agree a renewal with the tenant and owner, " +
                "or serve notice if it won't be renewed."));
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

        var recipients = await RecipientsAsync(toSend.Select(d => d.Lease).ToList(), ct);
        var organizationId = toSend[0].Lease.OrganizationId;
        var organization = await db.Organizations.Where(o => o.Id == organizationId).Select(o => o.Name).SingleAsync(ct);
        foreach (var item in toSend)
        {
            var to = recipients.GetValueOrDefault(item.Lease.PropertyId) ?? [];
            var unit = await db.Units.Where(u => u.Id == item.Lease.UnitId)
                .Select(u => new { u.Code, Property = db.Properties.Where(p => p.Id == u.PropertyId).Select(p => p.Name).First() })
                .FirstAsync(ct);
            var body = $"{item.Body}\n\nProperty: {unit.Property}, unit {unit.Code}.\n\n{organization}";
            foreach (var address in to) await email.SendAsync(address, $"{organization}: {item.Subject}", body, ct);
            db.LeaseReminders.Add(new LeaseReminder(item.Lease.OrganizationId, item.Lease.Id, item.Key, to.Count, clock.UtcNow));
        }
        await db.SaveChangesAsync(ct);
        return toSend.Count;
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
