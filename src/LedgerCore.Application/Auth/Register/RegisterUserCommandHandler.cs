using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Common;
using LedgerCore.Domain.Users;
using MediatR;

namespace LedgerCore.Application.Auth.Register;

public sealed class RegisterUserCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IRequestHandler<RegisterUserCommand, Guid>
{
    public async Task<Guid> Handle(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        // A friendly 409 for the common case. Two simultaneous sign-ups with the same email can
        // still both pass this check; the unique index on Users.Email rejects the second one.
        if (await users.EmailExistsAsync(command.Email, cancellationToken))
        {
            throw new ConflictException("An account with this email already exists.");
        }

        var user = User.Register(
            command.Email,
            passwordHasher.Hash(command.Password),
            UserRole.User,
            timeProvider.GetUtcNow().UtcDateTime);

        users.Add(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return user.Id;
    }
}
