using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;

namespace LedgerCore.Application.Abstractions.Persistence;

public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The platform's counterpart account for deposits and withdrawals in this currency.</summary>
    Task<Account?> GetSettlementAsync(Currency currency, CancellationToken cancellationToken);

    void Add(Account account);
}
