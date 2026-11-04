using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Infrastructure.Email;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.IntegrationTests;

/// <summary>The email outbox: queued emails are stored encrypted, retried with growing waits, and given up on in the end.</summary>
public sealed class EmailOutboxTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_queued_email_is_stored_encrypted_and_sent_by_the_delivery()
    {
        var clock = new TestClock(new DateTime(2026, 11, 4, 9, 0, 0, DateTimeKind.Utc));
        var to = $"queued-{Guid.NewGuid():N}@example.com";
        var sender = new OutboxEmailSender(Scopes, Protection, clock, new EmailOutboxSignal());
        await sender.SendAsync(to, "Reset your password", "Your link: https://example.com/reset?token=secret-token", default);

        using (var scope = factory.Services.CreateScope())
        {
            var row = await Db(scope).OutboundEmails.SingleAsync(e => e.ToAddress == to);
            Assert.DoesNotContain("secret-token", row.ProtectedBody);
            Assert.True(row.IsPending);
        }

        var transport = new FakeTransport(failuresBeforeSuccess: 0);
        var result = await DeliverAsync(transport, clock);

        Assert.True(result.Sent >= 1);
        var message = Assert.Single(transport.Delivered, m => m.To == to);
        Assert.Contains("secret-token", message.Body);
        using (var scope = factory.Services.CreateScope())
        {
            var row = await Db(scope).OutboundEmails.SingleAsync(e => e.ToAddress == to);
            Assert.Equal(clock.UtcNow, row.SentAtUtc);
            Assert.Equal(1, row.Attempts);
        }

        // Nothing left to send for this address.
        await DeliverAsync(transport, clock);
        Assert.Single(transport.Delivered, m => m.To == to);
    }

    [Fact]
    public async Task A_failed_email_waits_before_its_retry_then_sends()
    {
        var clock = new TestClock(new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc));
        var to = $"retry-{Guid.NewGuid():N}@example.com";
        await new OutboxEmailSender(Scopes, Protection, clock, new EmailOutboxSignal()).SendAsync(to, "Hello", "Body", default);
        var transport = new FakeTransport(failuresBeforeSuccess: 1, onlyTo: to);

        await DeliverAsync(transport, clock);
        var row = await RowAsync(to);
        Assert.Equal(1, row.Attempts);
        Assert.Null(row.SentAtUtc);
        Assert.Equal(clock.UtcNow.AddMinutes(1), row.NextAttemptAtUtc);
        Assert.Contains("mail server down", row.LastError);

        // Not due yet, so nothing is tried.
        clock.Advance(TimeSpan.FromSeconds(30));
        await DeliverAsync(transport, clock);
        Assert.Equal(1, (await RowAsync(to)).Attempts);

        clock.Advance(TimeSpan.FromSeconds(31));
        await DeliverAsync(transport, clock);
        row = await RowAsync(to);
        Assert.Equal(2, row.Attempts);
        Assert.NotNull(row.SentAtUtc);
        Assert.Null(row.LastError);
        Assert.Single(transport.Delivered, m => m.To == to);
    }

    [Fact]
    public async Task An_email_that_keeps_failing_is_given_up_on_and_removed_after_30_days()
    {
        var clock = new TestClock(new DateTime(2026, 11, 6, 9, 0, 0, DateTimeKind.Utc));
        var to = $"bounce-{Guid.NewGuid():N}@example.com";
        await new OutboxEmailSender(Scopes, Protection, clock, new EmailOutboxSignal()).SendAsync(to, "Hello", "Body", default);
        var transport = new FakeTransport(failuresBeforeSuccess: int.MaxValue, onlyTo: to);

        for (var attempt = 1; attempt <= OutboundEmail.MaxAttempts; attempt++)
        {
            await DeliverAsync(transport, clock);
            clock.Advance(TimeSpan.FromHours(5));
        }

        var row = await RowAsync(to);
        Assert.Equal(OutboundEmail.MaxAttempts, row.Attempts);
        Assert.NotNull(row.GaveUpAtUtc);
        Assert.False(row.IsPending);

        // Given up: never tried again.
        await DeliverAsync(transport, clock);
        Assert.Equal(OutboundEmail.MaxAttempts, (await RowAsync(to)).Attempts);

        clock.Advance(TimeSpan.FromDays(31));
        await DeliverAsync(transport, clock);
        using var scope = factory.Services.CreateScope();
        Assert.False(await Db(scope).OutboundEmails.AnyAsync(e => e.ToAddress == to));
    }

    [Fact]
    public async Task Sent_emails_are_removed_after_7_days()
    {
        var clock = new TestClock(new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc));
        var to = $"old-{Guid.NewGuid():N}@example.com";
        await new OutboxEmailSender(Scopes, Protection, clock, new EmailOutboxSignal()).SendAsync(to, "Hello", "Body", default);
        await DeliverAsync(new FakeTransport(0), clock);

        clock.Advance(TimeSpan.FromDays(6));
        await DeliverAsync(new FakeTransport(0), clock);
        Assert.NotNull((await RowAsync(to)).SentAtUtc);

        clock.Advance(TimeSpan.FromDays(2));
        await DeliverAsync(new FakeTransport(0), clock);
        using var scope = factory.Services.CreateScope();
        Assert.False(await Db(scope).OutboundEmails.AnyAsync(e => e.ToAddress == to));
    }

    [Fact]
    public async Task The_smtp_transport_hands_the_email_to_the_mail_server()
    {
        await using var server = new FakeSmtpServer();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:FromAddress"] = "noreply@example.com",
            ["Email:FromName"] = "Lagos Homes",
            ["Email:Smtp:Host"] = "127.0.0.1",
            ["Email:Smtp:Port"] = server.Port.ToString(),
            ["Email:Smtp:Security"] = "None",
            ["Email:Smtp:TimeoutSeconds"] = "10",
        }).Build();

        await new SmtpEmailTransport(config).SendAsync("tenant@example.com", "Your lease ends soon", "Please get in touch.", default);

        var data = await server.ReceivedAsync;
        Assert.Contains("RCPT TO:<tenant@example.com>", data);
        Assert.Contains("MAIL FROM:<noreply@example.com>", data);
        Assert.Contains("Subject: Your lease ends soon", data);
        Assert.Contains("Please get in touch.", data);
    }

    [Fact]
    public async Task The_smtp_transport_throws_when_the_server_refuses_so_the_outbox_retries()
    {
        await using var server = new FakeSmtpServer(refuseRecipient: true);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:FromAddress"] = "noreply@example.com",
            ["Email:Smtp:Host"] = "127.0.0.1",
            ["Email:Smtp:Port"] = server.Port.ToString(),
            ["Email:Smtp:Security"] = "None",
            ["Email:Smtp:TimeoutSeconds"] = "10",
        }).Build();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            new SmtpEmailTransport(config).SendAsync("nobody@example.com", "Hello", "Body", default));
    }

    private IServiceScopeFactory Scopes => factory.Services.GetRequiredService<IServiceScopeFactory>();
    private IDataProtectionProvider Protection => factory.Services.GetRequiredService<IDataProtectionProvider>();
    private static AppDbContext Db(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<AppDbContext>();

    private async Task<EmailDelivery.Result> DeliverAsync(IEmailTransport transport, IClock clock)
    {
        using var scope = factory.Services.CreateScope();
        var delivery = new EmailDelivery(Db(scope), transport, Protection, clock, NullLogger<EmailDelivery>.Instance);
        return await delivery.DeliverDueAsync(100, default);
    }

    private async Task<OutboundEmail> RowAsync(string to)
    {
        using var scope = factory.Services.CreateScope();
        return await Db(scope).OutboundEmails.AsNoTracking().SingleAsync(e => e.ToAddress == to);
    }

    private sealed class TestClock(DateTime start) : IClock
    {
        public DateTime UtcNow { get; private set; } = start;
        public void Advance(TimeSpan by) => UtcNow += by;
    }

    /// <summary>Fails the first sends to <paramref name="onlyTo"/> (or to anyone), then succeeds.</summary>
    private sealed class FakeTransport(int failuresBeforeSuccess, string? onlyTo = null) : IEmailTransport
    {
        private int _failures;
        public List<(string To, string Subject, string Body)> Delivered { get; } = [];

        public Task SendAsync(string to, string subject, string body, CancellationToken ct)
        {
            if ((onlyTo is null || to == onlyTo) && _failures < failuresBeforeSuccess)
            {
                _failures++;
                throw new InvalidOperationException("mail server down");
            }
            Delivered.Add((to, subject, body));
            return Task.CompletedTask;
        }
    }

    /// <summary>Just enough of an SMTP server to accept, or refuse, one plain-text email.</summary>
    private sealed class FakeSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task<string> _serve;

        public FakeSmtpServer(bool refuseRecipient = false)
        {
            _listener.Start();
            _serve = ServeAsync(refuseRecipient);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public Task<string> ReceivedAsync => _serve.WaitAsync(TimeSpan.FromSeconds(15));

        private async Task<string> ServeAsync(bool refuseRecipient)
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            var log = new StringBuilder();
            await writer.WriteLineAsync("220 localhost ready");
            while (await reader.ReadLineAsync() is { } line)
            {
                log.AppendLine(line);
                var command = line.Length >= 4 ? line[..4].ToUpperInvariant() : line.ToUpperInvariant();
                switch (command)
                {
                    case "EHLO":
                    case "HELO":
                        await writer.WriteLineAsync("250 localhost");
                        break;
                    case "RCPT" when refuseRecipient:
                        await writer.WriteLineAsync("550 No such user");
                        break;
                    case "DATA":
                        await writer.WriteLineAsync("354 Send it");
                        while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".") log.AppendLine(dataLine);
                        await writer.WriteLineAsync("250 Queued");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 Bye");
                        return log.ToString();
                    default:
                        await writer.WriteLineAsync("250 OK");
                        break;
                }
            }
            return log.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try { await _serve.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception) { }
        }
    }
}
