using LedgerCore.Application;
using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Abstractions.Idempotency;
using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Auth.Login;
using LedgerCore.Application.Behaviors;
using LedgerCore.Application.Ledger.Deposit;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LedgerCore.UnitTests.Application;

public class ApplicationRegistrationTests
{
    [Fact]
    public void Behaviors_run_in_the_intended_order()
    {
        var services = new ServiceCollection().AddApplication();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(Substitute.For<ICurrentUser>());
        services.AddSingleton(Substitute.For<IAccountRepository>());
        services.AddSingleton(Substitute.For<IIdempotencyContext>());
        services.AddSingleton(Substitute.For<IIdempotencyStore>());
        services.AddSingleton(Substitute.For<IUnitOfWork>());
        using var provider = services.BuildServiceProvider();

        var behaviors = provider.GetServices<IPipelineBehavior<DepositCommand, Guid>>();

        // Logging first, so rejected requests are logged too. A malformed account id must be a 400,
        // never reach the database lookup of the access check. Only a caller allowed to touch the
        // account gets a cached response or a transaction, and the idempotency key is claimed inside it.
        behaviors.Select(b => b.GetType().GetGenericTypeDefinition()).Should().Equal(
            typeof(LoggingBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(AccountAccessBehavior<,>),
            typeof(CachingBehavior<,>),
            typeof(TransactionBehavior<,>),
            typeof(IdempotencyBehavior<,>));
    }

    [Fact]
    public void Every_handler_and_validator_is_registered()
    {
        using var provider = new ServiceCollection().AddApplication().BuildServiceProvider();

        provider.GetServices<FluentValidation.IValidator<DepositCommand>>().Should().ContainSingle();
        provider.GetService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
    }

    [Theory]
    [InlineData("", "secret")]
    [InlineData("ada@example.com", "")]
    public void Login_validator_requires_both_fields(string email, string password)
    {
        var result = new LoginCommandValidator().Validate(new LoginCommand(email, password));

        result.IsValid.Should().BeFalse();
    }
}
