using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Api.Infrastructure;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Domain.Auditing;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Organizations;
using PropertyManagement.Domain.Organizations;
using PropertyManagement.Domain.Platform;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Api.Middleware;

/// <summary>
/// Sets the request's active organization (design 4.3, rules 1 and 2). The organization comes from the
/// {organizationId} route value or the X-Organization-Id header, and is accepted only if the signed-in user
/// has an active membership in it, checked against the database on every request so revocation is immediate.
/// </summary>
public sealed class OrganizationContextMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Organization-Id";
    public const string RouteKey = "organizationId";

    public async Task InvokeAsync(HttpContext context, AppDbContext db, ICurrentOrganization currentOrganization,
        ICurrentUser currentUser, IAuditLogger audit, TwoFactorPolicy twoFactor)
    {
        // Account endpoints act for the person. They still record the selected organization in audit entries when the
        // person has access to it, but never refuse because of it.
        var accountPath = context.Request.Path.StartsWithSegments("/api/v1/auth");
        var fromRoute = context.Request.RouteValues[RouteKey]?.ToString();
        var fromHeader = context.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(fromRoute) && string.IsNullOrEmpty(fromHeader))
        {
            await next(context);
            return;
        }

        if (!string.IsNullOrEmpty(fromRoute) && !string.IsNullOrEmpty(fromHeader) &&
            !string.Equals(fromRoute, fromHeader, StringComparison.OrdinalIgnoreCase))
        {
            await Problem(context, 400, "The organization in the URL and the X-Organization-Id header differ.", "organization_mismatch");
            return;
        }

        // Unauthenticated requests continue without an organization; authorization then answers 401.
        if (currentUser.UserId is not { } userId)
        {
            await next(context);
            return;
        }

        if (!Guid.TryParse(string.IsNullOrEmpty(fromRoute) ? fromHeader : fromRoute, out var organizationId))
        {
            await Problem(context, 400, "The organization ID is not valid.", "organization_invalid");
            return;
        }

        var access = await db.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.UserId == userId && m.Status == MembershipStatus.Active)
            .Join(db.Organizations, m => m.OrganizationId, o => o.Id, (m, o) => new
            {
                MembershipId = m.Id,
                OrganizationStatus = o.Status,
                Roles = m.Roles.Select(r => r.RoleName).ToList(),
                Branches = m.BranchScopes.Select(s => s.BranchId).ToList(),
                m.TwoFactorRequiredSinceUtc,
                CancelledAtUtc = db.Subscriptions.Where(s => s.OrganizationId == o.Id).OrderByDescending(s => s.StartedAtUtc)
                    .Select(s => s.CancelledAtUtc).FirstOrDefault(),
                TwoFactorEnabled = db.Users.Where(u => u.Id == userId).Select(u => u.TwoFactorEnabled).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(context.RequestAborted);

        if (access is null && await SupportGrantAsync(db, userId, organizationId, context.RequestAborted))
        {
            // Platform support with an approved grant (design 4.7): read-only, and every request is in the company's audit log.
            var readOnly = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !accountPath;
            // Loading their own account isn't a look at the company's data, so it isn't logged.
            if (!accountPath)
                await audit.RecordNowAsync(readOnly ? "support.change_blocked" : "support.viewed", nameof(Organization), organizationId.ToString(),
                    readOnly ? AuditOutcome.Denied : AuditOutcome.Success, new { context.Request.Method, context.Request.Path },
                    organizationId: organizationId, userId: userId, ct: context.RequestAborted);
            if (readOnly)
            {
                await Problem(context, 403, "Support access is read-only.", "support_read_only");
                return;
            }
            currentOrganization.Set(organizationId, null, [Roles.PlatformSupport], []);
            await next(context);
            return;
        }

        // A cancelled company is closed, except that its admins can download its data for a while (design 4.8).
        var cancelled = access?.OrganizationStatus == OrganizationStatus.Cancelled;
        if (cancelled && !accountPath && ExportStillOpen(context, access!.CancelledAtUtc))
            cancelled = false;
        else if (cancelled && !accountPath)
        {
            await Problem(context, 403, "This company's subscription has ended.", "organization_cancelled");
            return;
        }

        // Same answer whether the organization does not exist or the user is not a member: no probing.
        if (access is null || cancelled)
        {
            // Someone whose access just ended (removed, or a support grant ran out) can still load their account.
            if (accountPath)
            {
                await next(context);
                return;
            }

            await audit.RecordNowAsync("organization.access_denied", nameof(Organization), organizationId.ToString(),
                AuditOutcome.Denied, new { context.Request.Path }, organizationId: null, userId: userId,
                ct: context.RequestAborted);
            await Problem(context, 403, "You do not have access to this organization.", "organization_forbidden");
            return;
        }

        if (access.OrganizationStatus == OrganizationStatus.Suspended && !HttpMethods.IsGet(context.Request.Method) && !accountPath)
        {
            await Problem(context, 403, "This organization's subscription is suspended, so it is read-only.", "organization_suspended");
            return;
        }

        // Admins, accountants and finance approvers past their grace period must turn on two-step sign-in first.
        // Account endpoints stay open so they can.
        if (!access.TwoFactorEnabled && twoFactor.IsOverdue(access.TwoFactorRequiredSinceUtc, DateTime.UtcNow) && !accountPath)
        {
            await audit.RecordNowAsync("organization.access_denied", nameof(Organization), organizationId.ToString(),
                AuditOutcome.Denied, new { context.Request.Path, reason = "two_factor_required" }, organizationId: organizationId,
                userId: userId, ct: context.RequestAborted);
            await Problem(context, 403, "Turn on two-step sign-in to keep using this organization.", "two_factor_required");
            return;
        }

        var customRoles = await RoleCatalog.LoadAsync(db.CustomRoles.Where(r => r.OrganizationId == organizationId), access.Roles,
            context.RequestAborted);
        currentOrganization.Set(organizationId, access.MembershipId, access.Roles, access.Branches, customRoles);
        await next(context);
    }

    /// <summary>An approved, unexpired grant for a user who is still platform support, in an organization that is not cancelled.</summary>
    private static Task<bool> SupportGrantAsync(AppDbContext db, Guid userId, Guid organizationId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return db.SupportGrants.AnyAsync(g => g.OrganizationId == organizationId && g.RequestedByUserId == userId
            && g.Status == SupportGrantStatus.Approved && g.ExpiresAtUtc > now
            && db.Users.Any(u => u.Id == userId && u.IsPlatformAdmin)
            && db.Organizations.Any(o => o.Id == organizationId && o.Status != OrganizationStatus.Cancelled), ct);
    }

    /// <summary>Reading the data export or the subscription, while the export window after cancelling is open.</summary>
    private static bool ExportStillOpen(HttpContext context, DateTime? cancelledAtUtc) =>
        cancelledAtUtc is { } at && at.AddDays(OrganizationSubscription.ExportDays) > DateTime.UtcNow
        && HttpMethods.IsGet(context.Request.Method)
        && (context.Request.Path.StartsWithSegments("/api/v1/exports") || context.Request.Path.StartsWithSegments("/api/v1/subscription"));

    private static Task Problem(HttpContext context, int status, string title, string code)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status, Title = title, Extensions = { ["code"] = code },
        });
    }
}
