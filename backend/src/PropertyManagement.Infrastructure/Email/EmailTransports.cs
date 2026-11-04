using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace PropertyManagement.Infrastructure.Email;

/// <summary>Hands one email to a mail server. Throws when the server refuses it, so the outbox retries.</summary>
public interface IEmailTransport
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

/// <summary>
/// Sends through any SMTP provider (design 14.1), configured under Email:Smtp. Credentials belong in user secrets or
/// the production secrets manager, never in appsettings files.
/// </summary>
public sealed class SmtpEmailTransport(IConfiguration config) : IEmailTransport
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var smtp = config.GetSection("Email:Smtp");
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(config["Email:FromName"] ?? "Property Management", config["Email:FromAddress"]
            ?? throw new InvalidOperationException("Email:FromAddress is not configured.")));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        client.Timeout = (int)TimeSpan.FromSeconds(smtp.GetValue("TimeoutSeconds", 30)).TotalMilliseconds;
        var security = smtp.GetValue("Security", "StartTls") switch
        {
            "SslOnConnect" => SecureSocketOptions.SslOnConnect,
            "None" => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTls,
        };
        await client.ConnectAsync(smtp["Host"] ?? throw new InvalidOperationException("Email:Smtp:Host is not configured."), smtp.GetValue("Port", 587), security, ct);
        var username = smtp["Username"];
        if (!string.IsNullOrEmpty(username)) await client.AuthenticateAsync(username, smtp["Password"] ?? "", ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}

/// <summary>
/// For development and tests when no mail server is set: logs that a message would have been sent, without its body,
/// because bodies can carry single-use tokens.
/// </summary>
public sealed class LoggingEmailTransport(ILogger<LoggingEmailTransport> logger) : IEmailTransport
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        logger.LogInformation("Email to {To}: {Subject} ({Length} characters, body not logged)", to, subject, body.Length);
        return Task.CompletedTask;
    }
}
