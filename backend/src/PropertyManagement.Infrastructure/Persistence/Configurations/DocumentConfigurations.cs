using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyManagement.Domain.Documents;
using PropertyManagement.Domain.Properties;
using PropertyManagement.Infrastructure.Identity;

namespace PropertyManagement.Infrastructure.Persistence.Configurations;

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> b)
    {
        b.ToTable("Documents");
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id });
        // The subject is checked by the service; the property link goes through (OrganizationId, Id) like every other.
        b.HasOne<Property>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.PropertyId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.DeletedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.Ignore(x => x.IsDeleted);
        b.Ignore(x => x.IsImage);
        b.Property(x => x.Subject).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(Document.MaxFileNameLength).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(100).IsUnicode(false).IsRequired();
        b.HasIndex(x => x.StorageKey).IsUnique();
        b.HasIndex(x => new { x.OrganizationId, x.Subject, x.SubjectId });
        b.HasIndex(x => new { x.OrganizationId, x.ExpiresOn }).HasFilter("[ExpiresOn] IS NOT NULL AND [DeletedAtUtc] IS NULL");
        b.HasIndex(x => new { x.OrganizationId, x.PropertyId });
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}
