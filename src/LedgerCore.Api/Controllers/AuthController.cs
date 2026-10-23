using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Auth.Login;
using LedgerCore.Application.Auth.Register;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace LedgerCore.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        var userId = await sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new RegisterResponse(userId));
    }

    [HttpPost("login")]
    [ProducesResponseType<AccessToken>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<AccessToken> Login(LoginCommand command, CancellationToken cancellationToken) =>
        sender.Send(command, cancellationToken);
}

public sealed record RegisterResponse(Guid UserId);
