using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Common;

namespace PropertyManagement.Infrastructure.Services;

public sealed class CurrentOrganization : ICurrentOrganization
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();

    public Guid? OrganizationId { get; private set; }
    public Guid? MembershipId { get; private set; }
    public IReadOnlySet<string> Roles { get; private set; } = Empty;
    public IReadOnlySet<string> Permissions { get; private set; } = Empty;
    public IReadOnlySet<Guid> BranchScope { get; private set; } = new HashSet<Guid>();

    private IReadOnlyDictionary<string, IReadOnlySet<string>> _customRoles = new Dictionary<string, IReadOnlySet<string>>();

    public bool HasPermission(string permission) => OrganizationId.HasValue && Permissions.Contains(permission);

    public IReadOnlySet<string> PermissionsOf(string role) =>
        Roles.Contains(role) ? Application.Authorization.Roles.PermissionsFor([role], _customRoles) : Empty;

    public Guid RequireOrganizationId() =>
        OrganizationId ?? throw new OrganizationContextRequiredException();

    public void Set(Guid organizationId, Guid? membershipId, IEnumerable<string> roles, IEnumerable<Guid> branchScope,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? customRoles = null)
    {
        OrganizationId = organizationId;
        MembershipId = membershipId;
        Roles = roles.ToHashSet();
        _customRoles = customRoles ?? new Dictionary<string, IReadOnlySet<string>>();
        Permissions = Application.Authorization.Roles.PermissionsFor(Roles, _customRoles);
        BranchScope = branchScope.ToHashSet();
    }
}
