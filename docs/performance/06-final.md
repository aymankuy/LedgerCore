# Benchmark: 06-final

Measured 2026-11-16 13:23 UTC. 10,000,006 ledger entries, 50,008 accounts, Microsoft SQL Server 2022 (RTM-CU27-GDR) (KB5126934) - 16.0.4305.5 (X64).
Hot account: 94,561 entries, 7,667 of them in the 30-day window.
Warm figures are over 20 runs after 3 warm-up runs; cold is one run after `DBCC DROPCLEANBUFFERS`.

| Query | Rows | Cold ms | Warm median ms | Warm p95 ms | Logical reads |
|---|---:|---:|---:|---:|---:|
| 01-statement-hot-30d | 1,002 | 193.6 | 17.5 | 20.4 | 4,164 |
| 02-balance-hot | 1 | 15.9 | 1.1 | 1.3 | 12 |
| 03-balance-settlement-eur | 1 | 16.2 | 1.1 | 1.3 | 12 |
| 04-list-accounts-hot-owner | 1 | 23.9 | 1.3 | 1.5 | 15 |
| 05-report-daily-volume-30d | 372 | 1611.3 | 467.0 | 539.1 | 176,280 |
| 06-report-top-accounts-30d | 10 | 386.8 | 230.4 | 249.7 | 81,377 |
