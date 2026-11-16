# Benchmark: 02-procedures

Measured 2026-11-14 18:43 UTC. 10,000,006 ledger entries, 50,008 accounts, Microsoft SQL Server 2022 (RTM-CU27-GDR) (KB5126934) - 16.0.4305.5 (X64).
Hot account: 94,561 entries, 7,667 of them in the 30-day window.
Warm figures are over 20 runs after 3 warm-up runs; cold is one run after `DBCC DROPCLEANBUFFERS`.

| Query | Rows | Cold ms | Warm median ms | Warm p95 ms | Logical reads |
|---|---:|---:|---:|---:|---:|
| 01-statement-hot-30d | 7,668 | 950.2 | 478.0 | 535.3 | 233,948 |
| 02-balance-hot | 1 | 356.2 | 222.9 | 252.9 | 101,333 |
| 03-balance-settlement-eur | 1 | 415.0 | 263.0 | 280.0 | 101,333 |
| 04-list-accounts-hot-owner | 1 | 754.9 | 120.8 | 148.4 | 290,133 |
| 05-report-daily-volume-30d | 372 | 1358.6 | 612.6 | 666.6 | 197,097 |
| 06-report-top-accounts-30d | 10 | 480.6 | 351.4 | 366.9 | 103,244 |
