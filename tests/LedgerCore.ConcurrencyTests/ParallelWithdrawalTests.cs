using FluentAssertions;
using LedgerCore.Application.Accounts.GetBalance;
using LedgerCore.Application.Ledger.Withdraw;
using LedgerCore.ConcurrencyTests.Infrastructure;
using LedgerCore.Domain.Accounts;

namespace LedgerCore.ConcurrencyTests;

[Collection(ConcurrencyCollection.Name)]
public sealed class ParallelWithdrawalTests(ConcurrencyFixture fixture)
{
    [Fact]
    public async Task Hundred_parallel_withdrawals_never_overdraw_the_account()
    {
        // Enough money for exactly 10 of the 100 withdrawals.
        var accountId = await fixture.OpenFundedAccountAsync("EUR", initialBalance: 100m);

        var outcomes = await ParallelRun.AllAtOnceAsync(100, async _ =>
        {
            try
            {
                await fixture.SendAsync(new WithdrawCommand(accountId, 10m));
                return true;
            }
            catch (InsufficientFundsException)
            {
                return false;
            }
        });

        var balance = await fixture.SendAsync(new GetAccountBalanceQuery(accountId));
        balance.Balance.Should().Be(0m, "the balance must never go below zero");
        outcomes.Count(succeeded => succeeded).Should().Be(10, "only 10 withdrawals of 10 fit into 100");
    }
}
