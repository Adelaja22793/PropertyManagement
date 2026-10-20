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
