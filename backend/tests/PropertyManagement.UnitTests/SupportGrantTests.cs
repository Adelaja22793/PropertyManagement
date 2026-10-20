using PropertyManagement.Application.Authorization;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Platform;

namespace PropertyManagement.UnitTests;

public class SupportGrantTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    private static SupportGrant Grant(int hours = 4) => new(Guid.NewGuid(), Guid.NewGuid(), "Ticket 12", hours, Now);

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public void Access_lasts_from_one_hour_to_a_day(int hours) =>
        Assert.Equal("invalid_hours", Assert.Throws<DomainException>(() => Grant(hours)).Code);

    [Fact]
    public void A_reason_is_needed() =>
        Assert.Throws<DomainException>(() => new SupportGrant(Guid.NewGuid(), Guid.NewGuid(), " ", 1, Now));

    [Fact]
    public void Access_starts_on_approval_and_ends_after_the_hours_asked_for()
    {
        var grant = Grant();
        Assert.False(grant.IsActive(Now));
        grant.Approve(Guid.NewGuid(), Now.AddHours(1));
        Assert.Equal(Now.AddHours(5), grant.ExpiresAtUtc);
        Assert.True(grant.IsActive(Now.AddHours(4.9)));
        Assert.False(grant.IsActive(Now.AddHours(5)));
        Assert.Equal("already_decided", Assert.Throws<DomainException>(() => grant.Decline(Guid.NewGuid(), Now)).Code);
    }

    [Fact]
    public void Revoking_ends_access_at_once_and_only_once()
    {
        var grant = Grant();
        grant.Approve(Guid.NewGuid(), Now);
        grant.Revoke(Guid.NewGuid(), Now.AddMinutes(10));
        Assert.Equal(SupportGrantStatus.Revoked, grant.Status);
        Assert.False(grant.IsActive(Now.AddMinutes(11)));
        Assert.Throws<DomainException>(() => grant.Revoke(Guid.NewGuid(), Now.AddMinutes(12)));
    }

    [Fact]
    public void Support_reads_only_and_never_sees_identity_numbers()
    {
        var permissions = Roles.PermissionsFor([Roles.PlatformSupport]);
        Assert.All(permissions, p => Assert.EndsWith(".View", p));
        Assert.DoesNotContain(Permissions.Tenants.ViewKyc, permissions);
        Assert.DoesNotContain(Roles.PlatformSupport, Roles.StaffRoles);
    }
}
