namespace LedgerCore.Application.Abstractions.Messaging;

/// <summary>
/// A request that changes state. Every command runs in exactly one database transaction, opened
/// by <c>TransactionBehavior</c>; queries never do and go to Dapper instead.
/// </summary>
public interface ICommand;
