using System.Data;
using System.Text.Json;
using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Domain.Ledger;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LedgerCore.Infrastructure.Persistence.Repositories;

internal sealed class LedgerRepository(LedgerDbContext db) : ILedgerRepository
{
    // The same definition of a balance as every read (fn_AccountBalance). It runs inside the
    // command's transaction, after the account row is locked.
    public Task<decimal> GetBalanceAsync(Guid accountId, CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<decimal>($"SELECT Balance AS Value FROM dbo.fn_AccountBalance({accountId})")
            .SingleAsync(cancellationToken);

    public async Task PostAsync(LedgerTransaction transaction, CancellationToken cancellationToken)
    {
        var entries = new DataTable();
        entries.Columns.Add("AccountId", typeof(Guid));
        entries.Columns.Add("Direction", typeof(byte));
        entries.Columns.Add("Amount", typeof(decimal));
        foreach (var entry in transaction.Entries)
        {
            entries.Rows.Add(entry.AccountId, (byte)entry.Direction, entry.Amount);
        }

        SqlParameter[] parameters =
        [
            new("@TransactionId", transaction.Id),
            new("@Type", (byte)transaction.Type),
            new("@ReferenceId", (object?)transaction.ReferenceId ?? DBNull.Value),
            new("@Description", (object?)transaction.Description ?? DBNull.Value),
            new("@CreatedAtUtc", SqlDbType.DateTime2) { Scale = 3, Value = transaction.CreatedAtUtc },
            new("@Entries", SqlDbType.Structured) { TypeName = "dbo.LedgerEntryList", Value = entries },

            // The outbox row: the procedure writes it in the same transaction as the entries.
            new("@EventId", Guid.NewGuid()),
            new("@EventType", SqlDbType.NVarChar, 256) { Value = nameof(LedgerTransactionPosted) },
            new("@EventPayload", SqlDbType.NVarChar, -1) { Value = JsonSerializer.Serialize(transaction.PostedEvent) },
        ];

        // ExecuteSql joins the DbContext's current transaction, if one is open.
        await db.Database.ExecuteSqlRawAsync(
            "EXEC dbo.usp_PostLedgerTransaction @TransactionId, @Type, @ReferenceId, @Description, @CreatedAtUtc, @Entries, @EventId, @EventType, @EventPayload",
            parameters,
            cancellationToken);
    }
}
