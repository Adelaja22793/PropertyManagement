namespace PropertyManagement.Application.Abstractions;

/// <summary>
/// The organization the current request acts for. It is set only by the server after it has
/// verified an active membership; a client-supplied organization ID is never trusted on its own.
/// </summary>
public interface ICurrentOrganization
{
    /// <summary>The active organization, or null when the request has no organization context.</summary>
    Guid? OrganizationId { get; }
    Guid? MembershipId { get; }
    IReadOnlySet<string> Roles { get; }
    IReadOnlySet<string> Permissions { get; }

    /// <summary>Branches the member is limited to. Empty means the whole organization.</summary>
    IReadOnlySet<Guid> BranchScope { get; }

    bool HasPermission(string permission);

    /// <summary>The active organization ID, or an exception when there is none.</summary>
    Guid RequireOrganizationId();

    void Set(Guid organizationId, Guid? membershipId, IEnumerable<string> roles, IEnumerable<Guid> branchScope);
}
