using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Api.Contracts;
using PropertyManagement.Api.Infrastructure;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Organizations;
using PropertyManagement.Application.Platform;
using PropertyManagement.Domain.Auditing;
using PropertyManagement.Domain.Organizations;
using PropertyManagement.Infrastructure.Identity;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class AuthController(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    AppDbContext db,
    OrganizationService organizations,
    MembershipService memberships,
    IAuditLogger audit,
    IEmailSender email,
    ICurrentUser currentUser,
    TwoFactorPolicy twoFactor,
    SupportAccessService support) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<MeResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var joining = !string.IsNullOrWhiteSpace(request.InvitationToken);
        if (!joining && string.IsNullOrWhiteSpace(request.OrganizationName))
            return ValidationProblem("Enter your company name, or use an invitation code to join an existing company.");

        AppUser? user = null;
        // User, organization and membership are created together or not at all.
        var strategy = db.Database.CreateExecutionStrategy();
        var failure = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            user = new AppUser { UserName = request.Email.Trim(), Email = request.Email.Trim(), FullName = request.FullName.Trim() };
            var created = await users.CreateAsync(user, request.Password);
            if (!created.Succeeded) return created.Errors.ToList();

            if (joining)
                await memberships.AcceptAsync(request.InvitationToken!, user.Id, user.Email!, ct);
            else
                await organizations.CreateAsync(user.Id, request.OrganizationName!,
                    request.OrganizationType ?? OrganizationType.ManagementCompany, ct);

            audit.Record("user.registered", nameof(AppUser), user.Id.ToString(), userId: user.Id);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return null;
        });

        if (failure is not null)
        {
            foreach (var error in failure) ModelState.AddModelError(error.Code, error.Description);
            return ValidationProblem(ModelState);
        }

        await signIn.SignInAsync(user!, isPersistent: false);
        return Ok(await BuildMeAsync(user!, ct));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            await audit.RecordNowAsync("user.login", outcome: AuditOutcome.Failed, details: new { reason = "unknown_email" }, ct: ct);
            return Problem(statusCode: 401, title: "Email or password is incorrect.");
        }

        var result = await signIn.PasswordSignInAsync(user, request.Password, request.RememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            await audit.RecordNowAsync("user.login", nameof(AppUser), user.Id.ToString(), AuditOutcome.Denied,
                new { reason = "locked_out" }, userId: user.Id, ct: ct);
            return Problem(statusCode: 423, title: "Too many failed attempts. Try again in 15 minutes or reset your password.");
        }
        if (result.RequiresTwoFactor)
        {
            // Identity has set a short-lived cookie naming this user; the code must come next, from this browser.
            await audit.RecordNowAsync("user.login", nameof(AppUser), user.Id.ToString(), details: new { step = "password" },
                userId: user.Id, ct: ct);
            return Ok(new TwoFactorChallengeResponse(true));
        }
        if (!result.Succeeded)
        {
            await audit.RecordNowAsync("user.login", nameof(AppUser), user.Id.ToString(), AuditOutcome.Failed,
                new { reason = "wrong_password" }, userId: user.Id, ct: ct);
            return Problem(statusCode: 401, title: "Email or password is incorrect.");
        }

        await audit.RecordNowAsync("user.login", nameof(AppUser), user.Id.ToString(), userId: user.Id, ct: ct);
        return Ok(await BuildMeAsync(user, ct));
    }

    /// <summary>The second step: a 6-digit code from the authenticator app.</summary>
    [HttpPost("login/two-factor")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginTwoFactor(TwoFactorLoginRequest request, CancellationToken ct)
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null) return SignInExpired();
        var result = await signIn.TwoFactorAuthenticatorSignInAsync(CleanCode(request.Code), request.RememberMe, rememberClient: false);
        return await FinishSecondStepAsync(user, result, "authenticator", ct);
    }

    /// <summary>The second step with a one-time recovery code, for when the phone is lost.</summary>
    [HttpPost("login/recovery-code")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginRecoveryCode(RecoveryCodeLoginRequest request, CancellationToken ct)
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null) return SignInExpired();
        var result = await signIn.TwoFactorRecoveryCodeSignInAsync(request.Code.Replace(" ", "").Trim());
        if (result.Succeeded)
        {
            var left = await users.CountRecoveryCodesAsync(user);
            await email.SendAsync(user.Email!, "A recovery code was used to sign in",
                $"Someone signed in to your account with a recovery code. You have {left} left.\n\n" +
                "If this wasn't you, reset your password and turn two-step sign-in off and on again to get a new authenticator key.", ct);
        }
        return await FinishSecondStepAsync(user, result, "recovery_code", ct);
    }

    private async Task<IActionResult> FinishSecondStepAsync(AppUser user, Microsoft.AspNetCore.Identity.SignInResult result, string method,
        CancellationToken ct)
    {
        if (result.IsLockedOut)
        {
            await audit.RecordNowAsync("user.login", nameof(AppUser), user.Id.ToString(), AuditOutcome.Denied,
                new { reason = "locked_out", method }, userId: user.Id, ct: ct);
            return Problem(statusCode: 423, title: "Too many failed attempts. Try again in 15 minutes.");
        }
        if (!result.Succeeded)
        {
            await audit.RecordNowAsync("user.login", nameof(AppUser), user.Id.ToString(), AuditOutcome.Failed,
                new { reason = "wrong_code", method }, userId: user.Id, ct: ct);
            return Problem(statusCode: 401, title: method == "authenticator"
                ? "That code isn't right. Use the latest code from your authenticator app."
                : "That recovery code isn't right or has already been used.");
        }
        await audit.RecordNowAsync("user.login", nameof(AppUser), user.Id.ToString(), details: new { method }, userId: user.Id, ct: ct);
        return Ok(await BuildMeAsync(user, ct));
    }

    private ObjectResult SignInExpired() => Problem(statusCode: 401, title: "Your sign-in timed out. Enter your email and password again.",
        extensions: new Dictionary<string, object?> { ["code"] = "two_factor_expired" });

    /// <summary>Authenticator apps show codes as "123 456".</summary>
    internal static string CleanCode(string code) => code.Replace(" ", "").Replace("-", "").Trim();

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await signIn.SignOutAsync();
        await audit.RecordNowAsync("user.logout", nameof(AppUser), currentUser.UserId?.ToString(), ct: ct);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [DisableRateLimiting]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken ct)
    {
        var user = await users.FindByIdAsync(currentUser.RequireUserId().ToString());
        return user is null ? Unauthorized() : Ok(await BuildMeAsync(user, ct));
    }

    /// <summary>Always answers 202, so the response does not reveal which emails have accounts.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is not null)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            await email.SendAsync(user.Email!, "Reset your password",
                $"Use this code to reset your password: {token}", ct);
            await audit.RecordNowAsync("user.password_reset_requested", nameof(AppUser), user.Id.ToString(), userId: user.Id, ct: ct);
        }
        return Accepted();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null) return ValidationProblem("This reset code is not valid.");

        var result = await users.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(error.Code, error.Description);
            return ValidationProblem(ModelState);
        }
        // Resetting changes the security stamp, which signs the user out everywhere else.
        await users.ResetAccessFailedCountAsync(user);
        await audit.RecordNowAsync("user.password_reset", nameof(AppUser), user.Id.ToString(), userId: user.Id, ct: ct);
        return NoContent();
    }

    private async Task<MeResponse> BuildMeAsync(AppUser user, CancellationToken ct)
    {
        var own = await organizations.ListForUserAsync(user.Id, ct);
        // Platform support also sees the organizations an approved grant opens to them, unless they are a member there.
        var viaSupport = user.IsPlatformAdmin
            ? (await support.ListOpenOrganizationsAsync(user.Id, ct)).Where(s => own.All(o => o.Id != s.Id))
            : [];
        return new(user.Id, user.Email!, user.FullName, user.IsPlatformAdmin, [.. own, .. viaSupport],
            user.TwoFactorEnabled, twoFactor.GraceDays);
    }

    private ActionResult ValidationProblem(string message)
    {
        ModelState.AddModelError("", message);
        return ValidationProblem(ModelState);
    }
}
