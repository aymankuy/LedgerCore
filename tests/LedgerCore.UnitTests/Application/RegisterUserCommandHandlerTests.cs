using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Auth.Register;
using LedgerCore.Application.Common;
using LedgerCore.Domain.Users;
using LedgerCore.UnitTests.Domain;
using NSubstitute;

namespace LedgerCore.UnitTests.Application;

public class RegisterUserCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TimeProvider _time = Substitute.For<TimeProvider>();
    private readonly RegisterUserCommandHandler _handler;

    public RegisterUserCommandHandlerTests()
    {
        _time.GetUtcNow().Returns(new DateTimeOffset(TestAccounts.Now));
        _hasher.Hash("correct horse").Returns("hashed");
        _handler = new RegisterUserCommandHandler(_users, _hasher, _unitOfWork, _time);
    }

    [Fact]
    public async Task Handle_stores_a_user_with_the_hashed_password_and_the_user_role()
    {
        var id = await _handler.Handle(new RegisterUserCommand("Ada@Example.com", "correct horse"), CancellationToken.None);

        _users.Received(1).Add(Arg.Is<User>(u =>
            u.Id == id &&
            u.Email == "ada@example.com" &&
            u.PasswordHash == "hashed" &&
            u.Role == UserRole.User));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_rejects_an_email_that_is_already_registered()
    {
        _users.EmailExistsAsync("ada@example.com", Arg.Any<CancellationToken>()).Returns(true);

        var act = () => _handler.Handle(new RegisterUserCommand("ada@example.com", "correct horse"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        _users.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Theory]
    [InlineData("not-an-email", "long enough")]
    [InlineData("ada@example.com", "short")]
    public void Validator_rejects_bad_credentials(string email, string password)
    {
        var result = new RegisterUserCommandValidator().Validate(new RegisterUserCommand(email, password));

        result.IsValid.Should().BeFalse();
    }
}
