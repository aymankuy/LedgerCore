# 0002. READ COMMITTED with an explicit lock on the debited account

- **Status:** Accepted
- **Date:** 2026-11-12
- **Phase:** 2

## Context

A withdrawal or transfer reads the account's balance, checks that it covers the amount,
and then posts the ledger entries. In Phase 1 these steps ran with no transaction and no
lock. Two requests on the same account could both read the same balance and both pass the
check. The concurrency test showed it: 100 parallel withdrawals of 10 from a balance of 100
let 12 to 15 through and left the account at -20 to -50.

The balance is derived from `LedgerEntries` (spec rule 1), so there is no single balance
row whose update would make concurrent writers collide. The read and the write must be
protected together.

## Decision

- Every money movement that checks a balance runs in one explicit transaction at
  **`READ COMMITTED`**, through `IUnitOfWork.ExecuteInTransactionAsync`.
- Its first statement loads the account being debited with
  **`WITH (UPDLOCK, HOLDLOCK)`**. The lock is held until commit. A second request on the
  same account waits on that row, and when it continues, the first request's entries are
  already committed and included in the balance it reads.
- Only the account whose balance is checked is locked. The settlement account is not: it is
  never balance-checked, and locking it would serialize every withdrawal in its currency.
- Deposits do not check a balance and are not locked. A deposit that is still uncommitted
  while a withdrawal reads the balance is simply not counted, which errs on the safe side.

## Alternatives considered

- **`SERIALIZABLE` without hints.** It is correct, but the range locks it takes while
  summing `LedgerEntries` cover far more than one account's rows. Two readers can each
  hold a shared range lock and then both try to insert into it, which deadlocks by
  design. The protection it gives is broader than needed and it is implicit, so it is
  hard to reason about in a review.
- **`SNAPSHOT`.** Reads never block, but two withdrawals would each see the same
  pre-withdrawal snapshot and both pass the check (write skew). It is safe only if both
  also update one shared row, which brings back a lock in a less obvious form.
- **Optimistic concurrency on `Accounts.RowVersion`.** Each movement would bump the
  account's version and retry on conflict. Under contention on one hot account, most
  attempts fail and retry, so throughput collapses exactly where it matters.
  `RowVersion` is used instead for account state changes, which are rare.
- **`UPDLOCK` without `HOLDLOCK`.** For an existing row it behaves the same, because an
  update lock is kept to the end of the transaction. `HOLDLOCK` is kept so that the lookup
  of a missing id also holds its key range, and so that the intent is explicit.

## Consequences

- Movements on the same account are serialized; movements on different accounts run in
  parallel. The lock covers only the time from the balance read to the commit.
- Plain reads (balance, statement) are not blocked by the update lock.
- Correctness does not depend on the database default. Azure SQL enables
  `READ_COMMITTED_SNAPSHOT` by default; the lock still serializes writers, and the balance
  is read after the lock is granted, so it includes every movement committed before it.
- A transfer locks the source account only, so it cannot deadlock on account rows yet. When
  both accounts are locked, they must be locked in a fixed order (next decision in this
  phase).
