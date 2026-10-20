using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyManagement.Domain.Organizations;
using PropertyManagement.Infrastructure.Identity;

namespace PropertyManagement.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> b)
    {
        b.ToTable("Organizations");
        b.Property(o => o.Name).HasMaxLength(200).IsRequired();
        b.Property(o => o.Slug).HasMaxLength(100).IsRequired();
        b.HasIndex(o => o.Slug).IsUnique();
        b.Property(o => o.Type).HasConversion<string>().HasMaxLength(30);
        b.Property(o => o.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(o => o.BaseCurrency).HasMaxLength(3).IsFixedLength().HasDefaultValue("NGN");
        b.Property(o => o.TimeZone).HasMaxLength(64).HasDefaultValue("Africa/Lagos");
        b.Property(o => o.ContactEmail).HasMaxLength(256);
        b.Property(o => o.ContactPhone).HasMaxLength(30);
        b.Property(o => o.Address).HasMaxLength(500);
        b.Property(o => o.TaxIdentificationNumber).HasMaxLength(30);
        b.Property(o => o.VatNumber).HasMaxLength(30);
        b.Property(o => o.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(o => o.RowVersion).IsRowVersion();
    }
}

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> b)
    {
        b.ToTable("Branches");
        // (OrganizationId, Id) is the target of composite foreign keys, so a child row can
        // only point at a branch in its own organization (defence layer 3, design 4.4).
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Address).HasMaxLength(500);
        b.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<OrganizationMembership>
{
    public void Configure(EntityTypeBuilder<OrganizationMembership> b)
    {
        b.ToTable("OrganizationMemberships");
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Ignore(x => x.IsActive);
        // One active membership per user per organization; revoked ones stay as history.
        b.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique().HasFilter("[Status] = 'Active'");
        b.HasIndex(x => x.UserId);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasMany(x => x.Roles).WithOne()
            .HasForeignKey(r => new { r.OrganizationId, r.MembershipId })
            .HasPrincipalKey(m => new { m.OrganizationId, m.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.BranchScopes).WithOne()
            .HasForeignKey(s => new { s.OrganizationId, s.MembershipId })
            .HasPrincipalKey(m => new { m.OrganizationId, m.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MembershipRoleConfiguration : IEntityTypeConfiguration<MembershipRole>
{
    public void Configure(EntityTypeBuilder<MembershipRole> b)
    {
        b.ToTable("OrganizationMembershipRoles");
        b.Property(x => x.RoleName).HasMaxLength(50).IsRequired();
        b.HasIndex(x => new { x.MembershipId, x.RoleName }).IsUnique();
    }
}

internal sealed class MembershipBranchScopeConfiguration : IEntityTypeConfiguration<MembershipBranchScope>
{
    public void Configure(EntityTypeBuilder<MembershipBranchScope> b)
    {
        b.ToTable("MembershipBranchScopes");
        b.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.BranchId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.MembershipId, x.BranchId }).IsUnique();
    }
}

internal sealed class UserInvitationConfiguration : IEntityTypeConfiguration<UserInvitation>
{
    public void Configure(EntityTypeBuilder<UserInvitation> b)
    {
        b.ToTable("UserInvitations");
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.Property(x => x.RoleName).HasMaxLength(50).IsRequired();
        b.Property(x => x.BranchIds).HasMaxLength(2000);
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.OrganizationId, x.Email });
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Ignore(x => x.IsPortalInvitation);
    }
}
