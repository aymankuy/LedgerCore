using FluentAssertions;
using LedgerCore.IntegrationTests.Infrastructure;

namespace LedgerCore.IntegrationTests.Warehouse;

/// <summary>The numbers each Power BI page reads, checked against a ledger small enough to add up by hand.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class WarehouseReportingViewsTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Day = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _customer = Guid.NewGuid();
    private readonly Guid _settlement = Guid.NewGuid();
    private readonly Guid _feeRevenue = Guid.NewGuid();
    private WarehouseDatabase _dw = null!;

    public async Task InitializeAsync()
    {
        _dw = await WarehouseDatabase.CreateAsync(fixture);

        // A customer deposits 100 (the settlement account is the other side), then pays a fee of 0.50.
        await StageAccountAsync(1, _customer, kind: 1);
        await StageAccountAsync(2, _settlement, kind: 2);
        await StageAccountAsync(3, _feeRevenue, kind: 3);

        var deposit = Guid.NewGuid();
        var fee = Guid.NewGuid();
        await StageEntryAsync(1, deposit, _settlement, type: 1, direction: 1, 100m);
        await StageEntryAsync(2, deposit, _customer, type: 1, direction: 2, 100m);
        await StageEntryAsync(3, fee, _customer, type: 4, direction: 1, 0.5m);
        await StageEntryAsync(4, fee, _feeRevenue, type: 4, direction: 2, 0.5m);

        await _dw.ExecuteAsync("EXEC etl.usp_LoadDimAccount");
        await _dw.ExecuteAsync("EXEC etl.usp_LoadFactLedgerEntry");
    }

    public async Task DisposeAsync() => await _dw.DisposeAsync();

    [Fact]
    public async Task Daily_volume_counts_each_transaction_once()
    {
        var rows = await _dw.RowsAsync("SELECT TransactionType, Transactions, Volume FROM dbo.vw_DailyTransactionVolume ORDER BY TransactionType");

        rows.Select(r => ((string)r[0]!, (long)r[1]!, (decimal)r[2]!))
            .Should().Equal(("Deposit", 1L, 100m), ("Fee", 1L, 0.5m));
    }

    [Fact]
    public async Task Balances_are_the_sum_of_each_accounts_entries()
    {
        var rows = await _dw.RowsAsync("SELECT Kind, Balance, BalanceBand FROM dbo.vw_AccountBalance ORDER BY Kind");

        rows.Select(r => ((string)r[0]!, (decimal)r[1]!))
            .Should().Equal(("Customer", 99.5m), ("FeeRevenue", 0.5m), ("Settlement", -100m));
        rows.Single(r => (string)r[0]! == "Customer")[2].Should().Be("3  under 100");
    }

    [Fact]
    public async Task The_busiest_account_handled_the_most_money()
    {
        var top = (await _dw.RowsAsync("SELECT TOP (1) Kind, Entries, Volume FROM dbo.vw_TopAccountsByVolume ORDER BY Volume DESC")).Single();

        top[0].Should().Be("Customer");
        top[1].Should().Be(2L);
        top[2].Should().Be(100.5m);
    }

    [Fact]
    public async Task Fee_revenue_is_what_the_fee_account_was_credited()
    {
        var rows = await _dw.RowsAsync("SELECT [Date], FeeRevenue FROM dbo.vw_FeeRevenueDaily");

        var row = rows.Should().ContainSingle().Subject;
        row[0].Should().Be(Day.Date);
        row[1].Should().Be(0.5m);
    }

    private Task StageAccountAsync(long historyId, Guid accountId, byte kind) => _dw.ExecuteAsync(
        "INSERT INTO stg.AccountStatusChange (HistoryId, AccountId, OwnerUserId, Currency, Kind, AllowsOverdraft, Status, ChangedAtUtc) VALUES (@h, @a, @o, 'EUR', @k, 0, 1, @t)",
        ("@h", historyId), ("@a", accountId), ("@o", Guid.NewGuid()), ("@k", kind), ("@t", Day.AddDays(-1)));

    private Task StageEntryAsync(long entryId, Guid transactionId, Guid accountId, byte type, byte direction, decimal amount) => _dw.ExecuteAsync(
        "INSERT INTO stg.LedgerEntry (EntryId, TransactionId, AccountId, TransactionType, Direction, Amount, CreatedAtUtc) VALUES (@e, @tx, @a, @ty, @d, @m, @t)",
        ("@e", entryId), ("@tx", transactionId), ("@a", accountId), ("@ty", type), ("@d", direction), ("@m", amount), ("@t", Day));
}
