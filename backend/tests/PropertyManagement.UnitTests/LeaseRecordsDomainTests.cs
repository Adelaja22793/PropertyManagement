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
