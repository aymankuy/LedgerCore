using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Accounts.Freeze;
using LedgerCore.Application.Accounts.GetBalance;
using LedgerCore.Application.Accounts.GetStatement;
using LedgerCore.Application.Accounts.ListAccounts;
using LedgerCore.Application.Accounts.OpenAccount;
using LedgerCore.Application.Ledger.Deposit;
using LedgerCore.Application.Ledger.Withdraw;
using LedgerCore.Domain.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LedgerCore.Api.Controllers;

/// <summary>
/// Accounts and the money movements on a single account. Ownership is not checked here: every
/// request that names an account is checked in the MediatR pipeline (AccountAccessBehavior).
/// </summary>
[ApiController]
[Authorize]
[Route("api/accounts")]
[Produces("application/json")]
public sealed class AccountsController(ISender sender, ICurrentUser currentUser, TimeProvider timeProvider) : ControllerBase
{
    private static readonly TimeSpan DefaultStatementWindow = TimeSpan.FromDays(30);

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AccountSummary>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<AccountSummary>> List(CancellationToken cancellationToken) =>
        sender.Send(new ListMyAccountsQuery(), cancellationToken);

    /// <summary>Opens an account for the caller. The owner always comes from the token.</summary>
    [HttpPost]
    [ProducesResponseType<AccountCreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Open(OpenAccountRequest request, CancellationToken cancellationToken)
    {
        var accountId = await sender.Send(new OpenAccountCommand(currentUser.UserId, request.Currency), cancellationToken);
        return CreatedAtAction(nameof(GetBalance), new { accountId }, new AccountCreatedResponse(accountId));
    }

    [HttpGet("{accountId:guid}/balance")]
    [ProducesResponseType<AccountBalanceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<AccountBalanceResponse> GetBalance(Guid accountId, CancellationToken cancellationToken) =>
        sender.Send(new GetAccountBalanceQuery(accountId), cancellationToken);

    /// <summary>
    /// Entries in [from, to). Both default to the last 30 days. Offsets are accepted and
    /// converted to UTC, so "2026-03-01T00:00:00+03:00" means 2026-02-28T21:00:00Z.
    /// </summary>
    [HttpGet("{accountId:guid}/statement")]
    [ProducesResponseType<AccountStatementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<AccountStatementResponse> GetStatement(
        Guid accountId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var toUtc = (to ?? timeProvider.GetUtcNow()).UtcDateTime;
        var fromUtc = from?.UtcDateTime ?? toUtc - DefaultStatementWindow;

        return sender.Send(new GetAccountStatementQuery(accountId, fromUtc, toUtc), cancellationToken);
    }

    [HttpPost("{accountId:guid}/deposits")]
    [ProducesResponseType<TransactionCreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Deposit(Guid accountId, MoneyMovementRequest request, CancellationToken cancellationToken)
    {
        var transactionId = await sender.Send(
            new DepositCommand(accountId, request.Amount, request.ReferenceId, request.Description), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new TransactionCreatedResponse(transactionId));
    }

    [HttpPost("{accountId:guid}/withdrawals")]
    [ProducesResponseType<TransactionCreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Withdraw(Guid accountId, MoneyMovementRequest request, CancellationToken cancellationToken)
    {
        var transactionId = await sender.Send(
            new WithdrawCommand(accountId, request.Amount, request.ReferenceId, request.Description), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new TransactionCreatedResponse(transactionId));
    }

    /// <summary>Admin only. Blocks every money movement on the account until it is unfrozen.</summary>
    [HttpPost("{accountId:guid}/freeze")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Freeze(Guid accountId, CancellationToken cancellationToken)
    {
        await sender.Send(new SetAccountFrozenCommand(accountId, Frozen: true), cancellationToken);
        return NoContent();
    }

    /// <summary>Admin only.</summary>
    [HttpPost("{accountId:guid}/unfreeze")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Unfreeze(Guid accountId, CancellationToken cancellationToken)
    {
        await sender.Send(new SetAccountFrozenCommand(accountId, Frozen: false), cancellationToken);
        return NoContent();
    }
}

public sealed record OpenAccountRequest(string Currency);

public sealed record AccountCreatedResponse(Guid AccountId);

public sealed record MoneyMovementRequest(decimal Amount, string? ReferenceId = null, string? Description = null);

public sealed record TransactionCreatedResponse(Guid TransactionId);
