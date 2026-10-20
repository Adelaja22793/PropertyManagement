using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Api.Authorization;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Application.Properties;
using PropertyManagement.Domain.Properties;

namespace PropertyManagement.Api.Controllers;

/// <summary>Properties and their units and owners (design 9.2), limited to what the caller may see.</summary>
[ApiController]
[Route("api/v1/properties")]
public sealed class PropertiesController(PropertyService properties) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Properties.View)]
    public Task<PagedResult<PropertySummaryDto>> Search([FromQuery] string? search, [FromQuery] Guid? branchId,
        [FromQuery] PropertyStatus? status, [FromQuery] bool includeArchived = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        properties.SearchAsync(new PropertySearch(search, branchId, status, includeArchived), new PageRequest(page, pageSize), ct);

    /// <summary>Staff who can be assigned as a property's manager.</summary>
    [HttpGet("manager-options")]
    [RequirePermission(Permissions.Properties.Update)]
    public Task<IReadOnlyList<StaffOptionDto>> ManagerOptions(CancellationToken ct) => properties.ListManagerOptionsAsync(ct);

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.Properties.View)]
    public Task<PropertyDetailDto> Get(Guid id, CancellationToken ct) => properties.GetAsync(id, ct);

    [HttpPost]
    [RequirePermission(Permissions.Properties.Create)]
    public async Task<ActionResult<PropertyDetailDto>> Create(SavePropertyRequest request, CancellationToken ct)
    {
        var created = await properties.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Properties.Update)]
    public Task<PropertyDetailDto> Update(Guid id, SavePropertyRequest request, CancellationToken ct) =>
        properties.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/archive")]
    [RequirePermission(Permissions.Properties.Archive)]
    public Task<PropertyDetailDto> Archive(Guid id, CancellationToken ct) => properties.ArchiveAsync(id, ct);

    [HttpGet("{id:guid}/units")]
    [RequirePermission(Permissions.Properties.View)]
    public Task<IReadOnlyList<UnitDto>> Units(Guid id, [FromQuery] bool includeArchived, CancellationToken ct) =>
        properties.ListUnitsAsync(id, includeArchived, ct);

    [HttpPost("{id:guid}/units")]
    [RequirePermission(Permissions.Properties.Update)]
    public async Task<ActionResult<UnitDto>> CreateUnit(Guid id, SaveUnitRequest request, CancellationToken ct)
    {
        var unit = await properties.CreateUnitAsync(id, request, ct);
        return CreatedAtAction(nameof(UnitsController.Get), "Units", new { id = unit.Id }, unit);
    }

    [HttpGet("{id:guid}/owners")]
    [RequirePermission(Permissions.Properties.View)]
    public Task<OwnershipDto> Owners(Guid id, CancellationToken ct) => properties.GetOwnershipAsync(id, ct);

    [HttpPut("{id:guid}/owners")]
    [RequirePermission(Permissions.Properties.Update)]
    [RequirePermission(Permissions.Owners.View)]
    public Task<OwnershipDto> SetOwners(Guid id, SetOwnershipRequest request, CancellationToken ct) =>
        properties.SetOwnershipAsync(id, request, ct);
}

[ApiController]
[Route("api/v1/units")]
public sealed class UnitsController(PropertyService properties) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.Properties.View)]
    public Task<UnitDto> Get(Guid id, CancellationToken ct) => properties.GetUnitAsync(id, ct);

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Properties.Update)]
    public Task<UnitDto> Update(Guid id, SaveUnitRequest request, CancellationToken ct) => properties.UpdateUnitAsync(id, request, ct);

    [HttpPost("{id:guid}/archive")]
    [RequirePermission(Permissions.Properties.Archive)]
    public Task<UnitDto> Archive(Guid id, CancellationToken ct) => properties.ArchiveUnitAsync(id, ct);
}
