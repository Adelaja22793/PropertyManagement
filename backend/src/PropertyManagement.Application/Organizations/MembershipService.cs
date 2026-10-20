using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Organizations;

namespace PropertyManagement.Application.Organizations;

public sealed class MembershipService(
    IAppDbContext db,
    ICurrentOrganization currentOrganization,
    ICurrentUser currentUser,
    IUserDirectory users,
    IUserLookup userLookup,
    IEmailSender email,
    IAuditLogger audit,
    IClock clock)
{
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    public async Task<IReadOnlyList<MemberDto>> ListAsync(bool includeRevoked, CancellationToken ct)
    {
        var memberships = await db.Memberships
            .Include(m => m.Roles).Include(m => m.BranchScopes)
            .Where(m => includeRevoked || m.Status == MembershipStatus.Active)
            .ToListAsync(ct);
        var people = await users.GetAsync(memberships.Select(m => m.UserId), ct);
        return memberships
            .Select(m => ToDto(m, people.GetValueOrDefault(m.UserId)))
            .OrderBy(m => m.FullName)
            .ToList();
    }

    public async Task<CreatedInvitationDto> InviteAsync(CreateInvitationRequest request, CancellationToken ct)
    {
        var organizationId = currentOrganization.RequireOrganizationId();
        var emailAddress = request.Email.Trim().ToLowerInvariant();
        if (!emailAddress.Contains('@')) throw new DomainException("Enter a valid email address.", "invalid_email");
        RequireStaffRole(request.Role);
        var branchIds = await RequireBranchesAsync(request.BranchIds, ct);

        var existingUser = await userLookup.FindByEmailAsync(emailAddress, ct);
        if (existingUser is not null &&
            await db.Memberships.AnyAsync(m => m.UserId == existingUser.Id && m.Status == MembershipStatus.Active, ct))
            throw new DomainException("This person is already a member.", "already_member");

        if (await db.Invitations.AnyAsync(i => i.Email == emailAddress && i.Status == InvitationStatus.Pending
                                               && i.ExpiresAtUtc > clock.UtcNow, ct))
            throw new DomainException("An invitation is already pending for this email.", "invitation_pending");

        var token = TokenHasher.NewToken();
        var invitation = new UserInvitation(organizationId, emailAddress, request.Role, branchIds,
            TokenHasher.Hash(token), currentUser.RequireUserId(), InvitationLifetime);
        db.Invitations.Add(invitation);
        audit.Record("member.invited", nameof(UserInvitation), invitation.Id.ToString(),
            details: new { invitation.Email, invitation.RoleName });
        await db.SaveChangesAsync(ct);

        var organization = await db.Organizations.SingleAsync(o => o.Id == organizationId, ct);
        await email.SendAsync(emailAddress, $"You're invited to join {organization.Name}",
            $"Use this code to accept the invitation within 7 days: {token}", ct);
        return new CreatedInvitationDto(ToDto(invitation), token);
    }

    public async Task<IReadOnlyList<InvitationDto>> ListInvitationsAsync(CancellationToken ct) =>
        (await db.Invitations.OrderByDescending(i => i.CreatedAtUtc).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task CancelInvitationAsync(Guid invitationId, CancellationToken ct)
    {
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.Id == invitationId, ct)
                         ?? throw new NotFoundException("Invitation");
        invitation.Cancel();
        audit.Record("member.invitation_cancelled", nameof(UserInvitation), invitation.Id.ToString());
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Accepts an invitation for the signed-in user. The invitation is found by its token hash across
    /// organizations, and the email it was sent to must match the user's.
    /// </summary>
    public Task<Guid> AcceptAsync(string token, CancellationToken ct) =>
        AcceptAsync(token, currentUser.RequireUserId(), currentUser.Email ?? "", ct);

    /// <inheritdoc cref="AcceptAsync(string, CancellationToken)"/>
    public async Task<Guid> AcceptAsync(string token, Guid userId, string userEmail, CancellationToken ct)
    {
        var hash = TokenHasher.Hash(token.Trim());
        var invitation = await db.Invitations.IgnoreQueryFilters().SingleOrDefaultAsync(i => i.TokenHash == hash, ct)
                         ?? throw new DomainException("This invitation is no longer valid.", "invitation_invalid");

        invitation.Accept(userId, userEmail, clock.UtcNow);

        var existing = await db.Memberships.IgnoreQueryFilters()
            .SingleOrDefaultAsync(m => m.OrganizationId == invitation.OrganizationId && m.UserId == userId
                                       && m.Status == MembershipStatus.Active, ct);
        if (existing is not null) throw new DomainException("You are already a member of this organization.", "already_member");

        var membership = new OrganizationMembership(invitation.OrganizationId, userId, [invitation.RoleName]);
        currentOrganization.Set(invitation.OrganizationId, membership.Id, [invitation.RoleName], invitation.GetBranchIds());
        membership.SetBranchScope(invitation.GetBranchIds());
        db.Memberships.Add(membership);
        audit.Record("member.joined", nameof(OrganizationMembership), membership.Id.ToString(),
            details: new { invitation.RoleName, InvitationId = invitation.Id }, userId: userId);
        await db.SaveChangesAsync(ct);
        return invitation.OrganizationId;
    }

    public async Task<MemberDto> UpdateAsync(Guid membershipId, UpdateMemberRequest request, CancellationToken ct)
    {
        foreach (var role in request.Roles) RequireStaffRole(role);
        var membership = await LoadActiveAsync(membershipId, ct);
        var before = membership.Roles.Select(r => r.RoleName).Order().ToList();

        if (before.Contains(Roles.OrganizationAdmin) && !request.Roles.Contains(Roles.OrganizationAdmin))
            await EnsureAnotherAdminAsync(membership.Id, ct);

        membership.SetRoles(request.Roles);
        if (request.BranchIds is not null) membership.SetBranchScope(await RequireBranchesAsync(request.BranchIds, ct));

        audit.Record("member.updated", nameof(OrganizationMembership), membership.Id.ToString(),
            details: new { before, after = request.Roles, request.BranchIds });
        await db.SaveChangesAsync(ct);
        var people = await users.GetAsync([membership.UserId], ct);
        return ToDto(membership, people.GetValueOrDefault(membership.UserId));
    }

    /// <summary>Revokes a membership. Access ends on the member's next request.</summary>
    public async Task RevokeAsync(Guid membershipId, CancellationToken ct)
    {
        var membership = await LoadActiveAsync(membershipId, ct);
        if (membership.Roles.Any(r => r.RoleName == Roles.OrganizationAdmin))
            await EnsureAnotherAdminAsync(membership.Id, ct);
        membership.Revoke();
        audit.Record("member.revoked", nameof(OrganizationMembership), membership.Id.ToString(),
            details: new { membership.UserId });
        await db.SaveChangesAsync(ct);
    }

    private async Task<OrganizationMembership> LoadActiveAsync(Guid membershipId, CancellationToken ct) =>
        await db.Memberships.Include(m => m.Roles).Include(m => m.BranchScopes)
            .SingleOrDefaultAsync(m => m.Id == membershipId && m.Status == MembershipStatus.Active, ct)
        ?? throw new NotFoundException("Member");

    private async Task EnsureAnotherAdminAsync(Guid exceptMembershipId, CancellationToken ct)
    {
        var otherAdmins = await db.Memberships.CountAsync(m => m.Id != exceptMembershipId
            && m.Status == MembershipStatus.Active
            && m.Roles.Any(r => r.RoleName == Roles.OrganizationAdmin), ct);
        if (otherAdmins == 0)
            throw new DomainException("An organization must keep at least one Organization Admin.", "last_admin");
    }

    private async Task<IReadOnlyList<Guid>> RequireBranchesAsync(IReadOnlyList<Guid>? branchIds, CancellationToken ct)
    {
        var ids = (branchIds ?? []).Distinct().ToList();
        if (ids.Count == 0) return ids;
        // The query filter limits this to the active organization, so another organization's branch counts as missing.
        var found = await db.Branches.CountAsync(b => ids.Contains(b.Id), ct);
        if (found != ids.Count) throw new DomainException("One or more branches were not found.", "branch_not_found");
        return ids;
    }

    private static void RequireStaffRole(string role)
    {
        if (!Roles.StaffRoles.Contains(role))
            throw new DomainException($"'{role}' is not a staff role that can be assigned here.", "invalid_role");
    }

    private static MemberDto ToDto(OrganizationMembership m, UserSummary? user) => new(
        m.Id, m.UserId, user?.Email ?? "", user?.FullName ?? "", m.Status,
        m.Roles.Select(r => r.RoleName).Order().ToList(), m.BranchScopes.Select(s => s.BranchId).ToList(), m.CreatedAtUtc);

    private static InvitationDto ToDto(UserInvitation i) =>
        new(i.Id, i.Email, i.RoleName, i.GetBranchIds().ToList(), i.Status, i.CreatedAtUtc, i.ExpiresAtUtc);
}
