using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyManagement.Domain.Leases;
using PropertyManagement.Domain.Properties;
using PropertyManagement.Domain.Tenants;
using PropertyManagement.Infrastructure.Identity;

namespace PropertyManagement.Infrastructure.Persistence.Configurations;

// Same rule as the other tables: every link goes through (OrganizationId, Id), so a lease can never point at a
// unit, tenant or guarantor of another organization.

internal sealed class LeaseConfiguration : IEntityTypeConfiguration<Lease>
{
    public void Configure(EntityTypeBuilder<Lease> b)
    {
        b.ToTable("Leases");
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.HasOne<Property>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.PropertyId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PropertyUnit>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.UnitId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Lease>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.PreviousLeaseId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActivatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.Ignore(x => x.Reference);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.BillingFrequency).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RentAmount).HasPrecision(18, 2);
        b.Property(x => x.HoldoverRentAmount).HasPrecision(18, 2);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.RenewalTerms).HasMaxLength(1000);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.EndReason).HasMaxLength(500);
        b.HasIndex(x => new { x.OrganizationId, x.Number }).IsUnique();
        b.HasIndex(x => new { x.UnitId, x.Status });
        b.HasIndex(x => new { x.OrganizationId, x.Status, x.EndDate });
        // At most one renewal per lease.
        b.HasIndex(x => x.PreviousLeaseId).IsUnique().HasFilter("[PreviousLeaseId] IS NOT NULL AND [Status] <> 'Cancelled'");
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Leases_Dates", "[EndDate] > [StartDate]");
            t.HasCheckConstraint("CK_Leases_Rent", "[RentAmount] > 0");
        });
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasMany(x => x.Tenants).WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.LeaseId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Guarantors).WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.LeaseId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Charges).WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.LeaseId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Tenants).HasField("_tenants");
        b.Navigation(x => x.Guarantors).HasField("_guarantors");
        b.Navigation(x => x.Charges).HasField("_charges");
    }
}

internal sealed class LeaseTenantConfiguration : IEntityTypeConfiguration<LeaseTenant>
{
    public void Configure(EntityTypeBuilder<LeaseTenant> b)
    {
        b.ToTable("LeaseTenants");
        b.HasKey(x => new { x.LeaseId, x.TenantId });
        b.HasOne<Tenant>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.TenantId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.OrganizationId, x.TenantId });
    }
}

internal sealed class LeaseGuarantorConfiguration : IEntityTypeConfiguration<LeaseGuarantor>
{
    public void Configure(EntityTypeBuilder<LeaseGuarantor> b)
    {
        b.ToTable("LeaseGuarantors");
        b.HasKey(x => new { x.LeaseId, x.GuarantorId });
        b.HasOne<Guarantor>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.GuarantorId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.OrganizationId, x.GuarantorId });
    }
}

internal sealed class LeaseChargeConfiguration : IEntityTypeConfiguration<LeaseCharge>
{
    public void Configure(EntityTypeBuilder<LeaseCharge> b)
    {
        b.ToTable("LeaseCharges");
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Recurrence).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Description).HasMaxLength(200);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.ToTable(t => t.HasCheckConstraint("CK_LeaseCharges_Amount", "[Amount] > 0"));
    }
}

internal sealed class LeaseNoticeConfiguration : IEntityTypeConfiguration<LeaseNotice>
{
    public void Configure(EntityTypeBuilder<LeaseNotice> b)
    {
        b.ToTable("LeaseNotices");
        b.HasOne<Lease>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.LeaseId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.Method).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ServedBy).HasMaxLength(200);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.WithdrawnReason).HasMaxLength(500);
        b.HasIndex(x => new { x.LeaseId, x.ServedOn });
        b.HasIndex(x => new { x.OrganizationId, x.Status, x.ExpiresOn });
    }
}

internal sealed class LeaseInspectionConfiguration : IEntityTypeConfiguration<LeaseInspection>
{
    public void Configure(EntityTypeBuilder<LeaseInspection> b)
    {
        b.ToTable("LeaseInspections");
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.HasOne<Lease>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.LeaseId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.InspectedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Summary).HasMaxLength(2000);
        b.Ignore(x => x.TotalProposedDeduction);
        b.HasIndex(x => new { x.LeaseId, x.Type }).IsUnique();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasMany(x => x.Items).WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.InspectionId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Items).HasField("_items");
    }
}

internal sealed class InspectionItemConfiguration : IEntityTypeConfiguration<InspectionItem>
{
    public void Configure(EntityTypeBuilder<InspectionItem> b)
    {
        b.ToTable("InspectionItems");
        b.Property(x => x.Area).HasMaxLength(100).IsRequired();
        b.Property(x => x.Condition).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.ProposedDeduction).HasPrecision(18, 2);
    }
}

internal sealed class LeaseReminderConfiguration : IEntityTypeConfiguration<LeaseReminder>
{
    public void Configure(EntityTypeBuilder<LeaseReminder> b)
    {
        b.ToTable("LeaseReminders");
        b.HasOne<Lease>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.LeaseId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Key).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.LeaseId, x.Key }).IsUnique();
    }
}
