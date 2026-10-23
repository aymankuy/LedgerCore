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
    /// <summary>Moves money from one of the caller's accounts to any customer account.</summary>
    [HttpPost]
    [ProducesResponseType<TransactionCreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Transfer(TransferMoneyCommand command, CancellationToken cancellationToken)
    {
        var transactionId = await sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new TransactionCreatedResponse(transactionId));
    }
}
