using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Api.Authorization;
using PropertyManagement.Api.Contracts;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Organizations;

namespace PropertyManagement.Api.Controllers;

[ApiController]
[Route("api/v1/organizations")]
public sealed class OrganizationsController(
    OrganizationService organizations,
    MembershipService memberships,
    ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Organizations the signed-in user is an active member of.</summary>
    [HttpGet]
    [Authorize]
    public async Task<IReadOnlyList<OrganizationSummaryDto>> List(CancellationToken ct) =>
        await organizations.ListForUserAsync(currentUser.RequireUserId(), ct);

    /// <summary>Creates another organization with the caller as its Organization Admin.</summary>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<OrganizationSummaryDto>> Create(CreateOrganizationRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var created = await organizations.CreateAsync(userId, request.Name, request.Type, ct);
        var summary = (await organizations.ListForUserAsync(userId, ct)).Single(o => o.Id == created.Id);
        return CreatedAtAction(nameof(Get), new { organizationId = created.Id }, summary);
    }

    [HttpGet("{organizationId:guid}")]
    [RequirePermission(Permissions.Organization.View)]
    public Task<OrganizationDetailDto> Get(Guid organizationId, CancellationToken ct) => organizations.GetCurrentAsync(ct);

    [HttpPut("{organizationId:guid}")]
    [RequirePermission(Permissions.Organization.ManageSettings)]
    public Task<OrganizationDetailDto> Update(Guid organizationId, UpdateOrganizationRequest request, CancellationToken ct) =>
        organizations.UpdateCurrentAsync(request, ct);

    [HttpGet("{organizationId:guid}/members")]
    [RequirePermission(Permissions.Users.View)]
    public Task<IReadOnlyList<MemberDto>> Members(Guid organizationId, [FromQuery] bool includeRevoked, CancellationToken ct) =>
        memberships.ListAsync(includeRevoked, ct);

    [HttpPatch("{organizationId:guid}/members/{membershipId:guid}")]
    [RequirePermission(Permissions.Users.Manage)]
    public Task<MemberDto> UpdateMember(Guid organizationId, Guid membershipId, UpdateMemberRequest request, CancellationToken ct) =>
        memberships.UpdateAsync(membershipId, request, ct);

    [HttpDelete("{organizationId:guid}/members/{membershipId:guid}")]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<IActionResult> RevokeMember(Guid organizationId, Guid membershipId, CancellationToken ct)
    {
        await memberships.RevokeAsync(membershipId, ct);
        return NoContent();
    }

    [HttpGet("{organizationId:guid}/invitations")]
    [RequirePermission(Permissions.Users.Manage)]
    public Task<IReadOnlyList<InvitationDto>> Invitations(Guid organizationId, CancellationToken ct) =>
        memberships.ListInvitationsAsync(ct);

    [HttpPost("{organizationId:guid}/invitations")]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<ActionResult<CreatedInvitationDto>> Invite(Guid organizationId, CreateInvitationRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await memberships.InviteAsync(request, ct));

    [HttpDelete("{organizationId:guid}/invitations/{invitationId:guid}")]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<IActionResult> CancelInvitation(Guid organizationId, Guid invitationId, CancellationToken ct)
    {
        await memberships.CancelInvitationAsync(invitationId, ct);
        return NoContent();
    }
}
