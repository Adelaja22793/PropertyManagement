using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Leases;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Api.Infrastructure;

/// <summary>
/// Date-driven lease work. Keeps unit occupancy in step with lease dates (a lease that starts today makes its unit
/// occupied, one that has ended frees it) and sends due lease reminders. Runs shortly after start-up and then every
/// hour, one organization at a time, each in its own scope so the usual isolation rules apply. Both steps are safe
/// to repeat. Turn off with Jobs:Leases:Enabled = false.
/// </summary>
public sealed class LeaseScheduleJob(IServiceScopeFactory scopes, IConfiguration config, ILogger<LeaseScheduleJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Jobs:Leases:Enabled", true)) return;
        var interval = TimeSpan.FromMinutes(config.GetValue("Jobs:Leases:IntervalMinutes", 60));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(config.GetValue("Jobs:Leases:StartDelaySeconds", 30)), stoppingToken);
            using var timer = new PeriodicTimer(interval);
            do
            {
                await RunOnceAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        List<Guid> organizationIds;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            organizationIds = await db.Organizations.Select(o => o.Id).ToListAsync(ct);
        }

        foreach (var organizationId in organizationIds)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ICurrentOrganization>().Set(organizationId, null, [], []);
                var changed = await scope.ServiceProvider.GetRequiredService<LeaseService>().RefreshOccupancyAsync(ct);
                if (changed > 0) logger.LogInformation("Updated occupancy of {Count} units in {OrganizationId}", changed, organizationId);
                var reminders = await scope.ServiceProvider.GetRequiredService<LeaseReminderService>().SendDueAsync(ct);
                if (reminders > 0) logger.LogInformation("Sent {Count} lease reminders in {OrganizationId}", reminders, organizationId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One organization's failure must not stop the others.
                logger.LogError(ex, "Lease schedule failed for {OrganizationId}", organizationId);
            }
        }
    }
}
