using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Infrastructure.Persistence;
using PropertyManagement.Infrastructure.Services;

namespace PropertyManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Read when the first context is created, so configuration added by hosts and tests is seen.
        services.AddDbContext<AppDbContext>((sp, o) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Connection string 'Default' is not configured.");
            o.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(3));
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ICurrentOrganization, CurrentOrganization>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<UserDirectory>();
        services.AddScoped<IUserDirectory>(sp => sp.GetRequiredService<UserDirectory>());
        services.AddScoped<IUserLookup>(sp => sp.GetRequiredService<UserDirectory>());
        services.AddSingleton<IEmailSender, LoggingEmailSender>();
        return services;
    }
}
