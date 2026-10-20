using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Api.Authorization;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Application.Maintenance;
using PropertyManagement.Domain.Maintenance;

namespace PropertyManagement.Api.Controllers;

/// <summary>
/// Maintenance requests (design 6.11 and 9.5). Endpoints open to maintenance staff (Maintenance.UpdateAssigned)
/// only ever reach the jobs assigned to them; the service checks that on every call.
/// </summary>
[ApiController]
[Route("api/v1/maintenance")]
public sealed class MaintenanceController(MaintenanceService maintenance) : ControllerBase
{
    private const string View = Permissions.Maintenance.View;
    private const string Manage = Permissions.Maintenance.Manage;
    private const string Assigned = Permissions.Maintenance.UpdateAssigned;

    [HttpGet]
    [RequireAnyPermission(View, Assigned)]
    public Task<PagedResult<MaintenanceSummaryDto>> Search([FromQuery] string? search, [FromQuery] MaintenanceStatus? status,
        [FromQuery] bool? open, [FromQuery] MaintenanceUrgency? urgency, [FromQuery] Guid? propertyId, [FromQuery] Guid? unitId,
        [FromQuery] Guid? vendorId, [FromQuery] bool mine = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken ct = default) =>
        maintenance.SearchAsync(new MaintenanceSearch(search, status, open, urgency, propertyId, unitId, vendorId, mine),
            new PageRequest(page, pageSize), ct);

    [HttpGet("{id:guid}")]
    [RequireAnyPermission(View, Assigned)]
    public Task<MaintenanceDetailDto> Get(Guid id, CancellationToken ct) => maintenance.GetAsync(id, ct);

    [HttpPost]
    [RequirePermission(Manage)]
    public async Task<ActionResult<MaintenanceDetailDto>> Create(SaveMaintenanceRequest request, CancellationToken ct)
    {
        var created = await maintenance.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Manage)]
    public Task<MaintenanceDetailDto> Update(Guid id, SaveMaintenanceRequest request, CancellationToken ct) =>
        maintenance.UpdateAsync(id, request, ct);

    /// <summary>Members who can be given maintenance jobs.</summary>
    [HttpGet("assignees")]
    [RequirePermission(Manage)]
    public Task<IReadOnlyList<AssigneeDto>> Assignees(CancellationToken ct) => maintenance.AssigneesAsync(ct);

    /// <summary>The repair approval limit that applies to a property today.</summary>
    [HttpGet("approval-limit")]
    [RequirePermission(Manage)]
    public Task<ApprovalLimitDto> ApprovalLimit([FromQuery] Guid propertyId, CancellationToken ct) =>
        maintenance.ApprovalLimitAsync(propertyId, ct);

    [HttpPost("{id:guid}/comments")]
    [RequireAnyPermission(Manage, Assigned)]
    public Task<MaintenanceDetailDto> Comment(Guid id, CommentRequest request, CancellationToken ct) =>
        maintenance.CommentAsync(id, request, ct);

    [HttpPost("{id:guid}/review")]
    [RequirePermission(Manage)]
    public Task<MaintenanceDetailDto> Review(Guid id, CancellationToken ct) => maintenance.ReviewAsync(id, ct);

    [HttpPost("{id:guid}/estimate")]
    [RequirePermission(Manage)]
    public Task<MaintenanceDetailDto> Estimate(Guid id, EstimateRequest request, CancellationToken ct) =>
        maintenance.EstimateAsync(id, request, ct);

    [HttpPost("{id:guid}/owner-approval")]
    [RequirePermission(Manage)]
    public Task<MaintenanceDetailDto> OwnerApproval(Guid id, OwnerApprovalRequest request, CancellationToken ct) =>
        maintenance.RecordOwnerApprovalAsync(id, request, ct);

    [HttpPost("{id:guid}/approve")]
    [RequirePermission(Manage)]
    public Task<MaintenanceDetailDto> Approve(Guid id, CancellationToken ct) => maintenance.ApproveAsync(id, ct);

    [HttpPost("{id:guid}/assign")]
    [RequirePermission(Manage)]
    public Task<MaintenanceDetailDto> Assign(Guid id, AssignRequest request, CancellationToken ct) =>
        maintenance.AssignAsync(id, request, ct);

    [HttpPost("{id:guid}/start")]
    [RequireAnyPermission(Manage, Assigned)]
    public Task<MaintenanceDetailDto> Start(Guid id, CancellationToken ct) => maintenance.StartAsync(id, ct);

    [HttpPost("{id:guid}/awaiting-parts")]
    [RequireAnyPermission(Manage, Assigned)]
    public Task<MaintenanceDetailDto> AwaitParts(Guid id, NoteRequest request, CancellationToken ct) =>
        maintenance.AwaitPartsAsync(id, request, ct);

    [HttpPost("{id:guid}/complete")]
    [RequireAnyPermission(Manage, Assigned)]
    public Task<MaintenanceDetailDto> Complete(Guid id, CompleteRequest request, CancellationToken ct) =>
        maintenance.CompleteAsync(id, request, ct);

    [HttpPost("{id:guid}/reject")]
    [RequirePermission(Manage)]
    public Task<MaintenanceDetailDto> Reject(Guid id, ReasonRequest request, CancellationToken ct) => maintenance.RejectAsync(id, request, ct);

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(Manage)]
    public Task<MaintenanceDetailDto> Cancel(Guid id, ReasonRequest request, CancellationToken ct) => maintenance.CancelAsync(id, request, ct);

    /// <summary>Records the tenant's rating of a completed job.</summary>
    [HttpPost("{id:guid}/feedback")]
    [RequirePermission(Manage)]
    public Task<MaintenanceDetailDto> Feedback(Guid id, FeedbackRequest request, CancellationToken ct) =>
        maintenance.FeedbackAsync(id, request, ct);
}

/// <summary>Suppliers and contractors (design 6.10).</summary>
[ApiController]
[Route("api/v1/vendors")]
public sealed class VendorsController(VendorService vendors) : ControllerBase
{
    [HttpGet]
    [RequireAnyPermission(Permissions.Maintenance.View, Permissions.Maintenance.Manage)]
    public Task<PagedResult<VendorDto>> Search([FromQuery] string? search, [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        vendors.SearchAsync(search, includeInactive, new PageRequest(page, pageSize), ct);

    [HttpGet("{id:guid}")]
    [RequireAnyPermission(Permissions.Maintenance.View, Permissions.Maintenance.Manage)]
    public Task<VendorDto> Get(Guid id, CancellationToken ct) => vendors.GetAsync(id, ct);

    [HttpPost]
    [RequirePermission(Permissions.Maintenance.Manage)]
    public async Task<ActionResult<VendorDto>> Create(SaveVendorRequest request, CancellationToken ct)
    {
        var created = await vendors.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Maintenance.Manage)]
    public Task<VendorDto> Update(Guid id, SaveVendorRequest request, CancellationToken ct) => vendors.UpdateAsync(id, request, ct);

    [HttpPut("{id:guid}/active")]
    [RequirePermission(Permissions.Maintenance.Manage)]
    public Task<VendorDto> SetActive(Guid id, SetVendorActiveRequest request, CancellationToken ct) =>
        vendors.SetActiveAsync(id, request.IsActive, ct);
}
