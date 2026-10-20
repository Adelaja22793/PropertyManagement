using System.ComponentModel.DataAnnotations;
using PropertyManagement.Application.Organizations;
using PropertyManagement.Domain.Organizations;

namespace PropertyManagement.Api.Contracts;

/// <summary>
/// Creates a login. Either OrganizationName (start a new organization) or InvitationToken (join one) is required.
/// </summary>
public sealed record RegisterRequest(
    [Required, StringLength(200, MinimumLength = 2)] string FullName,
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128)] string Password,
    [StringLength(200)] string? OrganizationName,
    OrganizationType? OrganizationType,
    [StringLength(200)] string? InvitationToken);

public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password,
    bool RememberMe = false);

public sealed record ForgotPasswordRequest([Required, EmailAddress] string Email);

public sealed record ResetPasswordRequest(
    [Required, EmailAddress] string Email,
    [Required] string Token,
    [Required, StringLength(128)] string NewPassword);

public sealed record MeResponse(Guid UserId, string Email, string FullName, bool IsPlatformAdmin,
    IReadOnlyList<OrganizationSummaryDto> Organizations, bool TwoFactorEnabled = false, int TwoFactorGraceDays = 0);

/// <summary>The password was right and the account uses two-step sign-in: send the code next.</summary>
public sealed record TwoFactorChallengeResponse(bool RequiresTwoFactor);

public sealed record TwoFactorLoginRequest([Required, StringLength(20)] string Code, bool RememberMe = false);

public sealed record RecoveryCodeLoginRequest([Required, StringLength(20)] string Code, bool RememberMe = false);

public sealed record TwoFactorStatusResponse(bool Enabled, bool Required, int RecoveryCodesLeft);

public sealed record TwoFactorSetupResponse(string SharedKey, string AuthenticatorUri);

public sealed record TwoFactorCodeRequest([Required, StringLength(20)] string Code);

public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

public sealed record DisableTwoFactorRequest([Required] string Password);

public sealed record CreateOrganizationRequest(
    [Required, StringLength(200, MinimumLength = 2)] string Name,
    OrganizationType Type = OrganizationType.ManagementCompany);

public sealed record AcceptInvitationRequest([Required] string Token);
