# 0012: A star-schema warehouse, loaded by T-SQL that SSIS and Data Factory only trigger

Status: accepted (Phase 6)

## Context

Reports over the ledger (daily volume, balance distribution, top accounts, fee revenue) scan history and aggregate it. Running them against the OLTP database would compete with postings for the same pages and locks, and the OLTP schema is shaped for writing one transaction at a time, not for slicing by day, type and account status.

Phase 6 therefore adds a separate warehouse database, a nightly incremental load into it, and a Power BI report on top. The load has to be re-runnable (running it twice must not duplicate a row), and an account's history has to stay correct after the account is frozen or closed.

## Decisions

**A separate database with a star schema.** `FactLedgerEntry` (one row per ledger entry) and `DimAccount`, `DimDate`, `DimTransactionType`, in `data/warehouse/`. The report reads four views over it (`004_reporting_views.sql`), so it holds no business logic: any number on a page can be checked with a `SELECT`.

**`DimAccount` is slowly changing type 2 on status.** Every status an account has had is a row with the interval it was valid. A fact row points at the version that was valid when the entry was posted (a range join on `CreatedAtUtc`), so a report about March still describes an account as it was in March after it was closed in April.

**The source of status changes is a trigger-written history table, `AccountStatusHistory`.** `Accounts` only holds the current status, so the moment of a change was not recorded anywhere. Alternatives considered:

- Comparing the current status with the dimension at load time: needs no OLTP change, but the change date becomes "when the load noticed", and two changes between loads collapse into one.
- System-versioned temporal tables on `Accounts`: capture every change with the database clock too, but record every column, and a version is only visible through `FOR SYSTEM_TIME` queries; a narrow table with exactly what the warehouse needs is easier to extract and to reason about.
- Writing the row from the domain method: a code path that forgets it, or a manual `UPDATE`, would leave a hole. A trigger writes it in the same transaction as the change, so it cannot be skipped. The table is append-only, like the ledger.

Accounts that existed before the migration got one row with their current status at creation time; an earlier change of those accounts was never recorded.

**All load logic is T-SQL stored procedures in the warehouse (`etl.usp_LoadDimAccount`, `etl.usp_LoadFactLedgerEntry`). SSIS and Azure Data Factory only copy rows into staging and call them.** Logic inside SSIS data flows or Data Factory expressions cannot be run by an automated test without those runtimes. In the procedures it runs against a real SQL Server in the same Testcontainers suite as everything else (`WarehouseLoadTests`), and the two orchestrators cannot drift apart because there is only one implementation to drift from.

**Two different watermarks, each for a reason.**

- Ledger entries: a high-water mark on `LedgerEntries.Id`, kept in `etl.Watermark` and advanced in the same transaction as the rows it covers. Ids are handed out at insert but transactions commit in any order, so `MAX(Id)` may include an id above one still uncommitted, and a load that stopped there would skip it forever. `dbo.usp_GetSafeEntryWatermark` takes the posting lock exclusively for the instant it reads `MAX(Id)`, the same cut the balance snapshot uses (ADR 0009).
- Status history: no watermark. The staging copy is the whole table, as small as the number of accounts, and `DimAccount.SourceHistoryId` (unique) says which rows are already loaded. Changes of one account are serialised by the row lock on `Accounts`, so the versions of an account always arrive in order.

**A row that cannot be loaded does not fail the load.** An entry whose timestamp no account version covers goes to `etl.RejectedRow` with the reason, and the watermark moves past it, so it is recorded once and not retried every night. `etl.LoadAudit` records rows read, loaded and rejected per table per run.

**Power BI: a PBIP project and the `.pbix`.** The project (`data/powerbi/LedgerCore.pbip`, text files) can be reviewed in a diff; the `.pbix` is what the spec asks for. Amounts are never added across currencies: volume and fee revenue are drawn as one line per currency, and the top-accounts page has a currency slicer.

**Azure Data Factory runs the same load in the cloud.** The pipeline JSON is in `data/adf/`. It is the cloud counterpart of the SSIS package: SSIS is the on-premises ecosystem (a package on a Windows server with SQL Server), Data Factory is Azure's managed one (no server, pay per run). Having both shows the same load in both.

The pipeline was run twice against the deployed ledger on Azure SQL, loading a second free-tier database, `ledgercoredw`, on the same server. Run one copied 10 entries and 8 status rows and the warehouse matched the ledger exactly (10 facts, 8 account versions, 0 rejected). Run two copied 0 entries and loaded nothing new, so the load is re-runnable in the cloud as well (`etl.LoadAudit` has both runs; screenshot in `docs/images/adf-pipeline-run.png`). The factory's managed identity has `EXECUTE` on three extract procedures in the ledger and only the `etl` and `stg` schemas in the warehouse.

The factory was deleted after the recorded run to keep the demo at zero cost; an idle factory costs nothing, but nothing here needs one running. The trigger is checked in as stopped for the same reason. The run was driven by hand, from the Azure CLI, rather than by the nightly trigger.

Running it on the first attempt failed: the datasets carried an empty `schema` array, which the runtime tried to read as the SQL schema name. They now point at a table, which the activities' queries and procedures override.

## Consequences

- The warehouse is as fresh as the last load (nightly), which is fine for reports and wrong for anything transactional.
- `usp_GetSafeEntryWatermark` briefly blocks new postings while it waits for in-flight ones; the SSIS package and the pipeline call it once per run.
- The load procedures are the only place that knows how the star is built. Changing the schema means changing them and the tests, not two pipelines.
- Rows that do not match any account version are rejected, not guessed. Synthetic data that predated its system accounts exposed this: the data generator now dates the system accounts to the start of its window.
- The SSIS package stores connection passwords encrypted with the Windows user key, so another developer re-enters them. The password is the local Docker default.
- Data Factory's own row-level error routing (a redirect to blob storage) is not used: it would need a storage account for no extra insight, and rejected rows already land in `etl.RejectedRow`.
