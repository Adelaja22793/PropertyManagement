using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyManagement.Domain.Documents;
using PropertyManagement.Domain.Listings;
using PropertyManagement.Domain.Properties;
using PropertyManagement.Infrastructure.Identity;

namespace PropertyManagement.Infrastructure.Persistence.Configurations;

internal sealed class ListingConfiguration : IEntityTypeConfiguration<Listing>
{
    public void Configure(EntityTypeBuilder<Listing> b)
    {
        b.ToTable("Listings");
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.HasOne<Property>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.PropertyId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PropertyUnit>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.UnitId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AgentUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.Ignore(x => x.IsLive);
        b.Property(x => x.PublicCode).HasMaxLength(20).IsUnicode(false).IsRequired();
        b.HasIndex(x => x.PublicCode).IsUnique();
        b.Property(x => x.Headline).HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.AskingRent).HasPrecision(18, 2);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.BillingFrequency).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Features).HasMaxLength(2000);
        b.Property(x => x.ContactName).HasMaxLength(200);
        b.Property(x => x.ContactPhone).HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.CloseReason).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.OrganizationId, x.Status });
        // At most one open listing per unit, also when two people create one at the same moment.
        b.HasIndex(x => x.UnitId).IsUnique().HasFilter("[Status] <> 'Closed'");
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasMany(x => x.Photos).WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.ListingId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Photos).HasField("_photos");
    }
}

internal sealed class ListingPhotoConfiguration : IEntityTypeConfiguration<ListingPhoto>
{
    public void Configure(EntityTypeBuilder<ListingPhoto> b)
    {
        b.ToTable("ListingPhotos");
        b.HasOne<Document>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.DocumentId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ListingId, x.DocumentId }).IsUnique();
    }
}

internal sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> b)
    {
        b.ToTable("Leads");
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.HasOne<Listing>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ListingId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.Ignore(x => x.Reference);
        b.Ignore(x => x.IsOpen);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Message).HasMaxLength(2000);
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.OrganizationId, x.Number }).IsUnique();
        b.HasIndex(x => new { x.ListingId, x.Status });
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasMany(x => x.Viewings).WithOne()
            .HasForeignKey(x => new { x.OrganizationId, x.LeadId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Viewings).HasField("_viewings");
    }
}

internal sealed class ViewingConfiguration : IEntityTypeConfiguration<Viewing>
{
    public void Configure(EntityTypeBuilder<Viewing> b)
    {
        b.ToTable("Viewings");
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AgentUserId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.Outcome).HasMaxLength(1000);
        b.HasIndex(x => new { x.AgentUserId, x.ScheduledAtUtc });
    }
}
