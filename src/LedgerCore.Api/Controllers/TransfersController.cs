using LedgerCore.Application.Ledger.Transfer;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LedgerCore.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/transfers")]
[Produces("application/json")]
public sealed class TransfersController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Moves money from one of the caller's accounts to any customer account. The source also pays
    /// the transfer fee of its currency, which the response reports.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<TransferCreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Transfer(TransferMoneyCommand command, CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new TransferCreatedResponse(result.TransactionId, result.Fee));
    }
}

public sealed record TransferCreatedResponse(Guid TransactionId, decimal Fee);
