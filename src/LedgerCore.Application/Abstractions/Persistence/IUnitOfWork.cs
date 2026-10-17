namespace LedgerCore.Application.Abstractions.Persistence;

/// <summary>Commits the changes tracked during one command as a single unit.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
