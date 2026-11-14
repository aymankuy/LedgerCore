using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;

namespace LedgerCore.UnitTests.Domain;

/// <summary>Shared builders so each test states only the detail it cares about.</summary>
internal static class TestAccounts
{
    public static readonly DateTime Now = new(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);
    public static readonly Currency Eur = Currency.From("EUR");

    public static Account Customer(string currency = "EUR", bool allowsOverdraft = false) =>
        Account.Open(Guid.NewGuid(), Currency.From(currency), Now, allowsOverdraft);

    public static Account Settlement(string currency = "EUR") =>
        Account.OpenSettlement(Guid.NewGuid(), Currency.From(currency), Now);

    public static Account FeeRevenue(string currency = "EUR") =>
        Account.OpenFeeRevenue(Guid.NewGuid(), Currency.From(currency), Now);
}
