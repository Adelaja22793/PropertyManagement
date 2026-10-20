using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Organizations;
using PropertyManagement.Domain.Owners;
using PropertyManagement.Domain.Tenants;

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
        var staffRoles = Roles.StaffRoles.ToArray();
        var memberships = await db.Memberships
            .Include(m => m.Roles).Include(m => m.BranchScopes)
            .Where(m => includeRevoked || m.Status == MembershipStatus.Active)
            // Owners and tenants with portal access only are not staff, so they are not listed as team members.
            .Where(m => m.Roles.Any(r => staffRoles.Contains(r.RoleName)))
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

        return await CreateInvitationAsync(organizationId, emailAddress, request.Role, branchIds, null, null, ct);
    }

    /// <summary>
    /// Invites an owner or tenant to the portal. Accepting links their login to the record, which is all the
    /// portal lets them see (design 4.6).
    /// </summary>
    public async Task<CreatedInvitationDto> InvitePortalUserAsync(Guid? ownerId, Guid? tenantId, CancellationToken ct)
    {
        var organizationId = currentOrganization.RequireOrganizationId();
        string? address;
        if (ownerId is { } oid)
        {
            address = await db.Owners.Where(o => o.Id == oid).Select(o => o.Email).SingleOrDefaultAsync(ct)
                      ?? (await db.Owners.AnyAsync(o => o.Id == oid, ct) ? null : throw new NotFoundException("Owner"));
            if (await db.OwnerUserLinks.AnyAsync(l => l.OwnerId == oid, ct))
                throw new DomainException("This owner already has portal access.", "portal_exists");
        }
        else if (tenantId is { } tid)
        {
            address = await db.Tenants.Where(t => t.Id == tid).Select(t => t.Email).SingleOrDefaultAsync(ct)
                      ?? (await db.Tenants.AnyAsync(t => t.Id == tid, ct) ? null : throw new NotFoundException("Tenant"));
            if (await db.TenantUserLinks.AnyAsync(l => l.TenantId == tid, ct))
                throw new DomainException("This tenant already has portal access.", "portal_exists");
        }
        else throw new ArgumentException("An owner or tenant is required.");

        if (address is null) throw new DomainException("Add an email address before inviting them to the portal.", "email_required");
        var role = ownerId is not null ? Roles.Owner : Roles.Tenant;
        return await CreateInvitationAsync(organizationId, address, role, [], ownerId, tenantId, ct);
    }

    private async Task<CreatedInvitationDto> CreateInvitationAsync(Guid organizationId, string emailAddress, string role,
        IReadOnlyList<Guid> branchIds, Guid? ownerId, Guid? tenantId, CancellationToken ct)
    {
        if (await db.Invitations.AnyAsync(i => i.Email == emailAddress && i.Status == InvitationStatus.Pending
                                               && i.ExpiresAtUtc > clock.UtcNow, ct))
            throw new DomainException("An invitation is already pending for this email.", "invitation_pending");

        var token = TokenHasher.NewToken();
        var invitation = new UserInvitation(organizationId, emailAddress, role, branchIds,
            TokenHasher.Hash(token), currentUser.RequireUserId(), InvitationLifetime, ownerId, tenantId);
        db.Invitations.Add(invitation);
        audit.Record(invitation.IsPortalInvitation ? "portal.invited" : "member.invited", nameof(UserInvitation),
            invitation.Id.ToString(), details: new { invitation.Email, invitation.RoleName, ownerId, tenantId });
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

        var existing = await db.Memberships.IgnoreQueryFilters().Include(m => m.Roles)
            .SingleOrDefaultAsync(m => m.OrganizationId == invitation.OrganizationId && m.UserId == userId
                                       && m.Status == MembershipStatus.Active, ct);
        if (existing is not null && !invitation.IsPortalInvitation)
            throw new DomainException("You are already a member of this organization.", "already_member");

        OrganizationMembership membership;
        if (existing is not null)
        {
            // A staff member who is also a landlord or tenant keeps one membership and gains the portal role.
            membership = existing;
            currentOrganization.Set(invitation.OrganizationId, membership.Id, [invitation.RoleName], []);
            membership.SetRoles(membership.Roles.Select(r => r.RoleName).Append(invitation.RoleName).ToList());
        }
        else
        {
            membership = new OrganizationMembership(invitation.OrganizationId, userId, [invitation.RoleName]);
            currentOrganization.Set(invitation.OrganizationId, membership.Id, [invitation.RoleName], invitation.GetBranchIds());
            membership.SetBranchScope(invitation.GetBranchIds());
            db.Memberships.Add(membership);
        }

        if (invitation.OwnerId is { } ownerId)
            db.OwnerUserLinks.Add(new OwnerUserLink(invitation.OrganizationId, ownerId, userId));
        if (invitation.TenantId is { } tenantId)
            db.TenantUserLinks.Add(new TenantUserLink(invitation.OrganizationId, tenantId, userId));
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

        // Portal roles come from owner and tenant links, so a staff role edit keeps them.
        var portalRoles = membership.Roles.Select(r => r.RoleName).Where(r => !Roles.StaffRoles.Contains(r));
        membership.SetRoles(request.Roles.Concat(portalRoles).ToList());
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
