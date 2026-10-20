using Microsoft.AspNetCore.Authorization;

namespace PropertyManagement.Api.Authorization;

/// <summary>
/// Requires the caller's membership in the selected organization to grant a permission.
/// Fails when no organization is selected, so an endpoint with this attribute is always organization-scoped.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(string permission) : AuthorizeAttribute(PolicyPrefix + permission)
{
    public const string PolicyPrefix = "permission:";
}

/// <summary>Requires at least one of the listed permissions. The service then decides which records the caller may act on.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireAnyPermissionAttribute(params string[] permissions)
    : AuthorizeAttribute(RequirePermissionAttribute.PolicyPrefix + string.Join('|', permissions));

/// <summary>
/// Requires a portal role (Owner or Tenant) in the selected organization. Portal roles carry no staff permissions;
/// the portal service then limits the caller to the records their owner or tenant link reaches.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequirePortalRoleAttribute(string role) : AuthorizeAttribute(PolicyPrefix + role)
{
    public const string PolicyPrefix = "portal-role:";
}

public sealed class PortalRoleRequirement(string role) : IAuthorizationRequirement
{
    public string Role { get; } = role;
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
