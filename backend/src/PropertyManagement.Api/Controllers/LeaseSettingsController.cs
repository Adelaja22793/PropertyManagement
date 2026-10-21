using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Api.Authorization;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Leases;

namespace PropertyManagement.Api.Controllers;

/// <summary>Lease reminder days and the organization's own notice periods (design 8.9).</summary>
[ApiController]
[Route("api/v1/lease-settings")]
public sealed class LeaseSettingsController(LeaseSettingsService settings) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Leases.View)]
    public Task<LeaseSettingsDto> Get(CancellationToken ct) => settings.GetAsync(ct);

    [HttpPut]
    [RequirePermission(Permissions.Organization.ManageSettings)]
    public Task<LeaseSettingsDto> Update(UpdateLeaseSettingsRequest request, CancellationToken ct) => settings.UpdateAsync(request, ct);

    [HttpPost("notice-periods")]
    [RequirePermission(Permissions.Organization.ManageSettings)]
    public async Task<ActionResult<NoticePeriodRuleDto>> CreateRule(SaveNoticePeriodRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await settings.CreateRuleAsync(request, ct));

    [HttpPut("notice-periods/{id:guid}")]
    [RequirePermission(Permissions.Organization.ManageSettings)]
    public Task<NoticePeriodRuleDto> UpdateRule(Guid id, SaveNoticePeriodRequest request, CancellationToken ct) =>
        settings.UpdateRuleAsync(id, request, ct);

    [HttpDelete("notice-periods/{id:guid}")]
    [RequirePermission(Permissions.Organization.ManageSettings)]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken ct)
    {
        await settings.DeleteRuleAsync(id, ct);
        return NoContent();
    }
}
