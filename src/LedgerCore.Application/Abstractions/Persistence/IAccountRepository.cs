using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;

namespace LedgerCore.Application.Abstractions.Persistence;

public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Loads the account and locks its row until the current transaction ends, so no other
    /// money movement on it can read its balance in the meantime. Must run inside
    /// <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/>.
    /// </summary>
    Task<Account?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The platform's counterpart account for deposits and withdrawals in this currency.</summary>
    Task<Account?> GetSettlementAsync(Currency currency, CancellationToken cancellationToken);

    /// <summary>The platform's account that collects transfer fees in this currency.</summary>
    Task<Account?> GetFeeRevenueAsync(Currency currency, CancellationToken cancellationToken);

    void Add(Account account);
}
