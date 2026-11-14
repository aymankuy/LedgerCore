using System.Data;
using Dapper;
using FluentValidation;
using LedgerCore.Application.Abstractions.Data;
using LedgerCore.Domain.Ledger;
using MediatR;

namespace LedgerCore.Application.Reports;

/// <summary>Money moved per day, currency and transaction type in [FromUtc, ToUtc). Admin only.</summary>
public sealed record GetDailyVolumeQuery(DateTime FromUtc, DateTime ToUtc, string? Currency)
    : IRequest<IReadOnlyList<DailyVolumeRow>>, IReportWindow;

public sealed record DailyVolumeRow(DateOnly Day, string Currency, TransactionType Type, long Transactions, decimal Volume);

public sealed class GetDailyVolumeQueryValidator : AbstractValidator<GetDailyVolumeQuery>
{
    public GetDailyVolumeQueryValidator() => Include(new ReportWindowValidator());
}

public sealed class GetDailyVolumeQueryHandler(IDbConnectionFactory connectionFactory)
    : IRequestHandler<GetDailyVolumeQuery, IReadOnlyList<DailyVolumeRow>>
{
    public async Task<IReadOnlyList<DailyVolumeRow>> Handle(GetDailyVolumeQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var parameters = new DynamicParameters();
        parameters.Add("FromUtc", query.FromUtc, DbType.DateTime2);
        parameters.Add("ToUtc", query.ToUtc, DbType.DateTime2);
        parameters.Add("Currency", query.Currency, DbType.AnsiStringFixedLength, size: 3);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            "dbo.usp_GetDailyVolume", parameters, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));

        return rows
            .Select(r => new DailyVolumeRow(DateOnly.FromDateTime(r.Day), r.Currency, (TransactionType)r.Type, r.Transactions, r.Volume))
            .ToList();
    }

    private sealed record Row(DateTime Day, string Currency, byte Type, long Transactions, decimal Volume);
}
