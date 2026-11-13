using FluentAssertions;
using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.ConcurrencyTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.ConcurrencyTests;

[Collection(ConcurrencyCollection.Name)]
public sealed class DeadlockRetryTests(ConcurrencyFixture fixture)
{
    [Fact]
    public async Task A_deadlock_victim_is_retried_and_both_transactions_commit()
    {
        var a = await fixture.OpenFundedAccountAsync("CHF", initialBalance: 1m);
        var b = await fixture.OpenFundedAccountAsync("CHF", initialBalance: 1m);

        var aLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = new int[2];

        // Forces a real deadlock on the first attempt: worker 0 locks A, worker 1 locks B, and
        // only then does each ask for the other's account. SQL Server aborts one of them.
        Task<bool> LockInOrder(int worker, Guid firstId, Guid secondId, TaskCompletionSource mine, TaskCompletionSource theirs) =>
            fixture.InScopeAsync(services =>
            {
                var unitOfWork = services.GetRequiredService<IUnitOfWork>();
                var accounts = services.GetRequiredService<IAccountRepository>();

                return unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var attempt = Interlocked.Increment(ref attempts[worker]);
                    await accounts.GetByIdForUpdateAsync(firstId, CancellationToken.None);

                    if (attempt == 1)
                    {
                        mine.SetResult();
                        await theirs.Task;
                    }

                    await accounts.GetByIdForUpdateAsync(secondId, CancellationToken.None);
                    return true;
                }, CancellationToken.None);
            });

        var results = await Task.WhenAll(
            LockInOrder(0, a, b, aLocked, bLocked),
            LockInOrder(1, b, a, bLocked, aLocked));

        results.Should().AllSatisfy(committed => committed.Should().BeTrue());
        attempts.Sum().Should().Be(3, "one worker was the deadlock victim and ran exactly once more");
    }
}
