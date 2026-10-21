using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Properties;

namespace PropertyManagement.Domain.Leases;

public enum NoticeType
{
    /// <summary>Ends a periodic or holding-over tenancy on a date (Lagos Tenancy Law 2011, s.13).</summary>
    NoticeToQuit = 0,
    /// <summary>The 7-day notice of the owner's intention to recover possession, served after a notice to quit expires (s.16).</summary>
    OwnersIntentionToRecoverPossession = 1,
    RentReview = 2,
    Other = 3,
}

public enum ServiceMethod { PersonalService = 0, PastedOnPremises = 1, RegisteredPost = 2, Courier = 3, Email = 4 }

public enum NoticeStatus { Served = 0, Withdrawn = 1 }

public sealed record NoticeDetails(NoticeType Type, DateOnly ServedOn, ServiceMethod Method, string? ServedBy, DateOnly ExpiresOn, string? Notes);

/// <summary>
/// A notice served on a lease's tenants (design 6.5): what was served, when, how and when it expires. The system
/// records and tracks notices; the wording comes from the organization's lawyer. Withdrawing keeps the record.
/// </summary>
public class LeaseNotice : IOrganizationScoped
{
    private LeaseNotice() { }

    public LeaseNotice(Guid organizationId, Guid leaseId, NoticeDetails details, Guid recordedByUserId, DateTime nowUtc)
    {
        Guard.Defined(details.Type, "Notice type");
        Guard.Defined(details.Method, "Method of service");
        if (details.ExpiresOn < details.ServedOn) throw new DomainException("A notice can't expire before it is served.");
        if (details.Type == NoticeType.Other && string.IsNullOrWhiteSpace(details.Notes))
            throw new DomainException("Describe the notice in the notes.");
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        LeaseId = leaseId;
        Type = details.Type;
        ServedOn = details.ServedOn;
        Method = details.Method;
        ServedBy = Guard.Optional(details.ServedBy, "Served by", 200);
        ExpiresOn = details.ExpiresOn;
        Notes = Guard.Optional(details.Notes, "Notes", 2000);
        Status = NoticeStatus.Served;
        RecordedByUserId = recordedByUserId;
        RecordedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid LeaseId { get; private set; }
    public NoticeType Type { get; private set; }
    public DateOnly ServedOn { get; private set; }
    public ServiceMethod Method { get; private set; }
    /// <summary>Who served it, such as a lawyer, bailiff or staff member.</summary>
    public string? ServedBy { get; private set; }
    /// <summary>The last day of the notice period.</summary>
    public DateOnly ExpiresOn { get; private set; }
    public string? Notes { get; private set; }
    public NoticeStatus Status { get; private set; }
    public string? WithdrawnReason { get; private set; }
    public Guid RecordedByUserId { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }

    public void Withdraw(string? reason)
    {
        if (Status == NoticeStatus.Withdrawn) throw new DomainException("This notice is already withdrawn.", "already_decided");
        Status = NoticeStatus.Withdrawn;
        WithdrawnReason = Guard.Optional(reason, "Reason", 500);
    }
}

/// <summary>
/// Default notice periods, so staff get a sensible expiry date to start from. They follow the Lagos State Tenancy
/// Law 2011 and are a starting point only: other states differ, and a lawyer must confirm them for each organization.
/// Staff can always change the expiry date, and a lease's own notice period wins for a notice to quit.
/// </summary>
public sealed record NoticePeriod(int Days, string Basis);

public static class NoticePeriods
{
    public static int DefaultDays(NoticeType type, BillingFrequency frequency, int? leaseNoticeDays) => type switch
    {
        NoticeType.OwnersIntentionToRecoverPossession => 7,
        NoticeType.NoticeToQuit when leaseNoticeDays is { } days => days,
        NoticeType.NoticeToQuit => frequency switch
        {
            BillingFrequency.Monthly => 30,
            BillingFrequency.Quarterly or BillingFrequency.Biannual => 90,
            _ => 182,
        },
        _ => leaseNoticeDays ?? 30,
    };

    /// <summary>
    /// The notice period to suggest for a lease, and why. A lease's own notice period wins, except for the statutory
    /// 7-day notice of owner's intention; then the organization's most specific matching rule (state before rent
    /// frequency); then the built-in Lagos default.
    /// </summary>
    public static NoticePeriod Resolve(NoticeType type, BillingFrequency frequency, int? leaseNoticeDays, string? state,
        IEnumerable<NoticePeriodRule> rules)
    {
        if (type != NoticeType.OwnersIntentionToRecoverPossession && leaseNoticeDays is { } agreed)
            return new NoticePeriod(agreed, "The notice period agreed in this lease.");

        var rule = rules.Where(r => r.Matches(type, state, frequency)).OrderByDescending(r => r.Specificity).FirstOrDefault();
        if (rule is not null)
        {
            var scope = (rule.State ?? "all states") + (rule.Frequency is { } f ? $", {f.ToString().ToLowerInvariant()} rent" : "");
            return new NoticePeriod(rule.Days, rule.Basis is null
                ? $"Your company's notice period for {scope}."
                : $"{rule.Basis} (your company's notice period for {scope}).");
        }

        var days = DefaultDays(type, frequency, null);
        return new NoticePeriod(days, type switch
        {
            NoticeType.OwnersIntentionToRecoverPossession => "7 days, after the notice to quit has expired (Lagos Tenancy Law 2011).",
            NoticeType.NoticeToQuit => "The Lagos Tenancy Law 2011 period for this kind of tenancy. Other states differ.",
            _ => "A 30-day default.",
        });
    }

    /// <summary>The last day of a notice served on <paramref name="servedOn"/> that gives <paramref name="days"/> days.</summary>
    public static DateOnly ExpiryFor(DateOnly servedOn, int days) => servedOn.AddDays(days);
}
