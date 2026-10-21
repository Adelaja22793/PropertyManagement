using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Organizations;
using PropertyManagement.Domain.Leases;
using PropertyManagement.Domain.Properties;

namespace PropertyManagement.Application.Properties;

public sealed class PropertyService(
    IAppDbContext db,
    ICurrentOrganization currentOrganization,
    PropertyAccess access,
    IUserDirectory users,
    IAuditLogger audit,
    IClock clock,
    Platform.SubscriptionService subscriptions)
{
    public async Task<PagedResult<PropertySummaryDto>> SearchAsync(PropertySearch search, PageRequest page, CancellationToken ct)
    {
        var query = access.VisibleProperties();
        if (!search.IncludeArchived && search.Status != PropertyStatus.Archived)
            query = query.Where(p => p.Status != PropertyStatus.Archived);
        if (search.Status is { } status) query = query.Where(p => p.Status == status);
        if (search.BranchId is { } branchId) query = query.Where(p => p.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(search.Search))
        {
            var term = search.Search.Trim();
            query = query.Where(p => p.Name.Contains(term) || p.Code.Contains(term) || p.AddressLine.Contains(term)
                                     || (p.City != null && p.City.Contains(term)));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(p => p.Name)
            .Skip(page.Skip).Take(page.SafePageSize)
            .Select(p => new PropertySummaryDto(p.Id, p.Code, p.Name, p.Type, p.AddressLine, p.City, p.State,
                p.BranchId, db.Branches.Where(b => b.Id == p.BranchId).Select(b => b.Name).FirstOrDefault(),
                p.ManagerUserId, p.Status,
                db.Units.Count(u => u.PropertyId == p.Id && !u.IsArchived),
                db.Units.Count(u => u.PropertyId == p.Id && !u.IsArchived && u.OccupancyStatus == OccupancyStatus.Occupied)))
            .ToListAsync(ct);
        return new PagedResult<PropertySummaryDto>(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<PropertyDetailDto> GetAsync(Guid id, CancellationToken ct) => await ToDetailAsync(await LoadAsync(id, ct), ct);

    public async Task<PropertyDetailDto> CreateAsync(SavePropertyRequest request, CancellationToken ct)
    {
        var organizationId = currentOrganization.RequireOrganizationId();
        await ValidateReferencesAsync(request, ct);
        var property = new Property(organizationId, request.ToDetails());
        await EnsureUniqueCodeAsync(property.Code, null, ct);
        db.Properties.Add(property);
        audit.Record("property.created", nameof(Property), property.Id.ToString(), details: new { property.Code, property.Name });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(property, ct);
    }

    public async Task<PropertyDetailDto> UpdateAsync(Guid id, SavePropertyRequest request, CancellationToken ct)
    {
        var property = await LoadAsync(id, ct);
        await ValidateReferencesAsync(request, ct);
        var before = Snapshot(property);
        property.Update(request.ToDetails(), request.Status);
        await EnsureUniqueCodeAsync(property.Code, property.Id, ct);
        audit.Record("property.updated", nameof(Property), property.Id.ToString(), details: new { before, after = Snapshot(property) });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(property, ct);
    }

    public async Task<PropertyDetailDto> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var property = await LoadAsync(id, ct);
        if (await db.Units.AnyAsync(u => u.PropertyId == id && !u.IsArchived && u.OccupancyStatus == OccupancyStatus.Occupied, ct))
            throw new DomainException("A property with occupied units can't be archived.", "unit_occupied");
        property.Archive(clock.UtcNow);
        audit.Record("property.archived", nameof(Property), property.Id.ToString(), details: new { property.Code });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(property, ct);
    }

    // Units

    public async Task<IReadOnlyList<UnitDto>> ListUnitsAsync(Guid propertyId, bool includeArchived, CancellationToken ct)
    {
        var property = await LoadAsync(propertyId, ct);
        var units = await db.Units.Where(u => u.PropertyId == propertyId && (includeArchived || !u.IsArchived))
            .OrderBy(u => u.Code).ToListAsync(ct);
        var active = await db.Leases.Where(l => l.PropertyId == propertyId && l.Status == LeaseStatus.Active)
            .GroupBy(l => l.UnitId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return units.Select(u => ToDto(u, property.Code, active.GetValueOrDefault(u.Id))).ToList();
    }

    public async Task<UnitDto> GetUnitAsync(Guid unitId, CancellationToken ct)
    {
        var (unit, property) = await LoadUnitAsync(unitId, ct);
        return ToDto(unit, property.Code, await ActiveLeasesAsync(unit.Id, ct));
    }

    public async Task<UnitDto> CreateUnitAsync(Guid propertyId, SaveUnitRequest request, CancellationToken ct)
    {
        var property = await LoadAsync(propertyId, ct);
        property.EnsureNotArchived();
        await subscriptions.EnsureRoomForUnitsAsync(1, ct);
        var unit = new PropertyUnit(property.OrganizationId, property.Id, request.ToDetails());
        if (request.OccupancyStatus != OccupancyStatus.Vacant) unit.Update(request.ToDetails(), request.OccupancyStatus);
        await EnsureUniqueUnitCodeAsync(property.Id, unit.Code, null, ct);
        db.Units.Add(unit);
        audit.Record("unit.created", nameof(PropertyUnit), unit.Id.ToString(), details: new { Property = property.Code, unit.Code });
        await db.SaveChangesAsync(ct);
        return ToDto(unit, property.Code, 0);
    }

    public async Task<UnitDto> UpdateUnitAsync(Guid unitId, SaveUnitRequest request, CancellationToken ct)
    {
        var (unit, property) = await LoadUnitAsync(unitId, ct);
        property.EnsureNotArchived();
        var activeLeases = await ActiveLeasesAsync(unit.Id, ct);
        var before = ToDto(unit, property.Code, activeLeases);
        if (request.OccupancyStatus != unit.OccupancyStatus && activeLeases > 0)
            throw new DomainException("This unit's occupancy comes from its lease. End or renew the lease instead.", "unit_has_lease");
        unit.Update(request.ToDetails(), request.OccupancyStatus);
        await EnsureSharedLimitFitsAsync(unit, ct);
        await EnsureUniqueUnitCodeAsync(property.Id, unit.Code, unit.Id, ct);
        audit.Record("unit.updated", nameof(PropertyUnit), unit.Id.ToString(), details: new { before, after = ToDto(unit, property.Code, activeLeases) });
        await db.SaveChangesAsync(ct);
        return ToDto(unit, property.Code, activeLeases);
    }

    public async Task<UnitDto> ArchiveUnitAsync(Guid unitId, CancellationToken ct)
    {
        var (unit, property) = await LoadUnitAsync(unitId, ct);
        if (await db.Leases.AnyAsync(l => l.UnitId == unit.Id && l.Status == LeaseStatus.Active, ct))
            throw new DomainException("A unit with an active lease can't be archived.", "unit_has_lease");
        unit.Archive();
        audit.Record("unit.archived", nameof(PropertyUnit), unit.Id.ToString(), details: new { Property = property.Code, unit.Code });
        await db.SaveChangesAsync(ct);
        return ToDto(unit, property.Code, 0);
    }

    // Ownership

    public async Task<OwnershipDto> GetOwnershipAsync(Guid propertyId, CancellationToken ct)
    {
        await LoadAsync(propertyId, ct);
        return await OwnershipAsync(propertyId, ct);
    }

    /// <summary>
    /// Replaces the owners from a date. Current shares end the day before; shares that start on or after that
    /// date are corrected rather than kept, so the totals stay at 100% on every date.
    /// </summary>
    public async Task<OwnershipDto> SetOwnershipAsync(Guid propertyId, SetOwnershipRequest request, CancellationToken ct)
    {
        var property = await LoadAsync(propertyId, ct);
        property.EnsureNotArchived();
        var shares = request.Shares.Select(s => (s.OwnerId, s.SharePercent)).ToList();
        PropertyOwnership.ValidateSet(shares);

        var ownerIds = shares.Select(s => s.OwnerId).ToList();
        // The query filter limits this to the active organization, so another organization's owner counts as missing.
        if (await db.Owners.CountAsync(o => ownerIds.Contains(o.Id), ct) != ownerIds.Count)
            throw new DomainException("One or more owners were not found.", "owner_not_found");

        var existing = await db.PropertyOwnerships.Where(o => o.PropertyId == propertyId).ToListAsync(ct);
        foreach (var later in existing.Where(o => o.EffectiveFrom >= request.EffectiveFrom))
            db.PropertyOwnerships.Remove(later);
        foreach (var open in existing.Where(o => o.EffectiveFrom < request.EffectiveFrom
                                                 && (o.EffectiveTo is null || o.EffectiveTo >= request.EffectiveFrom)))
            open.EndOn(request.EffectiveFrom.AddDays(-1));

        foreach (var (ownerId, percent) in shares)
            db.PropertyOwnerships.Add(new PropertyOwnership(property.OrganizationId, propertyId, ownerId, percent, request.EffectiveFrom));

        audit.Record("property.ownership_changed", nameof(Property), propertyId.ToString(),
            details: new { request.EffectiveFrom, request.Shares });
        await db.SaveChangesAsync(ct);
        return await OwnershipAsync(propertyId, ct);
    }

    /// <summary>Active staff who can be made a property's manager. Names only, so it needs no Users permission.</summary>
    public async Task<IReadOnlyList<StaffOptionDto>> ListManagerOptionsAsync(CancellationToken ct)
    {
        var staffRoles = Roles.StaffRoles.ToArray();
        var userIds = await db.Memberships
            .Where(m => m.Status == MembershipStatus.Active
                && m.Roles.Any(r => staffRoles.Contains(r.RoleName) || r.RoleName.StartsWith(CustomRole.KeyPrefix)))
            .Select(m => m.UserId).ToListAsync(ct);
        var people = await users.GetAsync(userIds, ct);
        return people.Values.Select(u => new StaffOptionDto(u.Id, u.FullName)).OrderBy(u => u.FullName).ToList();
    }

    internal async Task<Property> LoadAsync(Guid id, CancellationToken ct) =>
        await access.VisibleProperties().SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Property");

    private Task<int> ActiveLeasesAsync(Guid unitId, CancellationToken ct) =>
        db.Leases.CountAsync(l => l.UnitId == unitId && l.Status == LeaseStatus.Active, ct);

    /// <summary>A shared unit's limit can't go below the number of leases already holding it on one day.</summary>
    private async Task EnsureSharedLimitFitsAsync(PropertyUnit unit, CancellationToken ct)
    {
        var leases = await db.Leases.Where(l => l.UnitId == unit.Id && l.Status != LeaseStatus.Draft && l.Status != LeaseStatus.Cancelled)
            .ToListAsync(ct);
        var most = UnitCapacity.MostAtOnceFrom(clock.TodayInNigeria(), leases);
        if (most > unit.MaxConcurrentLeases)
            throw new DomainException(
                $"{most} leases hold this unit at the same time, so it must take at least {most} at once. End some of them first.",
                "shared_occupancy_in_use");
    }

    private async Task<(PropertyUnit Unit, Property Property)> LoadUnitAsync(Guid unitId, CancellationToken ct)
    {
        var unit = await access.VisibleUnits().SingleOrDefaultAsync(u => u.Id == unitId, ct) ?? throw new NotFoundException("Unit");
        var property = await db.Properties.SingleAsync(p => p.Id == unit.PropertyId, ct);
        return (unit, property);
    }

    private async Task<OwnershipDto> OwnershipAsync(Guid propertyId, CancellationToken ct)
    {
        var today = clock.TodayInNigeria();
        var rows = await db.PropertyOwnerships.Where(o => o.PropertyId == propertyId)
            .Join(db.Owners, o => o.OwnerId, w => w.Id, (o, w) => new { o, w.Name })
            .OrderByDescending(x => x.o.EffectiveFrom).ThenByDescending(x => x.o.SharePercent)
            .ToListAsync(ct);
        var all = rows.Select(x => (Row: x.o, Dto: new OwnershipShareDto(x.o.OwnerId, x.Name, x.o.SharePercent, x.o.EffectiveFrom, x.o.EffectiveTo))).ToList();
        return new OwnershipDto(all.Where(x => x.Row.IsCurrentOn(today)).Select(x => x.Dto).ToList(), all.Select(x => x.Dto).ToList());
    }

    private async Task ValidateReferencesAsync(SavePropertyRequest request, CancellationToken ct)
    {
        if (request.BranchId is { } branchId)
        {
            if (!await db.Branches.AnyAsync(b => b.Id == branchId, ct))
                throw new DomainException("The branch was not found.", "branch_not_found");
            if (access.IsBranchLimited(out var allowed) && !allowed.Contains(branchId))
                throw new ForbiddenException("You can only manage properties in your own branches.");
        }
        else if (access.IsBranchLimited(out _))
        {
            throw new DomainException("Choose one of your branches for this property.", "branch_required");
        }

        if (request.ManagerUserId is { } managerId)
        {
            var staffRoles = Roles.StaffRoles.ToArray();
            var isStaff = await db.Memberships.AnyAsync(m => m.UserId == managerId && m.Status == MembershipStatus.Active
                && m.Roles.Any(r => staffRoles.Contains(r.RoleName) || r.RoleName.StartsWith(CustomRole.KeyPrefix)), ct);
            if (!isStaff) throw new DomainException("The manager must be an active staff member.", "manager_not_found");
        }
    }

    private async Task EnsureUniqueCodeAsync(string code, Guid? exceptId, CancellationToken ct)
    {
        // Checked against all properties in the organization, including ones the caller can't see.
        if (await db.Properties.AnyAsync(p => p.Code == code && p.Id != exceptId, ct))
            throw new DomainException($"Another property already uses the code '{code}'.", "duplicate_code");
    }

    private async Task EnsureUniqueUnitCodeAsync(Guid propertyId, string code, Guid? exceptId, CancellationToken ct)
    {
        if (await db.Units.AnyAsync(u => u.PropertyId == propertyId && u.Code == code && u.Id != exceptId, ct))
            throw new DomainException($"This property already has a unit '{code}'.", "duplicate_code");
    }

    private async Task<PropertyDetailDto> ToDetailAsync(Property p, CancellationToken ct)
    {
        var branchName = p.BranchId is { } b ? await db.Branches.Where(x => x.Id == b).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var managerName = p.ManagerUserId is { } m ? (await users.GetAsync([m], ct)).GetValueOrDefault(m)?.FullName : null;
        var unitCount = await db.Units.CountAsync(u => u.PropertyId == p.Id && !u.IsArchived, ct);
        var occupied = await db.Units.CountAsync(u => u.PropertyId == p.Id && !u.IsArchived && u.OccupancyStatus == OccupancyStatus.Occupied, ct);
        var ownership = await OwnershipAsync(p.Id, ct);
        return new PropertyDetailDto(p.Id, p.Code, p.Name, p.Type, p.Description, p.AddressLine, p.City, p.State,
            p.LocalGovernmentArea, p.BranchId, branchName, p.ManagerUserId, managerName, p.Amenities, p.Status,
            p.CreatedAtUtc, p.ArchivedAtUtc, unitCount, occupied, ownership.Current);
    }

    private static object Snapshot(Property p) => new
    {
        p.Code, p.Name, p.Type, p.AddressLine, p.City, p.State, p.LocalGovernmentArea, p.BranchId, p.ManagerUserId, p.Status,
    };

    private static UnitDto ToDto(PropertyUnit u, string propertyCode, int activeLeases) => new(u.Id, u.PropertyId, propertyCode,
        u.Code, u.Type, u.Floor, u.Bedrooms, u.Bathrooms, u.SizeSquareMetres, u.DefaultRent, u.Currency, u.BillingFrequency,
        u.ServiceChargeSharePercent, u.OccupancyStatus, u.Amenities, u.Notes, u.IsArchived, u.SharedOccupancyLimit, activeLeases);
}
