using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Api.Contracts;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Domain.Organizations;
using PropertyManagement.Infrastructure.Identity;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Api.Controllers;

/// <summary>
/// Two-step sign-in with an authenticator app (design 11.1): set up, confirm with a code, get one-time recovery
/// codes, and turn off when no role needs it. Each change is audited and emailed to the account holder.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/auth/two-factor")]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class TwoFactorController(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    AppDbContext db,
    IAuditLogger audit,
    IEmailSender email,
    ICurrentUser currentUser) : ControllerBase
{
    private const string Issuer = "Property Management";
    private const int RecoveryCodeCount = 10;

    [HttpGet]
    public async Task<ActionResult<TwoFactorStatusResponse>> Status(CancellationToken ct)
    {
        var user = await CurrentAsync();
        return new TwoFactorStatusResponse(user.TwoFactorEnabled, await IsRequiredAsync(user.Id, ct),
            user.TwoFactorEnabled ? await users.CountRecoveryCodesAsync(user) : 0);
    }

    /// <summary>Starts setup with a new secret key. Nothing changes until a code from the app confirms it.</summary>
    [HttpPost("setup")]
    public async Task<ActionResult<TwoFactorSetupResponse>> Setup()
    {
        var user = await CurrentAsync();
        if (user.TwoFactorEnabled) return AlreadyOn();
        await users.ResetAuthenticatorKeyAsync(user);
        // Resetting the key changes the security stamp; keep this browser signed in.
        await signIn.RefreshSignInAsync(user);
        var key = (await users.GetAuthenticatorKeyAsync(user))!;
        var uri = $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(user.Email!)}" +
                  $"?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";
        return new TwoFactorSetupResponse(Group(key), uri);
    }

    [HttpPost("enable")]
    public async Task<ActionResult<RecoveryCodesResponse>> Enable(TwoFactorCodeRequest request, CancellationToken ct)
    {
        var user = await CurrentAsync();
        if (user.TwoFactorEnabled) return AlreadyOn();
        if (await users.GetAuthenticatorKeyAsync(user) is null || !await VerifyAsync(user, request.Code))
            return WrongCode();
        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))!.ToList();
        await signIn.RefreshSignInAsync(user);
        await audit.RecordNowAsync("user.two_factor_enabled", nameof(AppUser), user.Id.ToString(), userId: user.Id, ct: ct);
        await email.SendAsync(user.Email!, "Two-step sign-in is on",
            "Two-step sign-in is now on for your account. You'll need a code from your authenticator app each time you sign in.\n\n" +
            "If you didn't do this, reset your password straight away.", ct);
        return new RecoveryCodesResponse(codes);
    }

    /// <summary>Replaces the recovery codes. Old ones stop working.</summary>
    [HttpPost("recovery-codes")]
    public async Task<ActionResult<RecoveryCodesResponse>> NewRecoveryCodes(TwoFactorCodeRequest request, CancellationToken ct)
    {
        var user = await CurrentAsync();
        if (!user.TwoFactorEnabled) return Problem(statusCode: 409, title: "Two-step sign-in is off.");
        if (!await VerifyAsync(user, request.Code)) return WrongCode();
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))!.ToList();
        await audit.RecordNowAsync("user.two_factor_recovery_codes", nameof(AppUser), user.Id.ToString(), userId: user.Id, ct: ct);
        return new RecoveryCodesResponse(codes);
    }

    [HttpPost("disable")]
    public async Task<IActionResult> Disable(DisableTwoFactorRequest request, CancellationToken ct)
    {
        var user = await CurrentAsync();
        if (!user.TwoFactorEnabled) return NoContent();
        if (await IsRequiredAsync(user.Id, ct))
            return Problem(statusCode: 409, title: "One of your roles needs two-step sign-in, so it can't be turned off.",
                extensions: new Dictionary<string, object?> { ["code"] = "two_factor_required" });
        if (!await users.CheckPasswordAsync(user, request.Password))
            return Problem(statusCode: 400, title: "That password isn't right.");
        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await signIn.RefreshSignInAsync(user);
        await audit.RecordNowAsync("user.two_factor_disabled", nameof(AppUser), user.Id.ToString(), userId: user.Id, ct: ct);
        await email.SendAsync(user.Email!, "Two-step sign-in is off",
            "Two-step sign-in has been turned off for your account.\n\nIf you didn't do this, reset your password straight away.", ct);
        return NoContent();
    }

    private async Task<AppUser> CurrentAsync() =>
        await users.FindByIdAsync(currentUser.RequireUserId().ToString()) ?? throw new UnauthorizedAccessException();

    /// <summary>True when any active membership holds a role that needs two-step sign-in.</summary>
    private Task<bool> IsRequiredAsync(Guid userId, CancellationToken ct) =>
        db.Memberships.IgnoreQueryFilters()
            .AnyAsync(m => m.UserId == userId && m.Status == MembershipStatus.Active && m.TwoFactorRequiredSinceUtc != null, ct);

    private Task<bool> VerifyAsync(AppUser user, string code) =>
        users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, AuthController.CleanCode(code));

    private ObjectResult WrongCode() =>
        Problem(statusCode: 400, title: "That code isn't right. Use the latest code from your app, and check your phone's clock is set automatically.");

    private ObjectResult AlreadyOn() => Problem(statusCode: 409, title: "Two-step sign-in is already on.");

    /// <summary>"ABCD EFGH ..." is easier to type into an app than one long string.</summary>
    private static string Group(string key)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4) sb.Append(key.AsSpan(i, Math.Min(4, key.Length - i))).Append(' ');
        return sb.ToString().TrimEnd();
    }
}
