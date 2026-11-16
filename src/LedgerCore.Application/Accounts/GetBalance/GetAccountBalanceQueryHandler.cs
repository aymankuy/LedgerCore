using Dapper;
using LedgerCore.Application.Abstractions.Data;
using LedgerCore.Application.Common;
using LedgerCore.Domain.Accounts;
using MediatR;

namespace LedgerCore.Application.Accounts.GetBalance;

public sealed class GetAccountBalanceQueryHandler(IDbConnectionFactory connectionFactory)
    : IRequestHandler<GetAccountBalanceQuery, AccountBalanceResponse>
{
    // The balance is derived from the ledger (spec rule 1). fn_AccountBalance is its only
    // definition, shared with the account list and the overdraft check.
    private const string Sql = """
        SELECT a.Id AS AccountId, a.Currency, b.Balance
        FROM dbo.Accounts AS a
        CROSS APPLY dbo.fn_AccountBalance(a.Id) AS b
        WHERE a.Id = @AccountId;
        """;

    public async Task<AccountBalanceResponse> Handle(GetAccountBalanceQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { query.AccountId }, cancellationToken: cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<AccountBalanceResponse>(command)
            ?? throw new NotFoundException(nameof(Account), query.AccountId);
    }
}
