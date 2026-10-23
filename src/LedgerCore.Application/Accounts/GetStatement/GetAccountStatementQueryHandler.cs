using Dapper;
using LedgerCore.Application.Abstractions.Data;
using LedgerCore.Application.Common;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Ledger;
using MediatR;

namespace LedgerCore.Application.Accounts.GetStatement;

public sealed class GetAccountStatementQueryHandler(IDbConnectionFactory connectionFactory)
    : IRequestHandler<GetAccountStatementQuery, AccountStatementResponse>
{
    // Two result sets in one round trip: the account header with its opening balance, then the
    // entries in the window. The running balance is a window function over the entries, seeded
    // with the opening balance, so the database does the arithmetic in a single ordered pass.
    // Phase 4 moves this into a stored procedure and adds a covering index for it.
    private const string Sql = """
        DECLARE @OpeningBalance decimal(19, 4) =
            (SELECT COALESCE(SUM(CASE Direction WHEN 2 THEN Amount ELSE -Amount END), 0)
             FROM dbo.LedgerEntries
             WHERE AccountId = @AccountId AND CreatedAtUtc < @FromUtc);

        SELECT Id AS AccountId, Currency, @OpeningBalance AS OpeningBalance
        FROM dbo.Accounts
        WHERE Id = @AccountId;

        SELECT e.Id AS EntryId,
               e.TransactionId,
               t.Type,
               t.ReferenceId,
               t.Description,
               s.Amount,
               @OpeningBalance + SUM(s.Amount) OVER (ORDER BY e.CreatedAtUtc, e.Id ROWS UNBOUNDED PRECEDING) AS BalanceAfter,
               e.CreatedAtUtc
        FROM dbo.LedgerEntries AS e
        JOIN dbo.LedgerTransactions AS t ON t.Id = e.TransactionId
        CROSS APPLY (SELECT CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END AS Amount) AS s
        WHERE e.AccountId = @AccountId
          AND e.CreatedAtUtc >= @FromUtc
          AND e.CreatedAtUtc < @ToUtc
        ORDER BY e.CreatedAtUtc, e.Id;
        """;

    public async Task<AccountStatementResponse> Handle(GetAccountStatementQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var command = new CommandDefinition(
            Sql,
            new { query.AccountId, query.FromUtc, query.ToUtc },
            cancellationToken: cancellationToken);

        await using var results = await connection.QueryMultipleAsync(command);

        var header = await results.ReadSingleOrDefaultAsync<HeaderRow>()
            ?? throw new NotFoundException(nameof(Account), query.AccountId);
        var rows = await results.ReadAsync<LineRow>();

        var lines = rows
            .Select(r => new StatementLine(
                r.EntryId,
                r.TransactionId,
                (TransactionType)r.Type,
                r.ReferenceId,
                r.Description,
                r.Amount,
                r.BalanceAfter,
                DateTime.SpecifyKind(r.CreatedAtUtc, DateTimeKind.Utc)))
            .ToList();

        return new AccountStatementResponse(
            header.AccountId,
            header.Currency,
            query.FromUtc,
            query.ToUtc,
            header.OpeningBalance,
            lines.Count == 0 ? header.OpeningBalance : lines[^1].BalanceAfter,
            lines);
    }

    // Raw shapes as SQL returns them. datetime2 comes back without a Kind and tinyint as byte,
    // so the mapping above restores UTC and the enum.
    private sealed record HeaderRow(Guid AccountId, string Currency, decimal OpeningBalance);

    private sealed record LineRow(
        long EntryId,
        Guid TransactionId,
        byte Type,
        string? ReferenceId,
        string? Description,
        decimal Amount,
        decimal BalanceAfter,
        DateTime CreatedAtUtc);
}
