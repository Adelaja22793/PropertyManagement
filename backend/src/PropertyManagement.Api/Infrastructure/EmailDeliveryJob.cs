using PropertyManagement.Infrastructure.Email;

namespace PropertyManagement.Api.Infrastructure;

/// <summary>
/// Sends queued emails: straight away when one is queued, and otherwise every Jobs:Email:IntervalSeconds (15) to
/// pick up retries. Safe to run on several API instances, since each email is claimed before it is sent. Turn off
/// with Jobs:Email:Enabled = false.
/// </summary>
public sealed class EmailDeliveryJob(IServiceScopeFactory scopes, EmailOutboxSignal signal, IConfiguration config,
    ILogger<EmailDeliveryJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Jobs:Email:Enabled", true)) return;
        var interval = TimeSpan.FromSeconds(config.GetValue("Jobs:Email:IntervalSeconds", 15));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                await signal.WaitAsync(interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<EmailDelivery>().DeliverDueAsync(50, ct);
            if (result.Sent + result.Failed > 0)
                logger.LogInformation("Email outbox: {Sent} sent, {Failed} failed", result.Sent, result.Failed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A database outage must not stop the job; the next round tries again.
            logger.LogError(ex, "Email delivery round failed");
        }
    }
}
