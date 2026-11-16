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
    public async Task Window_bounds_are_exact_to_the_millisecond()
    {
        // .002 is not representable as legacy datetime, which would round the bound up to .003
        // and silently drop an entry written at exactly the start of the window.
        var at = Day1.AddMilliseconds(2);
        var accountId = await _app.OpenAccountAsync(Currency);
        await _app.SendAsync(new DepositCommand(accountId, 10m), at);

        var statement = await _app.SendAsync(new GetAccountStatementQuery(accountId, at, at.AddDays(1)));

        statement.Lines.Should().ContainSingle().Which.CreatedAtUtc.Should().Be(at);
    }

    [Fact]
    public async Task Pages_chain_into_the_whole_statement()
    {
        var accountId = await _app.OpenAccountAsync(Currency);
        await _app.SendAsync(new DepositCommand(accountId, 1_000m), Day1);

        // Seven lines in the window, two of them at the same millisecond, so the cursor has to
        // break the tie by entry id.
        for (var i = 1; i <= 6; i++)
        {
            await _app.SendAsync(new WithdrawCommand(accountId, i), Day2.AddMinutes(i));
        }

        await _app.SendAsync(new DepositCommand(accountId, 7m), Day2.AddMinutes(6));

        var whole = await _app.SendAsync(new GetAccountStatementQuery(accountId, Day2, Day3));
        whole.Lines.Should().HaveCount(7);
        whole.NextCursor.Should().BeNull();

        var pages = new List<AccountStatementResponse>();
        string? cursor = null;
        do
        {
            var page = await _app.SendAsync(new GetAccountStatementQuery(accountId, Day2, Day3, PageSize: 3, Cursor: cursor));
            pages.Add(page);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        pages.Select(p => p.Lines.Count).Should().Equal(3, 3, 1);
        pages.SelectMany(p => p.Lines).Should().Equal(whole.Lines);

        // Each page opens at the balance the previous one closed at.
        pages[0].OpeningBalance.Should().Be(1_000m);
        pages[1].OpeningBalance.Should().Be(pages[0].ClosingBalance);
        pages[2].OpeningBalance.Should().Be(pages[1].ClosingBalance);
        pages[2].ClosingBalance.Should().Be(whole.ClosingBalance);
    }

    [Fact]
    public async Task A_page_that_fills_exactly_has_no_cursor_after_it()
    {
        var accountId = await _app.OpenAccountAsync(Currency);
        await _app.SendAsync(new DepositCommand(accountId, 10m), Day2);
        await _app.SendAsync(new DepositCommand(accountId, 20m), Day2.AddHours(1));

        var page = await _app.SendAsync(new GetAccountStatementQuery(accountId, Day2, Day3, PageSize: 2));

        page.Lines.Should().HaveCount(2);
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Unknown_account_is_not_found()
    {
        var act = () => _app.SendAsync(new GetAccountStatementQuery(Guid.NewGuid(), Day1, Day2));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
