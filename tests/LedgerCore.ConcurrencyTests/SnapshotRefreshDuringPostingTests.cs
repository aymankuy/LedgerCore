using FluentAssertions;
using LedgerCore.Application.Ledger.Deposit;
using LedgerCore.Application.Ledger.Transfer;
using LedgerCore.ConcurrencyTests.Infrastructure;
using LedgerCore.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.ConcurrencyTests;

[Collection(ConcurrencyCollection.Name)]
public sealed class SnapshotRefreshDuringPostingTests(ConcurrencyFixture fixture)
{
    private const string Currency = "CHF";

    [Fact]
    public async Task A_refresh_never_sets_its_watermark_past_an_uncommitted_entry()
    {
        var account = await fixture.OpenFundedAccountAsync(Currency, initialBalance: 100m);

        // Posting A gets its Ids first but stays uncommitted. Posting B gets higher Ids and
        // commits. Identity order is not commit order.
        await using var slow = new SqlConnection(fixture.ConnectionString);
        await slow.OpenAsync();
        await using var slowTransaction = slow.BeginTransaction();
        await PostDepositAsync(slow, slowTransaction, account, 5m);
        await fixture.SendAsync(new DepositCommand(account, 7m));

        // Without the posting lock this refresh would read MAX(Id) = B's entry and skip A's
        // entries for good. With it, the refresh waits until A has committed.
        var refresh = fixture.InScopeAsync(s => s.GetRequiredService<BalanceSnapshotRefresher>().RefreshAsync(CancellationToken.None));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await slowTransaction.CommitAsync();
        await refresh;
        await RefreshAsync();

        (await MismatchedAccountsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Refreshes_overlapping_parallel_transfers_raise_no_errors_and_stay_exact()
    {
        var accounts = new Guid[10];
        for (var i = 0; i < accounts.Length; i++)
        {
            accounts[i] = await fixture.OpenFundedAccountAsync(Currency, initialBalance: 10_000m);
        }

        // A soak test for the locks: refreshes back to back while 200 transfers run. Any
        // deadlock or lock timeout escapes and fails the test. The window between an insert
        // and its commit is too short to hit by chance, which is why the test above holds a
        // posting open on purpose.
        using var stop = new CancellationTokenSource();
        var refreshes = 0;
        var refresher = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                await RefreshAsync();
                refreshes++;
            }
        });

        await ParallelRun.AllAtOnceAsync(200, i => fixture.SendAsync(
            new TransferMoneyCommand(accounts[i % accounts.Length], accounts[(i * 7 + 3) % accounts.Length], 1m + (i % 5))));

        await stop.CancelAsync();
        await refresher;
        await RefreshAsync();

        refreshes.Should().BeGreaterThan(1, "the refreshes must overlap the transfers");
        (await MismatchedAccountsAsync()).Should().Be(0);
    }

    private Task RefreshAsync() =>
        fixture.InScopeAsync(s => s.GetRequiredService<BalanceSnapshotRefresher>().RefreshAsync(CancellationToken.None));

    // After a final refresh with nothing in flight, every snapshot must be the full sum.
    private async Task<int> MismatchedAccountsAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT(*)
            FROM dbo.Accounts AS a
            LEFT JOIN dbo.AccountBalanceSnapshots AS s ON s.AccountId = a.Id
            CROSS APPLY (SELECT COALESCE(SUM(CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END), 0) AS Total
                         FROM dbo.LedgerEntries AS e WHERE e.AccountId = a.Id) AS raw
            WHERE COALESCE(s.Balance, 0) <> raw.Total
            """, connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    // A deposit through the posting procedure, inside a transaction the caller commits.
    private static async Task PostDepositAsync(SqlConnection connection, SqlTransaction transaction, Guid account, decimal amount)
    {
        await using var command = new SqlCommand("""
            DECLARE @settlement uniqueidentifier =
                (SELECT Id FROM dbo.Accounts WHERE Kind = 2 AND Currency = (SELECT Currency FROM dbo.Accounts WHERE Id = @Account));
            DECLARE @entries dbo.LedgerEntryList;
            INSERT INTO @entries (AccountId, Direction, Amount) VALUES (@settlement, 1, @Amount), (@Account, 2, @Amount);
            DECLARE @id uniqueidentifier = NEWID(), @event uniqueidentifier = NEWID(), @now datetime2(3) = SYSUTCDATETIME();
            EXEC dbo.usp_PostLedgerTransaction @id, 1, NULL, NULL, @now, @entries, @event, N'Test', N'{}';
            """, connection, transaction);
        command.Parameters.AddWithValue("@Account", account);
        command.Parameters.AddWithValue("@Amount", amount);
        await command.ExecuteNonQueryAsync();
    }
}
