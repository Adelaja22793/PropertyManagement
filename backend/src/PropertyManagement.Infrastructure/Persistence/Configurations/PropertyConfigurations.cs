using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyManagement.Domain.Organizations;
using PropertyManagement.Domain.Owners;
using PropertyManagement.Domain.Properties;
using PropertyManagement.Domain.Tenants;
using PropertyManagement.Infrastructure.Identity;

namespace PropertyManagement.Infrastructure.Persistence.Configurations;

// Every child table points at its parent through (OrganizationId, ParentId), so the database itself refuses a
// row that links records from two organizations (defence layer 3, design 4.4 and 7.7).

internal sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> b)
    {
        b.ToTable("Properties");
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.BranchId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ManagerUserId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.AddressLine).HasMaxLength(300).IsRequired();
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.State).HasMaxLength(50).IsRequired();
        b.Property(x => x.LocalGovernmentArea).HasMaxLength(100);
        b.Property(x => x.Amenities).HasMaxLength(1000);
        b.Ignore(x => x.IsArchived);
        b.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.OrganizationId, x.BranchId });
        b.HasIndex(x => new { x.OrganizationId, x.ManagerUserId });
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

internal sealed class PropertyUnitConfiguration : IEntityTypeConfiguration<PropertyUnit>
{
    public void Configure(EntityTypeBuilder<PropertyUnit> b)
    {
        b.ToTable("PropertyUnits");
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.HasOne<Property>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.PropertyId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.BillingFrequency).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.OccupancyStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Floor).HasMaxLength(20);
        b.Property(x => x.SizeSquareMetres).HasPrecision(10, 2);
        b.Property(x => x.DefaultRent).HasPrecision(18, 2);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.ServiceChargeSharePercent).HasPrecision(7, 4);
        b.Property(x => x.Amenities).HasMaxLength(1000);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasIndex(x => new { x.PropertyId, x.Code }).IsUnique();
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

internal sealed class PropertyOwnershipConfiguration : IEntityTypeConfiguration<PropertyOwnership>
{
    public void Configure(EntityTypeBuilder<PropertyOwnership> b)
    {
        b.ToTable("PropertyOwners");
        b.HasOne<Property>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.PropertyId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Owner>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.OwnerId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.SharePercent).HasPrecision(5, 2);
        b.HasIndex(x => new { x.PropertyId, x.EffectiveFrom });
        b.HasIndex(x => new { x.OrganizationId, x.OwnerId });
        b.ToTable(t => t.HasCheckConstraint("CK_PropertyOwners_Share", "[SharePercent] > 0 AND [SharePercent] <= 100"));
    }
}

internal sealed class OwnerConfiguration : IEntityTypeConfiguration<Owner>
{
    public void Configure(EntityTypeBuilder<Owner> b)
    {
        b.ToTable("Owners");
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.PreferredChannel).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.TaxIdentificationNumber).HasMaxLength(30);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasIndex(x => new { x.OrganizationId, x.Name });
        b.HasIndex(x => new { x.OrganizationId, x.Email }).IsUnique().HasFilter("[Email] IS NOT NULL");
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

internal sealed class OwnerBankAccountConfiguration : IEntityTypeConfiguration<OwnerBankAccount>
{
    public void Configure(EntityTypeBuilder<OwnerBankAccount> b)
    {
        b.ToTable("OwnerBankAccounts");
        b.HasOne<Owner>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.OwnerId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.BankName).HasMaxLength(100).IsRequired();
        b.Property(x => x.AccountNumber).HasMaxLength(10).IsFixedLength().IsRequired();
        b.Property(x => x.AccountName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.DecisionNote).HasMaxLength(500);
        b.Ignore(x => x.MaskedNumber);
        // At most one approved and one pending account per owner at a time.
        b.HasIndex(x => x.OwnerId).IsUnique().HasFilter("[Status] = 'Approved'").HasDatabaseName("IX_OwnerBankAccounts_OneApproved");
        b.HasIndex(x => x.OwnerId).IsUnique().HasFilter("[Status] = 'PendingApproval'").HasDatabaseName("IX_OwnerBankAccounts_OnePending");
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

internal sealed class ManagementAgreementConfiguration : IEntityTypeConfiguration<ManagementAgreement>
{
    public void Configure(EntityTypeBuilder<ManagementAgreement> b)
    {
        b.ToTable("ManagementAgreements");
        b.HasOne<Owner>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.OwnerId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Property>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.PropertyId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.FeeBasis).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.PayoutFrequency).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.AdvanceRentRelease).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.FeeValue).HasPrecision(18, 2);
        b.Property(x => x.LettingFeePercent).HasPrecision(5, 2);
        b.Property(x => x.ReserveAmount).HasPrecision(18, 2);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasIndex(x => new { x.OwnerId, x.PropertyId, x.StartDate });
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

internal sealed class OwnerUserLinkConfiguration : IEntityTypeConfiguration<OwnerUserLink>
{
    public void Configure(EntityTypeBuilder<OwnerUserLink> b)
    {
        b.ToTable("OwnerUserLinks");
        b.HasOne<Owner>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.OwnerId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.OwnerId).IsUnique();
        b.HasIndex(x => new { x.OrganizationId, x.UserId });
    }
}

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("Tenants");
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.AlternatePhone).HasMaxLength(30);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.Employer).HasMaxLength(200);
        b.Property(x => x.Occupation).HasMaxLength(100);
        b.Property(x => x.EmergencyContactName).HasMaxLength(200);
        b.Property(x => x.EmergencyContactPhone).HasMaxLength(30);
        b.Property(x => x.CompanyRegistrationNumber).HasMaxLength(30);
        b.Property(x => x.TaxIdentificationNumber).HasMaxLength(30);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasIndex(x => new { x.OrganizationId, x.Name });
        b.HasIndex(x => new { x.OrganizationId, x.Email }).IsUnique().HasFilter("[Email] IS NOT NULL");
        b.HasIndex(x => new { x.OrganizationId, x.Phone });
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

internal sealed class TenantKycConfiguration : IEntityTypeConfiguration<TenantKyc>
{
    public void Configure(EntityTypeBuilder<TenantKyc> b)
    {
        b.ToTable("TenantKyc");
        b.HasOne<Tenant>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.TenantId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.TenantId).IsUnique();
        b.Property(x => x.IdType).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        // Data Protection output for an 11-digit number is well under 500 characters.
        b.Property(x => x.IdNumberEncrypted).HasMaxLength(500);
        b.Property(x => x.BvnEncrypted).HasMaxLength(500);
        b.Property(x => x.IdNumberLast4).HasMaxLength(4);
        b.Property(x => x.BvnLast4).HasMaxLength(4);
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

internal sealed class GuarantorConfiguration : IEntityTypeConfiguration<Guarantor>
{
    public void Configure(EntityTypeBuilder<Guarantor> b)
    {
        b.ToTable("Guarantors");
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.HasOne<Tenant>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.TenantId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.Relationship).HasMaxLength(100);
        b.Property(x => x.Employer).HasMaxLength(200);
        b.Property(x => x.Occupation).HasMaxLength(100);
        b.HasIndex(x => x.TenantId);
    }
}

internal sealed class ScreeningCheckConfiguration : IEntityTypeConfiguration<ScreeningCheck>
{
    public void Configure(EntityTypeBuilder<ScreeningCheck> b)
    {
        b.ToTable("TenantScreeningChecks");
        b.HasOne<Tenant>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.TenantId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasIndex(x => x.TenantId);
    }
}

internal sealed class TenantUserLinkConfiguration : IEntityTypeConfiguration<TenantUserLink>
{
    public void Configure(EntityTypeBuilder<TenantUserLink> b)
    {
        b.ToTable("TenantUserLinks");
        b.HasOne<Tenant>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.TenantId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.TenantId).IsUnique();
        b.HasIndex(x => new { x.OrganizationId, x.UserId });
    }
}
