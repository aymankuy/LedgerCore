using LedgerCore.Application.Abstractions.Idempotency;
using MediatR;

namespace LedgerCore.Application.Auth.Register;

/// <summary>Self-service sign-up. Always creates a <c>User</c>; admins are seeded, never registered.</summary>
public sealed record RegisterUserCommand(string Email, string Password) : IRequest<Guid>, IIdempotentCommand;
