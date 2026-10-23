using Dapper;
using LedgerCore.Application.Abstractions.Data;
using LedgerCore.Application.Common;
using LedgerCore.Domain.Accounts;
using MediatR;

namespace LedgerCore.Application.Accounts.GetBalance;

public sealed class GetAccountBalanceQueryHandler(IDbConnectionFactory connectionFactory)
    : IRequestHandler<GetAccountBalanceQuery, AccountBalanceResponse>
{
    // The balance is derived from the ledger (spec rule 1): credits minus debits.
    // Phase 4 replaces the full scan with the latest snapshot plus the entries after it.
    private const string Sql = """
        SELECT a.Id AS AccountId,
               a.Currency,
               COALESCE(SUM(CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END), 0) AS Balance
        FROM dbo.Accounts AS a
        LEFT JOIN dbo.LedgerEntries AS e ON e.AccountId = a.Id
        WHERE a.Id = @AccountId
        GROUP BY a.Id, a.Currency;
        """;

    public async Task<AccountBalanceResponse> Handle(GetAccountBalanceQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { query.AccountId }, cancellationToken: cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<AccountBalanceResponse>(command)
            ?? throw new NotFoundException(nameof(Account), query.AccountId);
    }
}
