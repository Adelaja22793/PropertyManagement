using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Auditing;

namespace PropertyManagement.Application.Auditing;

public sealed record AuditLogDto(Guid Id, DateTime TimestampUtc, Guid? UserId, string? UserEmail, string Action,
    string? ResourceType, string? ResourceId, AuditOutcome Outcome, string? Details);

public sealed class AuditQueryService(IAppDbContext db, ICurrentOrganization currentOrganization, IUserDirectory users)
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(PageRequest page, CancellationToken ct)
    {
        // Audit logs are not query-filtered (some have no organization), so scope them explicitly.
        var organizationId = currentOrganization.RequireOrganizationId();
        var query = db.AuditLogs.Where(a => a.OrganizationId == organizationId);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.TimestampUtc)
            .Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        var people = await users.GetAsync(rows.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value), ct);
        var items = rows.Select(a => new AuditLogDto(a.Id, a.TimestampUtc, a.UserId,
            a.UserId is { } id ? people.GetValueOrDefault(id)?.Email : null,
            a.Action, a.ResourceType, a.ResourceId, a.Outcome, a.Details)).ToList();
        return new PagedResult<AuditLogDto>(items, page.SafePage, page.SafePageSize, total);
    }
}
