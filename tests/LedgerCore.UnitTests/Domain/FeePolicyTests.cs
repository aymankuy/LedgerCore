using System.Globalization;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;
using LedgerCore.Domain.Ledger;
using LedgerCore.Domain.Ledger.Fees;

namespace LedgerCore.UnitTests.Domain;

public class FeePolicyTests
{
    [Theory]
    [InlineData("10", "1.00")]
    [InlineData("100000", "1.00")]
    public void Flat_fee_is_the_same_for_every_amount(string amount, string expected)
    {
        new FlatFee(1.00m).Calculate(decimal.Parse(amount, CultureInfo.InvariantCulture))
            .Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("1000", "5.00")] // 0.5% of 1000
    [InlineData("50", "0.50")] // 0.25 is below the minimum
    [InlineData("101", "0.51")] // 0.505 rounds half up, not to the even 0.50
    [InlineData("100000", "20.00")] // 500 is capped at the maximum
    public void Percentage_fee_is_rounded_to_cents_and_kept_between_minimum_and_maximum(string amount, string expected)
    {
        var policy = new PercentageFee(rate: 0.005m, minimum: 0.50m, maximum: 20.00m);

        policy.Calculate(decimal.Parse(amount, CultureInfo.InvariantCulture))
            .Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("1000", "5.00")] // a limit belongs to its own tier
    [InlineData("1000.01", "15.00")]
    [InlineData("10000", "15.00")]
    [InlineData("10000.01", "25.00")]
    public void Tiered_fee_depends_on_the_amount_band(string amount, string expected)
    {
        var policy = new TieredFee([new(1000m, 5m), new(10000m, 15m), new(null, 25m)]);

        policy.Calculate(decimal.Parse(amount, CultureInfo.InvariantCulture))
            .Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void No_fee_charges_nothing()
    {
        NoFee.Instance.Calculate(500m).Should().Be(0m);
    }

    public static TheoryData<Func<IFeePolicy>> InvalidPolicies => new()
    {
        () => new FlatFee(-1m),
        () => new FlatFee(0.001m),
        () => new PercentageFee(0m),
        () => new PercentageFee(1m),
        () => new PercentageFee(0.01m, minimum: 5m, maximum: 1m),
        () => new TieredFee([]),
        () => new TieredFee([new(1000m, 5m)]),
        () => new TieredFee([new(1000m, 5m), new(500m, 10m), new(null, 15m)]),
    };

    [Theory]
    [MemberData(nameof(InvalidPolicies))]
    public void Invalid_policies_are_rejected(Func<IFeePolicy> create)
    {
        create.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Fee_transaction_debits_the_payer_and_credits_fee_revenue()
    {
        var payer = TestAccounts.Customer();
        var feeRevenue = TestAccounts.FeeRevenue();
        var transferId = Guid.NewGuid();

        var fee = LedgerTransaction.Fee(payer, payerBalance: 10m, feeRevenue, 0.50m, TestAccounts.Now, transferId);

        fee.Type.Should().Be(TransactionType.Fee);
        fee.ReferenceId.Should().Be(transferId.ToString());
        fee.Net.Should().Be(0m);
        fee.Entries.Should().ContainSingle(e => e.AccountId == payer.Id && e.Direction == EntryDirection.Debit && e.Amount == 0.50m);
        fee.Entries.Should().ContainSingle(e => e.AccountId == feeRevenue.Id && e.Direction == EntryDirection.Credit && e.Amount == 0.50m);
    }

    [Fact]
    public void Fee_above_the_remaining_balance_is_rejected()
    {
        var act = () => LedgerTransaction.Fee(
            TestAccounts.Customer(), payerBalance: 0.20m, TestAccounts.FeeRevenue(), 0.50m, TestAccounts.Now, Guid.NewGuid());

        act.Should().Throw<InsufficientFundsException>();
    }

    [Fact]
    public void Fee_credited_to_any_other_account_kind_is_rejected()
    {
        var act = () => LedgerTransaction.Fee(
            TestAccounts.Customer(), payerBalance: 10m, TestAccounts.Settlement(), 0.50m, TestAccounts.Now, Guid.NewGuid());

        act.Should().Throw<DomainValidationException>();
    }
}
