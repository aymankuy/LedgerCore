namespace LedgerCore.Infrastructure.Outbox;

/// <summary>
/// Reacts to one type of outbox message. Delivery is at least once: if the Worker stops after a
/// handler ran but before the message was marked processed, the handler runs again. Handlers
/// must therefore be safe to repeat.
/// </summary>
public interface IOutboxMessageHandler
{
    /// <summary>The <c>OutboxMessages.Type</c> this handler reacts to, e.g. "LedgerTransactionPosted".</summary>
    string MessageType { get; }

    Task HandleAsync(string payload, CancellationToken cancellationToken);
}
