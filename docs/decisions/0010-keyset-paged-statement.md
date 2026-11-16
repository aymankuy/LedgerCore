# 0010. The statement is paged by keyset

- **Status:** Accepted
- **Date:** 2026-11-16
- **Phase:** 4

## Context

The spec's target is a 30-day statement for a hot account in under 50 ms at 10M entries.
After the covering index (ADR 0008) and the balance snapshots (ADR 0009), the opening balance
cost almost nothing. The statement still took 58 ms (warm median), because the window holds
7,667 lines and the time went into returning them:

- about 16 ms to read and send the lines;
- about 29 ms to look up their 7,667 transactions;
- about 12 ms to compute the running balance.

No index removes that work. Only returning fewer lines per call does.

## Decision

`GET /api/accounts/{id}/statement` returns at most `pageSize` lines: 100 by default, at most
1,000. A response that has more lines after it carries `nextCursor`. To get the next page, the
client sends the same `from` and `to` together with `cursor`.

- **The cursor is the last line's position**: `(CreatedAtUtc, EntryId)`, the statement's own
  sort order. It is base64url-encoded and treated as opaque by clients. The next page starts
  strictly after it. The `EntryId` breaks ties between lines posted in the same millisecond.
- **Each page carries its own opening and closing balance.** The opening balance is the
  current balance minus every entry from the page's start onwards, the same formula as
  ADR 0009 applied at the cursor's position. Pages chain exactly: each opens where the
  previous one closed, and a test checks this.
- **`usp_GetAccountStatement` applies `TOP (@PageSize + 1)` inside a derived table**, before
  the join to `LedgerTransactions` and before the running balance. Only the page's lines are
  joined. The extra line tells the handler whether to issue a cursor.

## Why keyset and not OFFSET

- **OFFSET reads and discards every skipped row.** Page 8 of the hot account would walk 7,000
  index rows to throw them away. A keyset page seeks straight to its start, so the last page
  costs about the same as the first.
- **OFFSET pages shift when lines are added.** An entry posted between two requests moves
  every later line by one, so a client would see a line twice or miss one. A keyset position
  does not move.

## Measured (10M entries, hot account, 30-day window, warm median)

| Request | Time | Logical reads |
|---|---:|---:|
| Whole window, unpaged (before) | 58 ms | 31,359 |
| First page, 1,000 lines | 18 ms | 4,241 |
| First page, 100 lines (the default) | 12 ms | 577 |
| Last page, 1,000-line pages | 14 ms | 2,823 |
| First page of a window a year back | 28 ms | 5,668 |

The last row is the slowest case. There, the opening balance subtracts nearly the account's
whole history from the current balance. It is still well under the target.

## Consequences

- The 50 ms target is met for every page, at the largest allowed page size.
- **This is an API change.** Clients that want the whole window must follow `nextCursor`. The
  response gains `nextCursor`, and `openingBalance` and `closingBalance` now describe the page.
  For a window that fits on one page, the response is the same as before.
- A cursor that does not decode is rejected with 400 by the validator. A cursor taken from
  another window or account is not an error. It only sets where the page starts.
