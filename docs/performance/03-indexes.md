# Benchmark: 03-indexes

Measured 2026-11-16 11:21 UTC. 10,000,006 ledger entries, 50,008 accounts, Microsoft SQL Server 2022 (RTM-CU27-GDR) (KB5126934) - 16.0.4305.5 (X64).
Hot account: 94,561 entries, 7,667 of them in the 30-day window.
Warm figures are over 20 runs after 3 warm-up runs; cold is one run after `DBCC DROPCLEANBUFFERS`.

| Query | Rows | Cold ms | Warm median ms | Warm p95 ms | Logical reads |
|---|---:|---:|---:|---:|---:|
| 01-statement-hot-30d | 7,668 | 586.2 | 85.6 | 101.4 | 31,902 |
| 02-balance-hot | 1 | 48.2 | 22.0 | 27.8 | 750 |
| 03-balance-settlement-eur | 1 | 62.2 | 33.2 | 37.6 | 10,995 |
| 04-list-accounts-hot-owner | 1 | 41.8 | 20.3 | 27.0 | 753 |
| 05-report-daily-volume-30d | 372 | 1526.0 | 444.7 | 517.4 | 176,280 |
| 06-report-top-accounts-30d | 10 | 397.3 | 241.8 | 669.1 | 81,377 |
