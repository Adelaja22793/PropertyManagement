using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Application.Common;
using PropertyManagement.Application.Platform;
using PropertyManagement.Domain.Common;
using PropertyManagement.Infrastructure.Identity;

namespace PropertyManagement.Api.Controllers;

/// <summary>
/// Platform administration of companies' subscriptions: trials, paid-up dates, plans, suspension and cancellation.
/// Only platform staff with two-step sign-in on may use it. It shows no company records.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/platform/organizations")]
public sealed class PlatformOrganizationsController(PlatformOrganizationService organizations, UserManager<AppUser> users) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<PlatformOrganizationDto>> List([FromQuery] string? search, CancellationToken ct)
    {
        await RequirePlatformStaffAsync();
        return await organizations.ListAsync(search, ct);
    }

    [HttpGet("{id:guid}")]
    public async Task<PlatformOrganizationDto> Get(Guid id, CancellationToken ct)
    {
        await RequirePlatformStaffAsync();
        return await organizations.GetAsync(id, ct);
    }

    [HttpPost("{id:guid}/extend-trial")]
    public async Task<PlatformOrganizationDto> ExtendTrial(Guid id, ExtendTrialRequest request, CancellationToken ct)
    {
        await RequirePlatformStaffAsync();
        return await organizations.ExtendTrialAsync(id, request, ct);
    }

    /// <summary>Records the date the company has paid up to, after payment was confirmed outside the app.</summary>
    [HttpPost("{id:guid}/activate")]
    public async Task<PlatformOrganizationDto> Activate(Guid id, ActivateSubscriptionRequest request, CancellationToken ct)
    {
        await RequirePlatformStaffAsync();
        return await organizations.ActivateAsync(id, request, ct);
    }

    [HttpPost("{id:guid}/plan")]
    public async Task<PlatformOrganizationDto> ChangePlan(Guid id, ChangePlanRequest request, CancellationToken ct)
    {
        await RequirePlatformStaffAsync();
        return await organizations.ChangePlanAsync(id, request, ct);
    }

    [HttpPost("{id:guid}/suspend")]
    public async Task<PlatformOrganizationDto> Suspend(Guid id, SubscriptionReasonRequest request, CancellationToken ct)
    {
        await RequirePlatformStaffAsync();
        return await organizations.SuspendAsync(id, request, ct);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<PlatformOrganizationDto> Cancel(Guid id, SubscriptionReasonRequest request, CancellationToken ct)
    {
        await RequirePlatformStaffAsync();
        return await organizations.CancelAsync(id, request, ct);
    }

    private async Task RequirePlatformStaffAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null || !user.IsPlatformAdmin) throw new ForbiddenException("Only platform staff can do this.");
        if (!user.TwoFactorEnabled)
            throw new DomainException("Turn on two-step sign-in before managing companies.", "two_factor_required");
    }
}
