using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Api.Authorization;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Platform;

namespace PropertyManagement.Api.Controllers;

/// <summary>The selected organization's plan, lifecycle and plan usage (design 4.8).</summary>
[ApiController]
[Route("api/v1/subscription")]
public sealed class SubscriptionController(SubscriptionService subscriptions) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Organization.View)]
    public Task<SubscriptionDto> Get(CancellationToken ct) => subscriptions.GetAsync(ct);
}
