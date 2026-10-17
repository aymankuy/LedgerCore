using System.Data;
using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Domain.Ledger;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LedgerCore.Infrastructure.Persistence.Repositories;

internal sealed class LedgerRepository(LedgerDbContext db) : ILedgerRepository
{
    public Task<decimal> GetBalanceAsync(Guid accountId, CancellationToken cancellationToken) =>
        db.LedgerEntries
            .Where(e => e.AccountId == accountId)
            .SumAsync(e => e.Direction == EntryDirection.Credit ? e.Amount : -e.Amount, cancellationToken);

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
        ];

        // ExecuteSql joins the DbContext's current transaction, if one is open.
        await db.Database.ExecuteSqlRawAsync(
            "EXEC dbo.usp_PostLedgerTransaction @TransactionId, @Type, @ReferenceId, @Description, @CreatedAtUtc, @Entries",
            parameters,
            cancellationToken);
    }
}
