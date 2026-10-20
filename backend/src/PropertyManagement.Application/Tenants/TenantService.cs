using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Tenants;

namespace PropertyManagement.Application.Tenants;

/// <summary>Tenants, their identity details, guarantors and screening checks (design 6.4).</summary>
public sealed class TenantService(
    IAppDbContext db,
    ICurrentOrganization currentOrganization,
    ICurrentUser currentUser,
    ISensitiveDataProtector protector,
    IUserDirectory users,
    IAuditLogger audit,
    IClock clock)
{
    public async Task<PagedResult<TenantSummaryDto>> SearchAsync(string? search, PageRequest page, CancellationToken ct)
    {
        var query = db.Tenants.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(t => t.Name.Contains(term) || (t.Email != null && t.Email.Contains(term))
                                     || (t.Phone != null && t.Phone.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(t => t.Name).Skip(page.Skip).Take(page.SafePageSize)
            .Select(t => new TenantSummaryDto(t.Id, t.Type, t.Name, t.Email, t.Phone, t.Employer,
                db.TenantKyc.Where(k => k.TenantId == t.Id).Select(k => k.Status).FirstOrDefault(),
                db.Guarantors.Count(g => g.TenantId == t.Id),
                db.TenantUserLinks.Any(l => l.TenantId == t.Id),
                t.CreatedAtUtc))
            .ToListAsync(ct);
        return new PagedResult<TenantSummaryDto>(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<TenantDetailDto> GetAsync(Guid id, CancellationToken ct) => await ToDetailAsync(await LoadAsync(id, ct), ct);

    public async Task<TenantDetailDto> CreateAsync(SaveTenantRequest request, CancellationToken ct)
    {
        var tenant = new Tenant(currentOrganization.RequireOrganizationId(), request.ToDetails());
        await EnsureNoDuplicateAsync(tenant, ct);
        db.Tenants.Add(tenant);
        audit.Record("tenant.created", nameof(Tenant), tenant.Id.ToString(), details: new { tenant.Name });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(tenant, ct);
    }

    public async Task<TenantDetailDto> UpdateAsync(Guid id, SaveTenantRequest request, CancellationToken ct)
    {
        var tenant = await LoadAsync(id, ct);
        var before = Snapshot(tenant);
        tenant.Update(request.ToDetails());
        await EnsureNoDuplicateAsync(tenant, ct);
        audit.Record("tenant.updated", nameof(Tenant), tenant.Id.ToString(), details: new { before, after = Snapshot(tenant) });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(tenant, ct);
    }

    // KYC

    /// <summary>Decrypts and returns the identity numbers. The caller must hold Tenants.ViewKyc; the read is audited.</summary>
    public async Task<KycDetailDto> RevealKycAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await LoadAsync(tenantId, ct);
        var kyc = await db.TenantKyc.SingleOrDefaultAsync(k => k.TenantId == tenantId, ct);
        audit.Record("tenant.kyc_viewed", nameof(Tenant), tenant.Id.ToString());
        await db.SaveChangesAsync(ct);
        if (kyc is null) return new KycDetailDto(null, null, null, VerificationStatus.Unverified, null, null);
        return new KycDetailDto(kyc.IdType,
            kyc.IdNumberEncrypted is null ? null : protector.Unprotect(kyc.IdNumberEncrypted),
            kyc.BvnEncrypted is null ? null : protector.Unprotect(kyc.BvnEncrypted),
            kyc.Status, kyc.VerifiedAtUtc, await NameAsync(kyc.VerifiedByUserId, ct));
    }

    /// <summary>Replaces the identity numbers. Anyone who can update tenants can enter them; reading them back needs Tenants.ViewKyc.</summary>
    public async Task<KycSummaryDto> SaveKycAsync(Guid tenantId, SaveKycRequest request, CancellationToken ct)
    {
        var tenant = await LoadAsync(tenantId, ct);
        var kyc = await db.TenantKyc.SingleOrDefaultAsync(k => k.TenantId == tenantId, ct);
        if (kyc is null)
        {
            kyc = new TenantKyc(tenant.OrganizationId, tenant.Id);
            db.TenantKyc.Add(kyc);
        }
        kyc.SetIdentity(request.IdType, request.IdNumber, request.Bvn, protector.Protect, clock.UtcNow);
        // Only which fields were set is logged; the numbers themselves never go in the audit log.
        audit.Record("tenant.kyc_updated", nameof(Tenant), tenant.Id.ToString(),
            details: new { kyc.IdType, IdNumberLast4 = kyc.IdNumberLast4, BvnProvided = kyc.BvnEncrypted is not null });
        await db.SaveChangesAsync(ct);
        return await KycSummaryAsync(kyc, ct);
    }

    public async Task<KycSummaryDto> SetVerificationAsync(Guid tenantId, SetVerificationRequest request, CancellationToken ct)
    {
        await LoadAsync(tenantId, ct);
        var kyc = await db.TenantKyc.SingleOrDefaultAsync(k => k.TenantId == tenantId, ct)
                  ?? throw new DomainException("Add identity details before recording a verification result.");
        kyc.SetVerification(request.Status, currentUser.RequireUserId(), clock.UtcNow);
        audit.Record("tenant.kyc_verification_set", nameof(Tenant), tenantId.ToString(), details: new { request.Status });
        await db.SaveChangesAsync(ct);
        return await KycSummaryAsync(kyc, ct);
    }

    // Guarantors

    public async Task<IReadOnlyList<GuarantorDto>> ListGuarantorsAsync(Guid tenantId, CancellationToken ct)
    {
        await LoadAsync(tenantId, ct);
        return (await db.Guarantors.Where(g => g.TenantId == tenantId).OrderBy(g => g.FullName).ToListAsync(ct))
            .Select(ToDto).ToList();
    }

    public async Task<GuarantorDto> AddGuarantorAsync(Guid tenantId, SaveGuarantorRequest request, CancellationToken ct)
    {
        var tenant = await LoadAsync(tenantId, ct);
        var guarantor = new Guarantor(tenant.OrganizationId, tenant.Id, request.ToDetails());
        db.Guarantors.Add(guarantor);
        audit.Record("tenant.guarantor_added", nameof(Guarantor), guarantor.Id.ToString(),
            details: new { TenantId = tenant.Id, guarantor.FullName });
        await db.SaveChangesAsync(ct);
        return ToDto(guarantor);
    }

    public async Task<GuarantorDto> UpdateGuarantorAsync(Guid tenantId, Guid guarantorId, SaveGuarantorRequest request, CancellationToken ct)
    {
        var guarantor = await LoadGuarantorAsync(tenantId, guarantorId, ct);
        guarantor.Update(request.ToDetails());
        audit.Record("tenant.guarantor_updated", nameof(Guarantor), guarantor.Id.ToString(), details: new { TenantId = tenantId });
        await db.SaveChangesAsync(ct);
        return ToDto(guarantor);
    }

    public async Task RemoveGuarantorAsync(Guid tenantId, Guid guarantorId, CancellationToken ct)
    {
        var guarantor = await LoadGuarantorAsync(tenantId, guarantorId, ct);
        if (await db.LeaseGuarantors.AnyAsync(g => g.GuarantorId == guarantorId, ct))
            throw new DomainException("This guarantor is named on a lease and can't be removed.", "guarantor_in_use");
        db.Guarantors.Remove(guarantor);
        audit.Record("tenant.guarantor_removed", nameof(Guarantor), guarantor.Id.ToString(),
            details: new { TenantId = tenantId, guarantor.FullName });
        await db.SaveChangesAsync(ct);
    }

    // Screening

    public async Task<IReadOnlyList<ScreeningCheckDto>> ListScreeningAsync(Guid tenantId, CancellationToken ct)
    {
        await LoadAsync(tenantId, ct);
        var checks = await db.ScreeningChecks.Where(c => c.TenantId == tenantId).OrderByDescending(c => c.CheckedAtUtc).ToListAsync(ct);
        var people = await users.GetAsync(checks.Select(c => c.CheckedByUserId).Distinct(), ct);
        return checks.Select(c => new ScreeningCheckDto(c.Id, c.Type, c.Outcome, c.Notes, c.CheckedByUserId,
            people.GetValueOrDefault(c.CheckedByUserId)?.FullName, c.CheckedAtUtc)).ToList();
    }

    public async Task<ScreeningCheckDto> AddScreeningAsync(Guid tenantId, AddScreeningCheckRequest request, CancellationToken ct)
    {
        var tenant = await LoadAsync(tenantId, ct);
        var userId = currentUser.RequireUserId();
        var check = new ScreeningCheck(tenant.OrganizationId, tenant.Id, request.Type, request.Outcome, request.Notes, userId, clock.UtcNow);
        db.ScreeningChecks.Add(check);
        audit.Record("tenant.screening_recorded", nameof(ScreeningCheck), check.Id.ToString(),
            details: new { TenantId = tenant.Id, check.Type, check.Outcome });
        await db.SaveChangesAsync(ct);
        return new ScreeningCheckDto(check.Id, check.Type, check.Outcome, check.Notes, userId, await NameAsync(userId, ct), check.CheckedAtUtc);
    }

    internal async Task<Tenant> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Tenants.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Tenant");

    private async Task<Guarantor> LoadGuarantorAsync(Guid tenantId, Guid guarantorId, CancellationToken ct)
    {
        await LoadAsync(tenantId, ct);
        return await db.Guarantors.SingleOrDefaultAsync(g => g.Id == guarantorId && g.TenantId == tenantId, ct)
               ?? throw new NotFoundException("Guarantor");
    }

    private async Task EnsureNoDuplicateAsync(Tenant tenant, CancellationToken ct)
    {
        if (tenant.Email is { } address && await db.Tenants.AnyAsync(t => t.Email == address && t.Id != tenant.Id, ct))
            throw new DomainException("Another tenant already uses this email address.", "duplicate_tenant");
    }

    private async Task<string?> NameAsync(Guid? userId, CancellationToken ct) =>
        userId is { } id ? (await users.GetAsync([id], ct)).GetValueOrDefault(id)?.FullName : null;

    private async Task<KycSummaryDto> KycSummaryAsync(TenantKyc? k, CancellationToken ct) => k is null
        ? new KycSummaryDto(null, null, null, VerificationStatus.Unverified, null, null)
        : new KycSummaryDto(k.IdType, k.IdNumberLast4, k.BvnLast4, k.Status, k.VerifiedAtUtc, await NameAsync(k.VerifiedByUserId, ct));

    private async Task<TenantDetailDto> ToDetailAsync(Tenant t, CancellationToken ct)
    {
        var kyc = await db.TenantKyc.SingleOrDefaultAsync(k => k.TenantId == t.Id, ct);
        var portal = await db.TenantUserLinks.AnyAsync(l => l.TenantId == t.Id, ct);
        return new TenantDetailDto(t.Id, t.Type, t.Name, t.Email, t.Phone, t.AlternatePhone, t.Address, t.Employer,
            t.Occupation, t.EmergencyContactName, t.EmergencyContactPhone, t.CompanyRegistrationNumber,
            t.TaxIdentificationNumber, t.Notes, t.CreatedAtUtc, await KycSummaryAsync(kyc, ct), portal);
    }

    private static GuarantorDto ToDto(Guarantor g) => new(g.Id, g.TenantId, g.FullName, g.Phone, g.Email, g.Address,
        g.Relationship, g.Employer, g.Occupation, g.SignedFormReceived, g.CreatedAtUtc);

    private static object Snapshot(Tenant t) => new { t.Type, t.Name, t.Email, t.Phone, t.Address, t.Employer };
}
