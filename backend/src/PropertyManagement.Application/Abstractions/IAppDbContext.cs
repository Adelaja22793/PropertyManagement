using Microsoft.EntityFrameworkCore;
using PropertyManagement.Domain.Auditing;
using PropertyManagement.Domain.Documents;
using PropertyManagement.Domain.Leases;
using PropertyManagement.Domain.Listings;
using PropertyManagement.Domain.Maintenance;
using PropertyManagement.Domain.Organizations;
using PropertyManagement.Domain.Owners;
using PropertyManagement.Domain.Platform;
using PropertyManagement.Domain.Properties;
using PropertyManagement.Domain.Tenants;

namespace PropertyManagement.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Organization> Organizations { get; }
    DbSet<Branch> Branches { get; }
    DbSet<OrganizationMembership> Memberships { get; }
    DbSet<UserInvitation> Invitations { get; }
    DbSet<CustomRole> CustomRoles { get; }
    DbSet<SubscriptionPlan> SubscriptionPlans { get; }
    DbSet<OrganizationSubscription> Subscriptions { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<SupportGrant> SupportGrants { get; }
    DbSet<Property> Properties { get; }
    DbSet<PropertyUnit> Units { get; }
    DbSet<PropertyOwnership> PropertyOwnerships { get; }
    DbSet<Owner> Owners { get; }
    DbSet<OwnerBankAccount> OwnerBankAccounts { get; }
    DbSet<ManagementAgreement> ManagementAgreements { get; }
    DbSet<OwnerUserLink> OwnerUserLinks { get; }
    DbSet<Tenant> Tenants { get; }
    DbSet<TenantKyc> TenantKyc { get; }
    DbSet<Guarantor> Guarantors { get; }
    DbSet<ScreeningCheck> ScreeningChecks { get; }
    DbSet<TenantUserLink> TenantUserLinks { get; }
    DbSet<Lease> Leases { get; }
    DbSet<LeaseTenant> LeaseTenants { get; }
    DbSet<LeaseGuarantor> LeaseGuarantors { get; }
    DbSet<LeaseCharge> LeaseCharges { get; }
    DbSet<LeaseNotice> LeaseNotices { get; }
    DbSet<LeaseInspection> LeaseInspections { get; }
    DbSet<LeaseReminder> LeaseReminders { get; }
    DbSet<Vendor> Vendors { get; }
    DbSet<MaintenanceRequest> MaintenanceRequests { get; }
    DbSet<MaintenanceUpdate> MaintenanceUpdates { get; }
    DbSet<Document> Documents { get; }
    DbSet<Listing> Listings { get; }
    DbSet<Lead> Leads { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
