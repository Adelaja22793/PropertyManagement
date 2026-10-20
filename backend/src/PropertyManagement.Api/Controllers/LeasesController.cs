using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Api.Authorization;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Application.Leases;
using PropertyManagement.Domain.Leases;

namespace PropertyManagement.Api.Controllers;

/// <summary>Leases: drafts, activation, renewal and move-out (design 6.5 and 9.3). No money is posted yet.</summary>
[ApiController]
[Route("api/v1/leases")]
public sealed class LeasesController(LeaseService leases, LeaseRecordsService records) : ControllerBase
{
    /// <param name="stage">Draft, Upcoming, Active, Expiring, HoldingOver, Renewed, Ended, Terminated or Cancelled.</param>
    /// <param name="endingWithinDays">Active leases whose end date is within this many days, including any already past it.</param>
    [HttpGet]
    [RequirePermission(Permissions.Leases.View)]
    public Task<PagedResult<LeaseSummaryDto>> Search([FromQuery] string? search, [FromQuery] LeaseStage? stage,
        [FromQuery] Guid? propertyId, [FromQuery] Guid? unitId, [FromQuery] Guid? tenantId, [FromQuery] int? endingWithinDays,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        leases.SearchAsync(new LeaseSearch(search, stage, propertyId, unitId, tenantId, endingWithinDays),
            new PageRequest(page, pageSize), ct);

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.Leases.View)]
    public Task<LeaseDetailDto> Get(Guid id, CancellationToken ct) => leases.GetAsync(id, ct);

    /// <summary>Creates a draft lease. It doesn't affect the unit until it is activated.</summary>
    [HttpPost]
    [RequirePermission(Permissions.Leases.Create)]
    public async Task<ActionResult<LeaseDetailDto>> Create(SaveLeaseRequest request, CancellationToken ct)
    {
        var created = await leases.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Leases.Create)]
    public Task<LeaseDetailDto> Update(Guid id, SaveLeaseRequest request, CancellationToken ct) => leases.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/activate")]
    [RequirePermission(Permissions.Leases.Create)]
    public Task<LeaseDetailDto> Activate(Guid id, CancellationToken ct) => leases.ActivateAsync(id, ct);

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(Permissions.Leases.Create)]
    public Task<LeaseDetailDto> Cancel(Guid id, CancelLeaseRequest request, CancellationToken ct) => leases.CancelAsync(id, request, ct);

    [HttpPost("{id:guid}/renew")]
    [RequirePermission(Permissions.Leases.Renew)]
    public async Task<ActionResult<LeaseDetailDto>> Renew(Guid id, RenewLeaseRequest request, CancellationToken ct)
    {
        var renewal = await leases.RenewAsync(id, request, ct);
        return CreatedAtAction(nameof(Get), new { id = renewal.Id }, renewal);
    }

    /// <summary>Records the move-out. Before the end date this terminates the lease early and needs a reason.</summary>
    [HttpPost("{id:guid}/end")]
    [RequirePermission(Permissions.Leases.Terminate)]
    public Task<LeaseDetailDto> End(Guid id, EndLeaseRequest request, CancellationToken ct) => leases.EndAsync(id, request, ct);

    // Notices

    [HttpGet("{id:guid}/notices")]
    [RequirePermission(Permissions.Leases.View)]
    public Task<IReadOnlyList<LeaseNoticeDto>> Notices(Guid id, CancellationToken ct) => records.ListNoticesAsync(id, ct);

    /// <summary>The suggested expiry date for a notice of this type served on this date.</summary>
    [HttpGet("{id:guid}/notices/default")]
    [RequirePermission(Permissions.Leases.View)]
    public Task<NoticeDefaultDto> NoticeDefault(Guid id, [FromQuery] NoticeType type, [FromQuery] DateOnly? servedOn, CancellationToken ct) =>
        records.DefaultNoticeAsync(id, type, servedOn, ct);

    [HttpPost("{id:guid}/notices")]
    [RequirePermission(Permissions.Leases.IssueNotice)]
    public async Task<ActionResult<LeaseNoticeDto>> ServeNotice(Guid id, ServeNoticeRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await records.ServeNoticeAsync(id, request, ct));

    [HttpPost("{id:guid}/notices/{noticeId:guid}/withdraw")]
    [RequirePermission(Permissions.Leases.IssueNotice)]
    public Task<LeaseNoticeDto> WithdrawNotice(Guid id, Guid noticeId, WithdrawNoticeRequest request, CancellationToken ct) =>
        records.WithdrawNoticeAsync(id, noticeId, request, ct);

    // Inspections

    [HttpGet("{id:guid}/inspections")]
    [RequirePermission(Permissions.Leases.View)]
    public Task<LeaseInspectionsDto> Inspections(Guid id, CancellationToken ct) => records.GetInspectionsAsync(id, ct);

    /// <summary>Records or replaces the MoveIn or MoveOut report.</summary>
    [HttpPut("{id:guid}/inspections/{type}")]
    [RequirePermission(Permissions.Leases.Create)]
    public Task<LeaseInspectionDto> SaveInspection(Guid id, InspectionType type, SaveInspectionRequest request, CancellationToken ct) =>
        records.SaveInspectionAsync(id, type, request, ct);
}
