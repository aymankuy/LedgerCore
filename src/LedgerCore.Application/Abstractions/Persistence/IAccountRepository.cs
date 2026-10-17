using LedgerCore.Domain.Accounts;

namespace LedgerCore.Application.Abstractions.Persistence;

public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    void Add(Account account);
}