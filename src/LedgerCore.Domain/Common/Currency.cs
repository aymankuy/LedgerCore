namespace LedgerCore.Domain.Common;

/// <summary>ISO 4217 alphabetic currency code, e.g. "EUR", "TRY".</summary>
public readonly record struct Currency
{
    public string Code { get; }

    private Currency(string code) => Code = code;

    public static Currency From(string? code)
    {
        if (code is not { Length: 3 } || !code.All(char.IsAsciiLetterUpper))
        {
            throw new DomainValidationException($"'{code}' is not a valid ISO 4217 currency code.");
        }

        return new Currency(code);
    }

    public override string ToString() => Code;
}
