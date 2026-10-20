using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Organizations;

namespace PropertyManagement.UnitTests;

public sealed class DomainTests
{
    [Theory]
    [InlineData("Lekki Homes Ltd", "lekki-homes-ltd")]
    [InlineData("  ABC  Property & Co. ", "abc-property-co")]
    [InlineData("!!!", "org")]
    public void Organization_slug_is_url_safe(string name, string expected) =>
        Assert.Equal(expected, Organization.CreateSlug(name));

    [Fact]
    public void Membership_needs_at_least_one_role() =>
        Assert.Throws<DomainException>(() => new OrganizationMembership(Guid.NewGuid(), Guid.NewGuid(), []));

    [Fact]
    public void Membership_roles_are_replaced_not_appended()
    {
        var membership = new OrganizationMembership(Guid.NewGuid(), Guid.NewGuid(), ["Accountant", "Agent"]);
        membership.SetRoles(["Agent", "PropertyManager"]);
        Assert.Equal(["Agent", "PropertyManager"], membership.Roles.Select(r => r.RoleName).Order());
        Assert.All(membership.Roles, r => Assert.Equal(membership.OrganizationId, r.OrganizationId));
    }

    [Fact]
    public void Revoking_records_when_it_happened()
    {
        var membership = new OrganizationMembership(Guid.NewGuid(), Guid.NewGuid(), ["Agent"]);
        membership.Revoke();
        Assert.Equal(MembershipStatus.Revoked, membership.Status);
        Assert.NotNull(membership.RevokedAtUtc);
    }

    [Fact]
    public void Invitation_rejects_a_different_email()
    {
        var invitation = NewInvitation("ada@example.com");
        var error = Assert.Throws<DomainException>(() => invitation.Accept(Guid.NewGuid(), "bola@example.com", DateTime.UtcNow));
        Assert.Equal("invitation_email_mismatch", error.Code);
    }

    [Fact]
    public void Invitation_email_match_ignores_case()
    {
        var invitation = NewInvitation("Ada@Example.com");
        invitation.Accept(Guid.NewGuid(), "ADA@example.COM", DateTime.UtcNow);
        Assert.Equal(InvitationStatus.Accepted, invitation.Status);
    }

    [Fact]
    public void Invitation_expires()
    {
        var invitation = NewInvitation("ada@example.com");
        Assert.Throws<DomainException>(() => invitation.Accept(Guid.NewGuid(), "ada@example.com", DateTime.UtcNow.AddDays(8)));
    }

    [Fact]
    public void Invitation_cannot_be_used_twice()
    {
        var invitation = NewInvitation("ada@example.com");
        invitation.Accept(Guid.NewGuid(), "ada@example.com", DateTime.UtcNow);
        Assert.Throws<DomainException>(() => invitation.Accept(Guid.NewGuid(), "ada@example.com", DateTime.UtcNow));
    }

    [Fact]
    public void Tokens_are_random_and_only_their_hash_is_stored()
    {
        var a = TokenHasher.NewToken();
        var b = TokenHasher.NewToken();
        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 40);
        Assert.Equal(TokenHasher.Hash(a), TokenHasher.Hash(a));
        Assert.DoesNotContain(a, TokenHasher.Hash(a));
    }

    private static UserInvitation NewInvitation(string email) =>
        new(Guid.NewGuid(), email, "Accountant", [], TokenHasher.Hash(TokenHasher.NewToken()), Guid.NewGuid(), TimeSpan.FromDays(7));
}
