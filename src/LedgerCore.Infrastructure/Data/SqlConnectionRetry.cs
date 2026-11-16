using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LedgerCore.Infrastructure.Data;

/// <summary>
/// Retries opening a connection on transient errors, such as error 40613 ("not currently
/// available") while a paused serverless Azure SQL database resumes. Measured on the live
/// deployment, resuming took about 35 seconds, and the first requests failed with 500 before
/// this was in place (ADR 0011).
/// <para>
/// Only <c>Open</c> is retried, never a command: a command may run inside a transaction, and
/// repeating it there could move money twice. Deadlocks in transactions have their own retry
/// around the whole transaction (ADR 0002).
/// </para>
/// </summary>
public static class SqlConnectionRetry
{
    /// <summary>
    /// Up to about 90 seconds in total, with exponentially growing pauses. The transient error
    /// list is SqlClient's default, which includes 40613, 40197, 40501 and 4060.
    /// </summary>
    public static SqlRetryLogicBaseProvider Provider { get; } = Create(numberOfTries: 8, delta: TimeSpan.FromSeconds(2), max: TimeSpan.FromSeconds(20));

    public static SqlRetryLogicBaseProvider Create(int numberOfTries, TimeSpan delta, TimeSpan max) =>
        SqlConfigurableRetryFactory.CreateExponentialRetryProvider(new SqlRetryLogicOption
        {
            NumberOfTries = numberOfTries,
            DeltaTime = delta,
            MaxTimeInterval = max,
        });
}

/// <summary>Gives every connection EF Core opens the same open-retry as the Dapper read side.</summary>
internal sealed class SqlConnectionRetryInterceptor : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        Apply(connection);
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        Apply(connection);
        return ValueTask.FromResult(result);
    }

    private static void Apply(DbConnection connection)
    {
        if (connection is SqlConnection sql)
        {
            sql.RetryLogicProvider = SqlConnectionRetry.Provider;
        }
    }
}
