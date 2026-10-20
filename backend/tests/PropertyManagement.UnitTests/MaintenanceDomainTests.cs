using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Maintenance;

namespace PropertyManagement.UnitTests;

public class MaintenanceDomainTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    private static MaintenanceRequest NewRequest() => new(Guid.NewGuid(), 7, Guid.NewGuid(),
        new MaintenanceDetails(null, null, "Caretaker", "Generator won't start", null, MaintenanceCategory.Generator,
            MaintenanceUrgency.Emergency), User, Now);

    [Fact]
    public void Reference_is_padded() => Assert.Equal("MNT-00007", NewRequest().Reference);

    [Fact]
    public void Estimate_above_the_limit_needs_the_owner_before_approval()
    {
        var r = NewRequest();
        r.Estimate(80_000m, null, 50_000m, null, User, Now);
        Assert.True(r.NeedsOwnerApproval);
        Assert.Equal("owner_approval_required", Assert.Throws<DomainException>(() => r.Approve(User, Now)).Code);
        r.RecordOwnerApproval("Called Mr Okafor", User, Now);
        r.Approve(User, Now);
        Assert.Equal(MaintenanceStatus.Approved, r.Status);
    }

    [Fact]
    public void Estimate_within_the_limit_or_with_no_limit_needs_no_owner()
    {
        var r = NewRequest();
        r.Estimate(50_000m, null, 50_000m, null, User, Now);
        Assert.False(r.NeedsOwnerApproval);
        Assert.Throws<DomainException>(() => r.RecordOwnerApproval("not needed", User, Now));
        r.Estimate(5_000_000m, null, null, null, User, Now);
        Assert.False(r.NeedsOwnerApproval);
    }

    [Fact]
    public void A_higher_estimate_after_approval_must_carry_the_owners_approval()
    {
        var r = NewRequest();
        r.Approve(User, Now);
        Assert.Equal("owner_approval_required",
            Assert.Throws<DomainException>(() => r.Estimate(90_000m, null, 50_000m, null, User, Now)).Code);
        r.Estimate(90_000m, null, 50_000m, "Owner agreed by email", User, Now);
        Assert.Equal("Owner agreed by email", r.OwnerApprovalNote);
    }

    [Fact]
    public void Work_moves_through_assignment_to_completion_and_closed_requests_stay_closed()
    {
        var r = NewRequest();
        Assert.Throws<DomainException>(() => r.Assign(User, null, "x", User, Now));
        r.Approve(User, Now);
        Assert.Throws<DomainException>(() => r.Assign(null, null, "x", User, Now));
        r.Assign(User, null, "Assigned", User, Now);
        r.Start(User, Now);
        r.AwaitParts("Spark plug", User, Now);
        r.Start(User, Now);
        r.Complete(new DateOnly(2026, 10, 20), 12_500.555m, "Replaced plug", User, Now);
        Assert.Equal(12_500.56m, r.ActualCost);
        Assert.False(r.IsOpen);
        Assert.Throws<DomainException>(() => r.Cancel("late", User, Now));
        Assert.Throws<DomainException>(() => r.RecordFeedback(6, null, User, Now));
        r.RecordFeedback(5, null, User, Now);
        Assert.Equal(Enumerable.Range(1, r.Updates.Count), r.Updates.Select(u => u.Sequence));
    }

    [Fact]
    public void Vendors_need_a_way_to_reach_them()
    {
        Assert.Throws<DomainException>(() => new Vendor(Guid.NewGuid(), new VendorDetails("No Contact", null, null, null, null, null, null), Now));
        var v = new Vendor(Guid.NewGuid(), new VendorDetails(" Ade Electricals ", "Electrical", null, "0803", null, null, null), Now);
        Assert.Equal("Ade Electricals", v.Name);
        Assert.True(v.IsActive);
    }
}
