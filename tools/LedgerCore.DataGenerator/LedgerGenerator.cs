using System.Data;
using System.Diagnostics;
using Bogus;
using LedgerCore.Application.Ledger.Fees;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;
using LedgerCore.Domain.Ledger;
using LedgerCore.Domain.Users;
using Microsoft.Data.SqlClient;

namespace LedgerCore.DataGenerator;

public sealed record GeneratorOptions(
    int Transactions,
    int Customers,
    int HotAccounts,
    double HotShare,
    int Days,
    DateTime EndUtc,
    int Seed,
    string DemoPassword)
{
    public const int BatchSize = 50_000;
    public const string EmailDomain = "gen.ledgercore.local";
}

/// <summary>
/// Simulates a year of activity and bulk-loads it. Every transaction is built by the same domain
/// factory methods the API uses, so the generated ledger obeys the same rules: transfers only
/// between customer accounts of one currency, fees from the configured schedule, and no customer
/// balance ever below zero. SqlBulkCopy runs with FIRE_TRIGGERS and CHECK_CONSTRAINTS, so the
/// net-zero trigger and every constraint check the data as it lands.
/// </summary>
public sealed class LedgerGenerator(GeneratorOptions options, FeeSchedule fees, string passwordHash)
{
    private static readonly Currency[] Currencies = [Currency.From("EUR"), Currency.From("USD"), Currency.From("TRY")];

    private readonly Randomizer _random = new(options.Seed);
    private readonly DateTime _startUtc = options.EndUtc.AddDays(-options.Days);

    private Account[] _customers = [];
    private decimal[] _balances = [];
    private int[][] _tailByCurrency = [];
    private int[] _tail = [];
    private Dictionary<Currency, Account> _settlement = [];
    private Dictionary<Currency, Account> _feeRevenue = [];
    private string[] _descriptions = [];

    public async Task RunAsync(SqlConnection connection, IReadOnlyList<Account> systemAccounts)
    {
        _settlement = systemAccounts.Where(a => a.Kind == AccountKind.Settlement).ToDictionary(a => a.Currency);
        _feeRevenue = systemAccounts.Where(a => a.Kind == AccountKind.FeeRevenue).ToDictionary(a => a.Currency);
        var faker = new Faker { Random = _random };
        _descriptions = Enumerable.Range(0, 500).Select(_ => faker.Commerce.ProductName()).ToArray();

        var total = Stopwatch.StartNew();
        await LoadCustomersAsync(connection);

        var transactions = new List<LedgerTransaction>(GeneratorOptions.BatchSize + 2);
        var written = 0;
        while (written + transactions.Count < options.Transactions)
        {
            // Time advances with every transaction, so entry ids and timestamps rise together,
            // as they do in production.
            var progress = (double)(written + transactions.Count) / options.Transactions;
            var now = Truncate(_startUtc + TimeSpan.FromTicks((long)((options.EndUtc - _startUtc).Ticks * progress)));

            Simulate(now, transactions);

            if (transactions.Count >= GeneratorOptions.BatchSize)
            {
                await WriteBatchAsync(connection, transactions);
                written += transactions.Count;
                transactions.Clear();
                Console.WriteLine($"  {written:N0} transactions ({written * 2:N0} entries), {total.Elapsed:mm\\:ss}");
            }
        }

        await WriteBatchAsync(connection, transactions);
        written += transactions.Count;
        Console.WriteLine($"Loaded {written:N0} transactions in {total.Elapsed:mm\\:ss}.");
    }

    private async Task LoadCustomersAsync(SqlConnection connection)
    {
        var users = new List<User>(options.Customers);
        _customers = new Account[options.Customers];
        _balances = new decimal[options.Customers];

        for (var i = 0; i < options.Customers; i++)
        {
            var hot = i < options.HotAccounts;
            var email = hot ? $"hot{i + 1}@{GeneratorOptions.EmailDomain}" : $"user{i + 1}@{GeneratorOptions.EmailDomain}";
            var user = User.Register(email, passwordHash, UserRole.User, _startUtc);
            users.Add(user);

            // Hot accounts are EUR, the currency with the most activity; the tail is 70/20/10.
            var currency = hot ? Currencies[0] : _random.WeightedRandom(Currencies, [0.7f, 0.2f, 0.1f]);
            _customers[i] = Account.Open(user.Id, currency, _startUtc);
        }

        // Shuffled, so the busy end of the tail is spread over all currencies.
        var tail = _random.Shuffle(Enumerable.Range(options.HotAccounts, options.Customers - options.HotAccounts)).ToArray();
        _tail = tail;
        _tailByCurrency = Currencies.Select(c => tail.Where(i => _customers[i].Currency == c).ToArray()).ToArray();

        var userTable = new DataTable();
        userTable.Columns.Add("Id", typeof(Guid));
        userTable.Columns.Add("Email", typeof(string));
        userTable.Columns.Add("PasswordHash", typeof(string));
        userTable.Columns.Add("Role", typeof(byte));
        userTable.Columns.Add("CreatedAtUtc", typeof(DateTime));
        foreach (var u in users)
        {
            userTable.Rows.Add(u.Id, u.Email, u.PasswordHash, (byte)u.Role, u.CreatedAtUtc);
        }

        var accountTable = new DataTable();
        accountTable.Columns.Add("Id", typeof(Guid));
        accountTable.Columns.Add("OwnerUserId", typeof(Guid));
        accountTable.Columns.Add("Currency", typeof(string));
        accountTable.Columns.Add("Status", typeof(byte));
        accountTable.Columns.Add("AllowsOverdraft", typeof(bool));
        accountTable.Columns.Add("CreatedAtUtc", typeof(DateTime));
        accountTable.Columns.Add("Kind", typeof(byte));
        foreach (var a in _customers)
        {
            accountTable.Rows.Add(a.Id, a.OwnerUserId, a.Currency.Code, (byte)a.Status, a.AllowsOverdraft, a.CreatedAtUtc, (byte)a.Kind);
        }

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await BulkCopyAsync(connection, transaction, "dbo.Users", userTable);
        await BulkCopyAsync(connection, transaction, "dbo.Accounts", accountTable);
        await transaction.CommitAsync();

        Console.WriteLine($"Created {options.Customers:N0} users and customer accounts ({options.HotAccounts} hot).");
    }

    /// <summary>One customer action: a deposit, a withdrawal, or a transfer with its fee.</summary>
    private void Simulate(DateTime now, List<LedgerTransaction> transactions)
    {
        var source = PickAccount();
        var account = _customers[source];
        var roll = _random.Double();

        if (roll < 0.5 && TryTransfer(source, now, transactions))
        {
            return;
        }

        if (roll < 0.7 && TryWithdraw(source, now, transactions))
        {
            return;
        }

        // Customers deposit when they have nothing to spend, which keeps every balance positive.
        var amount = Money(20m, 1_000m);
        transactions.Add(LedgerTransaction.Deposit(
            _settlement[account.Currency], account, amount, now, referenceId: $"PSP-{_random.AlphaNumeric(12).ToUpperInvariant()}"));
        _balances[source] += amount;
    }

    private bool TryWithdraw(int source, DateTime now, List<LedgerTransaction> transactions)
    {
        var amount = Money(10m, 400m);
        if (_balances[source] < amount)
        {
            return false;
        }

        var account = _customers[source];
        transactions.Add(LedgerTransaction.Withdraw(
            account, _balances[source], _settlement[account.Currency], amount, now,
            referenceId: $"PAYOUT-{_random.AlphaNumeric(12).ToUpperInvariant()}"));
        _balances[source] -= amount;
        return true;
    }

    private bool TryTransfer(int source, DateTime now, List<LedgerTransaction> transactions)
    {
        var account = _customers[source];
        var amount = Money(5m, 250m);
        var fee = fees.For(account.Currency).Calculate(amount);
        if (_balances[source] < amount + fee)
        {
            return false;
        }

        var destination = PickAccount(account.Currency);
        if (destination == source)
        {
            return false;
        }

        var description = _random.Bool() ? _random.ArrayElement(_descriptions) : null;
        var transfer = LedgerTransaction.Transfer(account, _balances[source], _customers[destination], amount, now, description: description);
        _balances[source] -= amount;
        _balances[destination] += amount;
        transactions.Add(transfer);

        if (fee > 0)
        {
            transactions.Add(LedgerTransaction.Fee(account, _balances[source], _feeRevenue[account.Currency], fee, now, transfer.Id));
            _balances[source] -= fee;
        }

        return true;
    }

    /// <summary>
    /// A few hot accounts take <see cref="GeneratorOptions.HotShare"/> of the activity; the rest is
    /// a long tail where a small number of accounts are busy and most are nearly idle.
    /// </summary>
    private int PickAccount(Currency? currency = null)
    {
        if ((currency is null || currency == Currencies[0]) && _random.Double() < options.HotShare)
        {
            return _random.Int(0, options.HotAccounts - 1);
        }

        var pool = currency is null ? _tail : _tailByCurrency[Array.IndexOf(Currencies, currency.Value)];
        return pool[(int)(pool.Length * Math.Pow(_random.Double(), 2.5))];
    }

    private decimal Money(decimal min, decimal max) => Math.Round(_random.Decimal(min, max), 2);

    private static DateTime Truncate(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

    private static async Task WriteBatchAsync(SqlConnection connection, List<LedgerTransaction> transactions)
    {
        if (transactions.Count == 0)
        {
            return;
        }

        var header = new DataTable();
        header.Columns.Add("Id", typeof(Guid));
        header.Columns.Add("Type", typeof(byte));
        header.Columns.Add("ReferenceId", typeof(string));
        header.Columns.Add("Description", typeof(string));
        header.Columns.Add("CreatedAtUtc", typeof(DateTime));

        var entries = new DataTable();
        entries.Columns.Add("TransactionId", typeof(Guid));
        entries.Columns.Add("AccountId", typeof(Guid));
        entries.Columns.Add("Direction", typeof(byte));
        entries.Columns.Add("Amount", typeof(decimal));
        entries.Columns.Add("CreatedAtUtc", typeof(DateTime));

        foreach (var t in transactions)
        {
            header.Rows.Add(t.Id, (byte)t.Type, t.ReferenceId, t.Description, t.CreatedAtUtc);
            foreach (var e in t.Entries)
            {
                entries.Rows.Add(e.TransactionId, e.AccountId, (byte)e.Direction, e.Amount, e.CreatedAtUtc);
            }
        }

        // Headers and entries of one batch commit together, so the trigger sees complete transactions.
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await BulkCopyAsync(connection, transaction, "dbo.LedgerTransactions", header);
        await BulkCopyAsync(connection, transaction, "dbo.LedgerEntries", entries);
        await transaction.CommitAsync();
    }

    private static async Task BulkCopyAsync(SqlConnection connection, SqlTransaction transaction, string table, DataTable rows)
    {
        // CHECK_CONSTRAINTS also keeps the foreign keys trusted, which the optimizer relies on.
        using var bulk = new SqlBulkCopy(
            connection, SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.FireTriggers, transaction)
        {
            DestinationTableName = table,
            BulkCopyTimeout = 0,
            BatchSize = 0,
        };

        foreach (DataColumn column in rows.Columns)
        {
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulk.WriteToServerAsync(rows);
    }
}
