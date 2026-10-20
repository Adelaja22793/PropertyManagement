using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PropertyManagement.Infrastructure.Services;

namespace PropertyManagement.Infrastructure.Persistence;

/// <summary>Used only by the dotnet-ef tool to create migrations.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=PropertyManagement_Design;Trusted_Connection=False")
            .Options;
        return new AppDbContext(options, new CurrentOrganization());
    }
}
