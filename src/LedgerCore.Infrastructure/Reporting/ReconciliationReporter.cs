using System.Data;
using Dapper;
using LedgerCore.Application.Abstractions.Data;

namespace LedgerCore.Infrastructure.Reporting;

/// <summary>Builds the reconciliation report from usp_GetReconciliationReport.</summary>
public sealed class ReconciliationReporter(IDbConnectionFactory connectionFactory, TimeProvider timeProvider)
{
    // At 10M entries the procedure scans the whole ledger in about 9 seconds.
    private static readonly int CommandTimeoutSeconds = (int)TimeSpan.FromMinutes(10).TotalSeconds;

    public async Task<ReconciliationReport> CreateAsync(DateOnly day, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var parameters = new DynamicParameters();
        parameters.Add("DayStartUtc", day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), DbType.DateTime2);

        await using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            "dbo.usp_GetReconciliationReport",
            parameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: CommandTimeoutSeconds,
            cancellationToken: cancellationToken));

        var activity = (await results.ReadAsync<CurrencyActivity>()).ToList();
        var unbalanced = (await results.ReadAsync<UnbalancedRow>()).ToList();
        var drifted = (await results.ReadAsync<DriftedRow>()).ToList();
        var nets = (await results.ReadAsync<CurrencyNet>()).ToList();

        return new ReconciliationReport(
            day,
            timeProvider.GetUtcNow().UtcDateTime,
            activity,
            unbalanced.FirstOrDefault()?.Total ?? 0,
            unbalanced.Select(r => new UnbalancedTransaction(r.TransactionId, r.Net)).ToList(),
            drifted.FirstOrDefault()?.Total ?? 0,
            drifted.Select(r => new DriftedAccount(r.AccountId, r.ReportedBalance, r.LedgerBalance)).ToList(),
            nets);
    }

    // Each sample row also carries the total number of problems found.
    private sealed record UnbalancedRow(long Total, Guid TransactionId, decimal Net);

    private sealed record DriftedRow(long Total, Guid AccountId, decimal ReportedBalance, decimal LedgerBalance);
}
