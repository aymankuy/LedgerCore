using FluentAssertions;
using LedgerCore.Application.Ledger.Fees;
using LedgerCore.DataGenerator;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;
using LedgerCore.Domain.Ledger;
using LedgerCore.Domain.Users;
using LedgerCore.Infrastructure.Persistence;
using LedgerCore.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LedgerCore.IntegrationTests.Tools;

/// <summary>
/// The Phase 4 data generator at a small scale, in a database of its own on the shared container,
/// so its verification sees only generated data. The rows pass the same triggers and constraints
/// as API writes, and the generator's own audit finds nothing wrong.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class DataGeneratorTests(SqlServerFixture fixture)
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Generated_data_nets_to_zero_never_overdraws_and_concentrates_on_hot_accounts()
    {
        var connectionString = new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            InitialCatalog = $"Generator_{Guid.NewGuid():N}",
        }.ConnectionString;
        await using var db = new LedgerDbContext(new DbContextOptionsBuilder<LedgerDbContext>().UseSqlServer(connectionString).Options);
        await db.Database.MigrateAsync();
        var systemAccounts = await SeedSystemAccountsAsync(db);

        var options = new GeneratorOptions(
            Transactions: 20_000, Customers: 500, HotAccounts: 5, HotShare: 0.3, Days: 30,
            EndUtc: StartUtc.AddDays(30), Seed: 1, DemoPassword: "unused");
        var fees = new FeeSchedule(new Dictionary<string, FeeRule> { ["EUR"] = new() { Type = FeeType.Flat, Amount = 1m } });

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await new LedgerGenerator(options, fees, "not-a-real-hash").RunAsync(connection, systemAccounts);

        // Throws if any transaction fails to net to zero, any account is overdrawn, or any
        // constraint lost its trusted flag during the bulk load.
        await Verification.RunAsync(connection);

        (await db.LedgerTransactions.CountAsync()).Should().BeInRange(options.Transactions, options.Transactions + 1);
        (await db.LedgerTransactions.CountAsync(t => t.Type == TransactionType.Fee)).Should().BePositive();

        var hotOwners = db.Users.Where(u => u.Email.StartsWith("hot")).Select(u => u.Id);
        var hotAccounts = db.Accounts.Where(a => hotOwners.Contains(a.OwnerUserId)).Select(a => a.Id);
        var customerAccounts = db.Accounts.Where(a => a.Kind == AccountKind.Customer).Select(a => a.Id);
        var hotEntries = await db.LedgerEntries.CountAsync(e => hotAccounts.Contains(e.AccountId));
        var customerEntries = await db.LedgerEntries.CountAsync(e => customerAccounts.Contains(e.AccountId));

        // 5 of 500 accounts (1%) carry a large share of the customer activity.
        ((double)hotEntries / customerEntries).Should().BeGreaterThan(0.2);
    }

    private static async Task<List<Account>> SeedSystemAccountsAsync(LedgerDbContext db)
    {
        var system = User.Register("system@test.local", "unused", UserRole.User, StartUtc);
        db.Users.Add(system);
        var accounts = new[] { "EUR", "USD", "TRY" }
            .SelectMany(code => new[]
            {
                Account.OpenSettlement(system.Id, Currency.From(code), StartUtc),
                Account.OpenFeeRevenue(system.Id, Currency.From(code), StartUtc),
            })
            .ToList();
        db.Accounts.AddRange(accounts);
        await db.SaveChangesAsync();
        return accounts;
    }
}
