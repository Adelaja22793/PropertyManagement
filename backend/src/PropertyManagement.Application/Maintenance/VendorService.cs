using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Maintenance;

namespace PropertyManagement.Application.Maintenance;

/// <summary>Suppliers and contractors (design 6.10). Shared across the organization, like owners and tenants.</summary>
public sealed class VendorService(IAppDbContext db, ICurrentOrganization currentOrganization, IAuditLogger audit, IClock clock)
{
    public async Task<PagedResult<VendorDto>> SearchAsync(string? search, bool includeInactive, PageRequest page, CancellationToken ct)
    {
        var query = db.Vendors.AsQueryable();
        if (!includeInactive) query = query.Where(v => v.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(v => v.Name.Contains(term) || (v.Trade != null && v.Trade.Contains(term))
                                     || (v.Phone != null && v.Phone.Contains(term)) || (v.Email != null && v.Email.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var items = await Project(query.OrderBy(v => v.Name).Skip(page.Skip).Take(page.SafePageSize)).ToListAsync(ct);
        return new PagedResult<VendorDto>(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<VendorDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project(db.Vendors.Where(v => v.Id == id)).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Vendor");

    public async Task<VendorDto> CreateAsync(SaveVendorRequest request, CancellationToken ct)
    {
        var vendor = new Vendor(currentOrganization.RequireOrganizationId(), request.ToDetails(), clock.UtcNow);
        db.Vendors.Add(vendor);
        audit.Record("vendor.created", nameof(Vendor), vendor.Id.ToString(), details: new { vendor.Name, vendor.Trade });
        await db.SaveChangesAsync(ct);
        return await GetAsync(vendor.Id, ct);
    }

    public async Task<VendorDto> UpdateAsync(Guid id, SaveVendorRequest request, CancellationToken ct)
    {
        var vendor = await db.Vendors.SingleOrDefaultAsync(v => v.Id == id, ct) ?? throw new NotFoundException("Vendor");
        vendor.Update(request.ToDetails());
        audit.Record("vendor.updated", nameof(Vendor), vendor.Id.ToString(), details: new { vendor.Name, vendor.Trade });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<VendorDto> SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var vendor = await db.Vendors.SingleOrDefaultAsync(v => v.Id == id, ct) ?? throw new NotFoundException("Vendor");
        vendor.SetActive(active);
        audit.Record(active ? "vendor.reactivated" : "vendor.deactivated", nameof(Vendor), vendor.Id.ToString(),
            details: new { vendor.Name });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private IQueryable<VendorDto> Project(IQueryable<Vendor> query) => query.Select(v => new VendorDto(
        v.Id, v.Name, v.Trade, v.ContactName, v.Phone, v.Email, v.Address, v.Notes, v.IsActive,
        db.MaintenanceRequests.Count(r => r.VendorId == v.Id && r.Status != MaintenanceStatus.Completed
                                          && r.Status != MaintenanceStatus.Rejected && r.Status != MaintenanceStatus.Cancelled),
        v.CreatedAtUtc));
}
