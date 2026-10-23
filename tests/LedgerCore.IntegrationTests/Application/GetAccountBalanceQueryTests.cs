using FluentAssertions;
using LedgerCore.Application.Accounts.GetBalance;
using LedgerCore.Application.Common;
using LedgerCore.Application.Ledger.Deposit;
using LedgerCore.Application.Ledger.Withdraw;
using LedgerCore.IntegrationTests.Infrastructure;

namespace LedgerCore.IntegrationTests.Application;

/// <summary>
/// Writes through the commands (EF Core + stored procedure) and reads through the balance
/// query (Dapper), so the write side and the read side are checked against each other.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class GetAccountBalanceQueryTests(SqlServerFixture fixture)
{
    // A currency no other test class uses, so this class owns its settlement account.
    private const string Currency = "JPY";

    private readonly LedgerTestApp _app = new(fixture);

    [Fact]
    public async Task Balance_is_deposits_minus_withdrawals()
    {
        var accountId = await _app.OpenAccountAsync(Currency);
        await _app.SendAsync(new DepositCommand(accountId, 100m));
        await _app.SendAsync(new WithdrawCommand(accountId, 30.25m));

        var result = await _app.SendAsync(new GetAccountBalanceQuery(accountId));

        result.Should().Be(new AccountBalanceResponse(accountId, Currency, 69.75m));
    }

    [Fact]
    public async Task New_account_has_a_zero_balance()
    {
        var accountId = await _app.OpenAccountAsync(Currency);

        var result = await _app.SendAsync(new GetAccountBalanceQuery(accountId));

        result.Balance.Should().Be(0m);
    }

    [Fact]
    public async Task Unknown_account_is_not_found()
    {
        var act = () => _app.SendAsync(new GetAccountBalanceQuery(Guid.NewGuid()));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
