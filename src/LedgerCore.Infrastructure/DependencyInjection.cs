using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Abstractions.Caching;
using LedgerCore.Application.Abstractions.Data;
using LedgerCore.Application.Abstractions.Idempotency;
using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Infrastructure.Authentication;
using LedgerCore.Infrastructure.Caching;
using LedgerCore.Infrastructure.Data;
using LedgerCore.Infrastructure.Outbox;
using LedgerCore.Infrastructure.Persistence;
using LedgerCore.Infrastructure.Persistence.Repositories;
using LedgerCore.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace LedgerCore.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<LedgerDbContext>(options => options.UseSqlServer(connectionString));

        // One DbContext per request, so the repositories and the unit of work share its changes.
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<LedgerDbContext>());
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ILedgerRepository, LedgerRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();

        // Used by the Worker: outbox dispatch and idempotency key clean-up.
        services.AddScoped<OutboxDispatcher>();
        services.AddScoped<IdempotencyKeyCleaner>();
        services.AddOptions<OutboxOptions>();

        // Read side: Dapper over its own connection, no repositories (spec Phase 3).
        services.AddSingleton<IDbConnectionFactory>(new SqlConnectionFactory(connectionString));

        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        // Options are bound by the host; unbound, every startup step is off.
        services.AddOptions<DatabaseOptions>();
        services.AddScoped<DatabaseInitializer>();

        return services;
    }

    /// <summary>
    /// Caches balance reads in Redis (API) and removes them when the ledger changes (Worker).
    /// The host starts even while Redis is down; the caching behavior then falls back to SQL Server.
    /// </summary>
    public static IServiceCollection AddRedisCache(this IServiceCollection services, string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;

        // While Redis is unreachable, fail each cache call at once instead of queueing it until a
        // 5-second timeout: a cache outage must not make every balance read slow.
        options.BacklogPolicy = BacklogPolicy.FailFast;

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options));
        services.AddSingleton<ICache, RedisCache>();
        services.AddSingleton<IOutboxMessageHandler, BalanceCacheInvalidator>();

        return services;
    }
}
