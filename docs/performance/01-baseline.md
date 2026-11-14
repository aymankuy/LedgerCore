# Benchmark: 01-baseline

Measured 2026-11-14 18:18 UTC. 10,000,006 ledger entries, 50,008 accounts, Microsoft SQL Server 2022 (RTM-CU27-GDR) (KB5126934) - 16.0.4305.5 (X64).
Hot account: 94,561 entries, 7,667 of them in the 30-day window.
Warm figures are over 20 runs after 3 warm-up runs; cold is one run after `DBCC DROPCLEANBUFFERS`.

| Query | Rows | Cold ms | Warm median ms | Warm p95 ms | Logical reads |
|---|---:|---:|---:|---:|---:|
| 01-statement-hot-30d | 7,668 | 1382.9 | 607.1 | 639.6 | 233,948 |
| 02-balance-hot | 1 | 446.7 | 289.7 | 310.0 | 101,333 |
| 03-balance-settlement-eur | 1 | 1724.8 | 1581.9 | 2199.8 | 100,477 |
| 04-list-accounts-hot-owner | 1 | 7412.6 | 206.4 | 266.7 | 284,214 |
