using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using PropertyManagement.Application.Abstractions;

namespace PropertyManagement.Api.Authorization;

/// <summary>Builds a policy for each "permission:X" name on demand.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(RequirePortalRoleAttribute.PolicyPrefix, StringComparison.Ordinal))
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PortalRoleRequirement(policyName[RequirePortalRoleAttribute.PolicyPrefix.Length..]))
                .Build();
        if (!policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            return await base.GetPolicyAsync(policyName);

        var permission = policyName[RequirePermissionAttribute.PolicyPrefix.Length..];
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();
    }
}

public sealed class PermissionHandler(ICurrentOrganization currentOrganization) : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        // "A|B" means any one of the listed permissions (see RequireAnyPermissionAttribute).
        if (requirement.Permission.Split('|').Any(currentOrganization.HasPermission)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public sealed class PortalRoleHandler(ICurrentOrganization currentOrganization) : AuthorizationHandler<PortalRoleRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PortalRoleRequirement requirement)
    {
        if (currentOrganization.Roles.Contains(requirement.Role)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
