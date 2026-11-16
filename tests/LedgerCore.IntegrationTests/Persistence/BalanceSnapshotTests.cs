using FluentAssertions;
using LedgerCore.Application.Accounts.GetBalance;
using LedgerCore.Application.Accounts.GetStatement;
using LedgerCore.Application.Ledger.Deposit;
using LedgerCore.Application.Ledger.Withdraw;
using LedgerCore.Infrastructure.Persistence;
using LedgerCore.Infrastructure.Persistence.Records;
using LedgerCore.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LedgerCore.IntegrationTests.Persistence;

[Collection(SqlServerCollection.Name)]
public sealed class BalanceSnapshotTests(SqlServerFixture fixture)
{
    // A currency no other test class uses, so this class owns its settlement account.
    private const string Currency = "NOK";

    private readonly LedgerTestApp _app = new(fixture);

    [Fact]
    public async Task Balance_is_the_snapshot_plus_the_entries_after_it()
    {
        var accountId = await _app.OpenAccountAsync(Currency);
        await _app.SendAsync(new DepositCommand(accountId, 100m));
        await RefreshAsync();

        (await SnapshotAsync(accountId)).Should().Be(100m);

        // Posted after the watermark: not in the snapshot yet, but in every balance read.
        await _app.SendAsync(new DepositCommand(accountId, 50m));
        await _app.SendAsync(new WithdrawCommand(accountId, 30m));

        (await SnapshotAsync(accountId)).Should().Be(100m);
        (await _app.SendAsync(new GetAccountBalanceQuery(accountId))).Balance.Should().Be(120m);

        await RefreshAsync();

        (await SnapshotAsync(accountId)).Should().Be(120m);
        (await _app.SendAsync(new GetAccountBalanceQuery(accountId))).Balance.Should().Be(120m);
    }

    [Fact]
    public async Task Each_refresh_starts_where_the_previous_one_stopped()
    {
        var accountId = await _app.OpenAccountAsync(Currency);
        await _app.SendAsync(new DepositCommand(accountId, 10m));

        var first = await RefreshAsync();
        var second = await RefreshAsync();

        first.ToEntryId.Should().BeGreaterThan(first.FromEntryId);
        second.FromEntryId.Should().Be(first.ToEntryId);
        second.ToEntryId.Should().Be(first.ToEntryId, "nothing was posted in between");
        second.AccountsUpdated.Should().Be(0);
    }

    [Fact]
    public async Task Every_snapshot_equals_the_sum_of_its_entries_after_a_refresh()
    {
        var accountId = await _app.OpenAccountAsync(Currency);
        await _app.SendAsync(new DepositCommand(accountId, 70m));
        await _app.SendAsync(new WithdrawCommand(accountId, 20m));

        await RefreshAsync();

        // Every account in the database, including the ones other tests wrote to.
        (await MismatchedSnapshotsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Statement_opening_balance_is_right_when_part_of_the_history_is_in_a_snapshot()
    {
        var day1 = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);
        var accountId = await _app.OpenAccountAsync(Currency);
        await _app.SendAsync(new DepositCommand(accountId, 100m), day1);
        await _app.SendAsync(new WithdrawCommand(accountId, 40m), day1.AddDays(1));
        await RefreshAsync();
        await _app.SendAsync(new DepositCommand(accountId, 25m), day1.AddDays(2));

        // The window starts after the deposit on day 1, which is folded into the snapshot.
        var statement = await _app.SendAsync(new GetAccountStatementQuery(accountId, day1.AddDays(1), day1.AddDays(3)));

        statement.OpeningBalance.Should().Be(100m);
        statement.Lines.Select(l => l.BalanceAfter).Should().Equal(60m, 85m);
    }

    [Fact]
    public async Task A_second_refresh_running_at_the_same_time_does_nothing()
    {
        await using var holder = new SqlConnection(fixture.ConnectionString);
        await holder.OpenAsync();
        await using var transaction = holder.BeginTransaction();
        await using (var command = new SqlCommand(
            "EXEC sp_getapplock @Resource = N'LedgerCore.BalanceSnapshotRefresh', @LockMode = N'Exclusive', @LockOwner = N'Transaction';",
            holder,
            transaction))
        {
            await command.ExecuteNonQueryAsync();
        }

        await using var db = fixture.CreateDbContext();
        var run = await new BalanceSnapshotRefresher(db).RefreshAsync(CancellationToken.None);

        run.Should().BeNull();
    }

    [Fact]
    public async Task Database_reads_committed_snapshots()
    {
        // fn_AccountBalance reads the watermark, the snapshot and the newer entries in one
        // statement. Only row versioning makes them a consistent picture (ADR 0009).
        await using var db = fixture.CreateDbContext();

        var on = await db.Database
            .SqlQuery<bool>($"SELECT is_read_committed_snapshot_on AS Value FROM sys.databases WHERE database_id = DB_ID()")
            .SingleAsync();

        on.Should().BeTrue();
    }

    private async Task<BalanceSnapshotRun> RefreshAsync()
    {
        await using var db = fixture.CreateDbContext();
        var run = await new BalanceSnapshotRefresher(db).RefreshAsync(CancellationToken.None);
        return run!;
    }

    private async Task<decimal?> SnapshotAsync(Guid accountId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.AccountBalanceSnapshots
            .Where(s => s.AccountId == accountId)
            .Select(s => (decimal?)s.Balance)
            .SingleOrDefaultAsync();
    }

    private async Task<int> MismatchedSnapshotsAsync()
    {
        await using var db = fixture.CreateDbContext();
        return await db.Database.SqlQuery<int>($"""
            SELECT COUNT(*) AS Value
            FROM dbo.AccountBalanceSnapshots AS s
            CROSS APPLY (SELECT SUM(CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END) AS Total
                         FROM dbo.LedgerEntries AS e
                         WHERE e.AccountId = s.AccountId
                           AND e.Id <= (SELECT TOP (1) ToEntryId FROM dbo.BalanceSnapshotRuns ORDER BY Id DESC)) AS raw
            WHERE s.Balance <> COALESCE(raw.Total, 0)
            """).SingleAsync();
    }
}
