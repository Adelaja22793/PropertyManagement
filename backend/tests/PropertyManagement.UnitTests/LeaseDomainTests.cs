using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Leases;
using PropertyManagement.Domain.Properties;

namespace PropertyManagement.UnitTests;

public sealed class LeaseDomainTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 20);

    private static Lease NewLease(DateOnly start, DateOnly end, params LeaseChargeInput[] charges)
    {
        var lease = new Lease(Org, 7, Guid.NewGuid(), Guid.NewGuid(),
            new LeaseTerms(start, end, 3_000_000m, "NGN", BillingFrequency.Annual, 0, 7, 180, null, null, null));
        lease.SetParties([Guid.NewGuid()], []);
        lease.SetCharges(charges);
        return lease;
    }

    [Fact]
    public void Reference_is_padded()
    {
        Assert.Equal("LSE-00007", NewLease(Today, Today.AddYears(1)).Reference);
    }

    [Theory]
    [InlineData(10, 400, LeaseStage.Upcoming)]
    [InlineData(-10, 355, LeaseStage.Active)]
    [InlineData(-300, 89, LeaseStage.Expiring)]
    [InlineData(-300, 0, LeaseStage.Expiring)]
    [InlineData(-400, -1, LeaseStage.HoldingOver)]
    public void Stage_follows_the_dates(int startOffset, int endOffset, LeaseStage expected)
    {
        var lease = NewLease(Today.AddDays(startOffset), Today.AddDays(endOffset));
        Assert.Equal(LeaseStage.Draft, lease.StageOn(Today));
        lease.Activate(Guid.NewGuid(), DateTime.UtcNow);
        Assert.Equal(expected, lease.StageOn(Today));
    }

    [Fact]
    public void Drafts_and_cancelled_leases_never_block_a_unit()
    {
        var lease = NewLease(Today, Today.AddYears(1));
        Assert.False(lease.BlocksUnitBetween(Today, Today.AddDays(5), Today));
        lease.Cancel("No longer needed", DateTime.UtcNow);
        Assert.False(lease.BlocksUnitBetween(Today, Today.AddDays(5), Today));
    }

    [Fact]
    public void An_active_lease_past_its_end_blocks_the_unit_until_ended()
    {
        var lease = NewLease(Today.AddYears(-1), Today.AddDays(-3));
        lease.Activate(Guid.NewGuid(), DateTime.UtcNow);
        Assert.True(lease.BlocksUnitBetween(Today.AddYears(2), Today.AddYears(3), Today));

        lease.End(Today, null, DateTime.UtcNow);
        Assert.Equal(LeaseStatus.Ended, lease.Status);
        Assert.True(lease.BlocksUnitBetween(Today.AddDays(-1), Today.AddDays(-1), Today));
        Assert.False(lease.BlocksUnitBetween(Today, Today.AddYears(1), Today));
    }

    [Fact]
    public void Ending_before_the_end_date_needs_a_reason_and_is_a_termination()
    {
        var lease = NewLease(Today.AddMonths(-2), Today.AddMonths(10));
        lease.Activate(Guid.NewGuid(), DateTime.UtcNow);
        Assert.Throws<DomainException>(() => lease.End(Today, " ", DateTime.UtcNow));
        lease.End(Today, "Job transfer", DateTime.UtcNow);
        Assert.Equal(LeaseStatus.Terminated, lease.Status);
        Assert.Equal(LeaseStage.Terminated, lease.StageOn(Today));
    }

    [Fact]
    public void Only_drafts_can_be_changed()
    {
        var lease = NewLease(Today, Today.AddYears(1));
        lease.Activate(Guid.NewGuid(), DateTime.UtcNow);
        var ex = Assert.Throws<DomainException>(() => lease.SetCharges([]));
        Assert.Equal("lease_not_draft", ex.Code);
        Assert.Throws<DomainException>(() => lease.Activate(Guid.NewGuid(), DateTime.UtcNow));
    }

    [Fact]
    public void Terms_and_charges_are_validated()
    {
        Assert.Throws<DomainException>(() => NewLease(Today, Today));
        Assert.Throws<DomainException>(() => NewLease(Today, Today.AddYears(1),
            new LeaseChargeInput(LeaseChargeType.CautionDeposit, null, 1m, ChargeRecurrence.EachPeriod)));
        Assert.Throws<DomainException>(() => NewLease(Today, Today.AddYears(1),
            new LeaseChargeInput(LeaseChargeType.Other, null, 1m, ChargeRecurrence.OneOff)));
        Assert.Throws<DomainException>(() => NewLease(Today, Today.AddYears(1),
            new LeaseChargeInput(LeaseChargeType.CautionDeposit, null, 1m, ChargeRecurrence.OneOff),
            new LeaseChargeInput(LeaseChargeType.CautionDeposit, null, 2m, ChargeRecurrence.OneOff)));
        Assert.Throws<DomainException>(() => NewLease(Today, Today.AddYears(1),
            new LeaseChargeInput(LeaseChargeType.ServiceCharge, null, 0m, ChargeRecurrence.EachPeriod)));
    }

    [Fact]
    public void Parties_keep_one_primary_tenant()
    {
        var lease = NewLease(Today, Today.AddYears(1));
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        lease.SetParties([a, b], []);
        Assert.True(lease.Tenants.Single(t => t.TenantId == a).IsPrimary);
        lease.SetParties([b, a], []);
        Assert.Equal(b, lease.Tenants.Single(t => t.IsPrimary).TenantId);
        Assert.Throws<DomainException>(() => lease.SetParties([], []));
        Assert.Throws<DomainException>(() => lease.SetParties([a, a], []));
    }

    [Theory]
    [InlineData(OccupancyStatus.Vacant, true, false, OccupancyStatus.Occupied)]
    [InlineData(OccupancyStatus.Vacant, false, true, OccupancyStatus.Reserved)]
    [InlineData(OccupancyStatus.Occupied, false, false, OccupancyStatus.Vacant)]
    [InlineData(OccupancyStatus.Reserved, false, false, OccupancyStatus.Vacant)]
    [InlineData(OccupancyStatus.Unavailable, false, false, OccupancyStatus.Unavailable)]
    [InlineData(OccupancyStatus.Unavailable, true, false, OccupancyStatus.Occupied)]
    public void Leases_drive_unit_occupancy(OccupancyStatus from, bool heldToday, bool heldLater, OccupancyStatus expected)
    {
        var unit = new PropertyUnit(Org, Guid.NewGuid(), new UnitDetails("A1", UnitType.Flat, null, 2, 2, null, 1m, "NGN",
            BillingFrequency.Annual, null, null, null));
        if (from != OccupancyStatus.Vacant)
            unit.Update(new UnitDetails("A1", UnitType.Flat, null, 2, 2, null, 1m, "NGN", BillingFrequency.Annual, null, null, null), from);
        unit.ApplyLeaseOccupancy(heldToday, heldLater);
        Assert.Equal(expected, unit.OccupancyStatus);
    }
}
