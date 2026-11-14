using LedgerCore.Application.Ledger.Fees;
using LedgerCore.Domain.Common;
using LedgerCore.Domain.Ledger.Fees;

namespace LedgerCore.UnitTests.Application;

public class FeeScheduleTests
{
    [Fact]
    public void Each_currency_gets_the_strategy_its_rule_names()
    {
        var schedule = new FeeSchedule(new Dictionary<string, FeeRule>
        {
            ["EUR"] = new() { Type = FeeType.Percentage, Rate = 0.005m, Minimum = 0.50m },
            ["USD"] = new() { Type = FeeType.Flat, Amount = 1.00m },
            ["TRY"] = new() { Type = FeeType.Tiered, Tiers = [new() { UpTo = 1000m, Fee = 5m }, new() { Fee = 25m }] },
        });

        schedule.For(Currency.From("EUR")).Should().BeOfType<PercentageFee>();
        schedule.For(Currency.From("USD")).Should().BeOfType<FlatFee>();
        schedule.For(Currency.From("TRY")).Should().BeOfType<TieredFee>();
    }

    [Fact]
    public void A_currency_without_a_rule_pays_no_fee()
    {
        FeeSchedule.None.For(Currency.From("GBP")).Should().BeSameAs(NoFee.Instance);
    }

    [Fact]
    public void An_invalid_rule_fails_at_startup_and_names_its_currency()
    {
        var act = () => new FeeSchedule(new Dictionary<string, FeeRule>
        {
            ["USD"] = new() { Type = FeeType.Flat },
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*USD*Amount is required*");
    }
}
