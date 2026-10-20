using Microsoft.AspNetCore.Mvc;

namespace PropertyManagement.Api.Middleware;

/// <summary>
/// CSRF protection for cookie authentication (design 11.1). State-changing API requests must carry
/// a custom header, which a cross-site form or image cannot send. Together with SameSite=Strict cookies
/// this blocks cross-site request forgery. Provider webhooks will be exempt (they are not cookie-authenticated).
/// </summary>
public sealed class CsrfHeaderMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Requested-With";
    public const string HeaderValue = "XMLHttpRequest";

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var safe = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
        if (!safe && context.Request.Path.StartsWithSegments("/api") &&
            context.Request.Headers[HeaderName] != HeaderValue)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = 400,
                Title = $"Missing the {HeaderName} header.",
                Extensions = { ["code"] = "csrf_header_missing" },
            });
            return;
        }
        await next(context);
    }
}
