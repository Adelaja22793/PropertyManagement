using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Leases;

namespace PropertyManagement.Application.Leases;

/// <summary>Notices served on a lease and its move-in and move-out inspections (design 6.5).</summary>
public sealed class LeaseRecordsService(
    IAppDbContext db,
    LeaseService leases,
    ICurrentUser currentUser,
    IUserDirectory users,
    IAuditLogger audit,
    IClock clock)
{
    // Notices

    public async Task<IReadOnlyList<LeaseNoticeDto>> ListNoticesAsync(Guid leaseId, CancellationToken ct)
    {
        await LoadLeaseAsync(leaseId, ct);
        var notices = await db.LeaseNotices.Where(n => n.LeaseId == leaseId)
            .OrderByDescending(n => n.ServedOn).ThenByDescending(n => n.RecordedAtUtc).ToListAsync(ct);
        var people = await users.GetAsync(notices.Select(n => n.RecordedByUserId).Distinct(), ct);
        var today = clock.TodayInNigeria();
        return notices.Select(n => ToDto(n, people.GetValueOrDefault(n.RecordedByUserId)?.FullName, today)).ToList();
    }

    public async Task<NoticeDefaultDto> DefaultNoticeAsync(Guid leaseId, NoticeType type, DateOnly? servedOn, CancellationToken ct)
    {
        var lease = await LoadLeaseAsync(leaseId, ct);
        Guard.Defined(type, "Notice type");
        var served = servedOn ?? clock.TodayInNigeria();
        var period = await PeriodAsync(lease, type, ct);
        return new NoticeDefaultDto(type, served, period.Days, NoticePeriods.ExpiryFor(served, period.Days), period.Basis);
    }

    public async Task<LeaseNoticeDto> ServeNoticeAsync(Guid leaseId, ServeNoticeRequest request, CancellationToken ct)
    {
        var lease = await LoadLeaseAsync(leaseId, ct);
        if (lease.Status != LeaseStatus.Active)
            throw new DomainException("Notices can only be served on an active lease.", "lease_not_active");
        if (request.ServedOn > clock.TodayInNigeria())
            throw new DomainException("Record a notice once it has been served. The served date can't be in the future.");
        if (request.ServedOn < lease.StartDate) throw new DomainException("A notice can't be served before the lease starts.");

        if (request.Type == NoticeType.OwnersIntentionToRecoverPossession)
        {
            var quitExpired = await db.LeaseNotices.AnyAsync(n => n.LeaseId == leaseId && n.Type == NoticeType.NoticeToQuit
                && n.Status == NoticeStatus.Served && n.ExpiresOn < request.ServedOn, ct);
            if (!quitExpired)
                throw new DomainException(
                    "Serve the notice of owner's intention to recover possession only after a notice to quit has expired.",
                    "notice_sequence");
        }

        var expires = request.ExpiresOn
                      ?? NoticePeriods.ExpiryFor(request.ServedOn, (await PeriodAsync(lease, request.Type, ct)).Days);
        var userId = currentUser.RequireUserId();
        var notice = new LeaseNotice(lease.OrganizationId, lease.Id,
            new NoticeDetails(request.Type, request.ServedOn, request.Method, request.ServedBy, expires, request.Notes),
            userId, clock.UtcNow);
        db.LeaseNotices.Add(notice);
        audit.Record("lease.notice_served", nameof(LeaseNotice), notice.Id.ToString(), details: new
        {
            lease.Reference, notice.Type, notice.ServedOn, notice.Method, notice.ExpiresOn,
        });
        await db.SaveChangesAsync(ct);
        return ToDto(notice, await NameAsync(userId, ct), clock.TodayInNigeria());
    }

    public async Task<LeaseNoticeDto> WithdrawNoticeAsync(Guid leaseId, Guid noticeId, WithdrawNoticeRequest request, CancellationToken ct)
    {
        var lease = await LoadLeaseAsync(leaseId, ct);
        var notice = await db.LeaseNotices.SingleOrDefaultAsync(n => n.Id == noticeId && n.LeaseId == leaseId, ct)
                     ?? throw new NotFoundException("Notice");
        notice.Withdraw(request.Reason);
        audit.Record("lease.notice_withdrawn", nameof(LeaseNotice), notice.Id.ToString(),
            details: new { lease.Reference, notice.Type, request.Reason });
        await db.SaveChangesAsync(ct);
        return ToDto(notice, await NameAsync(notice.RecordedByUserId, ct), clock.TodayInNigeria());
    }

    // Inspections

    public async Task<LeaseInspectionsDto> GetInspectionsAsync(Guid leaseId, CancellationToken ct)
    {
        await LoadLeaseAsync(leaseId, ct);
        var reports = await db.LeaseInspections.Include(i => i.Items).Where(i => i.LeaseId == leaseId).ToListAsync(ct);
        var people = await users.GetAsync(reports.Select(r => r.InspectedByUserId).Distinct(), ct);
        var deposit = await db.LeaseCharges.Where(c => c.LeaseId == leaseId && c.Type == LeaseChargeType.CautionDeposit)
            .Select(c => (decimal?)c.Amount).FirstOrDefaultAsync(ct);
        LeaseInspectionDto? Find(InspectionType type) => reports.FirstOrDefault(r => r.Type == type) is { } r
            ? ToDto(r, people.GetValueOrDefault(r.InspectedByUserId)?.FullName)
            : null;
        return new LeaseInspectionsDto(Find(InspectionType.MoveIn), Find(InspectionType.MoveOut), deposit);
    }

    /// <summary>Records or replaces the move-in or move-out report.</summary>
    public async Task<LeaseInspectionDto> SaveInspectionAsync(Guid leaseId, InspectionType type, SaveInspectionRequest request, CancellationToken ct)
    {
        Guard.Defined(type, "Inspection type");
        var lease = await LoadLeaseAsync(leaseId, ct);
        var allowed = type == InspectionType.MoveIn
            ? lease.Status is LeaseStatus.Draft or LeaseStatus.Active
            : lease.Status is LeaseStatus.Active or LeaseStatus.Ended or LeaseStatus.Terminated;
        if (!allowed)
            throw new DomainException(type == InspectionType.MoveIn
                ? "A move-in inspection is recorded on a draft or active lease."
                : "A move-out inspection is recorded on an active, ended or terminated lease.", "lease_not_active");
        if (request.InspectedOn > clock.TodayInNigeria()) throw new DomainException("The inspection date can't be in the future.");

        var userId = currentUser.RequireUserId();
        var details = new InspectionDetails(type, request.InspectedOn, request.TenantPresent, request.TenantAgreed,
            request.Summary, request.Items ?? []);
        var report = await db.LeaseInspections.Include(i => i.Items)
            .SingleOrDefaultAsync(i => i.LeaseId == leaseId && i.Type == type, ct);
        if (report is null)
        {
            report = new LeaseInspection(lease.OrganizationId, lease.Id, details, userId, clock.UtcNow);
            db.LeaseInspections.Add(report);
        }
        else
        {
            report.Update(details, userId, clock.UtcNow);
        }
        audit.Record(type == InspectionType.MoveIn ? "lease.move_in_inspected" : "lease.move_out_inspected",
            nameof(LeaseInspection), report.Id.ToString(), details: new
            {
                lease.Reference, report.InspectedOn, Areas = report.Items.Count, report.TotalProposedDeduction,
            });
        await db.SaveChangesAsync(ct);
        return ToDto(report, await NameAsync(userId, ct));
    }

    /// <summary>The suggested notice period for a lease, using the organization's rules for its property's state.</summary>
    private async Task<NoticePeriod> PeriodAsync(Lease lease, NoticeType type, CancellationToken ct)
    {
        var state = await db.Properties.Where(p => p.Id == lease.PropertyId).Select(p => p.State).SingleAsync(ct);
        var rules = await db.NoticePeriodRules.Where(r => r.Type == type).ToListAsync(ct);
        return NoticePeriods.Resolve(type, lease.BillingFrequency, lease.NoticePeriodDays, state, rules);
    }

    private async Task<Lease> LoadLeaseAsync(Guid leaseId, CancellationToken ct) =>
        await leases.VisibleLeases().SingleOrDefaultAsync(l => l.Id == leaseId, ct) ?? throw new NotFoundException("Lease");

    private async Task<string?> NameAsync(Guid userId, CancellationToken ct) =>
        (await users.GetAsync([userId], ct)).GetValueOrDefault(userId)?.FullName;

    private static LeaseNoticeDto ToDto(LeaseNotice n, string? recordedBy, DateOnly today) => new(n.Id, n.Type, n.ServedOn,
        n.Method, n.ServedBy, n.ExpiresOn, n.Notes, n.Status, n.WithdrawnReason, recordedBy, n.RecordedAtUtc,
        n.ExpiresOn < today, Math.Max(0, n.ExpiresOn.DayNumber - today.DayNumber));

    private static LeaseInspectionDto ToDto(LeaseInspection r, string? inspectedBy) => new(r.Id, r.Type, r.InspectedOn,
        r.TenantPresent, r.TenantAgreed, r.Summary, inspectedBy, r.UpdatedAtUtc,
        r.Items.OrderBy(i => i.Position).Select(i => new InspectionItemDto(i.Area, i.Condition, i.Notes, i.ProposedDeduction)).ToList(),
        r.TotalProposedDeduction);
}
