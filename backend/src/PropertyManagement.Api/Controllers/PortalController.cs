using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Api.Authorization;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Portal;

namespace PropertyManagement.Api.Controllers;

/// <summary>The owner portal (design 10.3). Reaches only the properties the signed-in owner holds a share of.</summary>
[ApiController]
[Route("api/v1/portal/owner")]
[RequirePortalRole(Roles.Owner)]
public sealed class OwnerPortalController(PortalService portal) : ControllerBase
{
    [HttpGet]
    public Task<OwnerPortalDto> Home(CancellationToken ct) => portal.OwnerHomeAsync(ct);

    [HttpGet("maintenance")]
    public Task<IReadOnlyList<PortalMaintenanceSummaryDto>> Maintenance(CancellationToken ct) => portal.OwnerMaintenanceAsync(ct);

    [HttpGet("maintenance/{id:guid}")]
    public Task<PortalMaintenanceDetailDto> MaintenanceDetail(Guid id, CancellationToken ct) => portal.OwnerMaintenanceDetailAsync(id, ct);

    /// <summary>Approves an estimate above the owner's repair approval limit.</summary>
    [HttpPost("maintenance/{id:guid}/approve")]
    public Task<PortalMaintenanceDetailDto> Approve(Guid id, PortalApprovalRequest request, CancellationToken ct) =>
        portal.OwnerApproveAsync(id, request, ct);

    [HttpPost("maintenance/{id:guid}/decline")]
    public Task<PortalMaintenanceDetailDto> Decline(Guid id, PortalDeclineRequest request, CancellationToken ct) =>
        portal.OwnerDeclineAsync(id, request, ct);
}

/// <summary>The tenant portal (design 10.4). Reaches only the leases the signed-in tenant is party to.</summary>
[ApiController]
[Route("api/v1/portal/tenant")]
[RequirePortalRole(Roles.Tenant)]
public sealed class TenantPortalController(PortalService portal) : ControllerBase
{
    [HttpGet]
    public Task<TenantPortalDto> Home(CancellationToken ct) => portal.TenantHomeAsync(ct);

    [HttpGet("maintenance")]
    public Task<IReadOnlyList<PortalMaintenanceSummaryDto>> Maintenance(CancellationToken ct) => portal.TenantMaintenanceAsync(ct);

    [HttpGet("maintenance/{id:guid}")]
    public Task<PortalMaintenanceDetailDto> MaintenanceDetail(Guid id, CancellationToken ct) => portal.TenantMaintenanceDetailAsync(id, ct);

    [HttpPost("maintenance")]
    public async Task<ActionResult<PortalMaintenanceDetailDto>> Report(PortalRepairRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await portal.TenantReportAsync(request, ct));

    [HttpPost("maintenance/{id:guid}/comments")]
    public Task<PortalMaintenanceDetailDto> Comment(Guid id, PortalCommentRequest request, CancellationToken ct) =>
        portal.TenantCommentAsync(id, request, ct);
}
