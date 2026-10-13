using LedgerCore.Domain.Common;

namespace LedgerCore.Domain.Accounts;

public sealed class InsufficientFundsException(Guid accountId, decimal balance, decimal requested)
    : DomainException(
        "insufficient_funds",
        $"Account {accountId} has a balance of {balance} and cannot be debited {requested}.");

public sealed class AccountNotActiveException(Guid accountId, AccountStatus status)
    : DomainException("account_not_active", $"Account {accountId} is {status} and cannot be used for transactions.");

public sealed class CurrencyMismatchException(Currency expected, Currency actual)
    : DomainException("currency_mismatch", $"Expected currency {expected} but got {actual}.");

public sealed class InvalidAccountStateTransitionException(Guid accountId, AccountStatus from, AccountStatus to)
    : DomainException("invalid_status_transition", $"Account {accountId} cannot move from {from} to {to}.");
