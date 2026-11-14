using System.Globalization;
using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Ledger.Fees;
using LedgerCore.DataGenerator;
using LedgerCore.Domain.Accounts;
using LedgerCore.Infrastructure;
using LedgerCore.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Phase 4 performance data: `generate` loads ~10 million ledger entries into a database the API
// has already migrated and seeded; `benchmark` times the key queries against it.
// Usage (from the main LedgerCore folder):
//   dotnet run -c Release --project tools/LedgerCore.DataGenerator -- generate --connection "<sa connection string>"
//   dotnet run -c Release --project tools/LedgerCore.DataGenerator -- benchmark --connection "..." --label before
// The connection string can also come from the LEDGERCORE_PERF_connection environment variable.
// It needs sa (or db_owner): the application login may not bulk-load or empty the buffer pool.

if (args.Length == 0 || args[0] is not ("generate" or "benchmark"))
{
    Console.Error.WriteLine("Usage: LedgerCore.DataGenerator generate|benchmark --connection <connection string> [options]");
    return 1;
}

var config = new ConfigurationBuilder()
    .AddEnvironmentVariables("LEDGERCORE_PERF_")
    .AddCommandLine(args[1..])
    .Build();

var connectionString = config["connection"];
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Pass --connection or set LEDGERCORE_PERF_connection.");
    return 1;
}

if (args[0] == "benchmark")
{
    var label = config["label"] ?? "benchmark";
    var output = config["output"] ?? Path.Combine("docs", "performance");
    await new Benchmark(connectionString, label, output, int.Parse(config["runs"] ?? "20", CultureInfo.InvariantCulture)).RunAsync();
    return 0;
}

var options = new GeneratorOptions(
    Transactions: int.Parse(config["transactions"] ?? "5000000", CultureInfo.InvariantCulture),
    Customers: int.Parse(config["customers"] ?? "50000", CultureInfo.InvariantCulture),
    HotAccounts: int.Parse(config["hot"] ?? "20", CultureInfo.InvariantCulture),
    HotShare: double.Parse(config["hot-share"] ?? "0.3", CultureInfo.InvariantCulture),
    Days: int.Parse(config["days"] ?? "365", CultureInfo.InvariantCulture),
    EndUtc: config["end"] is { } end
        ? DateTime.SpecifyKind(DateTime.Parse(end, CultureInfo.InvariantCulture), DateTimeKind.Utc)
        : DateTime.UtcNow.Date,
    Seed: int.Parse(config["seed"] ?? "20261114", CultureInfo.InvariantCulture),
    DemoPassword: config["password"] ?? "demo-password");

// The same fee schedule as the API, read from its appsettings.json.
var feesPath = config["fees"] ?? Path.Combine("src", "LedgerCore.Api", "appsettings.json");
var feeRules = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(feesPath), optional: false).Build()
    .GetSection("Fees").Get<Dictionary<string, FeeRule>>() ?? [];

await using var provider = new ServiceCollection().AddInfrastructure(connectionString).BuildServiceProvider();
var passwordHash = provider.GetRequiredService<IPasswordHasher>().Hash(options.DemoPassword);

List<Account> systemAccounts;
await using (var db = provider.GetRequiredService<LedgerDbContext>())
{
    if (await db.Users.AnyAsync(u => u.Email.EndsWith("@" + GeneratorOptions.EmailDomain)))
    {
        Console.Error.WriteLine("This database already holds generated data. Start from a fresh database (docker compose down -v).");
        return 1;
    }

    systemAccounts = await db.Accounts.AsNoTracking().Where(a => a.Kind != AccountKind.Customer).ToListAsync();
    if (systemAccounts.Count == 0)
    {
        Console.Error.WriteLine("No settlement accounts found. Start the API once so it migrates and seeds the database.");
        return 1;
    }
}

Console.WriteLine($"Generating {options.Transactions:N0} transactions from {options.EndUtc.AddDays(-options.Days):yyyy-MM-dd} to {options.EndUtc:yyyy-MM-dd} (seed {options.Seed}).");

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();
await new LedgerGenerator(options, new FeeSchedule(feeRules), passwordHash).RunAsync(connection, systemAccounts);
await Verification.RunAsync(connection);
return 0;
