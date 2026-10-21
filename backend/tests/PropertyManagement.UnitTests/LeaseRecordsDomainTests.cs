using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Leases;
using PropertyManagement.Domain.Properties;

namespace PropertyManagement.UnitTests;

public sealed class LeaseRecordsDomainTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 20);

    [Theory]
    [InlineData(NoticeType.NoticeToQuit, BillingFrequency.Annual, null, 182)]
    [InlineData(NoticeType.NoticeToQuit, BillingFrequency.Biannual, null, 90)]
    [InlineData(NoticeType.NoticeToQuit, BillingFrequency.Quarterly, null, 90)]
    [InlineData(NoticeType.NoticeToQuit, BillingFrequency.Monthly, null, 30)]
    [InlineData(NoticeType.NoticeToQuit, BillingFrequency.Annual, 120, 120)]
    [InlineData(NoticeType.OwnersIntentionToRecoverPossession, BillingFrequency.Annual, 120, 7)]
    [InlineData(NoticeType.RentReview, BillingFrequency.Annual, null, 30)]
    public void Notice_periods_default_by_type_and_tenancy(NoticeType type, BillingFrequency frequency, int? leaseDays, int expected)
    {
        Assert.Equal(expected, NoticePeriods.DefaultDays(type, frequency, leaseDays));
    }

    [Fact]
    public void A_notice_cannot_expire_before_it_is_served_and_withdraws_once()
    {
        Assert.Throws<DomainException>(() => new LeaseNotice(Org, Guid.NewGuid(),
            new NoticeDetails(NoticeType.NoticeToQuit, Today, ServiceMethod.Email, null, Today.AddDays(-1), null), Guid.NewGuid(), DateTime.UtcNow));
        Assert.Throws<DomainException>(() => new LeaseNotice(Org, Guid.NewGuid(),
            new NoticeDetails(NoticeType.Other, Today, ServiceMethod.Email, null, Today, null), Guid.NewGuid(), DateTime.UtcNow));

        var notice = new LeaseNotice(Org, Guid.NewGuid(),
            new NoticeDetails(NoticeType.NoticeToQuit, Today, ServiceMethod.PersonalService, "Bailiff", Today.AddDays(182), null),
            Guid.NewGuid(), DateTime.UtcNow);
        notice.Withdraw("Renewed instead");
        Assert.Equal(NoticeStatus.Withdrawn, notice.Status);
        Assert.Throws<DomainException>(() => notice.Withdraw(null));
    }

    [Fact]
    public void Inspections_total_move_out_deductions_and_replace_items()
    {
        var report = new LeaseInspection(Org, Guid.NewGuid(), new InspectionDetails(InspectionType.MoveOut, Today, true, false, null,
        [
            new InspectionItemInput("Kitchen", ItemCondition.Damaged, "Cabinet", 45_000m),
            new InspectionItemInput("Bedroom", ItemCondition.Good, null, null),
        ]), Guid.NewGuid(), DateTime.UtcNow);
        Assert.Equal(45_000m, report.TotalProposedDeduction);

        report.Update(new InspectionDetails(InspectionType.MoveOut, Today, true, true, "Agreed", [new InspectionItemInput("Kitchen", ItemCondition.Poor, null, 10m)]),
            Guid.NewGuid(), DateTime.UtcNow);
        Assert.Single(report.Items);
        Assert.Equal(10m, report.TotalProposedDeduction);

        Assert.Throws<DomainException>(() => report.Update(
            new InspectionDetails(InspectionType.MoveIn, Today, true, true, null, [new InspectionItemInput("Kitchen", ItemCondition.Good, null, null)]),
            Guid.NewGuid(), DateTime.UtcNow));
        Assert.Throws<DomainException>(() => new LeaseInspection(Org, Guid.NewGuid(),
            new InspectionDetails(InspectionType.MoveIn, Today, true, false, null, [new InspectionItemInput("Kitchen", ItemCondition.Good, null, 5m)]),
            Guid.NewGuid(), DateTime.UtcNow));
        Assert.Throws<DomainException>(() => new LeaseInspection(Org, Guid.NewGuid(),
            new InspectionDetails(InspectionType.MoveIn, Today, true, false, null, []), Guid.NewGuid(), DateTime.UtcNow));
    }
}

public sealed class NoticePeriodRuleTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc);

    private static NoticePeriodRule Rule(string? state, BillingFrequency? frequency, int days, NoticeType type = NoticeType.NoticeToQuit) =>
        new(Org, new NoticePeriodRuleDetails(state, type, frequency, days, null), Now);

    [Fact]
    public void Without_rules_the_Lagos_defaults_apply()
    {
        var period = NoticePeriods.Resolve(NoticeType.NoticeToQuit, BillingFrequency.Monthly, null, "Lagos", []);
        Assert.Equal(30, period.Days);
        Assert.Contains("Lagos Tenancy Law", period.Basis);
        Assert.Equal(7, NoticePeriods.Resolve(NoticeType.OwnersIntentionToRecoverPossession, BillingFrequency.Annual, 90, "Lagos", []).Days);
    }

    [Fact]
    public void The_most_specific_matching_rule_wins()
    {
        NoticePeriodRule[] rules =
        [
            Rule(null, null, 100), Rule(null, BillingFrequency.Annual, 110), Rule("Ogun", null, 120), Rule("Ogun", BillingFrequency.Annual, 130),
            Rule("Ogun", null, 5, NoticeType.RentReview),
        ];
        Assert.Equal(130, NoticePeriods.Resolve(NoticeType.NoticeToQuit, BillingFrequency.Annual, null, " ogun ", rules).Days);
        Assert.Equal(120, NoticePeriods.Resolve(NoticeType.NoticeToQuit, BillingFrequency.Monthly, null, "Ogun", rules).Days);
        Assert.Equal(110, NoticePeriods.Resolve(NoticeType.NoticeToQuit, BillingFrequency.Annual, null, "Oyo", rules).Days);
        Assert.Equal(100, NoticePeriods.Resolve(NoticeType.NoticeToQuit, BillingFrequency.Monthly, null, "Oyo", rules).Days);
        Assert.Equal(30, NoticePeriods.Resolve(NoticeType.RentReview, BillingFrequency.Monthly, null, "Oyo", rules).Days);
    }

    [Fact]
    public void A_lease_period_wins_except_for_the_owners_intention_notice()
    {
        NoticePeriodRule[] rules = [Rule(null, null, 100), Rule(null, null, 14, NoticeType.OwnersIntentionToRecoverPossession)];
        var lease = NoticePeriods.Resolve(NoticeType.NoticeToQuit, BillingFrequency.Annual, 45, "Lagos", rules);
        Assert.Equal(45, lease.Days);
        Assert.Contains("agreed in this lease", lease.Basis);
        Assert.Equal(14, NoticePeriods.Resolve(NoticeType.OwnersIntentionToRecoverPossession, BillingFrequency.Annual, 45, "Lagos", rules).Days);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(731)]
    public void Days_must_be_sensible(int days) =>
        Assert.Throws<DomainException>(() => Rule(null, null, days));

    [Fact]
    public void Reminder_days_are_sorted_deduplicated_and_checked()
    {
        var settings = new LeaseSettings(Org);
        Assert.Equal([90, 60, 30], settings.GetStaffDays());
        Assert.Equal([60, 30], settings.GetTenantDays());

        settings.Update([7, 45, 7], tenantRemindersEnabled: false, [], Now);
        Assert.Equal([45, 7], settings.GetStaffDays());
        Assert.Equal([60, 30], settings.GetTenantDays());

        Assert.Throws<DomainException>(() => settings.Update([], false, [], Now));
        Assert.Throws<DomainException>(() => settings.Update([1, 2, 3, 4, 5, 6], false, [], Now));
        Assert.Throws<DomainException>(() => settings.Update([30], true, [], Now));
        Assert.Throws<DomainException>(() => settings.Update([366], false, [], Now));
    }
}
