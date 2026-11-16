using System.Data;
using Dapper;
using FluentValidation;
using LedgerCore.Application.Abstractions.Data;
using MediatR;

namespace LedgerCore.Application.Reports;

/// <summary>The customer accounts that moved the most money in [FromUtc, ToUtc). Admin only.</summary>
public sealed record GetTopAccountsQuery(DateTime FromUtc, DateTime ToUtc, string? Currency, int Top)
    : IRequest<IReadOnlyList<TopAccountRow>>, IReportWindow;

public sealed record TopAccountRow(Guid AccountId, string Currency, long Entries, decimal MoneyIn, decimal MoneyOut, decimal Volume);

public sealed class GetTopAccountsQueryValidator : AbstractValidator<GetTopAccountsQuery>
{
    public const int MaxTop = 100;

    public GetTopAccountsQueryValidator()
    {
        Include(new ReportWindowValidator());
        RuleFor(q => q.Top).InclusiveBetween(1, MaxTop);
    }
}

public sealed class GetTopAccountsQueryHandler(IDbConnectionFactory connectionFactory)
    : IRequestHandler<GetTopAccountsQuery, IReadOnlyList<TopAccountRow>>
{
    public async Task<IReadOnlyList<TopAccountRow>> Handle(GetTopAccountsQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var parameters = new DynamicParameters();
        parameters.Add("FromUtc", query.FromUtc, DbType.DateTime2);
        parameters.Add("ToUtc", query.ToUtc, DbType.DateTime2);
        parameters.Add("Currency", query.Currency, DbType.AnsiStringFixedLength, size: 3);
        parameters.Add("Top", query.Top);

        var rows = await connection.QueryAsync<TopAccountRow>(new CommandDefinition(
            "dbo.usp_GetTopAccountsByVolume", parameters, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));

        return rows.ToList();
    }
}
