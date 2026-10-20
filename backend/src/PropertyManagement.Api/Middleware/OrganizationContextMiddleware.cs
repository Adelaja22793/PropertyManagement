using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Domain.Auditing;
using PropertyManagement.Domain.Organizations;
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
        ICurrentUser currentUser, IAuditLogger audit)
    {
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
            })
            .SingleOrDefaultAsync(context.RequestAborted);

        // Same answer whether the organization does not exist or the user is not a member: no probing.
        if (access is null || access.OrganizationStatus == OrganizationStatus.Cancelled)
        {
            await audit.RecordNowAsync("organization.access_denied", nameof(Organization), organizationId.ToString(),
                AuditOutcome.Denied, new { context.Request.Path }, organizationId: null, userId: userId,
                ct: context.RequestAborted);
            await Problem(context, 403, "You do not have access to this organization.", "organization_forbidden");
            return;
        }

        if (access.OrganizationStatus == OrganizationStatus.Suspended && !HttpMethods.IsGet(context.Request.Method))
        {
            await Problem(context, 403, "This organization's subscription is suspended, so it is read-only.", "organization_suspended");
            return;
        }

        currentOrganization.Set(organizationId, access.MembershipId, access.Roles, access.Branches);
        await next(context);
    }

    private static Task Problem(HttpContext context, int status, string title, string code)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status, Title = title, Extensions = { ["code"] = code },
        });
    }
}
