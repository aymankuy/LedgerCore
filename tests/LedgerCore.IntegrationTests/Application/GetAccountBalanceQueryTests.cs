using FluentAssertions;
using LedgerCore.Application;
using LedgerCore.Application.Accounts.GetBalance;
using LedgerCore.Application.Accounts.OpenAccount;
using LedgerCore.Application.Common;
using LedgerCore.Application.Ledger.Deposit;
using LedgerCore.Application.Ledger.Withdraw;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;
using LedgerCore.Domain.Users;
using LedgerCore.Infrastructure;
using LedgerCore.IntegrationTests.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.IntegrationTests.Application;

/// <summary>
/// Runs commands (EF Core + stored procedure) and the balance query (Dapper) through MediatR
/// against a real SQL Server, so the write side and the read side are checked against each other.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class GetAccountBalanceQueryTests(SqlServerFixture fixture)
{
    // A currency no other test uses, so this class owns its settlement account.
    private static readonly Currency Jpy = Currency.From("JPY");

    private static readonly DateTime Now = new(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Balance_is_deposits_minus_withdrawals()
    {
        var accountId = await OpenFundedAccountAsync();
        await SendAsync(new WithdrawCommand(accountId, 30.25m));

        var result = await SendAsync(new GetAccountBalanceQuery(accountId));

        result.Should().Be(new AccountBalanceResponse(accountId, "JPY", 69.75m));
    }

    [Fact]
    public async Task New_account_has_a_zero_balance()
    {
        var userId = await SeedUserAsync();
        var accountId = await SendAsync(new OpenAccountCommand(userId, "JPY"));

        var result = await SendAsync(new GetAccountBalanceQuery(accountId));

        result.Balance.Should().Be(0m);
    }

    [Fact]
    public async Task Unknown_account_is_not_found()
    {
        var act = () => SendAsync(new GetAccountBalanceQuery(Guid.NewGuid()));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Guid> OpenFundedAccountAsync()
    {
        await EnsureSettlementAccountAsync();
        var userId = await SeedUserAsync();
        var accountId = await SendAsync(new OpenAccountCommand(userId, "JPY"));
        await SendAsync(new DepositCommand(accountId, 100m));
        return accountId;
    }

    // Each call gets its own scope, like an HTTP request, so no DbContext is shared between steps.
    private async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        await using var services = new ServiceCollection()
            .AddApplication()
            .AddInfrastructure(fixture.ConnectionString)
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    private async Task<Guid> SeedUserAsync()
    {
        await using var db = fixture.CreateDbContext();
        var user = User.Register($"{Guid.NewGuid():N}@test.local", "hash", UserRole.User, Now);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task EnsureSettlementAccountAsync()
    {
        await using var db = fixture.CreateDbContext();
        if (await db.Accounts.AnyAsync(a => a.Kind == AccountKind.Settlement && a.Currency == Jpy))
        {
            return;
        }

        var systemUserId = await SeedUserAsync();
        db.Accounts.Add(Account.OpenSettlement(systemUserId, Jpy, Now));
        await db.SaveChangesAsync();
    }
}
