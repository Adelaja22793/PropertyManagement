using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using PropertyManagement.Application.Abstractions;

namespace PropertyManagement.Api.Authorization;

/// <summary>Builds a policy for each "permission:X" name on demand.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
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
        if (currentOrganization.HasPermission(requirement.Permission)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
