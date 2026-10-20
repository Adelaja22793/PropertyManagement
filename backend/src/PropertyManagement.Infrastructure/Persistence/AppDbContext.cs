using System.Linq.Expressions;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Auditing;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Leases;
using PropertyManagement.Domain.Organizations;
using PropertyManagement.Domain.Owners;
using PropertyManagement.Domain.Platform;
using PropertyManagement.Domain.Properties;
using PropertyManagement.Domain.Tenants;
using PropertyManagement.Infrastructure.Identity;

namespace PropertyManagement.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentOrganization currentOrganization)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IAppDbContext, IDataProtectionKeyContext
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<OrganizationMembership> Memberships => Set<OrganizationMembership>();
    public DbSet<UserInvitation> Invitations => Set<UserInvitation>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<OrganizationSubscription> Subscriptions => Set<OrganizationSubscription>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<PropertyUnit> Units => Set<PropertyUnit>();
    public DbSet<PropertyOwnership> PropertyOwnerships => Set<PropertyOwnership>();
    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<OwnerBankAccount> OwnerBankAccounts => Set<OwnerBankAccount>();
    public DbSet<ManagementAgreement> ManagementAgreements => Set<ManagementAgreement>();
    public DbSet<OwnerUserLink> OwnerUserLinks => Set<OwnerUserLink>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantKyc> TenantKyc => Set<TenantKyc>();
    public DbSet<Guarantor> Guarantors => Set<Guarantor>();
    public DbSet<ScreeningCheck> ScreeningChecks => Set<ScreeningCheck>();
    public DbSet<TenantUserLink> TenantUserLinks => Set<TenantUserLink>();
    public DbSet<Lease> Leases => Set<Lease>();
    public DbSet<LeaseTenant> LeaseTenants => Set<LeaseTenant>();
    public DbSet<LeaseGuarantor> LeaseGuarantors => Set<LeaseGuarantor>();
    public DbSet<LeaseCharge> LeaseCharges => Set<LeaseCharge>();

    /// <summary>Data Protection keys used to encrypt identity numbers. Shared by all API instances.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>
    /// Read by the global query filters on every query. With no organization selected it is
    /// <see cref="Guid.Empty"/>, so organization-scoped queries return nothing rather than everything.
    /// </summary>
    public Guid CurrentOrganizationId => currentOrganization.OrganizationId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Domain entities create their own IDs. Telling EF so means a new child added to a loaded parent
        // (for example a role added to an existing membership) is inserted, not treated as an existing row.
        foreach (var entityType in builder.Model.GetEntityTypes()
                     .Where(t => t.ClrType.Assembly == typeof(IOrganizationScoped).Assembly))
        {
            var key = entityType.FindPrimaryKey();
            if (key is { Properties: [{ ClrType: var type } keyProperty] } && type == typeof(Guid))
                keyProperty.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }

        // Defence layer 1 (design 4.4): every organization-owned entity is filtered to the active organization.
        foreach (var entityType in builder.Model.GetEntityTypes()
                     .Where(t => typeof(IOrganizationScoped).IsAssignableFrom(t.ClrType)))
        {
            var entity = Expression.Parameter(entityType.ClrType, "e");
            var filter = Expression.Lambda(
                Expression.Equal(
                    Expression.Property(entity, nameof(IOrganizationScoped.OrganizationId)),
                    Expression.Property(Expression.Constant(this), nameof(CurrentOrganizationId))),
                entity);
            builder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceIsolation();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceIsolation();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Defence layer 2: no write may touch a row of another organization, whatever the query that loaded it,
    /// and audit records can only ever be added.
    /// </summary>
    private void EnforceIsolation()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached) continue;

            if (entry.Entity is AuditLog && entry.State != EntityState.Added)
                throw new InvalidOperationException("Audit records are append-only.");

            if (entry.Entity is not IOrganizationScoped scoped) continue;

            var name = entry.Metadata.ClrType.Name;
            var active = currentOrganization.OrganizationId ?? throw new CrossOrganizationWriteException(name);
            if (scoped.OrganizationId != active) throw new CrossOrganizationWriteException(name);
            if (entry.State != EntityState.Added &&
                (Guid)entry.Property(nameof(IOrganizationScoped.OrganizationId)).OriginalValue! != active)
                throw new CrossOrganizationWriteException(name);
        }
    }
}
