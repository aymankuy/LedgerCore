using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Common;
using MediatR;

namespace LedgerCore.Application.Auth.Login;

public sealed class LoginCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ITokenService tokenService) : IRequestHandler<LoginCommand, AccessToken>
{
    public async Task<AccessToken> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByEmailAsync(command.Email, cancellationToken);

        if (user is null)
        {
            // Spend the same hashing time as a real check, so response timing does not reveal
            // whether the email is registered.
            passwordHasher.Hash(command.Password);
            throw new AuthenticationFailedException();
        }

        if (!passwordHasher.Verify(user.PasswordHash, command.Password))
        {
            throw new AuthenticationFailedException();
        }

        return tokenService.Issue(user);
    }
}
