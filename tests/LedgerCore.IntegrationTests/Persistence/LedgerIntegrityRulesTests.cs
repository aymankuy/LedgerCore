using System.Data;
using FluentAssertions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;
using LedgerCore.Domain.Ledger;
using LedgerCore.Domain.Users;
using LedgerCore.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace LedgerCore.IntegrationTests.Persistence;

/// <summary>
/// Proves the database-level guarantees from spec §5 against a real SQL Server:
/// entries net to zero (error 50002) and are append-only (error 50001). See ADR 0001.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class LedgerIntegrityRulesTests(SqlServerFixture fixture)
{
    private const int AppendOnlyError = 50001;
    private const int NetZeroError = 50002;

    private static readonly DateTime Now = new(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Balanced_transaction_is_stored()
    {
        var (a, b) = await SeedTwoAccountsAsync();
        var transactionId = Guid.NewGuid();

        await PostAsync(transactionId, (a, EntryDirection.Debit, 10m), (b, EntryDirection.Credit, 10m));

        (await CountEntriesAsync(transactionId)).Should().Be(2);
    }

    [Fact]
    public async Task Unbalanced_transaction_is_rejected_and_leaves_no_envelope()
    {
        var (a, b) = await SeedTwoAccountsAsync();
        var transactionId = Guid.NewGuid();

        var act = () => PostAsync(transactionId, (a, EntryDirection.Debit, 10m), (b, EntryDirection.Credit, 9.9999m));

        (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(NetZeroError);
        (await CountTransactionsAsync(transactionId)).Should().Be(0);
    }

    [Fact]
    public async Task Single_entry_transaction_is_rejected()
    {
        var (a, _) = await SeedTwoAccountsAsync();

        var act = () => PostAsync(Guid.NewGuid(), (a, EntryDirection.Debit, 10m));

        (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(NetZeroError);
    }

    [Fact]
    public async Task Updating_an_entry_is_rejected()
    {
        var transactionId = await PostBalancedTransactionAsync();

        var act = () => ExecuteAsync("UPDATE dbo.LedgerEntries SET Amount = 1 WHERE TransactionId = @id", transactionId);

        (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(AppendOnlyError);
        (await SumAmountsAsync(transactionId)).Should().Be(20m);
    }

    [Fact]
    public async Task Deleting_an_entry_is_rejected()
    {
        var transactionId = await PostBalancedTransactionAsync();

        var act = () => ExecuteAsync("DELETE FROM dbo.LedgerEntries WHERE TransactionId = @id", transactionId);

        (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(AppendOnlyError);
        (await CountEntriesAsync(transactionId)).Should().Be(2);
    }

    private async Task<Guid> PostBalancedTransactionAsync()
    {
        var (a, b) = await SeedTwoAccountsAsync();
        var transactionId = Guid.NewGuid();
        await PostAsync(transactionId, (a, EntryDirection.Debit, 10m), (b, EntryDirection.Credit, 10m));
        return transactionId;
    }

    private async Task<(Guid A, Guid B)> SeedTwoAccountsAsync()
    {
        await using var db = fixture.CreateDbContext();

        var user = User.Register($"{Guid.NewGuid():N}@test.local", "hash", UserRole.User, Now);
        var a = Account.Open(user.Id, Currency.From("EUR"), Now);
        var b = Account.Open(user.Id, Currency.From("EUR"), Now);

        db.Users.Add(user);
        db.Accounts.AddRange(a, b);
        await db.SaveChangesAsync();

        return (a.Id, b.Id);
    }

    [Fact]
    public async Task The_outbox_event_commits_and_rolls_back_together_with_the_entries()
    {
        var (a, b) = await SeedTwoAccountsAsync();
        var transactionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var outer = connection.BeginTransaction();

            await PostAsync(connection, outer, transactionId, eventId, (a, EntryDirection.Debit, 10m), (b, EntryDirection.Credit, 10m));

            // Inside the caller's transaction the procedure only nests, so rolling the caller back
            // must take the entries and the event with it.
            outer.Rollback();
        }

        (await CountEntriesAsync(transactionId)).Should().Be(0);
        (await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.OutboxMessages WHERE Id = @id", eventId)).Should().Be(0);

        await PostAsync(transactionId, (a, EntryDirection.Debit, 10m), (b, EntryDirection.Credit, 10m));
        (await CountEntriesAsync(transactionId)).Should().Be(2);
    }

    // Calls the procedure directly; the repository that wraps it comes with the write path.
    private async Task PostAsync(Guid transactionId, params (Guid AccountId, EntryDirection Direction, decimal Amount)[] entries)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await PostAsync(connection, transaction: null, transactionId, Guid.NewGuid(), entries);
    }

    private static async Task PostAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        Guid transactionId,
        Guid eventId,
        params (Guid AccountId, EntryDirection Direction, decimal Amount)[] entries)
    {
        var table = new DataTable();
        table.Columns.Add("AccountId", typeof(Guid));
        table.Columns.Add("Direction", typeof(byte));
        table.Columns.Add("Amount", typeof(decimal));
        foreach (var entry in entries)
        {
            table.Rows.Add(entry.AccountId, (byte)entry.Direction, entry.Amount);
        }

        await using var command = new SqlCommand("dbo.usp_PostLedgerTransaction", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure,
        };

        command.Parameters.AddWithValue("@TransactionId", transactionId);
        command.Parameters.AddWithValue("@Type", (byte)TransactionType.Transfer);
        command.Parameters.AddWithValue("@ReferenceId", DBNull.Value);
        command.Parameters.AddWithValue("@Description", DBNull.Value);
        command.Parameters.AddWithValue("@CreatedAtUtc", Now);

        var entriesParameter = command.Parameters.AddWithValue("@Entries", table);
        entriesParameter.SqlDbType = SqlDbType.Structured;
        entriesParameter.TypeName = "dbo.LedgerEntryList";

        command.Parameters.AddWithValue("@EventId", eventId);
        command.Parameters.AddWithValue("@EventType", "LedgerTransactionPosted");
        command.Parameters.AddWithValue("@EventPayload", "{}");

        await command.ExecuteNonQueryAsync();
    }

    private Task<int> CountEntriesAsync(Guid transactionId) =>
        ScalarAsync<int>("SELECT COUNT(*) FROM dbo.LedgerEntries WHERE TransactionId = @id", transactionId);

    private Task<int> CountTransactionsAsync(Guid transactionId) =>
        ScalarAsync<int>("SELECT COUNT(*) FROM dbo.LedgerTransactions WHERE Id = @id", transactionId);

    private Task<decimal> SumAmountsAsync(Guid transactionId) =>
        ScalarAsync<decimal>("SELECT SUM(Amount) FROM dbo.LedgerEntries WHERE TransactionId = @id", transactionId);

    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteAsync(string sql, Guid id)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }
}
