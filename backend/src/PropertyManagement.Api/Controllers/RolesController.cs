using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Api.Authorization;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Organizations;

namespace PropertyManagement.Api.Controllers;

/// <summary>Built-in and custom roles of the organization selected with the X-Organization-Id header (design 5).</summary>
[ApiController]
[Route("api/v1/roles")]
public sealed class RolesController(CustomRoleService roles) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Users.View)]
    public Task<IReadOnlyList<RoleDto>> List(CancellationToken ct) => roles.ListAsync(ct);

    /// <summary>Every permission a custom role can be given, grouped by module.</summary>
    [HttpGet("permissions")]
    [RequirePermission(Permissions.Users.View)]
    public IReadOnlyList<PermissionGroupDto> PermissionList() => CustomRoleService.PermissionGroups;

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.Users.View)]
    public Task<RoleDto> Get(Guid id, CancellationToken ct) => roles.GetAsync(id, ct);

    [HttpPost]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<ActionResult<RoleDto>> Create(SaveRoleRequest request, CancellationToken ct)
    {
        var role = await roles.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = role.Id }, role);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Users.Manage)]
    public Task<RoleDto> Update(Guid id, SaveRoleRequest request, CancellationToken ct) => roles.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await roles.DeleteAsync(id, ct);
        return NoContent();
    }
}
