using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyManagement.Api.Contracts;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Organizations;

namespace PropertyManagement.Api.Controllers;

[ApiController]
[Route("api/v1/invitations")]
public sealed class InvitationsController(
    MembershipService memberships,
    OrganizationService organizations,
    ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Accepts an invitation for the signed-in user, whose email must match the invitation's.</summary>
    [HttpPost("accept")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<OrganizationSummaryDto> Accept(AcceptInvitationRequest request, CancellationToken ct)
    {
        var organizationId = await memberships.AcceptAsync(request.Token, ct);
        return (await organizations.ListForUserAsync(currentUser.RequireUserId(), ct)).Single(o => o.Id == organizationId);
    }
}
