using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyManagement.Domain.Auditing;
using PropertyManagement.Domain.Organizations;
using PropertyManagement.Domain.Platform;
using PropertyManagement.Infrastructure.Identity;

namespace PropertyManagement.Infrastructure.Persistence.Configurations;

internal sealed class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> b)
    {
        b.ToTable("SubscriptionPlans");
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.MonthlyPriceNgn).HasPrecision(19, 4);
        b.Property(x => x.AnnualPriceNgn).HasPrecision(19, 4);
        // Placeholder plans so onboarding can start a trial. Prices are decision 18.4 in the design.
        b.HasData(
            new SubscriptionPlan(new Guid("6f1d2c3a-0001-4a5b-9c8d-000000000001"), "starter", "Starter", 50, 5, 0m, 0m, 30),
            new SubscriptionPlan(new Guid("6f1d2c3a-0002-4a5b-9c8d-000000000002"), "growth", "Growth", 500, 25, 0m, 0m, 30));
    }
}

internal sealed class OrganizationSubscriptionConfiguration : IEntityTypeConfiguration<OrganizationSubscription>
{
    public void Configure(EntityTypeBuilder<OrganizationSubscription> b)
    {
        b.ToTable("OrganizationSubscriptions");
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(x => x.OrganizationId);
    }
}

internal sealed class SupportGrantConfiguration : IEntityTypeConfiguration<SupportGrant>
{
    public void Configure(EntityTypeBuilder<SupportGrant> b)
    {
        b.ToTable("SupportGrants");
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Reason).HasMaxLength(SupportGrant.MaxReasonLength).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.OrganizationId, x.RequestedAtUtc });
        b.HasIndex(x => new { x.RequestedByUserId, x.OrganizationId, x.Status });
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.Property(x => x.Action).HasMaxLength(100).IsRequired();
        b.Property(x => x.ResourceType).HasMaxLength(100);
        b.Property(x => x.ResourceId).HasMaxLength(100);
        b.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.CorrelationId).HasMaxLength(100);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.HasIndex(x => new { x.OrganizationId, x.TimestampUtc });
    }
}

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
    }
}
