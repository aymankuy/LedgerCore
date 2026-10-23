using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace LedgerCore.Infrastructure.Persistence.Repositories;

internal sealed class AccountRepository(LedgerDbContext db) : IAccountRepository
{
    public async Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Accounts.FindAsync([id], cancellationToken);

    public Task<Account?> GetSettlementAsync(Currency currency, CancellationToken cancellationToken) =>
        db.Accounts.SingleOrDefaultAsync(a => a.Kind == AccountKind.Settlement && a.Currency == currency, cancellationToken);

    public void Add(Account account) => db.Accounts.Add(account);
}
