using FluentValidation;
using LedgerCore.Application.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AccountAccessBehavior<,>));
            cfg.AddOpenBehavior(typeof(IdempotencyBehavior<,>));
        });
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // Error messages are part of the API contract, so they stay English whatever the
        // server culture is (FluentValidation would otherwise localise them).
        ValidatorOptions.Global.LanguageManager.Enabled = false;

        // The system clock; tests substitute their own TimeProvider.
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
