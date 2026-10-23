using FluentAssertions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;
using LedgerCore.Domain.Users;
using LedgerCore.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LedgerCore.IntegrationTests.Persistence;

/// <summary>Proves the filtered unique index that allows one settlement account per currency (ADR 0003).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SettlementAccountTests(SqlServerFixture fixture)
{
    private static readonly DateTime Now = new(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Second_settlement_account_for_the_same_currency_is_rejected()
    {
        // A currency no other test uses, so the first insert always succeeds.
        var currency = Currency.From("CHF");
        var user = await SeedUserAsync();

        await using (var db = fixture.CreateDbContext())
        {
            db.Accounts.Add(Account.OpenSettlement(user.Id, currency, Now));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.Accounts.Add(Account.OpenSettlement(user.Id, currency, Now));
            var act = () => db.SaveChangesAsync();

            await act.Should().ThrowAsync<DbUpdateException>();
        }
    }

    [Fact]
    public async Task Customer_accounts_are_not_limited_per_currency()
    {
        var user = await SeedUserAsync();

        await using var db = fixture.CreateDbContext();
        db.Accounts.AddRange(
            Account.Open(user.Id, Currency.From("GBP"), Now),
            Account.Open(user.Id, Currency.From("GBP"), Now));
        var act = () => db.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    private async Task<User> SeedUserAsync()
    {
        await using var db = fixture.CreateDbContext();
        var user = User.Register($"{Guid.NewGuid():N}@test.local", "hash", UserRole.User, Now);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
