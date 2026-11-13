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

## Transfers: both accounts, in ascending id order

A transfer locks both accounts: the source because its balance is checked, and the
destination so that its status cannot change while money moves into it. Two transfers in
opposite directions that each lock their source first wait for each other forever. The
reciprocal transfer test showed exactly that: with source-first locking, three runs out of
three ended in a deadlock.

So every transfer locks the account with the lower id first, whatever the direction. Two
transfers between the same accounts then queue on the same first row and can never hold
one lock each. The order only has to be the same everywhere; it is the .NET `Guid`
comparison, which differs from SQL Server's `uniqueidentifier` sort order, and that does not
matter.

The cost is that incoming transfers to one hot account, such as a merchant receiving many
payments, are serialized as well. If that becomes a bottleneck, the destination could take a
shared lock held to commit (`HOLDLOCK` without `UPDLOCK`). Shared locks do not block each
other, but they still block a concurrent status change.

## Deadlock retry

Lock ordering removes the deadlocks we know about, but not every possible one, for example
a future query that takes locks in another order. So `ExecuteInTransactionAsync` runs the
whole transaction through a Polly retry: on SQL Server error 1205 (deadlock victim), up to
three more attempts, about 50, 100 and 200 ms apart, with jitter so two victims do not
collide again. The server has already rolled the victim back, so a retry starts clean; the
EF change tracker is cleared so no entity from the failed attempt is reused.

Only error 1205 is retried. Business errors such as insufficient funds are not, because
running them again gives the same answer. A test forces a real deadlock between two
transactions and shows that both commit, with exactly one retry; without the retry, the
deadlock reaches the caller.

## Account status changes: optimistic, on `RowVersion`

Freezing and unfreezing an account (admin only) is rare and never hot, so it takes no lock.
The handler reads the account, changes its status and saves. EF Core adds
`WHERE RowVersion = @read` to the `UPDATE`, so if another request changed the account in
between, no row matches and the API returns 409 instead of overwriting that change.

A test fires 20 parallel freezes at one account: exactly one succeeds, and the others get a
409 or, if they read after the winner committed, a 422 for an invalid transition. With the
`RowVersion` check disabled, two of the twenty succeeded in each of three runs.

A status change and a money movement on the same account do not race either: the
`UPDATE` needs an exclusive lock on the row, so it waits for any movement holding the
update lock, and a movement that starts later sees the new status.
