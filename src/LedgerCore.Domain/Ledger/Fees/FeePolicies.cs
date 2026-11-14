using LedgerCore.Domain.Common;

namespace LedgerCore.Domain.Ledger.Fees;

/// <summary>
/// Strategy for the fee charged on a transfer. Each currency has one policy, chosen from
/// configuration, so the transfer itself never needs to know how the fee is worked out.
/// </summary>
public interface IFeePolicy
{
    /// <summary>The fee for moving <paramref name="amount"/>, rounded to cents. Zero means no fee.</summary>
    decimal Calculate(decimal amount);
}

/// <summary>Charges nothing. Used for currencies without a configured fee.</summary>
public sealed class NoFee : IFeePolicy
{
    public static NoFee Instance { get; } = new();

    public decimal Calculate(decimal amount) => 0m;
}

/// <summary>The same fee for every transfer, e.g. 1.00 USD.</summary>
public sealed class FlatFee : IFeePolicy
{
    private readonly decimal _fee;

    public FlatFee(decimal fee)
    {
        _fee = FeeRounding.EnsureValidFee(fee, nameof(fee));
    }

    public decimal Calculate(decimal amount) => _fee;
}

/// <summary>A share of the amount, e.g. 0.5%, kept between an optional minimum and maximum.</summary>
public sealed class PercentageFee : IFeePolicy
{
    private readonly decimal _rate;
    private readonly decimal _minimum;
    private readonly decimal? _maximum;

    public PercentageFee(decimal rate, decimal minimum = 0m, decimal? maximum = null)
    {
        if (rate is <= 0m or >= 1m)
        {
            throw new DomainValidationException("A percentage fee rate must be between 0 and 1, e.g. 0.005 for 0.5%.");
        }

        _rate = rate;
        _minimum = FeeRounding.EnsureValidFee(minimum, nameof(minimum));
        _maximum = maximum is { } max ? FeeRounding.EnsureValidFee(max, nameof(maximum)) : null;

        if (_maximum < _minimum)
        {
            throw new DomainValidationException("A percentage fee's maximum must not be below its minimum.");
        }
    }

    public decimal Calculate(decimal amount)
    {
        var fee = Math.Max(FeeRounding.Round(amount * _rate), _minimum);
        return _maximum is { } max ? Math.Min(fee, max) : fee;
    }
}

/// <summary>
/// A flat fee that depends on the amount band, e.g. 5 up to 1,000, then 15 up to 10,000, then 25.
/// Each tier applies to amounts up to and including its limit; the last tier has no limit.
/// </summary>
public sealed class TieredFee : IFeePolicy
{
    private readonly IReadOnlyList<FeeTier> _tiers;

    public TieredFee(IEnumerable<FeeTier> tiers)
    {
        var ordered = tiers.ToList();

        if (ordered.Count == 0 || ordered[^1].UpTo is not null)
        {
            throw new DomainValidationException("A tiered fee needs at least one tier, and the last tier must have no limit.");
        }

        var previousLimit = 0m;
        foreach (var tier in ordered.SkipLast(1))
        {
            if (tier.UpTo is not { } limit || limit <= previousLimit)
            {
                throw new DomainValidationException("Tier limits must be positive and strictly increasing.");
            }

            previousLimit = limit;
        }

        foreach (var tier in ordered)
        {
            FeeRounding.EnsureValidFee(tier.Fee, "tier fee");
        }

        _tiers = ordered;
    }

    public decimal Calculate(decimal amount) => _tiers.First(t => t.UpTo is null || amount <= t.UpTo).Fee;
}

/// <param name="UpTo">The largest amount this tier covers, or null for the last tier.</param>
public sealed record FeeTier(decimal? UpTo, decimal Fee);

internal static class FeeRounding
{
    // Fees are charged in whole cents. Half a cent rounds up, the way a customer would expect
    // (banker's rounding, .NET's default, would sometimes round it down).
    public static decimal Round(decimal fee) => decimal.Round(fee, 2, MidpointRounding.AwayFromZero);

    public static decimal EnsureValidFee(decimal fee, string name)
    {
        if (fee < 0m || Round(fee) != fee)
        {
            throw new DomainValidationException($"The {name} must be zero or more, in whole cents.");
        }

        return fee;
    }
}
