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
    // Returns two result sets in one round trip: the account header with its opening balance,
    // then the entries in the window with a running balance (see the migration that creates it).
    private const string Procedure = "dbo.usp_GetAccountStatement";

    public async Task<AccountStatementResponse> Handle(GetAccountStatementQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        // Dapper sends DateTime as legacy datetime (3.33 ms steps) by default. Against a
        // datetime2(3) column that shifts the window bounds and forces an implicit conversion,
        // so the bounds are typed explicitly.
        var parameters = new DynamicParameters();
        parameters.Add("AccountId", query.AccountId);
        parameters.Add("FromUtc", query.FromUtc, DbType.DateTime2);
        parameters.Add("ToUtc", query.ToUtc, DbType.DateTime2);

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
