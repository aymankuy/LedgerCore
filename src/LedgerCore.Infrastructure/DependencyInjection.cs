using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Abstractions.Data;
using LedgerCore.Application.Abstractions.Idempotency;
using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Infrastructure.Authentication;
using LedgerCore.Infrastructure.Data;
using LedgerCore.Infrastructure.Outbox;
using LedgerCore.Infrastructure.Persistence;
using LedgerCore.Infrastructure.Persistence.Repositories;
using LedgerCore.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
}
