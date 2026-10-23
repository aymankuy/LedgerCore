using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Auth.Login;
using LedgerCore.Application.Common;
using LedgerCore.Domain.Users;
using LedgerCore.UnitTests.Domain;
using NSubstitute;

namespace LedgerCore.UnitTests.Application;

public class LoginCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly LoginCommandHandler _handler;

    private readonly User _user = User.Register("ada@example.com", "stored-hash", UserRole.User, TestAccounts.Now);

    public LoginCommandHandlerTests()
    {
        _users.GetByEmailAsync("ada@example.com", Arg.Any<CancellationToken>()).Returns(_user);
        _handler = new LoginCommandHandler(_users, _hasher, _tokens);
    }

    [Fact]
    public async Task Correct_password_returns_a_token()
    {
        var token = new AccessToken("jwt", TestAccounts.Now.AddHours(1));
        _hasher.Verify("stored-hash", "secret-pass").Returns(true);
        _tokens.Issue(_user).Returns(token);

        var result = await _handler.Handle(new LoginCommand("ada@example.com", "secret-pass"), CancellationToken.None);

        result.Should().Be(token);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        _hasher.Verify("stored-hash", "wrong").Returns(false);

        var act = () => _handler.Handle(new LoginCommand("ada@example.com", "wrong"), CancellationToken.None);

        await act.Should().ThrowAsync<AuthenticationFailedException>();
        _tokens.DidNotReceiveWithAnyArgs().Issue(default!);
    }

    [Fact]
    public async Task Unknown_email_is_rejected_with_the_same_error_and_still_hashes()
    {
        var act = () => _handler.Handle(new LoginCommand("nobody@example.com", "secret-pass"), CancellationToken.None);

        await act.Should().ThrowAsync<AuthenticationFailedException>();
        _hasher.Received(1).Hash("secret-pass");
        _tokens.DidNotReceiveWithAnyArgs().Issue(default!);
    }
}
