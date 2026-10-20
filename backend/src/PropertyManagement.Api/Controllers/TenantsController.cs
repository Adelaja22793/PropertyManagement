using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Api.Authorization;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Application.Organizations;
using PropertyManagement.Application.Tenants;

namespace PropertyManagement.Api.Controllers;

/// <summary>Tenants, identity details, guarantors, screening and portal invitations (design 9.2).</summary>
[ApiController]
[Route("api/v1/tenants")]
public sealed class TenantsController(TenantService tenants, MembershipService memberships) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Tenants.View)]
    public Task<PagedResult<TenantSummaryDto>> Search([FromQuery] string? search, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        tenants.SearchAsync(search, new PageRequest(page, pageSize), ct);

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.Tenants.View)]
    public Task<TenantDetailDto> Get(Guid id, CancellationToken ct) => tenants.GetAsync(id, ct);

    [HttpPost]
    [RequirePermission(Permissions.Tenants.Create)]
    public async Task<ActionResult<TenantDetailDto>> Create(SaveTenantRequest request, CancellationToken ct)
    {
        var created = await tenants.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Tenants.Update)]
    public Task<TenantDetailDto> Update(Guid id, SaveTenantRequest request, CancellationToken ct) => tenants.UpdateAsync(id, request, ct);

    /// <summary>Full identity numbers. Every call is recorded in the audit log.</summary>
    [HttpGet("{id:guid}/kyc")]
    [RequirePermission(Permissions.Tenants.ViewKyc)]
    public Task<KycDetailDto> Kyc(Guid id, CancellationToken ct) => tenants.RevealKycAsync(id, ct);

    [HttpPut("{id:guid}/kyc")]
    [RequirePermission(Permissions.Tenants.Update)]
    public Task<KycSummaryDto> SaveKyc(Guid id, SaveKycRequest request, CancellationToken ct) => tenants.SaveKycAsync(id, request, ct);

    [HttpPut("{id:guid}/kyc/verification")]
    [RequirePermission(Permissions.Tenants.ViewKyc)]
    public Task<KycSummaryDto> SetVerification(Guid id, SetVerificationRequest request, CancellationToken ct) =>
        tenants.SetVerificationAsync(id, request, ct);

    [HttpGet("{id:guid}/guarantors")]
    [RequirePermission(Permissions.Tenants.View)]
    public Task<IReadOnlyList<GuarantorDto>> Guarantors(Guid id, CancellationToken ct) => tenants.ListGuarantorsAsync(id, ct);

    [HttpPost("{id:guid}/guarantors")]
    [RequirePermission(Permissions.Tenants.Update)]
    public async Task<ActionResult<GuarantorDto>> AddGuarantor(Guid id, SaveGuarantorRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await tenants.AddGuarantorAsync(id, request, ct));

    [HttpPut("{id:guid}/guarantors/{guarantorId:guid}")]
    [RequirePermission(Permissions.Tenants.Update)]
    public Task<GuarantorDto> UpdateGuarantor(Guid id, Guid guarantorId, SaveGuarantorRequest request, CancellationToken ct) =>
        tenants.UpdateGuarantorAsync(id, guarantorId, request, ct);

    [HttpDelete("{id:guid}/guarantors/{guarantorId:guid}")]
    [RequirePermission(Permissions.Tenants.Update)]
    public async Task<IActionResult> RemoveGuarantor(Guid id, Guid guarantorId, CancellationToken ct)
    {
        await tenants.RemoveGuarantorAsync(id, guarantorId, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/screening")]
    [RequirePermission(Permissions.Tenants.View)]
    public Task<IReadOnlyList<ScreeningCheckDto>> Screening(Guid id, CancellationToken ct) => tenants.ListScreeningAsync(id, ct);

    [HttpPost("{id:guid}/screening")]
    [RequirePermission(Permissions.Tenants.Update)]
    public async Task<ActionResult<ScreeningCheckDto>> AddScreening(Guid id, AddScreeningCheckRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await tenants.AddScreeningAsync(id, request, ct));

    [HttpPost("{id:guid}/portal-invite")]
    [RequirePermission(Permissions.Tenants.Update)]
    public async Task<ActionResult<CreatedInvitationDto>> InviteToPortal(Guid id, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await memberships.InvitePortalUserAsync(null, id, ct));
}
