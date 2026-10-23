# 0003. Mark settlement accounts with an explicit account kind

- **Status:** Accepted
- **Date:** 2026-10-23
- **Phase:** 1

## Context

Deposits and withdrawals need a counterpart so that every transaction still nets to zero
(spec rule 3). A deposit debits a platform-owned settlement account and credits the
customer; a withdrawal does the reverse. The domain already models this, but the schema in
spec §5 has no way to tell a settlement account apart from a customer wallet, so the write
path cannot look one up.

## Decision

Add `Accounts.Kind tinyint NOT NULL DEFAULT 1` (`1 = Customer`, `2 = Settlement`), with:

- `CK_Accounts_Kind` restricting the column to known values;
- `UX_Accounts_SettlementPerCurrency`, a unique index on `Currency` filtered to
  `Kind = 2`, so there is at most one settlement account per currency and a lookup by
  currency is unambiguous.

Settlement accounts are created only through `Account.OpenSettlement`, which always allows
overdraft: the settlement balance is the negative mirror of all customer money. The domain
rejects a deposit or withdrawal whose counterpart is not a settlement account.

This deviates from the spec §5 schema by one column.

## Alternatives rejected

- **Well-known account ids in configuration or code.** No schema change, but the meaning of
  an account lives outside the data: a query over `Accounts` cannot tell system accounts
  from wallets, and nothing in the database stops a second "settlement" account from
  being used by mistake.
- **A separate `SettlementAccounts` table.** Ledger entries reference `Accounts`, so the
  settlement side would need its own foreign key path or a polymorphic reference. One table
  with a type column is how a chart of accounts is normally modelled.

## Consequences

- Every query that reports on customer money (statements, balance distribution, the
  warehouse in Phase 6) must filter on `Kind = 1`.
- Settlement accounts need an owning system user. It will be seeded at startup together with one settlement
  account per supported currency.
