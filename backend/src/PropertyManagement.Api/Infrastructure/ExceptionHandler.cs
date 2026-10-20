using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Common;

namespace PropertyManagement.Api.Infrastructure;

/// <summary>Turns known exceptions into RFC 9457 problem responses. Anything else is a 500 with no detail.</summary>
public sealed class ExceptionHandler(IProblemDetailsService problems, ILogger<ExceptionHandler> logger) : IExceptionHandler
{
    private static readonly HashSet<string> ConflictCodes =
    [
        "already_member", "invitation_pending", "duplicate_branch", "last_admin", "duplicate_code", "duplicate_owner",
        "duplicate_tenant", "approval_pending", "already_decided", "agreement_overlap", "portal_exists", "unit_occupied",
        "archived",
    ];

    /// <summary>Rules about who may act, rather than about the data.</summary>
    private static readonly HashSet<string> ForbiddenCodes = ["maker_checker"];

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, code) = exception switch
        {
            DomainException d => (ConflictCodes.Contains(d.Code) ? 409 : ForbiddenCodes.Contains(d.Code) ? 403 : 400, d.Message, d.Code),
            NotFoundException n => (404, n.Message, "not_found"),
            ForbiddenException f => (403, f.Message, "forbidden"),
            OrganizationContextRequiredException o => (400, o.Message, "organization_required"),
            DbUpdateConcurrencyException => (409, "This record was changed by someone else. Reload and try again.", "concurrency"),
            CrossOrganizationWriteException => (403, "You do not have access to this record.", "forbidden"),
            UnauthorizedAccessException => (401, "Sign in to continue.", "unauthenticated"),
            _ => (500, "Something went wrong. Please try again.", "server_error"),
        };

        if (exception is CrossOrganizationWriteException)
            logger.LogError(exception, "Blocked cross-organization write on {Path}", context.Request.Path);
        else if (status == 500)
            logger.LogError(exception, "Unhandled exception on {Path}", context.Request.Path);

        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } },
        });
    }
}
