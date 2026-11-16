# 0008. Ledger entry indexes: Id stays clustered, one covering index per account

- **Status:** Accepted
- **Date:** 2026-11-16
- **Phase:** 4

## Context

After Phase 4 step 2, every per-account read scanned the whole of `LedgerEntries`. The only
index on `AccountId` was the single-column one EF creates for the foreign key, and it did not
carry `Direction`, `Amount` or `CreatedAtUtc`. Finding a hot account's 94,561 rows through it
would have cost one key lookup per row, so the optimizer read all 101,333 pages of the
clustered index instead.

The reads that matter are:

- **Balance:** sum of `Direction`/`Amount` for one account (`fn_AccountBalance`, used by the
  balance endpoint, the account list and the overdraft check).
- **Statement:** one account's entries in `[From, To)`, ordered by `(CreatedAtUtc, Id)`, joined
  to `LedgerTransactions` for type and description.

Two questions had to be answered by measurement on the 10M-entry data set. The measurements
ran on SQL Server 2022 in Docker Desktop (WSL 2) on an Intel Core i7-12650H with 32 GB RAM,
and the container saw 16 cores and 16 GB. The adopted layout's numbers are in
[docs/performance/03-indexes.md](../performance/03-indexes.md). The rejected layouts were
measured in the same session as the A/B runs quoted below.

## Decision

1. **Add `IX_LedgerEntries_AccountId_CreatedAtUtc`**: key `(AccountId, CreatedAtUtc, Id)`,
   including `Direction`, `Amount`, `TransactionId`. The key is the statement's filter and its
   `ORDER BY`, so the window function needs no sort. The includes are every column the
   per-account reads use, so they never touch the table. `Id` is in the key, rather than
   left implicit, to show that the order is total and stable.
2. **Drop `IX_LedgerEntries_AccountId`**. The new index leads with `AccountId`, so it also
   serves the foreign key.
3. **Keep the clustered index on `Id`** (the identity).
4. **Add no index for the admin reports.**

## Alternatives measured and rejected

**Cluster on `(AccountId, CreatedAtUtc, Id)` and make the primary key nonclustered.** For reads,
the clustered layout was no better than the covering index. The statement took 81 ms against
78 ms, and the hot balance took 20 ms against 20 ms, because a covering index already is a
copy of the columns sorted by account. Writes were worse. We inserted 116,000 entries, one per
statement with the live account mix, into two 10M-row copies:

| Layout | Time | New leaf pages (table / TransactionId index) | Table fragmentation after |
|---|---:|---:|---:|
| Clustered on `Id` + covering index | 16.3 s | 951 / 34,061 | 0.1% |
| Clustered on `(AccountId, CreatedAtUtc, Id)` | 19.8 s | 21,856 / 40,839 | 41.3% |

On an append-only table, an ascending clustered key adds rows at the end of the table, and
page splits happen only in the narrow secondary index. With the account-first key, the
full-width table rows split all over the table. Every secondary index also carries the
32-byte clustering key instead of 8 bytes, which made the `TransactionId` index split more
as well. So inserts get about 22% slower, and reads gain nothing.

**A `CreatedAtUtc` index for the reports**, key `CreatedAtUtc`, including `AccountId`,
`Direction`, `Amount` and `TransactionId`. Daily volume went from 445 to 377 ms and top
accounts from 242 to 149 ms. The cost was another 613 MB, a full second copy of the hot
columns. These are admin-only reports that run rarely. Their proper home is the Phase 6
warehouse, which is built for exactly this kind of scan. If reporting moves off the OLTP
database, the index would be dead weight.

## Consequences

- Balance reads went from about 223 ms to 22 ms (750 logical reads instead of 101,333). The
  account list went from 121 ms to 20 ms, and the hot statement from 478 ms to 86 ms.
- **The statement is still above the 50 ms target.** Most of the remaining time is spent in
  these places:
  - the opening balance, which sums about 87,000 earlier entries (~27 ms);
  - 7,667 key lookups into `LedgerTransactions` (~22 ms);
  - sending 7,667 rows to the client (~20 ms).

  Step 4, the balance snapshots, removes the first of these. The final number is reported
  after step 4 and is not assumed here.
- Every insert maintains one more wide index than before Phase 4. The old single-column
  `AccountId` index is gone, so the net is one index of 614 MB in place of a smaller one.
- `IX_LedgerEntries_TransactionId` measured 1,007 MB before a rebuild and 615 MB after it.
  `TransactionId` is a random GUID, so inserts land anywhere in the index, and its pages
  settle at about 60–70% full. This is expected, and it is the price of the seek-only
  net-zero trigger (ADR 0001). Reclaiming the space is routine index maintenance and is not
  part of the schema.
