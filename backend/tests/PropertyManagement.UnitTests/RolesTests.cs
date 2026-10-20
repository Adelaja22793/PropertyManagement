using PropertyManagement.Application.Authorization;

namespace PropertyManagement.UnitTests;

public sealed class RolesTests
{
    [Fact]
    public void Organization_admin_has_every_permission() =>
        Assert.True(Permissions.All.SetEquals(Roles.PermissionsFor([Roles.OrganizationAdmin])));

    [Theory]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Tenant)]
    public void Portal_roles_have_no_staff_permissions(string role) =>
        Assert.Empty(Roles.PermissionsFor([role]));

    [Fact]
    public void Only_admins_can_manage_users_by_default()
    {
        foreach (var role in Roles.StaffRoles.Where(r => r != Roles.OrganizationAdmin))
            Assert.DoesNotContain(Permissions.Users.Manage, Roles.PermissionsFor([role]));
    }

    [Fact]
    public void Identity_numbers_are_limited_to_admins()
    {
        foreach (var role in Roles.StaffRoles.Where(r => r != Roles.OrganizationAdmin))
            Assert.DoesNotContain(Permissions.Tenants.ViewKyc, Roles.PermissionsFor([role]));
    }

    [Fact]
    public void Payouts_are_created_and_approved_by_different_roles()
    {
        // Maker-checker (design 5): no default role other than admin can both create and approve a payout.
        foreach (var role in Roles.StaffRoles.Where(r => r != Roles.OrganizationAdmin))
        {
            var permissions = Roles.PermissionsFor([role]);
            Assert.False(permissions.Contains(Permissions.Finance.PayoutsCreate) &&
                         permissions.Contains(Permissions.Finance.PayoutsApprove), role);
        }
    }

    [Fact]
    public void Unknown_roles_grant_nothing() => Assert.Empty(Roles.PermissionsFor(["SuperUser"]));

    [Fact]
    public void Permissions_of_several_roles_are_combined()
    {
        var combined = Roles.PermissionsFor([Roles.Accountant, Roles.Agent]);
        Assert.Contains(Permissions.Finance.PaymentsRecord, combined);
        Assert.Contains(Permissions.Listings.Manage, combined);
    }
}
