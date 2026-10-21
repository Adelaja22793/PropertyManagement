using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Leases;
using PropertyManagement.Domain.Properties;

namespace PropertyManagement.UnitTests;

public sealed class UnitCapacityTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 20);
    private static int _number;

    private static Lease Active(int startOffset, int endOffset)
    {
        var lease = new Lease(Org, ++_number, Guid.NewGuid(), Guid.NewGuid(),
            new LeaseTerms(Today.AddDays(startOffset), Today.AddDays(endOffset), 500_000m, "NGN", BillingFrequency.Annual, 0, 7, null, null, null, null));
        lease.SetParties([Guid.NewGuid()], []);
        lease.Activate(Guid.NewGuid(), DateTime.UtcNow);
        return lease;
    }

    [Fact]
    public void A_single_unit_is_full_with_one_overlapping_lease()
    {
        Lease[] others = [Active(0, 100)];
        var full = UnitCapacity.FirstFullDay(Today.AddDays(50), Today.AddDays(200), others, 1, Today);
        Assert.NotNull(full);
        Assert.Equal(Today.AddDays(50), full.Value.Day);
        Assert.Null(UnitCapacity.FirstFullDay(Today.AddDays(101), Today.AddDays(200), others, 1, Today));
    }

    [Fact]
    public void A_shared_unit_is_full_only_when_its_limit_is_reached_on_one_day()
    {
        // Two leases that never overlap each other leave room for a third on every day of a limit-2 unit.
        Lease[] apart = [Active(0, 50), Active(60, 120)];
        Assert.Null(UnitCapacity.FirstFullDay(Today, Today.AddDays(120), apart, 2, Today));

        // Once two overlap from day 30, a new lease covering day 30 can't fit.
        Lease[] together = [Active(0, 50), Active(30, 120)];
        var full = UnitCapacity.FirstFullDay(Today.AddDays(10), Today.AddDays(200), together, 2, Today);
        Assert.Equal(Today.AddDays(30), full!.Value.Day);
        Assert.Equal(2, full.Value.Holding.Count);
        Assert.Null(UnitCapacity.FirstFullDay(Today.AddDays(10), Today.AddDays(200), together, 3, Today));
    }

    [Fact]
    public void Most_at_once_counts_only_today_and_later()
    {
        var ended = Active(-200, -100);
        ended.End(Today.AddDays(-100), null, DateTime.UtcNow);
        Lease[] leases = [ended, Active(-150, 100), Active(10, 200), Active(150, 300)];
        Assert.Equal(2, UnitCapacity.MostAtOnceFrom(Today, leases));
        Assert.Equal(0, UnitCapacity.MostAtOnceFrom(Today, []));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(51)]
    public void A_shared_limit_must_be_sensible(int limit) =>
        Assert.Throws<DomainException>(() => new PropertyUnit(Org, Guid.NewGuid(),
            new UnitDetails("R1", UnitType.Room, null, 1, 1, null, 500_000m, "NGN", BillingFrequency.Annual, null, null, null, limit)));
}
