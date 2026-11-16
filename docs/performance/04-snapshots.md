# Benchmark: 04-snapshots

Measured 2026-11-16 11:50 UTC. 10,000,006 ledger entries, 50,008 accounts, Microsoft SQL Server 2022 (RTM-CU27-GDR) (KB5126934) - 16.0.4305.5 (X64).
Hot account: 94,561 entries, 7,667 of them in the 30-day window.
Warm figures are over 20 runs after 3 warm-up runs; cold is one run after `DBCC DROPCLEANBUFFERS`.

| Query | Rows | Cold ms | Warm median ms | Warm p95 ms | Logical reads |
|---|---:|---:|---:|---:|---:|
| 01-statement-hot-30d | 7,668 | 609.2 | 57.7 | 70.9 | 31,359 |
| 02-balance-hot | 1 | 18.1 | 1.2 | 1.6 | 17 |
| 03-balance-settlement-eur | 1 | 17.2 | 1.1 | 1.2 | 17 |
| 04-list-accounts-hot-owner | 1 | 20.7 | 1.1 | 1.2 | 20 |
| 05-report-daily-volume-30d | 372 | 1674.5 | 504.8 | 549.6 | 176,280 |
| 06-report-top-accounts-30d | 10 | 382.8 | 237.8 | 249.6 | 81,377 |
