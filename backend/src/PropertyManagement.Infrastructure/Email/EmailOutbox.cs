using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Infrastructure.Email;

/// <summary>Wakes the delivery job as soon as an email is queued, so people don't wait for its next round.</summary>
public sealed class EmailOutboxSignal
{
    private readonly SemaphoreSlim _queued = new(0, 1);

    public void Notify()
    {
        if (_queued.CurrentCount == 0)
        {
            try { _queued.Release(); }
            catch (SemaphoreFullException) { }
        }
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _queued.WaitAsync(timeout, ct);
}

/// <summary>
/// The app's <see cref="IEmailSender"/>: saves the email to the outbox straight away, in its own database scope so it
/// doesn't depend on the caller saving, and leaves sending to <see cref="EmailDelivery"/>.
/// </summary>
public sealed class OutboxEmailSender(IServiceScopeFactory scopes, IDataProtectionProvider protection, IClock clock, EmailOutboxSignal signal)
    : IEmailSender
{
    public const string ProtectionPurpose = "PropertyManagement.EmailOutbox.v1";
    private readonly IDataProtector _protector = protection.CreateProtector(ProtectionPurpose);

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.OutboundEmails.Add(new OutboundEmail(to, subject, _protector.Protect(body), clock.UtcNow));
        await db.SaveChangesAsync(ct);
        signal.Notify();
    }
}

/// <summary>
/// Sends due emails from the outbox, retrying failures with growing waits, and removes old ones: sent emails after
/// 7 days and abandoned ones after 30, since their bodies can hold personal data and links.
/// </summary>
public sealed class EmailDelivery(AppDbContext db, IEmailTransport transport, IDataProtectionProvider protection, IClock clock,
    ILogger<EmailDelivery> logger)
{
    private static readonly TimeSpan ClaimHold = TimeSpan.FromMinutes(5);
    private readonly IDataProtector _protector = protection.CreateProtector(OutboxEmailSender.ProtectionPurpose);

    public sealed record Result(int Sent, int Failed);

    public async Task<Result> DeliverDueAsync(int batchSize, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await db.OutboundEmails
            .Where(e => e.SentAtUtc == null && e.GaveUpAtUtc == null && e.NextAttemptAtUtc <= now)
            .OrderBy(e => e.NextAttemptAtUtc).Take(batchSize).ToListAsync(ct);
        int sent = 0, failed = 0;
        foreach (var email in due)
        {
            email.Claim(clock.UtcNow, ClaimHold);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another instance claimed it first.
                db.Entry(email).State = EntityState.Detached;
                continue;
            }

            try
            {
                await transport.SendAsync(email.ToAddress, email.Subject, _protector.Unprotect(email.ProtectedBody), ct);
                email.MarkSent(clock.UtcNow);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                email.MarkFailed($"{ex.GetType().Name}: {ex.Message}", clock.UtcNow);
                failed++;
                if (email.GaveUpAtUtc is not null)
                    logger.LogError("Gave up sending email {EmailId} to {To} after {Attempts} attempts: {Error}",
                        email.Id, email.ToAddress, email.Attempts, email.LastError);
                else
                    logger.LogWarning("Sending email {EmailId} failed (attempt {Attempts}), retrying at {Next}: {Error}",
                        email.Id, email.Attempts, email.NextAttemptAtUtc, email.LastError);
            }
            await db.SaveChangesAsync(ct);
        }

        await db.OutboundEmails.Where(e => e.SentAtUtc != null && e.SentAtUtc < now.AddDays(-7)).ExecuteDeleteAsync(ct);
        await db.OutboundEmails.Where(e => e.GaveUpAtUtc != null && e.GaveUpAtUtc < now.AddDays(-30)).ExecuteDeleteAsync(ct);
        return new Result(sent, failed);
    }
}
