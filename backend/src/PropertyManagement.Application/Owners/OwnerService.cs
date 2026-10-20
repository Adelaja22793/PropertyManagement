using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Application.Properties;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Owners;

namespace PropertyManagement.Application.Owners;

/// <summary>
/// Owners, their payout accounts and management agreements (design 6.3). Owner records are visible across the
/// organization to anyone with Owners.View; the properties listed on an owner follow the caller's property access.
/// </summary>
public sealed class OwnerService(
    IAppDbContext db,
    ICurrentOrganization currentOrganization,
    ICurrentUser currentUser,
    PropertyAccess propertyAccess,
    IUserDirectory users,
    IEmailSender email,
    IAuditLogger audit,
    IClock clock)
{
    public async Task<PagedResult<OwnerSummaryDto>> SearchAsync(string? search, PageRequest page, CancellationToken ct)
    {
        var query = db.Owners.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(o => o.Name.Contains(term) || (o.Email != null && o.Email.Contains(term))
                                     || (o.Phone != null && o.Phone.Contains(term)));
        }
        var today = Today;
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(o => o.Name).Skip(page.Skip).Take(page.SafePageSize)
            .Select(o => new OwnerSummaryDto(o.Id, o.Type, o.Name, o.Email, o.Phone,
                db.PropertyOwnerships.Where(s => s.OwnerId == o.Id && s.EffectiveFrom <= today
                                                 && (s.EffectiveTo == null || s.EffectiveTo >= today))
                    .Select(s => s.PropertyId).Distinct().Count(),
                db.OwnerBankAccounts.Any(a => a.OwnerId == o.Id && a.Status == BankAccountStatus.Approved),
                db.OwnerBankAccounts.Any(a => a.OwnerId == o.Id && a.Status == BankAccountStatus.PendingApproval),
                db.OwnerUserLinks.Any(l => l.OwnerId == o.Id)))
            .ToListAsync(ct);
        return new PagedResult<OwnerSummaryDto>(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<OwnerDetailDto> GetAsync(Guid id, CancellationToken ct) => await ToDetailAsync(await LoadAsync(id, ct), ct);

    public async Task<OwnerDetailDto> CreateAsync(SaveOwnerRequest request, CancellationToken ct)
    {
        var owner = new Owner(currentOrganization.RequireOrganizationId(), request.ToDetails());
        await EnsureNoDuplicateAsync(owner, ct);
        db.Owners.Add(owner);
        audit.Record("owner.created", nameof(Owner), owner.Id.ToString(), details: new { owner.Name });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(owner, ct);
    }

    public async Task<OwnerDetailDto> UpdateAsync(Guid id, SaveOwnerRequest request, CancellationToken ct)
    {
        var owner = await LoadAsync(id, ct);
        var before = Snapshot(owner);
        owner.Update(request.ToDetails());
        await EnsureNoDuplicateAsync(owner, ct);
        audit.Record("owner.updated", nameof(Owner), owner.Id.ToString(), details: new { before, after = Snapshot(owner) });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(owner, ct);
    }

    // Bank accounts

    public async Task<IReadOnlyList<BankAccountDto>> ListBankAccountsAsync(Guid ownerId, CancellationToken ct)
    {
        await LoadAsync(ownerId, ct);
        var accounts = await db.OwnerBankAccounts.Where(a => a.OwnerId == ownerId)
            .OrderByDescending(a => a.RequestedAtUtc).ToListAsync(ct);
        return await ToDtosAsync(accounts, ct);
    }

    /// <summary>Adds a payout account. It stays pending until someone else approves it.</summary>
    public async Task<BankAccountDto> AddBankAccountAsync(Guid ownerId, AddBankAccountRequest request, CancellationToken ct)
    {
        var owner = await LoadAsync(ownerId, ct);
        if (await db.OwnerBankAccounts.AnyAsync(a => a.OwnerId == ownerId && a.Status == BankAccountStatus.PendingApproval, ct))
            throw new DomainException("This owner already has a bank account waiting for approval.", "approval_pending");
        var account = new OwnerBankAccount(owner.OrganizationId, owner.Id, request.BankName, request.AccountNumber,
            request.AccountName, currentUser.RequireUserId(), clock.UtcNow);
        db.OwnerBankAccounts.Add(account);
        audit.Record("owner.bank_account_requested", nameof(OwnerBankAccount), account.Id.ToString(),
            details: new { OwnerId = owner.Id, account.BankName, Account = account.MaskedNumber, account.AccountName });
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([account], ct))[0];
    }

    /// <summary>
    /// Approves or rejects a pending account. Approval replaces the previous approved account and alerts the owner,
    /// so an owner learns of a change they didn't ask for.
    /// </summary>
    public async Task<BankAccountDto> DecideBankAccountAsync(Guid ownerId, Guid accountId, DecideBankAccountRequest request, CancellationToken ct)
    {
        var owner = await LoadAsync(ownerId, ct);
        var account = await db.OwnerBankAccounts.SingleOrDefaultAsync(a => a.Id == accountId && a.OwnerId == ownerId, ct)
                      ?? throw new NotFoundException("Bank account");
        var userId = currentUser.RequireUserId();

        if (request.Approve)
        {
            account.Approve(userId, clock.UtcNow);
            var previous = await db.OwnerBankAccounts
                .Where(a => a.OwnerId == ownerId && a.Status == BankAccountStatus.Approved && a.Id != accountId).ToListAsync(ct);
            previous.ForEach(a => a.MarkReplaced());
        }
        else
        {
            account.Reject(userId, clock.UtcNow, request.Note);
        }

        audit.Record(request.Approve ? "owner.bank_account_approved" : "owner.bank_account_rejected",
            nameof(OwnerBankAccount), account.Id.ToString(),
            details: new { OwnerId = owner.Id, Account = account.MaskedNumber, request.Note });
        await db.SaveChangesAsync(ct);

        if (request.Approve && owner.Email is not null)
        {
            var organization = await db.Organizations.Where(o => o.Id == owner.OrganizationId).Select(o => o.Name).SingleAsync(ct);
            await email.SendAsync(owner.Email, $"{organization}: your payout bank account was changed",
                $"Your rent payouts from {organization} will now go to {account.BankName} account {account.MaskedNumber} " +
                $"({account.AccountName}). If you did not request this, contact {organization} immediately.", ct);
        }
        return (await ToDtosAsync([account], ct))[0];
    }

    // Management agreements

    public async Task<IReadOnlyList<AgreementDto>> ListAgreementsAsync(Guid ownerId, CancellationToken ct)
    {
        await LoadAsync(ownerId, ct);
        return await db.ManagementAgreements.Where(a => a.OwnerId == ownerId)
            .OrderByDescending(a => a.StartDate)
            .Select(a => new AgreementDto(a.Id, a.OwnerId, a.PropertyId,
                db.Properties.Where(p => p.Id == a.PropertyId).Select(p => p.Name).FirstOrDefault(),
                a.FeeBasis, a.FeeValue, a.LettingFeePercent, a.PayoutFrequency, a.AdvanceRentRelease, a.ReserveAmount,
                a.StartDate, a.EndDate, a.Notes, a.RepairApprovalLimit))
            .ToListAsync(ct);
    }

    public async Task<AgreementDto> CreateAgreementAsync(Guid ownerId, SaveAgreementRequest request, CancellationToken ct)
    {
        var owner = await LoadAsync(ownerId, ct);
        var agreement = new ManagementAgreement(owner.OrganizationId, owner.Id, request.ToTerms());
        await ValidateAgreementAsync(agreement, ct);
        db.ManagementAgreements.Add(agreement);
        audit.Record("owner.agreement_created", nameof(ManagementAgreement), agreement.Id.ToString(), details: request);
        await db.SaveChangesAsync(ct);
        return (await ListAgreementsAsync(ownerId, ct)).Single(a => a.Id == agreement.Id);
    }

    public async Task<AgreementDto> UpdateAgreementAsync(Guid ownerId, Guid agreementId, SaveAgreementRequest request, CancellationToken ct)
    {
        await LoadAsync(ownerId, ct);
        var agreement = await db.ManagementAgreements.SingleOrDefaultAsync(a => a.Id == agreementId && a.OwnerId == ownerId, ct)
                        ?? throw new NotFoundException("Agreement");
        agreement.Update(request.ToTerms());
        await ValidateAgreementAsync(agreement, ct);
        audit.Record("owner.agreement_updated", nameof(ManagementAgreement), agreement.Id.ToString(), details: request);
        await db.SaveChangesAsync(ct);
        return (await ListAgreementsAsync(ownerId, ct)).Single(a => a.Id == agreement.Id);
    }

    internal async Task<Owner> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Owners.SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException("Owner");

    private DateOnly Today => clock.TodayInNigeria();

    private async Task ValidateAgreementAsync(ManagementAgreement agreement, CancellationToken ct)
    {
        if (agreement.PropertyId is { } propertyId && !await db.Properties.AnyAsync(p => p.Id == propertyId, ct))
            throw new DomainException("The property was not found.", "property_not_found");

        var others = await db.ManagementAgreements
            .Where(a => a.OwnerId == agreement.OwnerId && a.PropertyId == agreement.PropertyId && a.Id != agreement.Id)
            .ToListAsync(ct);
        if (others.Any(a => a.OverlapsWith(agreement.StartDate, agreement.EndDate)))
            throw new DomainException("Another agreement for this owner and property covers some of these dates.", "agreement_overlap");
    }

    private async Task EnsureNoDuplicateAsync(Owner owner, CancellationToken ct)
    {
        if (owner.Email is { } address && await db.Owners.AnyAsync(o => o.Email == address && o.Id != owner.Id, ct))
            throw new DomainException("Another owner already uses this email address.", "duplicate_owner");
    }

    private async Task<IReadOnlyList<BankAccountDto>> ToDtosAsync(IReadOnlyList<OwnerBankAccount> accounts, CancellationToken ct)
    {
        var people = await users.GetAsync(accounts.SelectMany(a => new[] { a.RequestedByUserId, a.DecidedByUserId ?? Guid.Empty })
            .Where(id => id != Guid.Empty).Distinct(), ct);
        var showFull = currentOrganization.HasPermission(Permissions.Owners.Manage)
                       || currentOrganization.HasPermission(Permissions.Owners.ApproveBankAccounts);
        return accounts.Select(a => new BankAccountDto(a.Id, a.BankName, showFull ? a.AccountNumber : a.MaskedNumber,
            a.AccountName, a.Status, a.RequestedByUserId, people.GetValueOrDefault(a.RequestedByUserId)?.FullName,
            a.RequestedAtUtc, a.DecidedByUserId,
            a.DecidedByUserId is { } d ? people.GetValueOrDefault(d)?.FullName : null, a.DecidedAtUtc, a.DecisionNote)).ToList();
    }

    private async Task<OwnerDetailDto> ToDetailAsync(Owner o, CancellationToken ct)
    {
        var today = Today;
        var properties = await db.PropertyOwnerships
            .Where(s => s.OwnerId == o.Id && s.EffectiveFrom <= today && (s.EffectiveTo == null || s.EffectiveTo >= today))
            .Join(propertyAccess.VisibleProperties(), s => s.PropertyId, p => p.Id,
                (s, p) => new { p.Id, p.Code, p.Name, s.SharePercent, s.EffectiveFrom })
            .OrderBy(p => p.Name)
            .Select(p => new OwnedPropertyDto(p.Id, p.Code, p.Name, p.SharePercent, p.EffectiveFrom))
            .ToListAsync(ct);
        var portal = await db.OwnerUserLinks.AnyAsync(l => l.OwnerId == o.Id, ct);
        return new OwnerDetailDto(o.Id, o.Type, o.Name, o.Email, o.Phone, o.Address, o.TaxIdentificationNumber,
            o.PreferredChannel, o.Notes, o.CreatedAtUtc, properties, portal);
    }

    private static object Snapshot(Owner o) => new { o.Type, o.Name, o.Email, o.Phone, o.Address, o.TaxIdentificationNumber };
}
