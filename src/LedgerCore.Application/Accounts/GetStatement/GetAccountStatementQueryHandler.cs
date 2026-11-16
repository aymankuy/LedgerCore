using System.Data;
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
    // Returns two result sets in one round trip: the account header with the page's opening
    // balance, then up to PageSize + 1 lines with a running balance (see the migration that
    // creates it). The extra line only tells whether another page follows.
    private const string Procedure = "dbo.usp_GetAccountStatement";

    public async Task<AccountStatementResponse> Handle(GetAccountStatementQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        // The validator has already rejected a cursor that does not decode.
        StatementCursor? cursor = null;
        if (query.Cursor is not null)
        {
            StatementCursor.TryDecode(query.Cursor, out var decoded);
            cursor = decoded;
        }

        // Dapper sends DateTime as legacy datetime (3.33 ms steps) by default. Against a
        // datetime2(3) column that shifts the window bounds and forces an implicit conversion,
        // so the bounds are typed explicitly.
        var parameters = new DynamicParameters();
        parameters.Add("AccountId", query.AccountId);
        parameters.Add("FromUtc", query.FromUtc, DbType.DateTime2);
        parameters.Add("ToUtc", query.ToUtc, DbType.DateTime2);
        parameters.Add("PageSize", query.PageSize);
        parameters.Add("AfterUtc", cursor?.AfterUtc, DbType.DateTime2);
        parameters.Add("AfterEntryId", cursor?.AfterEntryId, DbType.Int64);

        var command = new CommandDefinition(
            Procedure, parameters, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);

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

        string? nextCursor = null;
        if (lines.Count > query.PageSize)
        {
            lines.RemoveAt(lines.Count - 1);
            nextCursor = new StatementCursor(lines[^1].CreatedAtUtc, lines[^1].EntryId).Encode();
        }

        return new AccountStatementResponse(
            header.AccountId,
            header.Currency,
            query.FromUtc,
            query.ToUtc,
            header.OpeningBalance,
            lines.Count == 0 ? header.OpeningBalance : lines[^1].BalanceAfter,
            lines,
            nextCursor);
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
