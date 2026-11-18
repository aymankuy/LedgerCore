using FluentAssertions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;
using LedgerCore.Domain.Users;
using LedgerCore.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LedgerCore.IntegrationTests.Persistence;

/// <summary>
/// The database records every account status in AccountStatusHistory, which is what the
/// warehouse's slowly changing dimension is built from.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AccountStatusHistoryTests(SqlServerFixture fixture)
{
    private const int AppendOnlyError = 50020;

    private static readonly DateTime Now = new(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Opening_an_account_records_its_first_status_at_creation_time()
    {
        var account = await OpenAccountAsync();

        var history = await HistoryAsync(account.Id);

        history.Should().ContainSingle();
        history[0].Status.Should().Be(AccountStatus.Active);
        history[0].ChangedAtUtc.Should().Be(Now);
    }

    [Fact]
    public async Task Each_status_change_appends_a_row_stamped_with_the_database_clock()
    {
        var account = await OpenAccountAsync();
        var before = DateTime.UtcNow;

        await ChangeAsync(account.Id, a => a.Freeze());
        await ChangeAsync(account.Id, a => a.Unfreeze());

        var history = await HistoryAsync(account.Id);

        history.Select(h => h.Status).Should().Equal(AccountStatus.Active, AccountStatus.Frozen, AccountStatus.Active);
        history[1].ChangedAtUtc.Should().BeOnOrAfter(before.AddSeconds(-1));
        history[2].ChangedAtUtc.Should().BeOnOrAfter(history[1].ChangedAtUtc);
    }

    [Fact]
    public async Task An_update_that_leaves_the_status_alone_adds_nothing()
    {
        var account = await OpenAccountAsync();

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("UPDATE dbo.Accounts SET AllowsOverdraft = 1 WHERE Id = @id", connection);
            command.Parameters.AddWithValue("@id", account.Id);
            await command.ExecuteNonQueryAsync();
        }

        (await HistoryAsync(account.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task History_is_append_only()
    {
        var account = await OpenAccountAsync();

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        foreach (var sql in new[]
                 {
                     "UPDATE dbo.AccountStatusHistory SET Status = 3 WHERE AccountId = @id",
                     "DELETE FROM dbo.AccountStatusHistory WHERE AccountId = @id",
                 })
        {
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@id", account.Id);

            var act = () => command.ExecuteNonQueryAsync();

            (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(AppendOnlyError);
        }

        (await HistoryAsync(account.Id)).Should().ContainSingle();
    }

    private async Task<Account> OpenAccountAsync()
    {
        await using var db = fixture.CreateDbContext();

        var user = User.Register($"{Guid.NewGuid():N}@test.local", "hash", UserRole.User, Now);
        var account = Account.Open(user.Id, Currency.From("EUR"), Now);

        db.Users.Add(user);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        return account;
    }

    private async Task ChangeAsync(Guid accountId, Action<Account> change)
    {
        await using var db = fixture.CreateDbContext();

        var account = await db.Accounts.SingleAsync(a => a.Id == accountId);
        change(account);
        await db.SaveChangesAsync();
    }

    private async Task<List<StatusRow>> HistoryAsync(Guid accountId)
    {
        await using var db = fixture.CreateDbContext();

        return await db.AccountStatusHistory
            .Where(h => h.AccountId == accountId)
            .OrderBy(h => h.Id)
            .Select(h => new StatusRow(h.Status, h.ChangedAtUtc))
            .ToListAsync();
    }

    private sealed record StatusRow(AccountStatus Status, DateTime ChangedAtUtc);
}
