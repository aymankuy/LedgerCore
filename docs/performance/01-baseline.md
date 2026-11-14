# Benchmark: 01-baseline

Measured 2026-11-14 18:42 UTC. 10,000,006 ledger entries, 50,008 accounts, Microsoft SQL Server 2022 (RTM-CU27-GDR) (KB5126934) - 16.0.4305.5 (X64).
Hot account: 94,561 entries, 7,667 of them in the 30-day window.
Warm figures are over 20 runs after 3 warm-up runs; cold is one run after `DBCC DROPCLEANBUFFERS`.

| Query | Rows | Cold ms | Warm median ms | Warm p95 ms | Logical reads |
|---|---:|---:|---:|---:|---:|
| 01-statement-hot-30d | 7,668 | 981.9 | 508.7 | 535.8 | 233,947 |
| 02-balance-hot | 1 | 359.8 | 226.2 | 260.3 | 101,333 |
| 03-balance-settlement-eur | 1 | 1211.5 | 1187.4 | 1262.5 | 100,885 |
| 04-list-accounts-hot-owner | 1 | 6235.7 | 126.6 | 133.5 | 284,214 |
