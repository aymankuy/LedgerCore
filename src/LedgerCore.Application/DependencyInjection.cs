using FluentValidation;
using LedgerCore.Application.Abstractions.Caching;
using LedgerCore.Application.Behaviors;
using LedgerCore.Application.Ledger.Fees;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LedgerCore.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            // Order matters: log everything, reject bad input and foreign accounts before any
            // transaction opens; serve cached queries only after the access check; then claim the
            // idempotency key inside the command's transaction.
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AccountAccessBehavior<,>));
            cfg.AddOpenBehavior(typeof(CachingBehavior<,>));
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
            cfg.AddOpenBehavior(typeof(IdempotencyBehavior<,>));
        });
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // Error messages are part of the API contract, so they stay English whatever the
        // server culture is (FluentValidation would otherwise localise them).
        ValidatorOptions.Global.LanguageManager.Enabled = false;

        // No cache unless the host registers one (Redis) before or after this call.
        services.TryAddSingleton<ICache, NoCache>();

        // No transfer fees unless the host registers a configured schedule before this call.
        services.TryAddSingleton(FeeSchedule.None);

        // The system clock; tests substitute their own TimeProvider.
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
