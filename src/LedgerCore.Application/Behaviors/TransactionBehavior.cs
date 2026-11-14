using LedgerCore.Application.Abstractions.Messaging;
using LedgerCore.Application.Abstractions.Persistence;
using MediatR;

namespace LedgerCore.Application.Behaviors;

/// <summary>
/// Runs every <see cref="ICommand"/> and everything after it in the pipeline (the idempotency
/// claim and the handler) in one database transaction, retried as a whole on a deadlock.
/// Handlers therefore never open transactions themselves, and none can forget to.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) =>
        request is ICommand
            ? unitOfWork.ExecuteInTransactionAsync(() => next(cancellationToken), cancellationToken)
            : next(cancellationToken);
}
