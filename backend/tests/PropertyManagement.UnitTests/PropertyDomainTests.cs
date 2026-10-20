using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Owners;
using PropertyManagement.Domain.Properties;
using PropertyManagement.Domain.Tenants;

namespace PropertyManagement.UnitTests;

public sealed class PropertyDomainTests
{
    private static readonly Guid Org = Guid.NewGuid();

    [Fact]
    public void Ownership_set_must_total_exactly_100()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        PropertyOwnership.ValidateSet([(a, 33.33m), (b, 66.67m)]);
        Assert.Throws<DomainException>(() => PropertyOwnership.ValidateSet([(a, 33.33m), (b, 66.66m)]));
        Assert.Throws<DomainException>(() => PropertyOwnership.ValidateSet([(a, 50m), (a, 50m)]));
        Assert.Throws<DomainException>(() => PropertyOwnership.ValidateSet([]));
    }

    [Fact]
    public void Ownership_share_must_have_at_most_two_decimals() =>
        Assert.Throws<DomainException>(() => new PropertyOwnership(Org, Guid.NewGuid(), Guid.NewGuid(), 33.333m, new DateOnly(2026, 1, 1)));

    [Fact]
    public void Bank_account_cannot_be_approved_by_the_person_who_added_it()
    {
        var maker = Guid.NewGuid();
        var account = new OwnerBankAccount(Org, Guid.NewGuid(), "GTBank", "0123456789", "Ada Obi", maker, DateTime.UtcNow);
        var error = Assert.Throws<DomainException>(() => account.Approve(maker, DateTime.UtcNow));
        Assert.Equal("maker_checker", error.Code);

        account.Approve(Guid.NewGuid(), DateTime.UtcNow);
        Assert.Equal(BankAccountStatus.Approved, account.Status);
        Assert.Equal("******6789", account.MaskedNumber);
        Assert.Throws<DomainException>(() => account.Reject(Guid.NewGuid(), DateTime.UtcNow, null));
    }

    [Theory]
    [InlineData("012345678")]
    [InlineData("01234567890")]
    [InlineData("01234abcde")]
    public void Bank_account_number_must_be_a_ten_digit_nuban(string number) =>
        Assert.Throws<DomainException>(() => new OwnerBankAccount(Org, Guid.NewGuid(), "UBA", number, "X", Guid.NewGuid(), DateTime.UtcNow));

    [Fact]
    public void Kyc_keeps_only_last_four_digits_in_clear_and_resets_verification()
    {
        var kyc = new TenantKyc(Org, Guid.NewGuid());
        kyc.SetIdentity(IdentityDocumentType.Nin, "123 4567 8901", null, v => $"enc({v})", DateTime.UtcNow);
        Assert.Equal("enc(12345678901)", kyc.IdNumberEncrypted);
        Assert.Equal("8901", kyc.IdNumberLast4);

        kyc.SetVerification(VerificationStatus.Verified, Guid.NewGuid(), DateTime.UtcNow);
        kyc.SetIdentity(IdentityDocumentType.Nin, "10987654321", null, v => v, DateTime.UtcNow);
        Assert.Equal(VerificationStatus.Unverified, kyc.Status);
        Assert.Null(kyc.VerifiedByUserId);
    }

    [Fact]
    public void Kyc_needs_both_id_type_and_number() =>
        Assert.Throws<DomainException>(() =>
            new TenantKyc(Org, Guid.NewGuid()).SetIdentity(null, "12345678901", null, v => v, DateTime.UtcNow));

    [Fact]
    public void Occupied_unit_cannot_be_archived()
    {
        var details = new UnitDetails("a1", UnitType.Flat, null, 2, 2, null, 2_000_000m, "ngn", BillingFrequency.Annual, null, null, null);
        var unit = new PropertyUnit(Org, Guid.NewGuid(), details);
        Assert.Equal("A1", unit.Code);
        Assert.Equal("NGN", unit.Currency);
        unit.Update(details, OccupancyStatus.Occupied);
        Assert.Throws<DomainException>(unit.Archive);
    }

    [Fact]
    public void Owner_needs_an_email_or_phone() =>
        Assert.Throws<DomainException>(() => new Owner(Org,
            new OwnerDetails(PartyType.Individual, "No Contact", null, null, null, null, ContactChannel.Email, null)));

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("two@@example.com")]
    public void Invalid_emails_are_refused(string email) =>
        Assert.Throws<DomainException>(() => Contact.Email(email));

    [Fact]
    public void Percentage_fee_cannot_exceed_100()
    {
        var terms = new AgreementTerms(null, ManagementFeeBasis.PercentOfRentCollected, 120m, null, PayoutFrequency.Monthly,
            AdvanceRentRelease.OnReceipt, 0m, new DateOnly(2026, 1, 1), null, null);
        Assert.Throws<DomainException>(() => new ManagementAgreement(Org, Guid.NewGuid(), terms));
        var fixedFee = terms with { FeeBasis = ManagementFeeBasis.FixedAmountPerMonth, FeeValue = 150_000m };
        Assert.Equal(150_000m, new ManagementAgreement(Org, Guid.NewGuid(), fixedFee).FeeValue);
    }
}
