using Microsoft.EntityFrameworkCore;
using PropertyManagement.Domain.Auditing;
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
    DbSet<SubscriptionPlan> SubscriptionPlans { get; }
    DbSet<OrganizationSubscription> Subscriptions { get; }
    DbSet<AuditLog> AuditLogs { get; }
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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
