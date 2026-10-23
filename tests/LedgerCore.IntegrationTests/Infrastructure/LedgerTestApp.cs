using LedgerCore.Application;
using LedgerCore.Application.Accounts.OpenAccount;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;
using LedgerCore.Domain.Users;
using LedgerCore.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.IntegrationTests.Infrastructure;

/// <summary>
/// Sends commands and queries through the real Application and Infrastructure wiring, so tests
/// exercise MediatR, EF Core, the stored procedure and Dapper together against SQL Server.
/// </summary>
public sealed class LedgerTestApp(SqlServerFixture fixture)
{
    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Each call gets its own scope, like an HTTP request, so no DbContext is shared between steps.
    /// Pass <paramref name="utcNow"/> to control the timestamp the handler records.
    /// </summary>
    public async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, DateTime? utcNow = null)
    {
        var services = new ServiceCollection()
            .AddApplication()
            .AddInfrastructure(fixture.ConnectionString);

        if (utcNow is { } now)
        {
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        }

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    /// <summary>Opens a customer account for a new user, creating the currency's settlement account if needed.</summary>
    public async Task<Guid> OpenAccountAsync(string currency)
    {
        await EnsureSettlementAccountAsync(Currency.From(currency));
        var userId = await SeedUserAsync();
        return await SendAsync(new OpenAccountCommand(userId, currency));
    }

    public async Task<Guid> SeedUserAsync()
    {
        await using var db = fixture.CreateDbContext();
        var user = User.Register($"{Guid.NewGuid():N}@test.local", "hash", UserRole.User, SeedTime);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task EnsureSettlementAccountAsync(Currency currency)
    {
        await using var db = fixture.CreateDbContext();
        if (await db.Accounts.AnyAsync(a => a.Kind == AccountKind.Settlement && a.Currency == currency))
        {
            return;
        }

        var systemUserId = await SeedUserAsync();
        db.Accounts.Add(Account.OpenSettlement(systemUserId, currency, SeedTime));
        await db.SaveChangesAsync();
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
