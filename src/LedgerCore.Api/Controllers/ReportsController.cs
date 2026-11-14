using LedgerCore.Application.Reports;
using LedgerCore.Domain.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LedgerCore.Api.Controllers;

/// <summary>
/// Read-only reports over the whole ledger, backed by stored procedures. Admin only: they cover
/// every customer's accounts. Windows are [from, to) and default to the last 30 days.
/// </summary>
[ApiController]
[Authorize(Roles = nameof(UserRole.Admin))]
[Route("api/reports")]
[Produces("application/json")]
public sealed class ReportsController(ISender sender, TimeProvider timeProvider) : ControllerBase
{
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(30);

    [HttpGet("daily-volume")]
    [ProducesResponseType<IReadOnlyList<DailyVolumeRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IReadOnlyList<DailyVolumeRow>> DailyVolume(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? currency,
        CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc) = Window(from, to);
        return sender.Send(new GetDailyVolumeQuery(fromUtc, toUtc, currency), cancellationToken);
    }

    [HttpGet("top-accounts")]
    [ProducesResponseType<IReadOnlyList<TopAccountRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IReadOnlyList<TopAccountRow>> TopAccounts(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? currency,
        [FromQuery] int top = 10,
        CancellationToken cancellationToken = default)
    {
        var (fromUtc, toUtc) = Window(from, to);
        return sender.Send(new GetTopAccountsQuery(fromUtc, toUtc, currency, top), cancellationToken);
    }

    private (DateTime FromUtc, DateTime ToUtc) Window(DateTimeOffset? from, DateTimeOffset? to)
    {
        var toUtc = (to ?? timeProvider.GetUtcNow()).UtcDateTime;
        return (from?.UtcDateTime ?? toUtc - DefaultWindow, toUtc);
    }
}
