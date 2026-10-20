using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Api.Authorization;
using PropertyManagement.Application.Auditing;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;

namespace PropertyManagement.Api.Controllers;

[ApiController]
[Route("api/v1/audit-logs")]
public sealed class AuditLogsController(AuditQueryService audit) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Audit.View)]
    public Task<PagedResult<AuditLogDto>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        audit.ListAsync(new PageRequest(page, pageSize), ct);
}
