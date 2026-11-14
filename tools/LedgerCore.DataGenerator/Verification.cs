using Microsoft.Data.SqlClient;

namespace LedgerCore.DataGenerator;

/// <summary>
/// Checks the loaded data the way an auditor would, independently of the generator's own
/// bookkeeping, then refreshes statistics so every benchmark starts from the same plans.
/// </summary>
public static class Verification
{
    private static readonly (string Name, string Sql)[] Checks =
    [
        ("transactions that do not net to zero",
            """
            SELECT COUNT_BIG(*) FROM (
                SELECT TransactionId FROM dbo.LedgerEntries
                GROUP BY TransactionId
                HAVING COUNT(*) < 2 OR SUM(CASE Direction WHEN 2 THEN Amount ELSE -Amount END) <> 0) AS t
            """),
        ("accounts below zero without overdraft",
            """
            SELECT COUNT_BIG(*) FROM (
                SELECT a.Id FROM dbo.Accounts AS a
                JOIN dbo.LedgerEntries AS e ON e.AccountId = a.Id
                WHERE a.AllowsOverdraft = 0
                GROUP BY a.Id
                HAVING SUM(CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END) < 0) AS t
            """),
        ("untrusted constraints and foreign keys",
            """
            SELECT (SELECT COUNT_BIG(*) FROM sys.check_constraints WHERE is_not_trusted = 1)
                 + (SELECT COUNT_BIG(*) FROM sys.foreign_keys WHERE is_not_trusted = 1)
            """),
    ];

    public static async Task RunAsync(SqlConnection connection)
    {
        var failed = false;
        foreach (var (name, sql) in Checks)
        {
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 0 };
            var count = (long)(await command.ExecuteScalarAsync())!;
            Console.WriteLine($"  {name}: {count}");
            failed |= count != 0;
        }

        if (failed)
        {
            throw new InvalidOperationException("The generated data failed verification.");
        }

        await using var statistics = new SqlCommand(
            "UPDATE STATISTICS dbo.LedgerEntries WITH FULLSCAN; UPDATE STATISTICS dbo.LedgerTransactions WITH FULLSCAN; UPDATE STATISTICS dbo.Accounts WITH FULLSCAN;",
            connection) { CommandTimeout = 0 };
        await statistics.ExecuteNonQueryAsync();
        Console.WriteLine("Verified. Statistics updated with FULLSCAN.");
    }
}
