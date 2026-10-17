using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Domain.Accounts;

namespace LedgerCore.Infrastructure.Persistence.Repositories;

internal sealed class AccountRepository(LedgerDbContext db) : IAccountRepository
{
    public async Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Accounts.FindAsync([id], cancellationToken);

    public void Add(Account account) => db.Accounts.Add(account);
}
