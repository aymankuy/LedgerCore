using FluentAssertions;
using LedgerCore.Application.Accounts.GetBalance;
using LedgerCore.Application.Ledger.Transfer;
using LedgerCore.ConcurrencyTests.Infrastructure;

namespace LedgerCore.ConcurrencyTests;

[Collection(ConcurrencyCollection.Name)]
public sealed class ReciprocalTransferTests(ConcurrencyFixture fixture)
{
    [Fact]
    public async Task Parallel_transfers_in_both_directions_complete_without_errors()
    {
        var a = await fixture.OpenFundedAccountAsync("USD", initialBalance: 1_000m);
        var b = await fixture.OpenFundedAccountAsync("USD", initialBalance: 1_000m);

        // Even calls move A to B, odd calls move B to A: 50 transfers each way at the same time.
        // Any exception, including a deadlock, escapes Task.WhenAll and fails the test.
        await ParallelRun.AllAtOnceAsync(100, i => i % 2 == 0
            ? fixture.SendAsync(new TransferMoneyCommand(a, b, 10m))
            : fixture.SendAsync(new TransferMoneyCommand(b, a, 10m)));

        var balanceA = await fixture.SendAsync(new GetAccountBalanceQuery(a));
        var balanceB = await fixture.SendAsync(new GetAccountBalanceQuery(b));
        balanceA.Balance.Should().Be(1_000m);
        balanceB.Balance.Should().Be(1_000m);
    }
}
