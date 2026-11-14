using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace LedgerCore.Infrastructure.Persistence.Repositories;

internal sealed class AccountRepository(LedgerDbContext db) : IAccountRepository
{
    public async Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Accounts.FindAsync([id], cancellationToken);

    public Task<Account?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        // Outside a transaction the lock would be released as soon as the SELECT finished.
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("An account can only be locked inside a transaction.");
        }

        // UPDLOCK: other lockers wait here until we commit, but plain reads are not blocked.
        // HOLDLOCK: the lock is kept to the end of the transaction, even if the row is missing.
        return db.Accounts
            .FromSql($"SELECT * FROM dbo.Accounts WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}")
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<Account?> GetSettlementAsync(Currency currency, CancellationToken cancellationToken) =>
        db.Accounts.SingleOrDefaultAsync(a => a.Kind == AccountKind.Settlement && a.Currency == currency, cancellationToken);

    public Task<Account?> GetFeeRevenueAsync(Currency currency, CancellationToken cancellationToken) =>
        db.Accounts.SingleOrDefaultAsync(a => a.Kind == AccountKind.FeeRevenue && a.Currency == currency, cancellationToken);

    public void Add(Account account) => db.Accounts.Add(account);
}
