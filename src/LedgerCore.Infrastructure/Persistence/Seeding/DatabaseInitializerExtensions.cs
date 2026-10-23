using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LedgerCore.Infrastructure.Persistence.Seeding;

public static class DatabaseInitializerExtensions
{
    /// <summary>Runs migrations and seeding when <see cref="DatabaseOptions.MigrateOnStartup"/> is on.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        if (!services.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnStartup)
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken);
    }
}
