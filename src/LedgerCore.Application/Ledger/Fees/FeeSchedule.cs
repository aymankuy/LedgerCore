using LedgerCore.Domain.Common;
using LedgerCore.Domain.Ledger.Fees;

namespace LedgerCore.Application.Ledger.Fees;

/// <summary>
/// The transfer fee policy of every currency, built once from configuration at startup. This is
/// where the strategy is chosen; a currency without a rule pays no fee. An invalid rule throws
/// here, so a misconfigured fee stops the app from starting instead of charging the wrong amount.
/// </summary>
public sealed class FeeSchedule
{
    private readonly Dictionary<Currency, IFeePolicy> _policies;

    public FeeSchedule(IReadOnlyDictionary<string, FeeRule> rules)
    {
        _policies = rules.ToDictionary(r => Currency.From(r.Key), r => ToPolicy(r.Key, r.Value));
    }

    public static FeeSchedule None { get; } = new(new Dictionary<string, FeeRule>());

    public IFeePolicy For(Currency currency) => _policies.GetValueOrDefault(currency, NoFee.Instance);

    private static IFeePolicy ToPolicy(string currency, FeeRule rule)
    {
        try
        {
            return rule.Type switch
            {
                FeeType.Flat => new FlatFee(Required(rule.Amount, nameof(rule.Amount))),
                FeeType.Percentage => new PercentageFee(Required(rule.Rate, nameof(rule.Rate)), rule.Minimum ?? 0m, rule.Maximum),
                FeeType.Tiered => new TieredFee(rule.Tiers.Select(t => new FeeTier(t.UpTo, t.Fee))),
                _ => throw new DomainValidationException($"Unknown fee type '{rule.Type}'."),
            };
        }
        catch (DomainValidationException e)
        {
            throw new InvalidOperationException($"The fee rule for {currency} is invalid: {e.Message}", e);
        }
    }

    private static decimal Required(decimal? value, string name) =>
        value ?? throw new DomainValidationException($"{name} is required.");
}

/// <summary>One currency's fee rule, as written in the "Fees" configuration section.</summary>
public sealed class FeeRule
{
    public FeeType Type { get; set; }

    /// <summary>Flat: the fee.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Percentage: the rate, e.g. 0.005 for 0.5%.</summary>
    public decimal? Rate { get; set; }

    /// <summary>Percentage: the smallest fee charged.</summary>
    public decimal? Minimum { get; set; }

    /// <summary>Percentage: the largest fee charged, if any.</summary>
    public decimal? Maximum { get; set; }

    /// <summary>Tiered: the amount bands, the last one without a limit.</summary>
    public List<FeeTierRule> Tiers { get; set; } = [];
}

public sealed class FeeTierRule
{
    public decimal? UpTo { get; set; }
    public decimal Fee { get; set; }
}

public enum FeeType
{
    Flat = 1,
    Percentage = 2,
    Tiered = 3,
}
