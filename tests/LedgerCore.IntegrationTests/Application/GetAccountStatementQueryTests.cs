using FluentAssertions;
using LedgerCore.Application.Accounts.GetStatement;
using LedgerCore.Application.Common;
using LedgerCore.Application.Ledger.Deposit;
using LedgerCore.Application.Ledger.Withdraw;
using LedgerCore.Domain.Ledger;
using LedgerCore.IntegrationTests.Infrastructure;

namespace LedgerCore.IntegrationTests.Application;

[Collection(SqlServerCollection.Name)]
public sealed class GetAccountStatementQueryTests(SqlServerFixture fixture)
{
    // A currency no other test class uses, so this class owns its settlement account.
    private const string Currency = "SEK";

    private static readonly DateTime Day1 = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = Day1.AddDays(1);
    private static readonly DateTime Day3 = Day1.AddDays(2);
    private static readonly DateTime Day4 = Day1.AddDays(3);

    private readonly LedgerTestApp _app = new(fixture);

    [Fact]
    public async Task Statement_starts_from_the_opening_balance_and_runs_line_by_line()
    {
        var accountId = await _app.OpenAccountAsync(Currency);
        await _app.SendAsync(new DepositCommand(accountId, 100m), Day1);
        await _app.SendAsync(new WithdrawCommand(accountId, 30m, Description: "ATM"), Day2);
        await _app.SendAsync(new DepositCommand(accountId, 50m, ReferenceId: "psp-42"), Day3);
        await _app.SendAsync(new WithdrawCommand(accountId, 20m), Day4);

        // Day 1 falls before the window and Day 4 on its exclusive upper bound.
        var statement = await _app.SendAsync(new GetAccountStatementQuery(accountId, Day2, Day4));

        statement.Currency.Should().Be(Currency);
        statement.OpeningBalance.Should().Be(100m);
        statement.ClosingBalance.Should().Be(120m);
        statement.Lines.Select(l => (l.Type, l.Amount, l.BalanceAfter)).Should().Equal(
            (TransactionType.Withdrawal, -30m, 70m),
            (TransactionType.Deposit, 50m, 120m));
        statement.Lines[0].Description.Should().Be("ATM");
        statement.Lines[1].ReferenceId.Should().Be("psp-42");
        statement.Lines[0].CreatedAtUtc.Should().Be(Day2);
        statement.Lines[0].CreatedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Empty_window_closes_at_the_opening_balance()
    {
        var accountId = await _app.OpenAccountAsync(Currency);
        await _app.SendAsync(new DepositCommand(accountId, 75m), Day1);

        var statement = await _app.SendAsync(new GetAccountStatementQuery(accountId, Day2, Day3));

        statement.Lines.Should().BeEmpty();
        statement.OpeningBalance.Should().Be(75m);
        statement.ClosingBalance.Should().Be(75m);
    }

    [Fact]
    public async Task Unknown_account_is_not_found()
    {
        var act = () => _app.SendAsync(new GetAccountStatementQuery(Guid.NewGuid(), Day1, Day2));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
