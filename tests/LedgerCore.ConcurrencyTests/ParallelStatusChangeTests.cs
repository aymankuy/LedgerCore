using FluentAssertions;
using LedgerCore.Application.Accounts.Freeze;
using LedgerCore.Application.Common;
using LedgerCore.Application.Ledger.Withdraw;
using LedgerCore.ConcurrencyTests.Infrastructure;
using LedgerCore.Domain.Accounts;

namespace LedgerCore.ConcurrencyTests;

[Collection(ConcurrencyCollection.Name)]
public sealed class ParallelStatusChangeTests(ConcurrencyFixture fixture)
{
    [Fact]
    public async Task Only_one_of_twenty_parallel_freezes_succeeds()
    {
        var accountId = await fixture.OpenFundedAccountAsync("SEK", initialBalance: 100m);

        // Every request reads "Active" and tries to change it. Without the RowVersion check, each
        // one that read before the first commit would also succeed and silently overwrite it.
        var outcomes = await ParallelRun.AllAtOnceAsync(20, async _ =>
        {
            try
            {
                await fixture.SendAsync(new SetAccountFrozenCommand(accountId, Frozen: true));
                return "frozen";
            }
            catch (ConflictException)
            {
                return "conflict";
            }
            catch (InvalidAccountStateTransitionException)
            {
                // Read the account after the winner committed, so it already saw "Frozen".
                return "already frozen";
            }
        });

        outcomes.Count(o => o == "frozen").Should().Be(1);

        var withdrawal = () => fixture.SendAsync(new WithdrawCommand(accountId, 10m));
        await withdrawal.Should().ThrowAsync<AccountNotActiveException>();
    }
}
