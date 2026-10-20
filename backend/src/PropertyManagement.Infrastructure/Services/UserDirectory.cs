using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Infrastructure.Services;

public sealed class UserDirectory(AppDbContext db) : IUserDirectory, IUserLookup
{
    public async Task<IReadOnlyDictionary<Guid, UserSummary>> GetAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, UserSummary>();
        return await db.Users.Where(u => ids.Contains(u.Id))
            .Select(u => new UserSummary(u.Id, u.Email!, u.FullName))
            .ToDictionaryAsync(u => u.Id, ct);
    }

    public async Task<UserSummary?> FindByEmailAsync(string email, CancellationToken ct)
    {
        var normalized = email.Trim().ToUpperInvariant();
        return await db.Users.Where(u => u.NormalizedEmail == normalized)
            .Select(u => new UserSummary(u.Id, u.Email!, u.FullName))
            .SingleOrDefaultAsync(ct);
    }
}
