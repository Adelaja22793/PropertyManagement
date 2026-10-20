using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Api.Authorization;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Application.Organizations;
using PropertyManagement.Application.Owners;

namespace PropertyManagement.Api.Controllers;

/// <summary>Owners, payout accounts, management agreements and portal invitations (design 9.2).</summary>
[ApiController]
[Route("api/v1/owners")]
public sealed class OwnersController(OwnerService owners, MembershipService memberships) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Owners.View)]
    public Task<PagedResult<OwnerSummaryDto>> Search([FromQuery] string? search, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        owners.SearchAsync(search, new PageRequest(page, pageSize), ct);

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.Owners.View)]
    public Task<OwnerDetailDto> Get(Guid id, CancellationToken ct) => owners.GetAsync(id, ct);

    [HttpPost]
    [RequirePermission(Permissions.Owners.Manage)]
    public async Task<ActionResult<OwnerDetailDto>> Create(SaveOwnerRequest request, CancellationToken ct)
    {
        var created = await owners.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Owners.Manage)]
    public Task<OwnerDetailDto> Update(Guid id, SaveOwnerRequest request, CancellationToken ct) => owners.UpdateAsync(id, request, ct);

    [HttpGet("{id:guid}/bank-accounts")]
    [RequirePermission(Permissions.Owners.View)]
    public Task<IReadOnlyList<BankAccountDto>> BankAccounts(Guid id, CancellationToken ct) => owners.ListBankAccountsAsync(id, ct);

    [HttpPost("{id:guid}/bank-accounts")]
    [RequirePermission(Permissions.Owners.Manage)]
    public async Task<ActionResult<BankAccountDto>> AddBankAccount(Guid id, AddBankAccountRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await owners.AddBankAccountAsync(id, request, ct));

    [HttpPost("{id:guid}/bank-accounts/{accountId:guid}/decision")]
    [RequirePermission(Permissions.Owners.ApproveBankAccounts)]
    public Task<BankAccountDto> DecideBankAccount(Guid id, Guid accountId, DecideBankAccountRequest request, CancellationToken ct) =>
        owners.DecideBankAccountAsync(id, accountId, request, ct);

    [HttpGet("{id:guid}/agreements")]
    [RequirePermission(Permissions.Owners.View)]
    public Task<IReadOnlyList<AgreementDto>> Agreements(Guid id, CancellationToken ct) => owners.ListAgreementsAsync(id, ct);

    [HttpPost("{id:guid}/agreements")]
    [RequirePermission(Permissions.Owners.Manage)]
    public async Task<ActionResult<AgreementDto>> CreateAgreement(Guid id, SaveAgreementRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await owners.CreateAgreementAsync(id, request, ct));

    [HttpPut("{id:guid}/agreements/{agreementId:guid}")]
    [RequirePermission(Permissions.Owners.Manage)]
    public Task<AgreementDto> UpdateAgreement(Guid id, Guid agreementId, SaveAgreementRequest request, CancellationToken ct) =>
        owners.UpdateAgreementAsync(id, agreementId, request, ct);

    [HttpPost("{id:guid}/portal-invite")]
    [RequirePermission(Permissions.Owners.Manage)]
    public async Task<ActionResult<CreatedInvitationDto>> InviteToPortal(Guid id, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await memberships.InvitePortalUserAsync(id, null, ct));
}
