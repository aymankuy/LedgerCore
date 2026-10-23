using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // The system clock; tests substitute their own TimeProvider.
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
