using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Accounts.GetBalance;
using LedgerCore.Application.Behaviors;
using LedgerCore.Application.Ledger.Deposit;
using NSubstitute;

namespace LedgerCore.UnitTests.Application;

public class TransactionBehaviorTests
{
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task A_command_runs_inside_one_transaction()
    {
        var handlerRanInside = false;
        _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<Guid>>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                handlerRanInside = true;
                return call.Arg<Func<Task<Guid>>>()();
            });
        var expected = Guid.NewGuid();

        var result = await new TransactionBehavior<DepositCommand, Guid>(_unitOfWork)
            .Handle(new DepositCommand(Guid.NewGuid(), 10m), _ => Task.FromResult(expected), CancellationToken.None);

        result.Should().Be(expected);
        handlerRanInside.Should().BeTrue();
    }

    [Fact]
    public async Task A_query_runs_without_a_transaction()
    {
        var response = new AccountBalanceResponse(Guid.NewGuid(), "EUR", 0m);

        var result = await new TransactionBehavior<GetAccountBalanceQuery, AccountBalanceResponse>(_unitOfWork)
            .Handle(new GetAccountBalanceQuery(Guid.NewGuid()), _ => Task.FromResult(response), CancellationToken.None);

        result.Should().Be(response);
        await _unitOfWork.DidNotReceiveWithAnyArgs().ExecuteInTransactionAsync<AccountBalanceResponse>(default!, default);
    }
}
