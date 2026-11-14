namespace LedgerCore.Application.Abstractions.Persistence;

/// <summary>Commits the changes tracked during one command as a single unit.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="work"/> inside one database transaction and commits it only if the
    /// work completes. Row locks taken inside it are held until that commit (ADR 0002).
    /// If the database aborts the transaction as a deadlock victim, the whole transaction is
    /// retried, so <paramref name="work"/> may run more than once and must only touch the database.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken);
}
