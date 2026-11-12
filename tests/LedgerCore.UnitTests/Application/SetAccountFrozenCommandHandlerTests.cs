using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Accounts.Freeze;
using LedgerCore.Application.Common;
using LedgerCore.Domain.Accounts;
using LedgerCore.UnitTests.Domain;
using NSubstitute;

namespace LedgerCore.UnitTests.Application;

public class SetAccountFrozenCommandHandlerTests
{
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly SetAccountFrozenCommandHandler _handler;

    private readonly Account _account = TestAccounts.Customer();

    public SetAccountFrozenCommandHandlerTests()
    {
        _accounts.GetByIdAsync(_account.Id, Arg.Any<CancellationToken>()).Returns(_account);
        _handler = new SetAccountFrozenCommandHandler(_accounts, _unitOfWork);
    }

    [Fact]
    public async Task Handle_freezes_and_saves()
    {
        await _handler.Handle(new SetAccountFrozenCommand(_account.Id, Frozen: true), CancellationToken.None);

        _account.Status.Should().Be(AccountStatus.Frozen);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_unfreezes_a_frozen_account()
    {
        _account.Freeze();

        await _handler.Handle(new SetAccountFrozenCommand(_account.Id, Frozen: false), CancellationToken.None);

        _account.Status.Should().Be(AccountStatus.Active);
    }

    [Fact]
    public async Task Handle_throws_not_found_for_an_unknown_account()
    {
        var act = () => _handler.Handle(new SetAccountFrozenCommand(Guid.NewGuid(), Frozen: true), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public void Validator_rejects_an_empty_account_id()
    {
        var result = new SetAccountFrozenCommandValidator().Validate(new SetAccountFrozenCommand(Guid.Empty, Frozen: true));

        result.IsValid.Should().BeFalse();
    }
}
