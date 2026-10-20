using PropertyManagement.Domain.Organizations;

namespace PropertyManagement.Application.Organizations;

public sealed record OrganizationSummaryDto(Guid Id, string Name, string Slug, OrganizationType Type,
    OrganizationStatus Status, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, DateTime? TwoFactorRequiredSinceUtc = null);

public sealed record OrganizationDetailDto(Guid Id, string Name, string Slug, OrganizationType Type,
    OrganizationStatus Status, string BaseCurrency, string TimeZone, string? ContactEmail, string? ContactPhone,
    string? Address, string? TaxIdentificationNumber, string? VatNumber, DateTime CreatedAtUtc);

public sealed record UpdateOrganizationRequest(string Name, string? ContactEmail, string? ContactPhone,
    string? Address, string? TaxIdentificationNumber, string? VatNumber, string? TimeZone);

public sealed record MemberDto(Guid MembershipId, Guid UserId, string Email, string FullName, MembershipStatus Status,
    IReadOnlyList<string> Roles, IReadOnlyList<Guid> BranchIds, DateTime JoinedAtUtc);

public sealed record UpdateMemberRequest(IReadOnlyList<string> Roles, IReadOnlyList<Guid>? BranchIds);

public sealed record CreateInvitationRequest(string Email, string Role, IReadOnlyList<Guid>? BranchIds);

public sealed record InvitationDto(Guid Id, string Email, string Role, IReadOnlyList<Guid> BranchIds,
    InvitationStatus Status, DateTime CreatedAtUtc, DateTime ExpiresAtUtc);

/// <summary>Returned once when an invitation is created. The token is never stored or shown again.</summary>
public sealed record CreatedInvitationDto(InvitationDto Invitation, string AcceptToken);
