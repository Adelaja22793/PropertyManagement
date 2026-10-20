using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Organizations;

namespace PropertyManagement.Application.Branches;

public sealed record BranchDto(Guid Id, string Name, string? Address, bool IsActive, DateTime CreatedAtUtc);

public sealed record SaveBranchRequest(string Name, string? Address, bool IsActive = true);

public sealed class BranchService(IAppDbContext db, ICurrentOrganization currentOrganization, IAuditLogger audit)
{
    /// <summary>Branches visible to the caller: all of them, or only their scoped branches.</summary>
    public async Task<IReadOnlyList<BranchDto>> ListAsync(CancellationToken ct) =>
        await Visible().OrderBy(b => b.Name).Select(b => ToDto(b)).ToListAsync(ct);

    public async Task<BranchDto> GetAsync(Guid id, CancellationToken ct) =>
        ToDto(await Visible().SingleOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Branch"));

    public async Task<BranchDto> CreateAsync(SaveBranchRequest request, CancellationToken ct)
    {
        var organizationId = currentOrganization.RequireOrganizationId();
        await EnsureUniqueNameAsync(request.Name, null, ct);
        var branch = new Branch(organizationId, request.Name, request.Address);
        db.Branches.Add(branch);
        audit.Record("branch.created", nameof(Branch), branch.Id.ToString(), details: new { branch.Name });
        await db.SaveChangesAsync(ct);
        return ToDto(branch);
    }

    public async Task<BranchDto> UpdateAsync(Guid id, SaveBranchRequest request, CancellationToken ct)
    {
        var branch = await Visible().SingleOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Branch");
        await EnsureUniqueNameAsync(request.Name, id, ct);
        var before = ToDto(branch);
        branch.Update(request.Name, request.Address, request.IsActive);
        audit.Record("branch.updated", nameof(Branch), branch.Id.ToString(), details: new { before, after = ToDto(branch) });
        await db.SaveChangesAsync(ct);
        return ToDto(branch);
    }

    private IQueryable<Branch> Visible()
    {
        var scope = currentOrganization.BranchScope;
        var orgWide = scope.Count == 0 || currentOrganization.Roles.Overlaps(Roles.OrganizationWideRoles);
        return orgWide ? db.Branches : db.Branches.Where(b => scope.Contains(b.Id));
    }

    private async Task EnsureUniqueNameAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var trimmed = name.Trim();
        if (await db.Branches.AnyAsync(b => b.Name == trimmed && b.Id != exceptId, ct))
            throw new DomainException($"A branch called '{trimmed}' already exists.", "duplicate_branch");
    }

    private static BranchDto ToDto(Branch b) => new(b.Id, b.Name, b.Address, b.IsActive, b.CreatedAtUtc);
}
