using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace LedgerCore.DataGenerator;

/// <summary>
/// Times each query in Benchmarks/*.sql against the generated data: one cold run after the buffer
/// pool is emptied, then warm runs for the median and 95th percentile. It also records logical
/// reads and saves the actual execution plan, which SSMS or Azure Data Studio can open.
/// </summary>
internal sealed partial class Benchmark(string connectionString, string label, string outputDirectory, int runs)
{
    private const int WarmUpRuns = 3;

    public async Task RunAsync()
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var parameters = await FindParametersAsync(connection);
        Console.WriteLine($"Hot account {parameters.HotAccountId}: {parameters.HotEntriesInWindow:N0} entries in the 30-day window, {parameters.HotEntries:N0} in total.");

        var planDirectory = Path.Combine(outputDirectory, "plans", label);
        Directory.CreateDirectory(planDirectory);

        var report = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"# Benchmark: {label}")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Measured {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC. {await DescribeDataAsync(connection)}")
            .AppendLine(CultureInfo.InvariantCulture, $"Hot account: {parameters.HotEntries:N0} entries, {parameters.HotEntriesInWindow:N0} of them in the 30-day window.")
            .AppendLine(CultureInfo.InvariantCulture, $"Warm figures are over {runs} runs after {WarmUpRuns} warm-up runs; cold is one run after `DBCC DROPCLEANBUFFERS`.")
            .AppendLine()
            .AppendLine("| Query | Rows | Cold ms | Warm median ms | Warm p95 ms | Logical reads |")
            .AppendLine("|---|---:|---:|---:|---:|---:|");

        var queryDirectory = Path.Combine(AppContext.BaseDirectory, "Benchmarks");
        foreach (var file in Directory.GetFiles(queryDirectory, "*.sql").Order())
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var sql = await File.ReadAllTextAsync(file);

            await ExecuteAsync(connection, "CHECKPOINT; DBCC DROPCLEANBUFFERS WITH NO_INFOMSGS;");
            var (cold, rows) = await TimeAsync(connection, sql, parameters);

            for (var i = 0; i < WarmUpRuns; i++)
            {
                await TimeAsync(connection, sql, parameters);
            }

            var warm = new List<double>(runs);
            for (var i = 0; i < runs; i++)
            {
                warm.Add((await TimeAsync(connection, sql, parameters)).Milliseconds);
            }

            var reads = await LogicalReadsAsync(connection, sql, parameters);
            var plan = await ActualPlanAsync(connection, sql, parameters);
            await File.WriteAllTextAsync(Path.Combine(planDirectory, $"{name}.sqlplan"), plan);

            warm.Sort();
            var line = string.Create(CultureInfo.InvariantCulture,
                $"| {name} | {rows:N0} | {cold:F1} | {Percentile(warm, 0.5):F1} | {Percentile(warm, 0.95):F1} | {reads:N0} |");
            report.AppendLine(line);
            Console.WriteLine(line);
        }

        var reportPath = Path.Combine(outputDirectory, $"{label}.md");
        await File.WriteAllTextAsync(reportPath, report.ToString());
        Console.WriteLine($"Saved {reportPath} and the plans in {planDirectory}.");
    }

    private static async Task<(double Milliseconds, int Rows)> TimeAsync(SqlConnection connection, string sql, BenchmarkParameters parameters)
    {
        await using var command = CreateCommand(connection, sql, parameters);
        var stopwatch = Stopwatch.StartNew();
        var rows = 0;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            do
            {
                while (await reader.ReadAsync())
                {
                    rows++;
                }
            }
            while (await reader.NextResultAsync());
        }

        return (stopwatch.Elapsed.TotalMilliseconds, rows);
    }

    private static async Task<long> LogicalReadsAsync(SqlConnection connection, string sql, BenchmarkParameters parameters)
    {
        long reads = 0;
        void OnMessage(object sender, SqlInfoMessageEventArgs e)
        {
            foreach (Match match in LogicalReads().Matches(e.Message))
            {
                reads += long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        connection.InfoMessage += OnMessage;
        try
        {
            await ExecuteAsync(connection, "SET STATISTICS IO ON;");
            await TimeAsync(connection, sql, parameters);
        }
        finally
        {
            await ExecuteAsync(connection, "SET STATISTICS IO OFF;");
            connection.InfoMessage -= OnMessage;
        }

        return reads;
    }

    /// <summary>The actual plan arrives as one extra result set per statement; the plans are merged into one file.</summary>
    private static async Task<string> ActualPlanAsync(SqlConnection connection, string sql, BenchmarkParameters parameters)
    {
        var plans = new List<string>();
        await ExecuteAsync(connection, "SET STATISTICS XML ON;");
        try
        {
            await using var command = CreateCommand(connection, sql, parameters);
            await using var reader = await command.ExecuteReaderAsync();
            do
            {
                var isPlan = reader.FieldCount == 1 && reader.GetName(0).Contains("Showplan", StringComparison.Ordinal);
                while (await reader.ReadAsync())
                {
                    if (isPlan)
                    {
                        plans.Add(reader.GetString(0));
                    }
                }
            }
            while (await reader.NextResultAsync());
        }
        finally
        {
            await ExecuteAsync(connection, "SET STATISTICS XML OFF;");
        }

        return MergePlans(plans);
    }

    // Each plan is a full ShowPlanXML document. SSMS opens one document with several statements,
    // so the statements of the later plans are moved into the first one.
    private static string MergePlans(List<string> plans)
    {
        if (plans.Count <= 1)
        {
            return plans.SingleOrDefault() ?? string.Empty;
        }

        var first = System.Xml.Linq.XDocument.Parse(plans[0]);
        var ns = first.Root!.Name.Namespace;
        var statements = first.Descendants(ns + "Statements").First();
        foreach (var plan in plans.Skip(1))
        {
            var other = System.Xml.Linq.XDocument.Parse(plan);
            statements.Add(other.Descendants(ns + "Statements").First().Elements());
        }

        return first.ToString();
    }

    private static SqlCommand CreateCommand(SqlConnection connection, string sql, BenchmarkParameters parameters)
    {
        var command = new SqlCommand(sql, connection) { CommandTimeout = 0 };

        // Typed the way the application types them: datetime2 bounds, not legacy datetime.
        command.Parameters.Add("@AccountId", SqlDbType.UniqueIdentifier).Value = parameters.HotAccountId;
        command.Parameters.Add("@OwnerUserId", SqlDbType.UniqueIdentifier).Value = parameters.HotOwnerId;
        command.Parameters.Add("@SettlementAccountId", SqlDbType.UniqueIdentifier).Value = parameters.SettlementAccountId;
        command.Parameters.Add("@FromUtc", SqlDbType.DateTime2).Value = parameters.FromUtc;
        command.Parameters.Add("@ToUtc", SqlDbType.DateTime2).Value = parameters.ToUtc;
        return command;
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<BenchmarkParameters> FindParametersAsync(SqlConnection connection)
    {
        const string sql = $"""
            DECLARE @Hot uniqueidentifier, @Owner uniqueidentifier;
            SELECT @Hot = a.Id, @Owner = a.OwnerUserId
            FROM dbo.Accounts AS a
            JOIN dbo.Users AS u ON u.Id = a.OwnerUserId
            WHERE u.Email = 'hot1@{GeneratorOptions.EmailDomain}';

            DECLARE @To datetime2(3) = DATEADD(millisecond, 1, (SELECT MAX(CreatedAtUtc) FROM dbo.LedgerEntries WHERE AccountId = @Hot));
            DECLARE @From datetime2(3) = DATEADD(day, -30, @To);

            SELECT @Hot, @Owner,
                   (SELECT Id FROM dbo.Accounts WHERE Kind = 2 AND Currency = 'EUR'),
                   @From, @To,
                   (SELECT COUNT_BIG(*) FROM dbo.LedgerEntries WHERE AccountId = @Hot),
                   (SELECT COUNT_BIG(*) FROM dbo.LedgerEntries WHERE AccountId = @Hot AND CreatedAtUtc >= @From AND CreatedAtUtc < @To);
            """;

        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 0 };
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || reader.IsDBNull(0) || reader.IsDBNull(3))
        {
            throw new InvalidOperationException("No generated data found. Run the generate command first.");
        }

        return new BenchmarkParameters(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetDateTime(3), reader.GetDateTime(4),
            reader.GetInt64(5), reader.GetInt64(6));
    }

    private static async Task<string> DescribeDataAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand(
            "SELECT (SELECT COUNT_BIG(*) FROM dbo.LedgerEntries), (SELECT COUNT_BIG(*) FROM dbo.Accounts), @@VERSION",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        var version = reader.GetString(2).Split('\n')[0].Trim();
        return string.Create(CultureInfo.InvariantCulture, $"{reader.GetInt64(0):N0} ledger entries, {reader.GetInt64(1):N0} accounts, {version}.");
    }

    private static double Percentile(List<double> sorted, double percentile) =>
        sorted[(int)Math.Ceiling(percentile * sorted.Count) - 1];

    [GeneratedRegex(@"logical reads (\d+)")]
    private static partial Regex LogicalReads();
}

internal sealed record BenchmarkParameters(
    Guid HotAccountId,
    Guid HotOwnerId,
    Guid SettlementAccountId,
    DateTime FromUtc,
    DateTime ToUtc,
    long HotEntries,
    long HotEntriesInWindow);
