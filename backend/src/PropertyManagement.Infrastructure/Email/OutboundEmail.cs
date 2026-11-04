namespace PropertyManagement.Infrastructure.Email;

/// <summary>
/// An email waiting to be sent, or sent recently (the outbox). Writing the email here first means a slow or failed
/// mail server never fails the request that sent it, and failed sends are retried. The body is encrypted, because
/// it can carry a single-use link such as a password reset.
/// </summary>
public class OutboundEmail
{
    public const int MaxAttempts = 8;

    /// <summary>Wait after each failed attempt: 1 minute, then 5, 15, 30, 1 hour, 2, 4 hours.</summary>
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(4),
    ];

    private OutboundEmail() { }

    public OutboundEmail(string toAddress, string subject, string protectedBody, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        ToAddress = toAddress.Trim();
        Subject = subject.Length > 300 ? subject[..300] : subject;
        ProtectedBody = protectedBody;
        CreatedAtUtc = nowUtc;
        NextAttemptAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }
    public string ToAddress { get; private set; } = "";
    public string Subject { get; private set; } = "";
    public string ProtectedBody { get; private set; } = "";
    public DateTime CreatedAtUtc { get; private set; }
    public int Attempts { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    /// <summary>Set when every attempt failed. The email is then kept for staff to look into, not retried.</summary>
    public DateTime? GaveUpAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsPending => SentAtUtc is null && GaveUpAtUtc is null;

    /// <summary>
    /// Holds the email for one sender for <paramref name="hold"/>, so another API instance doesn't send it too. If the
    /// sender dies mid-send, the email becomes due again when the hold ends.
    /// </summary>
    public void Claim(DateTime nowUtc, TimeSpan hold) => NextAttemptAtUtc = nowUtc + hold;

    public void MarkSent(DateTime nowUtc)
    {
        Attempts++;
        SentAtUtc = nowUtc;
        LastError = null;
    }

    public void MarkFailed(string error, DateTime nowUtc)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
        if (Attempts >= MaxAttempts) GaveUpAtUtc = nowUtc;
        else NextAttemptAtUtc = nowUtc + Backoff[Math.Min(Attempts - 1, Backoff.Length - 1)];
    }
}
