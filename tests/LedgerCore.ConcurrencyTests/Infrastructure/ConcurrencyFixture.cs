using LedgerCore.Application;
using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Accounts.OpenAccount;
using LedgerCore.Application.Ledger.Deposit;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;
using LedgerCore.Domain.Users;
using LedgerCore.Infrastructure;
using LedgerCore.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace LedgerCore.ConcurrencyTests.Infrastructure;

/// <summary>
/// One real SQL Server 2022 container per test run. Locking behaviour cannot be reproduced by
/// an in-memory provider, so every concurrency test runs against this database.
/// </summary>
public sealed class ConcurrencyFixture : IAsyncLifetime
{
    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private ServiceProvider? _services;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using (var db = CreateDbContext())
        {
            await db.Database.MigrateAsync();
        }

        // One provider for the whole run, like the API process. Each SendAsync opens its own
        // scope, so parallel calls behave like parallel HTTP requests with separate DbContexts.
        _services = new ServiceCollection()
            .AddApplication()
            .AddInfrastructure(_container.GetConnectionString())
            .AddSingleton<ICurrentUser>(new AdminUser())
            .BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    public async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        await using var scope = _services!.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    public async Task SendAsync(IRequest request)
    {
        await using var scope = _services!.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    /// <summary>Runs <paramref name="action"/> with the services of one request scope.</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _services!.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    /// <summary>Opens a customer account for a new user and deposits <paramref name="initialBalance"/> into it.</summary>
    public async Task<Guid> OpenFundedAccountAsync(string currency, decimal initialBalance)
    {
        await EnsureSettlementAccountAsync(Currency.From(currency));
        var accountId = await SendAsync(new OpenAccountCommand(await SeedUserAsync(), currency));
        await SendAsync(new DepositCommand(accountId, initialBalance));
        return accountId;
    }

    private LedgerDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<LedgerDbContext>().UseSqlServer(_container.GetConnectionString()).Options);

    private async Task<Guid> SeedUserAsync()
    {
        await using var db = CreateDbContext();
        var user = User.Register($"{Guid.NewGuid():N}@test.local", "hash", UserRole.User, SeedTime);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task EnsureSettlementAccountAsync(Currency currency)
    {
        await using var db = CreateDbContext();
        if (await db.Accounts.AnyAsync(a => a.Kind == AccountKind.Settlement && a.Currency == currency))
        {
            return;
        }

        db.Accounts.Add(Account.OpenSettlement(await SeedUserAsync(), currency, SeedTime));
        await db.SaveChangesAsync();
    }

    // These tests exercise concurrency, not access control, so they run as an admin.
    private sealed class AdminUser : ICurrentUser
    {
        public Guid UserId { get; } = Guid.NewGuid();

        public bool IsAdmin => true;
    }
}

[CollectionDefinition(Name)]
public sealed class ConcurrencyCollection : ICollectionFixture<ConcurrencyFixture>
{
    public const string Name = "Concurrency";
}
