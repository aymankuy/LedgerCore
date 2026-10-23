using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Accounts.OpenAccount;
using LedgerCore.Domain.Accounts;
using NSubstitute;

namespace LedgerCore.UnitTests.Application;

public class OpenAccountCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TimeProvider _time = Substitute.For<TimeProvider>();

    public OpenAccountCommandHandlerTests() => _time.GetUtcNow().Returns(Now);

    [Fact]
    public async Task Handle_AddsActiveAccountAndSaves()
    {
        var owner = Guid.NewGuid();
        var handler = new OpenAccountCommandHandler(_accounts, _unitOfWork, _time);

        var id = await handler.Handle(new OpenAccountCommand(owner, "EUR"), CancellationToken.None);

        _accounts.Received(1).Add(Arg.Is<Account>(a =>
            a.Id == id &&
            a.OwnerUserId == owner &&
            a.Currency.Code == "EUR" &&
            a.Status == AccountStatus.Active &&
            a.CreatedAtUtc == Now.UtcDateTime));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("eur")]
    [InlineData("EU")]
    [InlineData("")]
    public void Validator_RejectsInvalidCurrency(string currency)
    {
        var result = new OpenAccountCommandValidator()
            .Validate(new OpenAccountCommand(Guid.NewGuid(), currency));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_RejectsEmptyOwner()
    {
        var result = new OpenAccountCommandValidator()
            .Validate(new OpenAccountCommand(Guid.Empty, "EUR"));

        result.IsValid.Should().BeFalse();
    }
}
