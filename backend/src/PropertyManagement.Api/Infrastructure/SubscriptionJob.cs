using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Platform;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Api.Infrastructure;

/// <summary>
/// Moves each company's subscription on when its trial or paid-up date passes (design 4.8): past due with a grace
/// period, then read-only, emailing the admins once at each stage. Runs shortly after start-up and then every hour,
/// one organization at a time. Safe to repeat. Turn off with Jobs:Subscriptions:Enabled = false.
/// </summary>
public sealed class SubscriptionJob(IServiceScopeFactory scopes, IConfiguration config, ILogger<SubscriptionJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Jobs:Subscriptions:Enabled", true)) return;
        var interval = TimeSpan.FromMinutes(config.GetValue("Jobs:Subscriptions:IntervalMinutes", 60));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(config.GetValue("Jobs:Subscriptions:StartDelaySeconds", 45)), stoppingToken);
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
                if (await scope.ServiceProvider.GetRequiredService<SubscriptionService>().RefreshAsync(ct))
                    logger.LogInformation("Updated the subscription of {OrganizationId}", organizationId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Subscription refresh failed for {OrganizationId}", organizationId);
            }
        }
    }
}
