using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyManagement.Domain.Maintenance;
using PropertyManagement.Domain.Properties;
using PropertyManagement.Domain.Tenants;
using PropertyManagement.Infrastructure.Identity;

namespace PropertyManagement.Infrastructure.Persistence.Configurations;

// Every link goes through (OrganizationId, Id), so a request can never point at another organization's
// property, unit, tenant or vendor.

internal sealed class VendorConfiguration : IEntityTypeConfiguration<Vendor>
{
    public void Configure(EntityTypeBuilder<Vendor> b)
    {
        b.ToTable("Vendors");
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Trade).HasMaxLength(100);
        b.Property(x => x.ContactName).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasIndex(x => new { x.OrganizationId, x.Name });
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

internal sealed class MaintenanceRequestConfiguration : IEntityTypeConfiguration<MaintenanceRequest>
{
    public void Configure(EntityTypeBuilder<MaintenanceRequest> b)
    {
        b.ToTable("MaintenanceRequests");
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.HasOne<Property>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.PropertyId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PropertyUnit>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.UnitId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Tenant>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.TenantId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Vendor>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.VendorId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.Ignore(x => x.Reference);
        b.Ignore(x => x.NeedsOwnerApproval);
        b.Ignore(x => x.IsOpen);
        b.Ignore(x => x.IsWorkStage);
        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Urgency).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ReportedBy).HasMaxLength(200);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.EstimateAmount).HasPrecision(18, 2);
        b.Property(x => x.EstimateNotes).HasMaxLength(1000);
        b.Property(x => x.ApprovalLimit).HasPrecision(18, 2);
        b.Property(x => x.OwnerApprovalNote).HasMaxLength(500);
        b.Property(x => x.ActualCost).HasPrecision(18, 2);
        b.Property(x => x.CompletionNotes).HasMaxLength(2000);
        b.Property(x => x.ClosedReason).HasMaxLength(500);
        b.Property(x => x.TenantFeedback).HasMaxLength(1000);
        b.HasIndex(x => new { x.OrganizationId, x.Number }).IsUnique();
        b.HasIndex(x => new { x.OrganizationId, x.Status, x.Urgency });
        b.HasIndex(x => new { x.PropertyId, x.Status });
        b.HasIndex(x => new { x.AssignedUserId, x.Status });
        b.ToTable(t => t.HasCheckConstraint("CK_MaintenanceRequests_Rating", "[TenantRating] IS NULL OR [TenantRating] BETWEEN 1 AND 5"));
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasMany(x => x.Updates).WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.RequestId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Updates).HasField("_updates");
    }
}

internal sealed class MaintenanceUpdateConfiguration : IEntityTypeConfiguration<MaintenanceUpdate>
{
    public void Configure(EntityTypeBuilder<MaintenanceUpdate> b)
    {
        b.ToTable("MaintenanceUpdates");
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ByUserId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Text).HasMaxLength(2000);
        b.HasIndex(x => new { x.RequestId, x.Sequence }).IsUnique();
    }
}
