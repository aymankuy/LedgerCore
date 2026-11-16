# 0009. Balance snapshots with a commit-safe watermark

- **Status:** Accepted
- **Date:** 2026-11-16
- **Phase:** 4

## Context

A balance is derived from the ledger and never stored on the account (spec rule 1). After the
index pass (ADR 0008), a balance read is a seek on the covering index, but it still sums every
entry the account has ever had. That took 22 ms for a hot customer account with 94,561
entries, and it keeps growing with the account's history. The spec asks for a snapshot table,
so that a read combines the latest snapshot with the entries after it.

The hard part is deciding which entries come "after" the snapshot.

## Decision

- **`AccountBalanceSnapshots`** holds one row per account: the balance over every entry up to
  a watermark.
- **`BalanceSnapshotRuns`** logs each refresh. The latest run's `ToEntryId` is that watermark,
  shared by all snapshot rows.
- **`usp_RefreshBalanceSnapshots`** sums the entries with `FromEntryId < Id <= ToEntryId` by
  account. It adds each sum to that account's snapshot and inserts the run row, all in one
  transaction. `BalanceSnapshotWorker` runs it every minute.
- **`fn_AccountBalance`**, still the only definition of a balance, adds three things in one
  statement: the snapshot, the account's entries with `Id > watermark`, and the watermark
  lookup that bounds them. All reads use it: the balance, the account list, the statement's
  opening balance, and the overdraft check inside the write transaction.
- **The statement's opening balance** is the current balance minus the entries since `From`.
  For a recent window that is a few thousand rows, not the whole history.

### Why the watermark is an entry Id, and why the Id needs a lock

`CreatedAtUtc` cannot be the watermark. It is stamped by the application, and entries may
carry past timestamps: the tests and the data generator post history. A time-based watermark
would silently miss them.

An identity value is a better watermark, but on its own it is not safe. **Identity values are
handed out at INSERT, and transactions commit in any order.** If posting A takes Id 100 and
posting B takes Id 101 and commits first, a refresh that reads `MAX(Id) = 101` while A is still
open sets its watermark past A's entries. Once A commits, the entries at Id 100 are below the
watermark and in no snapshot, and every later read skips them. Money would vanish from
balances with no error.

The fix is an application lock, `LedgerCore.LedgerPosting`:

- `usp_PostLedgerTransaction` takes it in **shared** mode, held until its transaction commits.
  Postings do not block each other.
- The refresh takes it in **exclusive** mode only for the instant it reads `MAX(Id)`. Once
  that lock is granted, no posting is in flight, so every Id up to the maximum is committed,
  and every later posting gets a higher Id. The refresh then releases the lock before doing
  any work. New postings queue only for the time that single read takes.

`A_refresh_never_sets_its_watermark_past_an_uncommitted_entry` holds a posting open, commits a
later one, then refreshes. With the lock it passes. With the posting procedure's lock
disabled, it fails: two accounts' snapshots are missing the open posting's entries. A soak
test of 200 parallel transfers against back-to-back refreshes did not catch the bug by
chance, because the window is too narrow. That is why the deterministic test exists.

The data generator's bulk load takes the same shared lock per batch, because it bypasses the
posting procedure.

### Why the delta uses the clustered index

The entries after the watermark are the newest rows, so they sit at the end of the clustered
index on `Id`. `WHERE Id > watermark AND AccountId = @a` reads only those pages. The
per-account index (ADR 0008) leads with `CreatedAtUtc` after `AccountId`, so it cannot seek on
`Id`. It would read the whole account history just to apply that filter. The optimizer does
not know the watermark at compile time and cannot tell the two plans apart, so
`fn_AccountBalance` pins the clustered index with `INDEX(1), FORCESEEK`.

This means the cost of a read grows with the number of entries posted since the last refresh,
across all accounts. It no longer grows with the account's history. Measured on the 10M data
set:

| Entries behind the watermark | Hot balance, warm median | Logical reads |
|---:|---:|---:|
| 0 | 1.2 ms | 17 |
| 1,000 | 1.6 ms | 37 |
| 10,000 | 4.0 ms | 185 |
| 100,000 | 16.4 ms | 1,665 |

This data set posts about 19 entries a minute, so a one-minute refresh keeps reads near the
first row. At 100,000 entries per minute (about 1,700 per second), the interval would be
shortened.

### Why READ_COMMITTED_SNAPSHOT is now switched on explicitly

The watermark, the snapshot row and the newer entries must come from the same point in time:

- **Too early:** a snapshot read from before a refresh, combined with a watermark read after
  it, misses the entries in between.
- **Too late:** a snapshot read from after a refresh, combined with the older watermark,
  counts them twice.

Under locking READ COMMITTED, one statement can see a refresh commit halfway through. Under
READ_COMMITTED_SNAPSHOT, every statement reads one committed version of the database.

Azure SQL has it on by default. The local and test databases had it off. The tests even ran
in `master`, because the Testcontainers connection string names no database. The
`AddBalanceSnapshots` migration now turns it on wherever it is off. The test fixtures use a
database of their own, and a test asserts that the setting is on. This also means local runs
and tests now behave like production.

ADR 0002's locking does not change. The overdraft check still runs after `UPDLOCK, HOLDLOCK`
on the account, so no other movement on that account can commit in between.

## Alternatives rejected

- **Reject backdated entries, then bound the delta by time.** If the posting procedure refused
  timestamps older than a few minutes, the delta could seek the per-account index by
  `CreatedAtUtc`. But tests and tooling legitimately post history. The rule would also make
  correctness depend on clocks, both the application server's and the database's.
- **A second index on `(AccountId, Id)`.** It would make the delta seek on any isolation
  level. But it is another index of about 450 MB that every insert must maintain, and
  ADR 0008 measured what that costs. The clustered index already orders entries by `Id`.
- **A watermark per snapshot row.** Each row would be self-consistent without row versioning.
  But accounts that saw no activity keep an old watermark, so their delta would be a long
  range scan of the clustered index.
- **Refreshing inside the posting transaction**, i.e. updating a stored balance. Every
  transfer would then update the same hot rows: the settlement account sits in half of all
  postings. That brings back the contention that deriving balances avoids. It is also,
  in effect, the stored balance column the spec forbids.

## Consequences

- Balance reads take about 1 ms instead of 22 ms (223 ms before ADR 0008), whatever the
  account's age. The EUR settlement account, with 1.36 million entries, reads in 1.1 ms.
- The first refresh on an existing ledger folds the whole history: 10M entries took 1.8 s.
  Later runs take milliseconds.
- If the Worker stops, balances stay correct and get slower, in proportion to the entries
  posted since the last run. `BalanceSnapshotRuns` shows when the last run happened.
- Entries must be written through `usp_PostLedgerTransaction`, or under the same shared
  lock. Any other writer could be skipped by a refresh.
- **The statement target is still not met.** The hot 30-day statement takes 58 ms (warm
  median). Its opening balance now costs almost nothing, so the remainder is returning
  7,667 lines:
  - about 16 ms to read and send them;
  - about 29 ms for their lookups into `LedgerTransactions`;
  - about 12 ms for the running balance.

  A 500-line page of the same query takes 4 ms. Whether to page the statement is an open
  decision. Until it is made, the miss is reported as measured.
