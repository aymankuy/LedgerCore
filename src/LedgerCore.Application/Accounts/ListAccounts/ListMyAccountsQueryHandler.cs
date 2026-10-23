using Dapper;
using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Abstractions.Data;
using LedgerCore.Domain.Accounts;
using MediatR;

namespace LedgerCore.Application.Accounts.ListAccounts;

public sealed class ListMyAccountsQueryHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    : IRequestHandler<ListMyAccountsQuery, IReadOnlyList<AccountSummary>>
{
    // The owner comes from the token, never from the request, so this cannot list anyone else's accounts.
    private const string Sql = """
        SELECT a.Id AS AccountId,
               a.Currency,
               a.Status,
               COALESCE(SUM(CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END), 0) AS Balance,
               a.CreatedAtUtc
        FROM dbo.Accounts AS a
        LEFT JOIN dbo.LedgerEntries AS e ON e.AccountId = a.Id
        WHERE a.OwnerUserId = @OwnerUserId AND a.Kind = 1
        GROUP BY a.Id, a.Currency, a.Status, a.CreatedAtUtc
        ORDER BY a.CreatedAtUtc, a.Id;
        """;

    public async Task<IReadOnlyList<AccountSummary>> Handle(ListMyAccountsQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { OwnerUserId = currentUser.UserId }, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<Row>(command);

        return rows
            .Select(r => new AccountSummary(
                r.AccountId,
                r.Currency,
                (AccountStatus)r.Status,
                r.Balance,
                DateTime.SpecifyKind(r.CreatedAtUtc, DateTimeKind.Utc)))
            .ToList();
    }

    private sealed record Row(Guid AccountId, string Currency, byte Status, decimal Balance, DateTime CreatedAtUtc);
}
