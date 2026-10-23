using FluentValidation;
using LedgerCore.Application.Accounts.OpenAccount;
using LedgerCore.Application.Behaviors;

namespace LedgerCore.UnitTests.Application;

public class ValidationBehaviorTests
{
    private readonly ValidationBehavior<OpenAccountCommand, Guid> _behavior =
        new([new OpenAccountCommandValidator()]);

    private int _handlerCalls;

    [Fact]
    public async Task Valid_request_reaches_the_handler()
    {
        var expected = Guid.NewGuid();

        var result = await _behavior.Handle(new OpenAccountCommand(Guid.NewGuid(), "EUR"), Next(expected), CancellationToken.None);

        result.Should().Be(expected);
        _handlerCalls.Should().Be(1);
    }

    [Fact]
    public async Task Invalid_request_is_rejected_before_the_handler_runs()
    {
        var act = () => _behavior.Handle(new OpenAccountCommand(Guid.Empty, "eur"), Next(Guid.NewGuid()), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Select(e => e.PropertyName)
            .Should().BeEquivalentTo(nameof(OpenAccountCommand.OwnerUserId), nameof(OpenAccountCommand.Currency));
        _handlerCalls.Should().Be(0);
    }

    private MediatR.RequestHandlerDelegate<Guid> Next(Guid response) => _ =>
    {
        _handlerCalls++;
        return Task.FromResult(response);
    };
}
