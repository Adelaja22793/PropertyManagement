using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Common;
using PropertyManagement.Application.Properties;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Leases;
using PropertyManagement.Domain.Properties;

namespace PropertyManagement.Application.Leases;

/// <summary>
/// Leases, their parties and charges, renewals and move-outs (design 6.5). Leases follow their property's visibility.
/// Unit occupancy is driven by leases: activating, ending or renewing a lease updates the unit, and
/// <see cref="RefreshOccupancyAsync"/> catches up date changes (a lease starting or running out) each day.
/// No money is posted here; Phase 4 turns lease terms into invoices once the finance design is signed off.
/// </summary>
public sealed partial class LeaseService(
    IAppDbContext db,
    PropertyAccess access,
    ICurrentOrganization currentOrganization,
    ICurrentUser currentUser,
    IUserDirectory users,
    IAuditLogger audit,
    IClock clock)
{
    public IQueryable<Lease> VisibleLeases()
    {
        var properties = access.VisibleProperties();
        return db.Leases.Where(l => properties.Any(p => p.Id == l.PropertyId));
    }

    public async Task<PagedResult<LeaseSummaryDto>> SearchAsync(LeaseSearch search, PageRequest page, CancellationToken ct)
    {
        var today = clock.TodayInNigeria();
        var query = Filter(VisibleLeases(), search, today);
        var total = await query.CountAsync(ct);
        var ordered = search.EndingWithinDays is not null || search.Stage is LeaseStage.Expiring or LeaseStage.HoldingOver
            ? query.OrderBy(l => l.EndDate).ThenBy(l => l.Number)
            : query.OrderByDescending(l => l.Number);
        var items = await Summaries(ordered.Skip(page.Skip).Take(page.SafePageSize), today, ct);
        return new PagedResult<LeaseSummaryDto>(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<LeaseDetailDto> GetAsync(Guid id, CancellationToken ct) => await ToDetailAsync(await LoadAsync(id, ct), ct);

    public async Task<LeaseDetailDto> CreateAsync(SaveLeaseRequest request, CancellationToken ct)
    {
        var unit = await LoadUnitAsync(request.UnitId, ct);
        var number = (await db.Leases.MaxAsync(l => (int?)l.Number, ct) ?? 0) + 1;
        var lease = new Lease(currentOrganization.RequireOrganizationId(), number, unit.PropertyId, unit.Id, request.ToTerms());
        await ApplyPartiesAndChargesAsync(lease, request, ct);
        await EnsureNoOverlapAsync(lease, ct);
        db.Leases.Add(lease);
        audit.Record("lease.created", nameof(Lease), lease.Id.ToString(), details: Snapshot(lease));
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(lease, ct);
    }

    public async Task<LeaseDetailDto> UpdateAsync(Guid id, SaveLeaseRequest request, CancellationToken ct)
    {
        var lease = await LoadAsync(id, ct);
        var before = Snapshot(lease);
        if (request.UnitId != lease.UnitId)
        {
            if (lease.PreviousLeaseId is not null) throw new DomainException("A renewal stays on the same unit.");
            throw new DomainException("A lease's unit can't be changed. Cancel this draft and start a new lease.");
        }
        lease.Update(request.ToTerms());
        await ApplyPartiesAndChargesAsync(lease, request, ct);
        await EnsureNoOverlapAsync(lease, ct);
        audit.Record("lease.updated", nameof(Lease), lease.Id.ToString(), details: new { before, after = Snapshot(lease) });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(lease, ct);
    }

    public async Task<LeaseDetailDto> ActivateAsync(Guid id, CancellationToken ct)
    {
        var lease = await LoadAsync(id, ct);
        var unit = await LoadUnitAsync(lease.UnitId, ct);
        if (unit.IsArchived) throw new DomainException("This unit is archived.", "archived");
        await EnsurePartiesStillValidAsync(lease, ct);

        Lease? previous = null;
        if (lease.PreviousLeaseId is { } previousId)
        {
            previous = await db.Leases.SingleAsync(l => l.Id == previousId, ct);
            if (previous.Status != LeaseStatus.Active)
                throw new DomainException("The lease being renewed is no longer active.", "lease_not_active");
            if (lease.StartDate <= previous.EndDate)
                throw new DomainException("A renewal must start after the current lease ends.");
        }
        await EnsureNoOverlapAsync(lease, ct);

        lease.Activate(currentUser.RequireUserId(), clock.UtcNow);
        previous?.MarkRenewed(clock.UtcNow);
        audit.Record("lease.activated", nameof(Lease), lease.Id.ToString(),
            details: new { lease.Reference, Renews = previous?.Reference });
        await RefreshUnitsAsync([lease.UnitId], [lease, .. previous is null ? Array.Empty<Lease>() : [previous]], ct);
        // A let unit is no longer advertised; a renewal keeps the same tenant in, so there is nothing to close.
        if (previous is null) await Listings.ListingService.CloseForLetUnitAsync(db, lease.UnitId, clock.UtcNow, ct);
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(lease, ct);
    }

    public async Task<LeaseDetailDto> CancelAsync(Guid id, CancelLeaseRequest request, CancellationToken ct)
    {
        var lease = await LoadAsync(id, ct);
        lease.Cancel(request.Reason, clock.UtcNow);
        audit.Record("lease.cancelled", nameof(Lease), lease.Id.ToString(), details: new { lease.Reference, request.Reason });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(lease, ct);
    }

    public async Task<LeaseDetailDto> EndAsync(Guid id, EndLeaseRequest request, CancellationToken ct)
    {
        var lease = await LoadAsync(id, ct);
        if (request.MoveOutDate > clock.TodayInNigeria().AddDays(366))
            throw new DomainException("The move-out date can be at most a year ahead.");
        var renewal = await db.Leases.SingleOrDefaultAsync(
            l => l.PreviousLeaseId == lease.Id && l.Status == LeaseStatus.Draft, ct);
        lease.End(request.MoveOutDate, request.Reason, clock.UtcNow);
        // A tenant who is leaving won't take up the renewal that was being prepared.
        renewal?.Cancel("The lease it renewed was ended.", clock.UtcNow);
        audit.Record(lease.Status == LeaseStatus.Terminated ? "lease.terminated" : "lease.ended", nameof(Lease),
            lease.Id.ToString(), details: new { lease.Reference, request.MoveOutDate, request.Reason });
        await RefreshUnitsAsync([lease.UnitId], [lease], ct);
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(lease, ct);
    }

    /// <summary>Drafts the next lease for the same unit and tenants. It takes effect when it is activated.</summary>
    public async Task<LeaseDetailDto> RenewAsync(Guid id, RenewLeaseRequest request, CancellationToken ct)
    {
        var current = await LoadAsync(id, ct);
        if (current.Status != LeaseStatus.Active)
            throw new DomainException("Only an active lease can be renewed.", "lease_not_active");
        if (await db.Leases.AnyAsync(l => l.PreviousLeaseId == current.Id && l.Status != LeaseStatus.Cancelled, ct))
            throw new DomainException("This lease already has a renewal.", "renewal_exists");

        var start = request.StartDate ?? current.EndDate.AddDays(1);
        if (start <= current.EndDate) throw new DomainException("A renewal must start after the current lease ends.");
        var number = (await db.Leases.MaxAsync(l => (int?)l.Number, ct) ?? 0) + 1;
        var renewal = new Lease(current.OrganizationId, number, current.PropertyId, current.UnitId,
            new LeaseTerms(start, request.EndDate, request.RentAmount, current.Currency, current.BillingFrequency,
                current.DueDaysBeforePeriod, current.GraceDays, current.NoticePeriodDays, current.HoldoverRentAmount,
                current.RenewalTerms, request.Notes),
            current.Id);
        renewal.SetParties(current.Tenants.OrderByDescending(t => t.IsPrimary).Select(t => t.TenantId).ToList(),
            current.Guarantors.Select(g => g.GuarantorId).ToList());
        // Move-in charges such as agency fee and deposit don't repeat; charges billed every period carry over.
        renewal.SetCharges(current.Charges.Where(c => c.Recurrence == ChargeRecurrence.EachPeriod)
            .Select(c => new LeaseChargeInput(c.Type, c.Description, c.Amount, c.Recurrence)).ToList());
        db.Leases.Add(renewal);
        audit.Record("lease.renewal_drafted", nameof(Lease), renewal.Id.ToString(), details: new
        {
            Renews = current.Reference, renewal.Reference, OldRent = current.RentAmount, NewRent = renewal.RentAmount,
            renewal.StartDate, renewal.EndDate,
        });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(renewal, ct);
    }

    /// <summary>
    /// Brings every leased unit's occupancy up to date for today, for the current organization. Run daily, so a lease
    /// that starts or runs out moves its unit to occupied or vacant without anyone touching it.
    /// </summary>
    public async Task<int> RefreshOccupancyAsync(CancellationToken ct)
    {
        var unitIds = await db.Leases.Where(l => l.Status != LeaseStatus.Draft && l.Status != LeaseStatus.Cancelled)
            .Select(l => l.UnitId).Distinct().ToListAsync(ct);
        var changed = await RefreshUnitsAsync(unitIds, [], ct);
        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }

    // Helpers

    private IQueryable<Lease> Filter(IQueryable<Lease> query, LeaseSearch s, DateOnly today)
    {
        if (s.PropertyId is { } propertyId) query = query.Where(l => l.PropertyId == propertyId);
        if (s.UnitId is { } unitId) query = query.Where(l => l.UnitId == unitId);
        if (s.TenantId is { } tenantId) query = query.Where(l => l.Tenants.Any(t => t.TenantId == tenantId));
        if (s.EndingWithinDays is { } days)
        {
            var limit = today.AddDays(Math.Clamp(days, 0, 3650));
            query = query.Where(l => l.Status == LeaseStatus.Active && l.EndDate <= limit);
        }
        if (s.Stage is { } stage)
        {
            var expiringFrom = today.AddDays(Lease.ExpiringWithinDays);
            query = stage switch
            {
                LeaseStage.Draft => query.Where(l => l.Status == LeaseStatus.Draft),
                LeaseStage.Upcoming => query.Where(l => l.Status == LeaseStatus.Active && l.StartDate > today),
                LeaseStage.Active => query.Where(l => l.Status == LeaseStatus.Active && l.StartDate <= today && l.EndDate >= expiringFrom),
                LeaseStage.Expiring => query.Where(l => l.Status == LeaseStatus.Active && l.StartDate <= today
                                                        && l.EndDate >= today && l.EndDate < expiringFrom),
                LeaseStage.HoldingOver => query.Where(l => l.Status == LeaseStatus.Active && l.EndDate < today),
                LeaseStage.Renewed => query.Where(l => l.Status == LeaseStatus.Renewed),
                LeaseStage.Ended => query.Where(l => l.Status == LeaseStatus.Ended),
                LeaseStage.Terminated => query.Where(l => l.Status == LeaseStatus.Terminated),
                LeaseStage.Cancelled => query.Where(l => l.Status == LeaseStatus.Cancelled),
                _ => throw new DomainException("Lease stage is not valid."),
            };
        }
        if (!string.IsNullOrWhiteSpace(s.Search))
        {
            var term = s.Search.Trim();
            var match = ReferencePattern().Match(term);
            if (match.Success)
            {
                var number = int.Parse(match.Groups[1].Value);
                query = query.Where(l => l.Number == number);
            }
            else
            {
                query = query.Where(l =>
                    l.Tenants.Any(t => db.Tenants.Any(x => x.Id == t.TenantId && x.Name.Contains(term)))
                    || db.Units.Any(u => u.Id == l.UnitId && u.Code.Contains(term))
                    || db.Properties.Any(p => p.Id == l.PropertyId && (p.Name.Contains(term) || p.Code.Contains(term))));
            }
        }
        return query;
    }

    [GeneratedRegex(@"^(?:LSE-?)?0*(\d{1,9})$", RegexOptions.IgnoreCase)]
    private static partial Regex ReferencePattern();

    private async Task<List<LeaseSummaryDto>> Summaries(IQueryable<Lease> query, DateOnly today, CancellationToken ct)
    {
        var rows = await query.Select(l => new
        {
            Lease = l,
            Property = db.Properties.Where(p => p.Id == l.PropertyId).Select(p => new { p.Code, p.Name }).First(),
            UnitCode = db.Units.Where(u => u.Id == l.UnitId).Select(u => u.Code).First(),
            Primary = l.Tenants.Where(t => t.IsPrimary)
                .Select(t => new { t.TenantId, Name = db.Tenants.Where(x => x.Id == t.TenantId).Select(x => x.Name).First() })
                .FirstOrDefault(),
            TenantCount = l.Tenants.Count(),
            RenewalId = db.Leases.Where(r => r.PreviousLeaseId == l.Id && r.Status != LeaseStatus.Cancelled)
                .Select(r => (Guid?)r.Id).FirstOrDefault(),
        }).ToListAsync(ct);
        return rows.Select(r => new LeaseSummaryDto(r.Lease.Id, r.Lease.Reference, r.Lease.PropertyId, r.Property.Code,
            r.Property.Name, r.Lease.UnitId, r.UnitCode, r.Primary?.TenantId, r.Primary?.Name, r.TenantCount,
            r.Lease.StartDate, r.Lease.EndDate, r.Lease.RentAmount, r.Lease.Currency, r.Lease.BillingFrequency,
            r.Lease.Status, r.Lease.StageOn(today), DaysToExpiry(r.Lease, today), r.RenewalId)).ToList();
    }

    private static int? DaysToExpiry(Lease l, DateOnly today) =>
        l.Status == LeaseStatus.Active ? l.EndDate.DayNumber - today.DayNumber : null;

    private async Task<Lease> LoadAsync(Guid id, CancellationToken ct) =>
        await VisibleLeases().Include(l => l.Tenants).Include(l => l.Guarantors).Include(l => l.Charges)
            .SingleOrDefaultAsync(l => l.Id == id, ct)
        ?? throw new NotFoundException("Lease");

    private async Task<PropertyUnit> LoadUnitAsync(Guid unitId, CancellationToken ct)
    {
        var unit = await access.VisibleUnits().SingleOrDefaultAsync(u => u.Id == unitId, ct) ?? throw new NotFoundException("Unit");
        if (unit.IsArchived) throw new DomainException("This unit is archived.", "archived");
        return unit;
    }

    private async Task ApplyPartiesAndChargesAsync(Lease lease, SaveLeaseRequest request, CancellationToken ct)
    {
        var tenantIds = request.TenantIds ?? [];
        var guarantorIds = request.GuarantorIds ?? [];
        var found = await db.Tenants.CountAsync(t => tenantIds.Contains(t.Id), ct);
        if (found != tenantIds.Distinct().Count()) throw new NotFoundException("Tenant");
        var guarantorOwners = await db.Guarantors.Where(g => guarantorIds.Contains(g.Id))
            .Select(g => new { g.Id, g.TenantId }).ToListAsync(ct);
        if (guarantorOwners.Count != guarantorIds.Distinct().Count()) throw new NotFoundException("Guarantor");
        if (guarantorOwners.Any(g => !tenantIds.Contains(g.TenantId)))
            throw new DomainException("Each guarantor must belong to one of the lease's tenants.");
        lease.SetParties(tenantIds, guarantorIds);
        lease.SetCharges(request.Charges ?? []);
    }

    private async Task EnsurePartiesStillValidAsync(Lease lease, CancellationToken ct)
    {
        var tenantIds = lease.Tenants.Select(t => t.TenantId).ToList();
        if (await db.Tenants.CountAsync(t => tenantIds.Contains(t.Id), ct) != tenantIds.Count)
            throw new NotFoundException("Tenant");
    }

    /// <summary>A unit can't have two leases holding it on the same day. Drafts and cancelled leases don't count.</summary>
    private async Task EnsureNoOverlapAsync(Lease lease, CancellationToken ct)
    {
        var today = clock.TodayInNigeria();
        var others = await db.Leases.Where(l => l.UnitId == lease.UnitId && l.Id != lease.Id && l.Id != lease.PreviousLeaseId
                                                && l.Status != LeaseStatus.Draft && l.Status != LeaseStatus.Cancelled
                                                && l.StartDate <= lease.EndDate)
            .ToListAsync(ct);
        var clash = others.FirstOrDefault(o => o.BlocksUnitBetween(lease.StartDate, lease.EndDate, today));
        if (clash is not null)
        {
            var until = clash.HoldsUnitUntil(today);
            throw new DomainException(until is null
                    ? $"This unit is still held by lease {clash.Reference}, which has passed its end date. End that lease first."
                    : $"These dates overlap lease {clash.Reference} ({clash.StartDate:d MMM yyyy} to {until:d MMM yyyy}).",
                "lease_overlap");
        }
    }

    /// <summary>Recomputes occupancy for the given units. <paramref name="tracked"/> are leases already changed in memory.</summary>
    private async Task<int> RefreshUnitsAsync(IReadOnlyCollection<Guid> unitIds, IReadOnlyCollection<Lease> tracked, CancellationToken ct)
    {
        if (unitIds.Count == 0) return 0;
        var today = clock.TodayInNigeria();
        var units = await db.Units.Where(u => unitIds.Contains(u.Id)).ToListAsync(ct);
        var trackedIds = tracked.Select(l => l.Id).ToHashSet();
        var stored = await db.Leases.Where(l => unitIds.Contains(l.UnitId) && !trackedIds.Contains(l.Id)
                                                && l.Status != LeaseStatus.Draft && l.Status != LeaseStatus.Cancelled)
            .ToListAsync(ct);
        var leases = stored.Concat(tracked).Where(l => l.Status is not (LeaseStatus.Draft or LeaseStatus.Cancelled)).ToList();
        var changed = 0;
        foreach (var unit in units)
        {
            var mine = leases.Where(l => l.UnitId == unit.Id).ToList();
            var heldToday = mine.Any(l => l.BlocksUnitBetween(today, today, today));
            var heldLater = mine.Any(l => l.Status == LeaseStatus.Active && l.StartDate > today);
            var before = unit.OccupancyStatus;
            if (!unit.ApplyLeaseOccupancy(heldToday, heldLater)) continue;
            changed++;
            audit.Record("unit.occupancy_changed", nameof(PropertyUnit), unit.Id.ToString(),
                details: new { From = before, To = unit.OccupancyStatus, Source = "leases" });
        }
        return changed;
    }

    private async Task<LeaseDetailDto> ToDetailAsync(Lease l, CancellationToken ct)
    {
        var today = clock.TodayInNigeria();
        var property = await db.Properties.Where(p => p.Id == l.PropertyId).Select(p => new { p.Code, p.Name }).SingleAsync(ct);
        var unitCode = await db.Units.Where(u => u.Id == l.UnitId).Select(u => u.Code).SingleAsync(ct);
        var tenantIds = l.Tenants.Select(t => t.TenantId).ToList();
        var tenants = await db.Tenants.Where(t => tenantIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var guarantorIds = l.Guarantors.Select(g => g.GuarantorId).ToList();
        var guarantors = await db.Guarantors.Where(g => guarantorIds.Contains(g.Id)).ToListAsync(ct);
        var previous = l.PreviousLeaseId is { } pid
            ? await db.Leases.Where(x => x.Id == pid).Select(x => new { x.Id, x.Number, x.Status, x.StartDate, x.EndDate, x.RentAmount }).SingleOrDefaultAsync(ct)
            : null;
        var renewal = await db.Leases.Where(x => x.PreviousLeaseId == l.Id && x.Status != LeaseStatus.Cancelled)
            .Select(x => new { x.Id, x.Number, x.Status, x.StartDate, x.EndDate, x.RentAmount }).FirstOrDefaultAsync(ct);
        var activatedBy = l.ActivatedByUserId is { } uid ? (await users.GetAsync([uid], ct)).GetValueOrDefault(uid)?.FullName : null;

        return new LeaseDetailDto(l.Id, l.Reference, l.PropertyId, property.Code, property.Name, l.UnitId, unitCode,
            l.Status, l.StageOn(today), DaysToExpiry(l, today), l.StartDate, l.EndDate, l.RentAmount, l.Currency,
            l.BillingFrequency, l.DueDaysBeforePeriod, l.GraceDays, l.NoticePeriodDays, l.HoldoverRentAmount,
            l.RenewalTerms, l.Notes,
            l.Tenants.OrderByDescending(t => t.IsPrimary).Select(t => tenants.TryGetValue(t.TenantId, out var x)
                ? new LeaseTenantDto(t.TenantId, x.Name, x.Phone, x.Email, t.IsPrimary)
                : new LeaseTenantDto(t.TenantId, "(unknown tenant)", null, null, t.IsPrimary)).ToList(),
            guarantors.OrderBy(g => g.FullName)
                .Select(g => new LeaseGuarantorDto(g.Id, g.TenantId, g.FullName, g.Phone, g.SignedFormReceived)).ToList(),
            l.Charges.Select(c => new LeaseChargeDto(c.Id, c.Type, c.Description, c.Amount, c.Recurrence)).ToList(),
            previous is null ? null : new LinkedLeaseDto(previous.Id, Lease.FormatReference(previous.Number), previous.Status,
                previous.StartDate, previous.EndDate, previous.RentAmount),
            renewal is null ? null : new LinkedLeaseDto(renewal.Id, Lease.FormatReference(renewal.Number), renewal.Status,
                renewal.StartDate, renewal.EndDate, renewal.RentAmount),
            l.CreatedAtUtc, l.ActivatedAtUtc, activatedBy, l.MoveOutDate, l.EndReason);
    }

    private static object Snapshot(Lease l) => new
    {
        l.Reference, l.UnitId, l.StartDate, l.EndDate, l.RentAmount, l.Currency, l.BillingFrequency,
        Tenants = l.Tenants.Select(t => t.TenantId), Guarantors = l.Guarantors.Select(g => g.GuarantorId),
        Charges = l.Charges.Select(c => new { c.Type, c.Amount, c.Recurrence }),
    };
}
