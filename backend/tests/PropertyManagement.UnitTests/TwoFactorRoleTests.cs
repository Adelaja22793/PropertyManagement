using PropertyManagement.Application.Authorization;
using PropertyManagement.Domain.Organizations;

namespace PropertyManagement.UnitTests;

public class TwoFactorRoleTests
{
    [Fact]
    public void Admins_accountants_and_finance_approvers_need_two_step_sign_in() =>
        Assert.Equal([Roles.Accountant, Roles.FinanceApprover, Roles.OrganizationAdmin], OrganizationMembership.TwoFactorRoles.Order());

    [Fact]
    public void The_grace_period_starts_when_such_a_role_is_first_given_and_ends_when_none_is_held()
    {
        var membership = new OrganizationMembership(Guid.NewGuid(), Guid.NewGuid(), [Roles.Agent]);
        Assert.Null(membership.TwoFactorRequiredSinceUtc);

        membership.SetRoles([Roles.Agent, Roles.Accountant]);
        var since = membership.TwoFactorRequiredSinceUtc;
        Assert.NotNull(since);
        // Adding another such role keeps the original start.
        membership.SetRoles([Roles.Accountant, Roles.OrganizationAdmin]);
        Assert.Equal(since, membership.TwoFactorRequiredSinceUtc);

        membership.SetRoles([Roles.PropertyManager]);
        Assert.Null(membership.TwoFactorRequiredSinceUtc);
    }
}

public class CustomRoleTwoFactorTests
{
    [Fact]
    public void Custom_roles_need_two_step_sign_in_for_exactly_the_powers_only_admin_and_finance_roles_have()
    {
        Assert.Equal(
            ["Audit.View", "Branches.Manage", "Data.Export", "Invoices.Manage", "Owners.ApproveBankAccounts", "Payments.Record",
             "Payouts.Approve", "Payouts.Create", "Settings.Manage", "Tenants.ViewKyc", "Users.Manage"],
            Roles.TwoFactorPermissions.Order(StringComparer.Ordinal));
        Assert.False(Roles.NeedsTwoFactor(["Properties.View", "Leases.Terminate"]));
        Assert.True(Roles.NeedsTwoFactor(["Properties.View", "Tenants.ViewKyc"]));
    }

    [Fact]
    public void A_membership_with_a_custom_role_follows_what_that_role_needs()
    {
        var role = new PropertyManagement.Domain.Organizations.CustomRole(Guid.NewGuid(), "Front desk", null, ["Tenants.View"]);
        var membership = new OrganizationMembership(role.OrganizationId, Guid.NewGuid(), [role.Key], _ => false);
        Assert.Null(membership.TwoFactorRequiredSinceUtc);
        membership.SetRoles([role.Key], key => key == role.Key);
        Assert.NotNull(membership.TwoFactorRequiredSinceUtc);
        Assert.Throws<PropertyManagement.Domain.Common.DomainException>(() => role.Update(" ", null, ["Tenants.View"]));
        Assert.Throws<PropertyManagement.Domain.Common.DomainException>(() => role.Update("Front desk", null, []));
    }
}
