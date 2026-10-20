using Microsoft.Extensions.Logging;
using PropertyManagement.Application.Abstractions;

namespace PropertyManagement.Infrastructure.Services;

/// <summary>
/// Development email sender: logs that a message would have been sent, without its body,
/// because bodies can carry single-use tokens. Replace with a real provider before production
/// (design 14.1, EmailProvider).
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        logger.LogInformation("Email to {To}: {Subject} ({Length} characters, body not logged)", to, subject, body.Length);
        return Task.CompletedTask;
    }
}
