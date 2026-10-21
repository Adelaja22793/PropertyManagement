using Microsoft.Extensions.DependencyInjection;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Auditing;
using PropertyManagement.Application.Branches;
using PropertyManagement.Application.Documents;
using PropertyManagement.Application.Leases;
using PropertyManagement.Application.Listings;
using PropertyManagement.Application.Maintenance;
using PropertyManagement.Application.Organizations;
using PropertyManagement.Application.Owners;
using PropertyManagement.Application.Platform;
using PropertyManagement.Application.Portal;
using PropertyManagement.Application.Properties;
using PropertyManagement.Application.Reports;
using PropertyManagement.Application.Tenants;

namespace PropertyManagement.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<OrganizationService>();
        services.AddScoped<MembershipService>();
        services.AddScoped<RoleCatalog>();
        services.AddScoped<CustomRoleService>();
        services.AddScoped<SubscriptionService>();
        services.AddScoped<PlatformOrganizationService>();
        services.AddScoped<BranchService>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<PropertyAccess>();
        services.AddScoped<PropertyService>();
        services.AddScoped<OwnerService>();
        services.AddScoped<TenantService>();
        services.AddScoped<LeaseService>();
        services.AddScoped<LeaseRecordsService>();
        services.AddScoped<LeaseReminderService>();
        services.AddScoped<LeaseSettingsService>();
        services.AddScoped<VendorService>();
        services.AddScoped<MaintenanceService>();
        services.AddScoped<PortalScope>();
        services.AddScoped<PortalService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<ListingService>();
        services.AddScoped<ReportService>();
        services.AddScoped<LeadService>();
        services.AddScoped<PublicListingService>();
        services.AddScoped<Platform.SupportAccessService>();
        return services;
    }
}
