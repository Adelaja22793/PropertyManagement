using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Owners;

public enum BankAccountStatus { PendingApproval = 0, Approved = 1, Rejected = 2, Replaced = 3 }

/// <summary>
/// An account the owner's rent is paid out to. A new or changed account takes effect only after a second
/// person approves it (maker-checker, design 5 and 6.3), because a swapped account is a common fraud.
/// </summary>
public class OwnerBankAccount : IOrganizationScoped
{
    private OwnerBankAccount() { }

    public OwnerBankAccount(Guid organizationId, Guid ownerId, string bankName, string accountNumber, string accountName,
        Guid requestedByUserId, DateTime nowUtc)
    {
        var number = (accountNumber ?? "").Trim();
        // Nigerian bank accounts use the 10-digit NUBAN format.
        if (number.Length != 10 || !number.All(char.IsAsciiDigit))
            throw new DomainException("Account number must be 10 digits (NUBAN).", "invalid_account_number");
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        OwnerId = ownerId;
        BankName = Guard.Required(bankName, "Bank name", 100);
        AccountNumber = number;
        AccountName = Guard.Required(accountName, "Account name", 200);
        Status = BankAccountStatus.PendingApproval;
        RequestedByUserId = requestedByUserId;
        RequestedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid OwnerId { get; private set; }
    public string BankName { get; private set; } = "";
    public string AccountNumber { get; private set; } = "";
    public string AccountName { get; private set; } = "";
    public BankAccountStatus Status { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }
    public string? DecisionNote { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public string MaskedNumber => $"******{AccountNumber[^4..]}";

    public void Approve(Guid approverUserId, DateTime nowUtc) => Decide(BankAccountStatus.Approved, approverUserId, nowUtc, null);

    public void Reject(Guid approverUserId, DateTime nowUtc, string? note) =>
        Decide(BankAccountStatus.Rejected, approverUserId, nowUtc, Guard.Optional(note, "Note", 500));

    public void MarkReplaced()
    {
        if (Status == BankAccountStatus.Approved) Status = BankAccountStatus.Replaced;
    }

    private void Decide(BankAccountStatus outcome, Guid approverUserId, DateTime nowUtc, string? note)
    {
        if (Status != BankAccountStatus.PendingApproval)
            throw new DomainException("This bank account has already been decided.", "already_decided");
        if (approverUserId == RequestedByUserId)
            throw new DomainException("The person who added a bank account can't approve or reject it.", "maker_checker");
        Status = outcome;
        DecidedByUserId = approverUserId;
        DecidedAtUtc = nowUtc;
        DecisionNote = note;
    }
}
