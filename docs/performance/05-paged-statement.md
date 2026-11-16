# Benchmark: 05-paged-statement

Measured 2026-11-16 13:02 UTC. 10,000,006 ledger entries, 50,008 accounts, Microsoft SQL Server 2022 (RTM-CU27-GDR) (KB5126934) - 16.0.4305.5 (X64).
Hot account: 94,561 entries, 7,667 of them in the 30-day window.
Warm figures are over 20 runs after 3 warm-up runs; cold is one run after `DBCC DROPCLEANBUFFERS`.

| Query | Rows | Cold ms | Warm median ms | Warm p95 ms | Logical reads |
|---|---:|---:|---:|---:|---:|
| 01-statement-hot-30d | 1,002 | 224.4 | 18.0 | 24.7 | 4,241 |
| 02-balance-hot | 1 | 8.7 | 1.1 | 1.2 | 17 |
| 03-balance-settlement-eur | 1 | 8.4 | 1.0 | 1.2 | 17 |
| 04-list-accounts-hot-owner | 1 | 9.2 | 1.2 | 1.5 | 20 |
| 05-report-daily-volume-30d | 372 | 2058.6 | 501.6 | 559.2 | 176,280 |
| 06-report-top-accounts-30d | 10 | 389.6 | 229.3 | 249.0 | 81,377 |
