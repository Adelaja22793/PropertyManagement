using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Api.Authorization;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Branches;

namespace PropertyManagement.Api.Controllers;

/// <summary>Branches of the organization selected with the X-Organization-Id header.</summary>
[ApiController]
[Route("api/v1/branches")]
public sealed class BranchesController(BranchService branches) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Branches.View)]
    public Task<IReadOnlyList<BranchDto>> List(CancellationToken ct) => branches.ListAsync(ct);

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.Branches.View)]
    public Task<BranchDto> Get(Guid id, CancellationToken ct) => branches.GetAsync(id, ct);

    [HttpPost]
    [RequirePermission(Permissions.Branches.Manage)]
    public async Task<ActionResult<BranchDto>> Create(SaveBranchRequest request, CancellationToken ct)
    {
        var branch = await branches.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = branch.Id }, branch);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Branches.Manage)]
    public Task<BranchDto> Update(Guid id, SaveBranchRequest request, CancellationToken ct) => branches.UpdateAsync(id, request, ct);
}
