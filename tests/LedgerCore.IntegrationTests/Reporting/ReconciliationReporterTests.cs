using FluentAssertions;
using LedgerCore.Application.Ledger.Deposit;
using LedgerCore.Application.Ledger.Transfer;
using LedgerCore.Infrastructure;
using LedgerCore.Infrastructure.Persistence;
using LedgerCore.Infrastructure.Reporting;
using LedgerCore.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.IntegrationTests.Reporting;

/// <summary>
/// Each test gets a database of its own, because two of them corrupt the ledger on purpose, to
/// prove the report notices. They do it as sa, with the net-zero trigger switched off, which
/// no application path can do.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ReconciliationReporterTests(SqlServerFixture fixture)
{
    private static readonly DateOnly Day = new(2026, 6, 10);
    private static readonly DateTime DayStart = Day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    [Fact]
    public async Task A_correct_ledger_is_balanced_and_the_day_counts_only_its_own_entries()
    {
        var app = await NewDatabaseAsync();
        var alice = await app.OpenAccountAsync("EUR");
        var bob = await app.OpenAccountAsync("EUR");
        await app.SendAsync(new DepositCommand(alice, 100m), DayStart.AddHours(9));
        await app.SendAsync(new TransferMoneyCommand(alice, bob, 40m), DayStart.AddHours(10));
        await app.SendAsync(new DepositCommand(bob, 7m), DayStart.AddDays(1).AddHours(9));

        var report = await ReportAsync(app);

        report.IsBalanced.Should().BeTrue();
        report.Day.Should().Be(Day);
        var eur = report.Activity.Should().ContainSingle().Which;
        eur.Currency.Should().Be("EUR");

        // The deposit and the transfer, two entries each; the test app charges no fees. The
        // next day's deposit is not counted.
        eur.Transactions.Should().Be(2);
        eur.Entries.Should().Be(4);
        eur.Debits.Should().Be(140m).And.Be(eur.Credits);
        report.CurrencyNets.Should().OnlyContain(c => c.Net == 0);
    }

    [Fact]
    public async Task A_transaction_that_does_not_net_to_zero_is_reported()
    {
        var app = await NewDatabaseAsync();
        var alice = await app.OpenAccountAsync("EUR");
        await app.SendAsync(new DepositCommand(alice, 100m), DayStart.AddHours(9));
        var broken = Guid.NewGuid();

        await AsSaAsync(app, """
            DISABLE TRIGGER dbo.TR_LedgerEntries_NetZero ON dbo.LedgerEntries;
            INSERT INTO dbo.LedgerTransactions (Id, Type, CreatedAtUtc) VALUES (@Id, 1, @At);
            INSERT INTO dbo.LedgerEntries (TransactionId, AccountId, Direction, Amount, CreatedAtUtc) VALUES (@Id, @Account, 2, 5, @At);
            ENABLE TRIGGER dbo.TR_LedgerEntries_NetZero ON dbo.LedgerEntries;
            """, ("@Id", broken), ("@Account", alice), ("@At", DayStart.AddHours(12)));

        var report = await ReportAsync(app);

        report.IsBalanced.Should().BeFalse();
        report.UnbalancedTransactionCount.Should().Be(1);
        report.UnbalancedTransactionSample.Should().ContainSingle().Which.Should().Be(new UnbalancedTransaction(broken, 5m));
        report.CurrencyNets.Should().ContainSingle(c => c.Currency == "EUR").Which.Net.Should().Be(5m, "five euros appeared from nowhere");
    }

    [Fact]
    public async Task A_snapshot_that_drifted_from_the_ledger_is_reported()
    {
        var app = await NewDatabaseAsync();
        var alice = await app.OpenAccountAsync("EUR");
        await app.SendAsync(new DepositCommand(alice, 100m), DayStart.AddHours(9));
        await using (var db = app.CreateDbContext())
        {
            await new BalanceSnapshotRefresher(db).RefreshAsync(CancellationToken.None);
        }

        await AsSaAsync(app, "UPDATE dbo.AccountBalanceSnapshots SET Balance = Balance + 1 WHERE AccountId = @Account;", ("@Account", alice));

        var report = await ReportAsync(app);

        report.IsBalanced.Should().BeFalse();
        report.DriftedAccountCount.Should().Be(1);
        report.DriftedAccountSample.Should().ContainSingle().Which.Should().Be(new DriftedAccount(alice, 101m, 100m));
        report.UnbalancedTransactionCount.Should().Be(0, "the entries themselves are fine");
    }

    [Fact]
    public async Task Nightly_maintenance_refreshes_the_snapshots_and_drains_the_outbox()
    {
        var app = await NewDatabaseAsync();
        var alice = await app.OpenAccountAsync("EUR");
        await app.SendAsync(new DepositCommand(alice, 100m));

        await using (var services = Services(app))
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<NightlyMaintenance>().RunAsync(CancellationToken.None);
        }

        await using var db = app.CreateDbContext();
        (await db.AccountBalanceSnapshots.SingleAsync(s => s.AccountId == alice)).Balance.Should().Be(100m);
        (await db.OutboxMessages.CountAsync(m => m.ProcessedAtUtc == null)).Should().Be(0);
    }

    private async Task<LedgerTestApp> NewDatabaseAsync()
    {
        var connectionString = new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            InitialCatalog = $"Reconciliation_{Guid.NewGuid():N}",
        }.ConnectionString;

        var app = new LedgerTestApp(connectionString);
        await using var db = app.CreateDbContext();
        await db.Database.MigrateAsync();
        return app;
    }

    private static async Task<ReconciliationReport> ReportAsync(LedgerTestApp app)
    {
        await using var services = Services(app);
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ReconciliationReporter>().CreateAsync(Day, CancellationToken.None);
    }

    private static ServiceProvider Services(LedgerTestApp app)
    {
        using var db = app.CreateDbContext();
        return new ServiceCollection()
            .AddInfrastructure(db.Database.GetConnectionString()!)
            .AddSingleton(TimeProvider.System)
            .AddLogging()
            .BuildServiceProvider();
    }

    private static async Task AsSaAsync(LedgerTestApp app, string sql, params (string Name, object Value)[] parameters)
    {
        await using var db = app.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(sql, parameters.Select(p => new SqlParameter(p.Name, p.Value)).ToArray());
    }
}
