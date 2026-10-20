using Microsoft.EntityFrameworkCore;
using PropertyManagement.Domain.Auditing;
using PropertyManagement.Domain.Organizations;
using PropertyManagement.Domain.Platform;

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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
