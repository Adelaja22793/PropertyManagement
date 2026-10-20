using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Application.Organizations;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Organizations;
using PropertyManagement.Domain.Platform;

namespace PropertyManagement.Application.Platform;

public sealed record RequestSupportAccessRequest(string Organization, string Reason, int Hours);

/// <summary>A support grant as the support person or the organization's admin sees it.</summary>
public sealed record SupportGrantDto(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string RequestedByName,
    string RequestedByEmail,
    string Reason,
    int Hours,
    /// <summary>Pending, Active, Declined, Revoked or Expired.</summary>
    string Status,
    DateTime RequestedAtUtc,
    string? DecidedByName,
    DateTime? DecidedAtUtc,
    DateTime? ExpiresAtUtc,
    DateTime? RevokedAtUtc);

/// <summary>
/// Support access grants (design 4.7). Platform support asks for read-only access to one organization; its admin
/// approves, declines or revokes it. Every step lands in that organization's audit log.
/// </summary>
public sealed class SupportAccessService(
    IAppDbContext db,
    ICurrentOrganization currentOrganization,
    ICurrentUser currentUser,
    IUserDirectory users,
    IEmailSender email,
    IAuditLogger audit,
    IClock clock)
{
    /// <summary>Asks an organization, found by its web address name, for access. The caller must already be checked as platform support.</summary>
    public async Task<SupportGrantDto> RequestAsync(RequestSupportAccessRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var slug = (request.Organization ?? "").Trim().ToLowerInvariant();
        var organization = await db.Organizations
            .Where(o => o.Slug == slug && o.Status != OrganizationStatus.Cancelled)
            .Select(o => new { o.Id, o.Name })
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Organization");

        var now = clock.UtcNow;
        var open = await db.SupportGrants.AnyAsync(g => g.OrganizationId == organization.Id && g.RequestedByUserId == userId
            && (g.Status == SupportGrantStatus.Pending || (g.Status == SupportGrantStatus.Approved && g.ExpiresAtUtc > now)), ct);
        if (open) throw new DomainException("You already have a request or access open for this organization.", "approval_pending");

        var grant = new SupportGrant(organization.Id, userId, request.Reason, request.Hours, now);
        db.SupportGrants.Add(grant);
        audit.Record("support.requested", nameof(SupportGrant), grant.Id.ToString(),
            details: new { grant.Hours, grant.Reason }, organizationId: organization.Id, userId: userId);
        await db.SaveChangesAsync(ct);

        await NotifyAdminsAsync(organization.Id, organization.Name, grant, ct);
        return (await ToDtosAsync([grant], ct)).Single();
    }

    /// <summary>Organizations the user can open now through an approved grant, shown alongside their own.</summary>
    public async Task<IReadOnlyList<OrganizationSummaryDto>> ListOpenOrganizationsAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var rows = await db.SupportGrants
            .Where(g => g.RequestedByUserId == userId && g.Status == SupportGrantStatus.Approved && g.ExpiresAtUtc > now)
            .Join(db.Organizations.Where(o => o.Status != OrganizationStatus.Cancelled), g => g.OrganizationId, o => o.Id,
                (g, o) => new { o.Id, o.Name, o.Slug, o.Type, o.Status, g.ExpiresAtUtc })
            .ToListAsync(ct);
        string[] roles = [Roles.PlatformSupport];
        var permissions = Roles.PermissionsFor(roles).Order().ToList();
        return rows.GroupBy(r => r.Id).Select(g => g.MaxBy(r => r.ExpiresAtUtc)!)
            .Select(r => new OrganizationSummaryDto(r.Id, r.Name, r.Slug, r.Type, r.Status, roles, permissions,
                SupportAccessUntilUtc: Utc(r.ExpiresAtUtc)))
            .OrderBy(r => r.Name).ToList();
    }

    /// <summary>The caller's own requests, newest first.</summary>
    public async Task<IReadOnlyList<SupportGrantDto>> ListMineAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var grants = await db.SupportGrants.Where(g => g.RequestedByUserId == userId)
            .OrderByDescending(g => g.RequestedAtUtc).Take(100).ToListAsync(ct);
        return await ToDtosAsync(grants, ct);
    }

    /// <summary>Requests for the active organization, newest first.</summary>
    public async Task<IReadOnlyList<SupportGrantDto>> ListForOrganizationAsync(CancellationToken ct)
    {
        var organizationId = currentOrganization.RequireOrganizationId();
        var grants = await db.SupportGrants.Where(g => g.OrganizationId == organizationId)
            .OrderByDescending(g => g.RequestedAtUtc).Take(100).ToListAsync(ct);
        return await ToDtosAsync(grants, ct);
    }

    public Task<SupportGrantDto> ApproveAsync(Guid id, CancellationToken ct) =>
        DecideAsync(id, "support.approved", (g, user, now) => g.Approve(user, now), ct);

    public Task<SupportGrantDto> DeclineAsync(Guid id, CancellationToken ct) =>
        DecideAsync(id, "support.declined", (g, user, now) => g.Decline(user, now), ct);

    public Task<SupportGrantDto> RevokeAsync(Guid id, CancellationToken ct) =>
        DecideAsync(id, "support.revoked", (g, user, now) => g.Revoke(user, now), ct);

    /// <summary>The support person ends their own access early or withdraws an unanswered request.</summary>
    public async Task<SupportGrantDto> EndOwnAsync(Guid id, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var grant = await db.SupportGrants.SingleOrDefaultAsync(g => g.Id == id && g.RequestedByUserId == userId, ct)
                    ?? throw new NotFoundException("Support request");
        grant.Revoke(userId, clock.UtcNow);
        audit.Record("support.ended", nameof(SupportGrant), grant.Id.ToString(), organizationId: grant.OrganizationId, userId: userId);
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([grant], ct)).Single();
    }

    private async Task<SupportGrantDto> DecideAsync(Guid id, string action, Action<SupportGrant, Guid, DateTime> decide, CancellationToken ct)
    {
        var organizationId = currentOrganization.RequireOrganizationId();
        var userId = currentUser.RequireUserId();
        // Only grants for the active organization: another organization's grant reads as not found.
        var grant = await db.SupportGrants.SingleOrDefaultAsync(g => g.Id == id && g.OrganizationId == organizationId, ct)
                    ?? throw new NotFoundException("Support request");
        decide(grant, userId, clock.UtcNow);
        audit.Record(action, nameof(SupportGrant), grant.Id.ToString(),
            details: new { grant.ExpiresAtUtc }, organizationId: organizationId, userId: userId);
        await db.SaveChangesAsync(ct);

        var people = await users.GetAsync([grant.RequestedByUserId], ct);
        if (people.GetValueOrDefault(grant.RequestedByUserId) is { } requester)
        {
            var organizationName = await db.Organizations.Where(o => o.Id == organizationId).Select(o => o.Name).SingleAsync(ct);
            var outcome = grant.Status switch
            {
                SupportGrantStatus.Approved => $"approved. It lasts until {grant.ExpiresAtUtc:yyyy-MM-dd HH:mm} UTC and is read-only.",
                SupportGrantStatus.Declined => "declined.",
                _ => "ended by the organization.",
            };
            await email.SendAsync(requester.Email, $"Support access to {organizationName}",
                $"Your request for access to {organizationName} was {outcome}", ct);
        }
        return (await ToDtosAsync([grant], ct)).Single();
    }

    private async Task NotifyAdminsAsync(Guid organizationId, string organizationName, SupportGrant grant, CancellationToken ct)
    {
        var adminIds = await db.Memberships.IgnoreQueryFilters()
            .Where(m => m.OrganizationId == organizationId && m.Status == MembershipStatus.Active
                && m.Roles.Any(r => r.RoleName == Roles.OrganizationAdmin))
            .Select(m => m.UserId).ToListAsync(ct);
        var people = await users.GetAsync(adminIds.Append(grant.RequestedByUserId), ct);
        var requester = people.GetValueOrDefault(grant.RequestedByUserId);
        foreach (var adminId in adminIds)
        {
            if (people.GetValueOrDefault(adminId) is not { } admin) continue;
            await email.SendAsync(admin.Email, $"Support has asked to view {organizationName}",
                $"{requester?.FullName ?? "Platform support"} has asked for read-only access to {organizationName} for {grant.Hours} hours.\n\n"
                + $"Reason: {grant.Reason}\n\nNothing is shared until you approve it under Team, Support access. You can end it at any time.", ct);
        }
    }

    private async Task<IReadOnlyList<SupportGrantDto>> ToDtosAsync(IReadOnlyList<SupportGrant> grants, CancellationToken ct)
    {
        var organizationIds = grants.Select(g => g.OrganizationId).Distinct().ToList();
        var names = await db.Organizations.Where(o => organizationIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var people = await users.GetAsync(grants.Select(g => g.RequestedByUserId)
            .Concat(grants.Where(g => g.DecidedByUserId.HasValue).Select(g => g.DecidedByUserId!.Value)), ct);
        var now = clock.UtcNow;
        return grants.Select(g => new SupportGrantDto(
            g.Id, g.OrganizationId, names.GetValueOrDefault(g.OrganizationId, ""),
            people.GetValueOrDefault(g.RequestedByUserId)?.FullName ?? "",
            people.GetValueOrDefault(g.RequestedByUserId)?.Email ?? "",
            g.Reason, g.Hours, StatusOf(g, now), Utc(g.RequestedAtUtc)!.Value,
            g.DecidedByUserId is { } d ? people.GetValueOrDefault(d)?.FullName : null,
            Utc(g.DecidedAtUtc), Utc(g.ExpiresAtUtc), Utc(g.RevokedAtUtc))).ToList();
    }

    public static string StatusOf(SupportGrant g, DateTime now) => g.Status switch
    {
        SupportGrantStatus.Approved => g.IsActive(now) ? "Active" : "Expired",
        var s => s.ToString(),
    };

    private static DateTime? Utc(DateTime? value) => value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
}
