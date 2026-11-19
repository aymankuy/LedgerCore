using FluentAssertions;
using LedgerCore.Application.Ledger.Deposit;
using LedgerCore.Application.Ledger.Transfer;
using LedgerCore.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LedgerCore.IntegrationTests.Warehouse;

/// <summary>
/// The warehouse load against a real SQL Server: a load can be run again without duplicating
/// rows, and the account dimension keeps one version per status (slowly changing type 2).
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class WarehouseLoadTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private const byte Active = 1;
    private const byte Frozen = 2;
    private const byte Credit = 2;
    private const byte Debit = 1;
    private const byte Deposit = 1;
    private const byte Transfer = 3;

    private static readonly DateTime T0 = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = T0.AddDays(4);
    private static readonly DateTime T2 = T0.AddDays(9);
    private static readonly DateTime OpenEnded = new(9999, 12, 31);

    private readonly Guid _account = Guid.NewGuid();
    private WarehouseDatabase _dw = null!;

    public async Task InitializeAsync() => _dw = await WarehouseDatabase.CreateAsync(fixture);

    public async Task DisposeAsync() => await _dw.DisposeAsync();

    [Fact]
    public async Task A_new_account_becomes_one_open_version()
    {
        await StageStatusesAsync((1, _account, Active, T0));

        await LoadAsync();

        var versions = await VersionsAsync();
        versions.Should().Equal(new Version(Active, T0, OpenEnded, true));
    }

    [Fact]
    public async Task A_status_change_closes_the_open_version_and_opens_a_new_one()
    {
        await StageStatusesAsync((1, _account, Active, T0));
        await LoadAsync();

        // The next extract carries the whole history again, old row included.
        await StageStatusesAsync((1, _account, Active, T0), (2, _account, Frozen, T1));
        await LoadAsync();

        var versions = await VersionsAsync();
        versions.Should().Equal(
            new Version(Active, T0, T1, false),
            new Version(Frozen, T1, OpenEnded, true));
    }

    [Fact]
    public async Task Several_changes_in_one_load_chain_together_and_only_the_last_stays_open()
    {
        await StageStatusesAsync((1, _account, Active, T0), (2, _account, Frozen, T1), (3, _account, Active, T2));

        await LoadAsync();

        var versions = await VersionsAsync();
        versions.Should().Equal(
            new Version(Active, T0, T1, false),
            new Version(Frozen, T1, T2, false),
            new Version(Active, T2, OpenEnded, true));
    }

    [Fact]
    public async Task Running_the_same_load_again_adds_no_rows_to_either_table()
    {
        await StageStatusesAsync((1, _account, Active, T0), (2, _account, Frozen, T1));
        await StageEntriesAsync((1, _account, Deposit, Credit, 10m, T0.AddHours(1)), (2, _account, Deposit, Credit, 5m, T1.AddHours(1)));
        await LoadAsync();

        var dimension = await _dw.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.DimAccount");
        var fact = await _dw.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.FactLedgerEntry");

        await LoadAsync();
        await LoadAsync();

        (await _dw.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.DimAccount")).Should().Be(dimension).And.Be(2);
        (await _dw.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.FactLedgerEntry")).Should().Be(fact).And.Be(2);
        (await _dw.ScalarAsync<decimal>("SELECT SUM(SignedAmount) FROM dbo.FactLedgerEntry")).Should().Be(15m);
        (await _dw.ScalarAsync<int>("SELECT COUNT(*) FROM etl.RejectedRow")).Should().Be(0);
    }

    [Fact]
    public async Task An_entry_points_to_the_account_version_that_was_valid_when_it_was_posted()
    {
        await StageStatusesAsync((1, _account, Active, T0), (2, _account, Frozen, T1));
        await StageEntriesAsync(
            (1, _account, Deposit, Credit, 10m, T0.AddHours(1)),
            (2, _account, Deposit, Credit, 5m, T1.AddHours(1)));

        await LoadAsync();

        var statuses = await _dw.RowsAsync("""
            SELECT f.EntryId, d.Status
            FROM dbo.FactLedgerEntry AS f
            JOIN dbo.DimAccount AS d ON d.AccountKey = f.AccountKey
            ORDER BY f.EntryId
            """);
        statuses.Select(r => ((long)r[0]!, Convert.ToByte(r[1]))).Should().Equal((1L, Active), (2L, Frozen));
    }

    [Fact]
    public async Task A_second_load_takes_only_the_entries_after_the_watermark()
    {
        await StageStatusesAsync((1, _account, Active, T0));
        await StageEntriesAsync(
            (1, _account, Deposit, Credit, 10m, T0.AddHours(1)),
            (2, _account, Deposit, Credit, 10m, T0.AddHours(2)),
            (3, _account, Deposit, Credit, 10m, T0.AddHours(3)));
        await LoadAsync();
        (await Watermark()).Should().Be(3);

        await StageStatusesAsync((1, _account, Active, T0));
        await StageEntriesAsync(
            (1, _account, Deposit, Credit, 10m, T0.AddHours(1)),
            (2, _account, Deposit, Credit, 10m, T0.AddHours(2)),
            (3, _account, Deposit, Credit, 10m, T0.AddHours(3)),
            (4, _account, Transfer, Debit, 7m, T0.AddHours(4)),
            (5, _account, Transfer, Debit, 3m, T0.AddHours(5)));
        await LoadAsync();

        (await _dw.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.FactLedgerEntry")).Should().Be(5);
        (await _dw.ScalarAsync<decimal>("SELECT SUM(SignedAmount) FROM dbo.FactLedgerEntry")).Should().Be(20m);
        (await Watermark()).Should().Be(5);

        var audit = await _dw.RowsAsync("SELECT TOP (1) RowsRead, RowsLoaded FROM etl.LoadAudit WHERE TargetTable = N'FactLedgerEntry' ORDER BY Id DESC");
        audit.Single().Should().Equal(2, 2);
    }

    [Fact]
    public async Task An_entry_that_cannot_be_loaded_is_set_aside_and_the_load_carries_on()
    {
        await StageStatusesAsync((1, _account, Active, T0));
        var unknownAccount = Guid.NewGuid();
        await StageEntriesAsync(
            (1, _account, Deposit, Credit, 10m, T0.AddHours(1)),
            (2, unknownAccount, Deposit, Credit, 99m, T0.AddHours(1)),
            (3, _account, Deposit, Credit, 5m, T0.AddHours(2)));

        await LoadAsync();

        (await _dw.ScalarAsync<decimal>("SELECT SUM(SignedAmount) FROM dbo.FactLedgerEntry")).Should().Be(15m);
        var rejected = await _dw.RowsAsync("SELECT SourceKey, Reason FROM etl.RejectedRow");
        rejected.Should().ContainSingle().Which[0].Should().Be("2");
        (await Watermark()).Should().Be(3, "a rejected row is recorded once and not retried on every load");
    }

    [Fact]
    public async Task A_load_from_the_real_ledger_matches_it_and_follows_later_status_changes()
    {
        var app = new LedgerTestApp(fixture);
        var payer = await app.OpenAccountAsync("DKK");
        var payee = await app.OpenAccountAsync("DKK");
        await app.SendAsync(new DepositCommand(payer, 100m));
        await app.SendAsync(new TransferMoneyCommand(payer, payee, 40m));
        await SetFrozenAsync(app, payee, true);

        await ExtractAndLoadAsync();
        await AssertMatchesTheLedgerAsync(payer, payee, expectedPayeeVersions: 2);

        // Nothing new in the ledger: another load changes nothing.
        await ExtractAndLoadAsync();
        await AssertMatchesTheLedgerAsync(payer, payee, expectedPayeeVersions: 2);

        await SetFrozenAsync(app, payee, false);
        await app.SendAsync(new DepositCommand(payee, 5m));

        await ExtractAndLoadAsync();
        await AssertMatchesTheLedgerAsync(payer, payee, expectedPayeeVersions: 3);
    }

    private static async Task SetFrozenAsync(LedgerTestApp app, Guid accountId, bool frozen)
    {
        await using var db = app.CreateDbContext();
        var account = await db.Accounts.SingleAsync(a => a.Id == accountId);
        if (frozen)
        {
            account.Freeze();
        }
        else
        {
            account.Unfreeze();
        }

        await db.SaveChangesAsync();
    }

    private async Task AssertMatchesTheLedgerAsync(Guid payer, Guid payee, int expectedPayeeVersions)
    {
        (await _dw.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.FactLedgerEntry"))
            .Should().Be(await LedgerScalarAsync<int>("SELECT COUNT(*) FROM dbo.LedgerEntries"));
        (await _dw.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.DimAccount"))
            .Should().Be(await LedgerScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountStatusHistory"));
        (await _dw.ScalarAsync<int>("SELECT COUNT(*) FROM etl.RejectedRow")).Should().Be(0);

        foreach (var account in new[] { payer, payee })
        {
            var inLedger = await LedgerScalarAsync<decimal>(
                "SELECT COALESCE(SUM(CASE Direction WHEN 2 THEN Amount ELSE -Amount END), 0) FROM dbo.LedgerEntries WHERE AccountId = @id", account);
            var inWarehouse = await _dw.ScalarAsync<decimal>(
                "SELECT COALESCE(SUM(f.SignedAmount), 0) FROM dbo.FactLedgerEntry AS f JOIN dbo.DimAccount AS d ON d.AccountKey = f.AccountKey WHERE d.AccountId = @id",
                ("@id", account));
            inWarehouse.Should().Be(inLedger);
        }

        (await _dw.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.DimAccount WHERE AccountId = @id", ("@id", payee)))
            .Should().Be(expectedPayeeVersions);
        (await _dw.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.DimAccount WHERE AccountId = @id AND IsCurrent = 1", ("@id", payee)))
            .Should().Be(1);
    }

    // What SSIS and Azure Data Factory do, in miniature: read the safe window from the ledger,
    // copy it into staging, call the load procedures.
    private async Task ExtractAndLoadAsync()
    {
        await _dw.ExecuteAsync("EXEC etl.usp_ClearStaging");

        var from = await _dw.ScalarAsync<long>("SELECT LastValue FROM etl.Watermark WHERE Source = N'LedgerEntries'");
        var to = await LedgerScalarAsync<long>("EXEC dbo.usp_GetSafeEntryWatermark");

        await CopyToStagingAsync("EXEC dbo.usp_ExtractLedgerEntries @From, @To", "stg.LedgerEntry", ("@From", from), ("@To", to));
        await CopyToStagingAsync("EXEC dbo.usp_ExtractAccountStatusHistory", "stg.AccountStatusChange");

        await LoadProceduresAsync();
    }

    private async Task CopyToStagingAsync(string extractSql, string stagingTable, params (string Name, object Value)[] parameters)
    {
        await using var source = new SqlConnection(fixture.ConnectionString);
        await source.OpenAsync();
        await using var command = new SqlCommand(extractSql, source);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using var reader = await command.ExecuteReaderAsync();
        using var bulk = new SqlBulkCopy(_dw.ConnectionString) { DestinationTableName = stagingTable };
        await bulk.WriteToServerAsync(reader);
    }

    private async Task<T> LedgerScalarAsync<T>(string sql, Guid? id = null)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        if (id is not null)
        {
            command.Parameters.AddWithValue("@id", id.Value);
        }

        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private async Task LoadAsync() => await LoadProceduresAsync();

    private async Task LoadProceduresAsync()
    {
        await _dw.ExecuteAsync("EXEC etl.usp_LoadDimAccount");
        await _dw.ExecuteAsync("EXEC etl.usp_LoadFactLedgerEntry");
    }

    private Task<long> Watermark() =>
        _dw.ScalarAsync<long>("SELECT LastValue FROM etl.Watermark WHERE Source = N'LedgerEntries'");

    private async Task StageStatusesAsync(params (long HistoryId, Guid AccountId, byte Status, DateTime At)[] rows)
    {
        await _dw.ExecuteAsync("TRUNCATE TABLE stg.AccountStatusChange");
        foreach (var row in rows)
        {
            await _dw.ExecuteAsync(
                "INSERT INTO stg.AccountStatusChange (HistoryId, AccountId, OwnerUserId, Currency, Kind, AllowsOverdraft, Status, ChangedAtUtc) VALUES (@h, @a, @o, 'EUR', 1, 0, @s, @t)",
                ("@h", row.HistoryId), ("@a", row.AccountId), ("@o", Guid.NewGuid()), ("@s", row.Status), ("@t", row.At));
        }
    }

    private async Task StageEntriesAsync(params (long EntryId, Guid AccountId, byte Type, byte Direction, decimal Amount, DateTime At)[] rows)
    {
        await _dw.ExecuteAsync("TRUNCATE TABLE stg.LedgerEntry");
        foreach (var row in rows)
        {
            await _dw.ExecuteAsync(
                "INSERT INTO stg.LedgerEntry (EntryId, TransactionId, AccountId, TransactionType, Direction, Amount, CreatedAtUtc) VALUES (@e, @tx, @a, @ty, @d, @m, @t)",
                ("@e", row.EntryId), ("@tx", Guid.NewGuid()), ("@a", row.AccountId), ("@ty", row.Type),
                ("@d", row.Direction), ("@m", row.Amount), ("@t", row.At));
        }
    }

    private async Task<List<Version>> VersionsAsync()
    {
        var rows = await _dw.RowsAsync(
            "SELECT Status, ValidFromUtc, ValidToUtc, IsCurrent FROM dbo.DimAccount WHERE AccountId = @a ORDER BY AccountKey",
            ("@a", _account));
        return rows.Select(r => new Version(Convert.ToByte(r[0]), (DateTime)r[1]!, (DateTime)r[2]!, (bool)r[3]!)).ToList();
    }

    private sealed record Version(byte Status, DateTime ValidFromUtc, DateTime ValidToUtc, bool IsCurrent);
}
