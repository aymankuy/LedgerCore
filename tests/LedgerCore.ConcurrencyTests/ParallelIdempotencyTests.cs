using FluentAssertions;
using LedgerCore.Application.Accounts.GetBalance;
using LedgerCore.Application.Ledger.Withdraw;
using LedgerCore.ConcurrencyTests.Infrastructure;

namespace LedgerCore.ConcurrencyTests;

[Collection(ConcurrencyCollection.Name)]
public sealed class ParallelIdempotencyTests(ConcurrencyFixture fixture)
{
    [Fact]
    public async Task The_same_key_sent_fifty_times_in_parallel_moves_money_exactly_once()
    {
        var accountId = await fixture.OpenFundedAccountAsync("NOK", initialBalance: 100m);
        var key = Guid.NewGuid().ToString();

        // A client that times out and retries aggressively: the same withdrawal, the same key.
        var transactionIds = await ParallelRun.AllAtOnceAsync(50, _ =>
            fixture.SendAsync(new WithdrawCommand(accountId, 10m), key));

        transactionIds.Distinct().Should().ContainSingle("every caller gets the response of the one withdrawal that ran");

        var balance = await fixture.SendAsync(new GetAccountBalanceQuery(accountId));
        balance.Balance.Should().Be(90m);
    }
}
