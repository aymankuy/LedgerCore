# 0001. Enforce net-zero transactions with a stored procedure and a trigger

- **Status:** Accepted
- **Date:** 2026-10-17
- **Phase:** 1

## Context

Spec rule 3 requires that the entries of every ledger transaction net to zero, enforced
by the database and not only by application code. The domain model already guarantees
this (`LedgerTransaction` can only be built through balanced factory methods), but the
database must reject an unbalanced transaction even if a bug or a manual script bypasses
the domain.

A `CHECK` constraint cannot do this, because it only sees one row at a time. The rule spans
several rows, so it needs a trigger. SQL Server has no deferred constraints: an
`AFTER INSERT` trigger runs at the end of each statement, not at commit.

We tested an `AFTER INSERT` trigger with EF Core `SaveChanges`. A two-entry transfer was
rejected, because EF sent the two entries as two separate `INSERT` statements and the
trigger saw a one-legged transaction after the first. A four-entry transaction passed only
because EF happened to batch those rows into one statement. Whether it passes therefore
depends on EF's internal batching, which is not a guarantee. `MinBatchSize(1)` did not
change the outcome.

## Decision

- Ledger transactions are written through `dbo.usp_PostLedgerTransaction`. It receives the
  entries as a table-valued parameter (`dbo.LedgerEntryList`) and inserts all of them in a
  single `INSERT ... SELECT`.
- `TR_LedgerEntries_NetZero` (`AFTER INSERT`) rejects any affected transaction that has
  fewer than two entries or whose signed amounts do not sum to zero (error 50002).
- The procedure runs with `SET XACT_ABORT ON` inside its own transaction, so a rejected
  call never leaves a `LedgerTransactions` row without entries. When it is called inside
  an outer transaction (EF Core's, in the write path), the inner `BEGIN TRANSACTION` only
  nests, so the outer transaction still owns the commit.
- `TR_LedgerEntries_AppendOnly` (`INSTEAD OF UPDATE, DELETE`) rejects any change to
  existing entries (error 50001), which enforces spec rule 2.

Accounts, users, idempotency keys and outbox messages are still written through EF Core.

## Consequences

- The rule is enforced by the database regardless of the caller, which is what the spec
  asks for.
- Ledger transactions are the one write that does not go through `SaveChanges`. The
  repository calls the procedure from within the EF Core transaction. This is a deliberate
  exception to "commands go through EF Core" in Phase 3.
- Writing all entries in one statement means one round trip per transaction instead of
  one per entry.
- `TRUNCATE TABLE` does not fire triggers, so the append-only trigger alone cannot stop it.
  It requires `ALTER` permission on the table, which the application login will not have.

- Integration tests cannot clean up with `DELETE FROM LedgerEntries`. They must use a fresh
  database per test run.

## Alternatives considered

- **Rely on EF Core batching.** Rejected: whether it passes depends on EF internals and on
  the number of entries, as the two-entry test showed.
- **No database check; rely on the domain plus the daily reconciliation report.**
  Rejected: violates spec rule 3. Reconciliation detects a problem after the fact; it does
  not prevent it.
- **A deferred check (validate at commit).** Not available in SQL Server.
