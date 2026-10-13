namespace LedgerCore.Domain.Common;

/// <summary>
/// Rules for monetary amounts. Amounts are stored as DECIMAL(19,4), so anything with more than
/// four decimal places or more than 15 integer digits cannot be persisted without silent rounding.
/// </summary>
public static class Money
{
    public const int Precision = 19;
    public const int Scale = 4;

    private const decimal MaxValue = 999_999_999_999_999.9999m;

    /// <summary>Validates that <paramref name="amount"/> is a strictly positive, storable amount.</summary>
    public static decimal EnsurePositive(decimal amount)
    {
        if (amount <= 0)
        {
            throw new DomainValidationException("Amount must be greater than zero.");
        }

        if (amount > MaxValue)
        {
            throw new DomainValidationException($"Amount must not exceed {MaxValue}.");
        }

        if (decimal.Round(amount, Scale) != amount)
        {
            throw new DomainValidationException($"Amount must have at most {Scale} decimal places.");
        }

        return amount;
    }
}
