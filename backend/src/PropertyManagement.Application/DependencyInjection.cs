using Microsoft.Extensions.DependencyInjection;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Auditing;
using PropertyManagement.Application.Branches;
using PropertyManagement.Application.Leases;
using PropertyManagement.Application.Organizations;
using PropertyManagement.Application.Owners;
using PropertyManagement.Application.Properties;
using PropertyManagement.Application.Tenants;

namespace PropertyManagement.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<OrganizationService>();
        services.AddScoped<MembershipService>();
        services.AddScoped<BranchService>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<PropertyAccess>();
        services.AddScoped<PropertyService>();
        services.AddScoped<OwnerService>();
        services.AddScoped<TenantService>();
        services.AddScoped<LeaseService>();
        return services;
    }
}
