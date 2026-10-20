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
    IReadOnlyList<OrganizationSummaryDto> Organizations);

public sealed record CreateOrganizationRequest(
    [Required, StringLength(200, MinimumLength = 2)] string Name,
    OrganizationType Type = OrganizationType.ManagementCompany);

public sealed record AcceptInvitationRequest([Required] string Token);
