using LedgerCore.Application.Abstractions.Data;
using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Infrastructure.Data;
using LedgerCore.Infrastructure.Persistence;
using LedgerCore.Infrastructure.Persistence.Repositories;
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

        // Read side: Dapper over its own connection, no repositories (spec Phase 3).
        services.AddSingleton<IDbConnectionFactory>(new SqlConnectionFactory(connectionString));

        return services;
    }
}
