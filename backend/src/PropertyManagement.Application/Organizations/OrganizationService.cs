using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Organizations;
using PropertyManagement.Domain.Platform;

namespace PropertyManagement.Application.Organizations;

public sealed class OrganizationService(
    IAppDbContext db,
    ICurrentOrganization currentOrganization,
    IAuditLogger audit,
    IClock clock)
{
    public const string DefaultPlanCode = "starter";

    /// <summary>
    /// Onboarding (design 4.5): creates the organization, makes the user its Organization Admin,
    /// starts a trial and records the event. Switches the request's organization context to it.
    /// </summary>
    public async Task<Organization> CreateAsync(Guid userId, string name, OrganizationType type, CancellationToken ct)
    {
        var organization = new Organization(name, await UniqueSlugAsync(name, ct), type);
        db.Organizations.Add(organization);

        var membership = new OrganizationMembership(organization.Id, userId, [Roles.OrganizationAdmin]);
        currentOrganization.Set(organization.Id, membership.Id, [Roles.OrganizationAdmin], []);
        db.Memberships.Add(membership);

        var plan = await db.SubscriptionPlans.SingleAsync(p => p.Code == DefaultPlanCode, ct);
        db.Subscriptions.Add(OrganizationSubscription.StartTrial(organization.Id, plan, clock.UtcNow));

        audit.Record("organization.created", nameof(Organization), organization.Id.ToString(),
            details: new { organization.Name, organization.Type, Plan = plan.Code }, organizationId: organization.Id, userId: userId);
        await db.SaveChangesAsync(ct);
        return organization;
    }

    /// <summary>Organizations where the user has an active membership. Reads across organizations by design.</summary>
    public async Task<IReadOnlyList<OrganizationSummaryDto>> ListForUserAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.Memberships.IgnoreQueryFilters()
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
            .Join(db.Organizations.Where(o => o.Status != OrganizationStatus.Cancelled),
                m => m.OrganizationId, o => o.Id, (m, o) => new { Membership = m, Organization = o })
            .Select(x => new
            {
                x.Organization.Id, x.Organization.Name, x.Organization.Slug, x.Organization.Type, x.Organization.Status,
                Roles = x.Membership.Roles.Select(r => r.RoleName).ToList(),
                x.Membership.TwoFactorRequiredSinceUtc,
            })
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

        return rows.Select(r => new OrganizationSummaryDto(r.Id, r.Name, r.Slug, r.Type, r.Status, r.Roles,
            Roles.PermissionsFor(r.Roles).Order().ToList(),
            r.TwoFactorRequiredSinceUtc is { } since ? DateTime.SpecifyKind(since, DateTimeKind.Utc) : null)).ToList();
    }

    public async Task<OrganizationDetailDto> GetCurrentAsync(CancellationToken ct)
    {
        var id = currentOrganization.RequireOrganizationId();
        var o = await db.Organizations.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Organization");
        return ToDetail(o);
    }

    public async Task<OrganizationDetailDto> UpdateCurrentAsync(UpdateOrganizationRequest request, CancellationToken ct)
    {
        var id = currentOrganization.RequireOrganizationId();
        var o = await db.Organizations.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Organization");
        var timeZone = string.IsNullOrWhiteSpace(request.TimeZone) ? o.TimeZone : request.TimeZone.Trim();
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _))
            throw new DomainException($"Unknown time zone '{timeZone}'.", "invalid_time_zone");

        var before = ToDetail(o);
        o.UpdateProfile(request.Name, request.ContactEmail, request.ContactPhone, request.Address,
            request.TaxIdentificationNumber, request.VatNumber, timeZone);
        audit.Record("organization.updated", nameof(Organization), o.Id.ToString(), details: new { before, after = ToDetail(o) });
        await db.SaveChangesAsync(ct);
        return ToDetail(o);
    }

    private async Task<string> UniqueSlugAsync(string name, CancellationToken ct)
    {
        var baseSlug = Organization.CreateSlug(name);
        var slug = baseSlug;
        for (var i = 2; await db.Organizations.AnyAsync(o => o.Slug == slug, ct); i++)
            slug = $"{baseSlug}-{i}";
        return slug;
    }

    private static OrganizationDetailDto ToDetail(Organization o) => new(o.Id, o.Name, o.Slug, o.Type, o.Status,
        o.BaseCurrency, o.TimeZone, o.ContactEmail, o.ContactPhone, o.Address, o.TaxIdentificationNumber, o.VatNumber,
        o.CreatedAtUtc);
}
