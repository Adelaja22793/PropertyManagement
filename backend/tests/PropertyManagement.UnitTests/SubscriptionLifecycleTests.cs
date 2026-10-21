using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Organizations;
using PropertyManagement.Domain.Platform;

namespace PropertyManagement.UnitTests;

public class SubscriptionLifecycleTests
{
    private static readonly SubscriptionPlan Starter = new(Guid.NewGuid(), "starter", "Starter", 50, 5, 0m, 0m, 30);
    private static readonly DateTime Start = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_trial_lapses_when_it_ends_then_turns_read_only_after_the_grace_period()
    {
        var s = OrganizationSubscription.StartTrial(Guid.NewGuid(), Starter, Start);
        Assert.False(s.Advance(Start.AddDays(29)));

        Assert.True(s.Advance(Start.AddDays(31)));
        Assert.Equal(SubscriptionStatus.PastDue, s.Status);
        Assert.Equal(Start.AddDays(30 + OrganizationSubscription.GraceDays), s.SuspendsAtUtc);
        Assert.Equal(OrganizationStatus.Active, s.OrganizationStatus);

        Assert.False(s.Advance(Start.AddDays(43)));
        Assert.True(s.Advance(Start.AddDays(45)));
        Assert.Equal(SubscriptionStatus.Suspended, s.Status);
        Assert.Equal(OrganizationStatus.Suspended, s.OrganizationStatus);
        Assert.False(s.Advance(Start.AddDays(400)));
    }

    [Fact]
    public void Paying_up_reinstates_and_cancelling_opens_a_90_day_export_window()
    {
        var s = OrganizationSubscription.StartTrial(Guid.NewGuid(), Starter, Start);
        while (s.Advance(Start.AddDays(60))) { }
        Assert.Equal(SubscriptionStatus.Suspended, s.Status);

        s.Activate(Starter, Start.AddDays(425), Start.AddDays(60));
        Assert.Equal(SubscriptionStatus.Active, s.Status);
        Assert.Null(s.SuspendsAtUtc);
        Assert.Throws<DomainException>(() => s.Activate(Starter, Start, Start.AddDays(60)));

        s.Cancel(Start.AddDays(100));
        Assert.Equal(Start.AddDays(190), s.ExportUntilUtc);
        Assert.Equal(OrganizationStatus.Cancelled, s.OrganizationStatus);
        Assert.Throws<DomainException>(() => s.ExtendTrial(Start.AddDays(200), Start.AddDays(101)));
        Assert.Throws<DomainException>(() => s.Suspend(Start.AddDays(101)));
    }

    [Fact]
    public void Each_notice_is_taken_once()
    {
        var s = OrganizationSubscription.StartTrial(Guid.NewGuid(), Starter, Start);
        Assert.True(s.TakeNotice("trial-ending:20260131"));
        Assert.False(s.TakeNotice("trial-ending:20260131"));
        Assert.True(s.TakeNotice("past-due:20260131"));
    }
}
