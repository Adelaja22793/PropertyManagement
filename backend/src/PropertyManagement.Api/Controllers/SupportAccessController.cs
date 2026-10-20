using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Api.Authorization;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Application.Platform;
using PropertyManagement.Domain.Common;
using PropertyManagement.Infrastructure.Identity;

namespace PropertyManagement.Api.Controllers;

/// <summary>
/// Platform support's side of support access (design 4.7): ask an organization for read-only access, see your
/// requests, and end access early. Only platform staff with two-step sign-in on may use it.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/platform/support-grants")]
public sealed class PlatformSupportController(SupportAccessService support, UserManager<AppUser> users) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<SupportGrantDto>> Mine(CancellationToken ct)
    {
        await RequirePlatformSupportAsync();
        return await support.ListMineAsync(ct);
    }

    [HttpPost]
    public async Task<ActionResult<SupportGrantDto>> Create(RequestSupportAccessRequest request, CancellationToken ct)
    {
        await RequirePlatformSupportAsync();
        var grant = await support.RequestAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, grant);
    }

    [HttpPost("{id:guid}/end")]
    public async Task<SupportGrantDto> End(Guid id, CancellationToken ct)
    {
        await RequirePlatformSupportAsync();
        return await support.EndOwnAsync(id, ct);
    }

    private async Task RequirePlatformSupportAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null || !user.IsPlatformAdmin) throw new ForbiddenException("Only platform support can do this.");
        if (!user.TwoFactorEnabled)
            throw new DomainException("Turn on two-step sign-in before asking for support access.", "two_factor_required");
    }
}

/// <summary>An organization admin's side of support access: approve, decline or end it.</summary>
[ApiController]
[Route("api/v1/support-grants")]
public sealed class SupportGrantsController(SupportAccessService support) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Users.Manage)]
    public Task<IReadOnlyList<SupportGrantDto>> List(CancellationToken ct) => support.ListForOrganizationAsync(ct);

    [HttpPost("{id:guid}/approve")]
    [RequirePermission(Permissions.Users.Manage)]
    public Task<SupportGrantDto> Approve(Guid id, CancellationToken ct) => support.ApproveAsync(id, ct);

    [HttpPost("{id:guid}/decline")]
    [RequirePermission(Permissions.Users.Manage)]
    public Task<SupportGrantDto> Decline(Guid id, CancellationToken ct) => support.DeclineAsync(id, ct);

    [HttpPost("{id:guid}/revoke")]
    [RequirePermission(Permissions.Users.Manage)]
    public Task<SupportGrantDto> Revoke(Guid id, CancellationToken ct) => support.RevokeAsync(id, ct);
}
