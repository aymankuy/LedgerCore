using LedgerCore.Application.Abstractions.Authentication;
using MediatR;

namespace LedgerCore.Application.Auth.Login;

public sealed record LoginCommand(string Email, string Password) : IRequest<AccessToken>;
