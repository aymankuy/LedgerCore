# LedgerCore — Project Specification

This file is the source of truth for this project. Read it before making changes.

---

## 1. What we are building

**LedgerCore** is a double-entry wallet and payment ledger API: user accounts hold balances, money is deposited, withdrawn and transferred between accounts, and every movement is recorded as an immutable ledger entry.

It is **not** a bank. Think of it as the wallet service behind a marketplace or gaming platform.

### Why it exists

This is a portfolio project for backend / database developer roles in Türkiye and the EU. It must demonstrate, with working code, the following competencies:

- ASP.NET Core Web API, EF Core, Clean Architecture, JWT auth
- SQL Server and advanced T-SQL (stored procedures, views, index design, execution plans)
- Correctness under concurrency (transactions, isolation levels, idempotency)
- CQRS and classic design patterns, applied where they are genuinely needed
- Docker, CI/CD (GitHub Actions **and** Azure DevOps), Azure deployment
- ETL and BI: star schema, SSIS, Azure Data Factory, Power BI

**Design implication:** every architectural choice must be *justifiable in an interview*. Do not add a technology because it is on a checklist. If a pattern or service is introduced, the README must state in one sentence what problem it solves here. Prefer a simpler design that is well-reasoned over a complex one that is decorative.

---

## 2. Non-negotiable domain rules

These are the core of the project. Violating them defeats its purpose.

1. **Balance is never a stored column.** There is no `Accounts.Balance`. Balance is derived from ledger entries (with a snapshot table for performance, see Phase 4).
2. **Ledger entries are append-only.** No `UPDATE`, no `DELETE` on `LedgerEntries`, ever. Corrections are new reversing entries.
3. **Every transaction nets to zero.** A transfer writes a debit and a credit whose amounts sum to exactly 0. This is enforced by a database constraint, not only by application code.
4. **Money is `DECIMAL(19,4)`.** Never `float`, never `double`, never `decimal` without explicit precision in EF Core configuration.
5. **An account balance must never go negative** unless the account is explicitly flagged `AllowsOverdraft`.
6. **Every write endpoint is idempotent** via an `Idempotency-Key` header.
7. **All timestamps are UTC** and stored as `datetime2(3)`.

---

## 3. Tech stack (pinned — do not substitute)

| Concern | Choice |
|---|---|
| Runtime | .NET 9 |
| API | ASP.NET Core Web API (controllers, not minimal APIs) |
| Write-side ORM | EF Core 9 |
| Read-side | Dapper + raw T-SQL |
| Database | SQL Server 2022 |
| Mediator | MediatR |
| Validation | FluentValidation |
| Mapping | Mapster |
| Cache | Redis (StackExchange.Redis) |
| Background jobs | Hangfire (local), Azure Functions (cloud) |
| Unit tests | xUnit + FluentAssertions + NSubstitute |
| Integration tests | Testcontainers for .NET (real SQL Server) |
| Test data | Bogus |
| API docs | Swashbuckle / Swagger UI |
| Logging | Serilog, structured, JSON sink |
| IaC | Bicep |

If a package is genuinely unsuitable, say so and ask before swapping it.

---

## 4. Solution layout

```
src/
  LedgerCore.Domain/          Entities, value objects, domain rules. No external dependencies.
  LedgerCore.Application/     Commands, queries, handlers, validators, interfaces. Depends only on Domain.
  LedgerCore.Infrastructure/  EF Core, Dapper, Redis, repositories, Outbox. Implements Application interfaces.
  LedgerCore.Api/             Controllers, middleware, DI wiring, auth.
  LedgerCore.Worker/          Separately deployable: Outbox dispatcher + reporting pipeline.
tests/
  LedgerCore.UnitTests/
  LedgerCore.IntegrationTests/
  LedgerCore.ConcurrencyTests/
data/
  warehouse/                  Star schema DDL
  ssis/                       SSIS package
  adf/                        Azure Data Factory pipeline JSON
  powerbi/                    .pbix file
infra/
  bicep/
docs/
  architecture.md             Mermaid diagrams
  performance.md              Index benchmark results
  decisions/                  ADRs, one file per significant decision
```

**Dependency rule:** Domain ← Application ← Infrastructure ← Api. Dependencies point inward only. Never reference Infrastructure from Application.

---

## 5. Data model (Phase 1 target)

```
Accounts
  Id              uniqueidentifier  PK
  OwnerUserId     uniqueidentifier  FK -> Users
  Currency        char(3)           NOT NULL
  Status          tinyint           NOT NULL   -- Active / Frozen / Closed
  AllowsOverdraft bit               NOT NULL DEFAULT 0
  CreatedAtUtc    datetime2(3)      NOT NULL
  RowVersion      rowversion

LedgerTransactions                  -- the "envelope": one business event
  Id              uniqueidentifier  PK
  Type            tinyint           NOT NULL   -- Deposit / Withdrawal / Transfer / Fee / Reversal
  ReferenceId     nvarchar(64)      NULL       -- external correlation
  Description     nvarchar(256)     NULL
  CreatedAtUtc    datetime2(3)      NOT NULL

LedgerEntries                       -- append-only; two or more rows per transaction
  Id              bigint IDENTITY   PK
  TransactionId   uniqueidentifier  FK -> LedgerTransactions
  AccountId       uniqueidentifier  FK -> Accounts
  Direction       tinyint           NOT NULL   -- 1 = Debit, 2 = Credit
  Amount          decimal(19,4)     NOT NULL CHECK (Amount > 0)
  CreatedAtUtc    datetime2(3)      NOT NULL

IdempotencyKeys
  Key             nvarchar(128)     PK
  RequestHash     char(64)          NOT NULL
  ResponseBody    nvarchar(max)     NULL
  StatusCode      int               NULL
  CreatedAtUtc    datetime2(3)      NOT NULL

OutboxMessages
  Id              uniqueidentifier  PK
  Type            nvarchar(256)     NOT NULL
  Payload         nvarchar(max)     NOT NULL
  OccurredAtUtc   datetime2(3)      NOT NULL
  ProcessedAtUtc  datetime2(3)      NULL
  Attempts        int               NOT NULL DEFAULT 0
```

**Database-level guarantees required:**
- A `CHECK` constraint or trigger enforcing that entries of one transaction net to zero.
- A unique index on `IdempotencyKeys.Key`.
- A filtered index on `OutboxMessages WHERE ProcessedAtUtc IS NULL`.
- Deny `UPDATE`/`DELETE` on `LedgerEntries` at the database level (trigger or permissions), with a test proving it.

---

## 6. Build order

Work phase by phase. **Do not start a phase before the previous one's acceptance criteria pass.** At the end of each phase: all tests green, README updated, merged to `main`.

---

### Phase 1 — Core (target: 2 weeks)

Scope: schema, EF Core, accounts and transfers, JWT auth, Docker Compose, CI.

Deliverables:
- Solution and project structure as in §4.
- EF Core migrations producing the schema in §5, including all constraints.
- Endpoints: register/login, create account, deposit, withdraw, transfer, get balance, get statement.
- JWT with role-based authorization (`User`, `Admin`). A user may only access their own accounts; `Admin` may access all.
- Global exception middleware returning RFC 7807 ProblemDetails. No stack traces in responses.
- FluentValidation on every request DTO.
- Serilog structured logging with a correlation id per request.
- Multi-stage `Dockerfile` (build image separate from runtime image).
- `docker-compose.yml`: `api` + `mssql` + `redis`. Migrations and seed data must run automatically on startup.
- GitHub Actions workflow: restore → build → test → publish.

Acceptance criteria:
- `docker compose up` on a clean machine brings up a working Swagger UI with seeded data. **Verify this yourself before declaring the phase done.**
- Transferring more than the available balance returns 422, not 500.
- A user cannot read another user's account; test proves it.
- Unit test coverage on Domain and Application ≥ 70%.

---

### Phase 2 — Correctness under concurrency (target: 1 week)

This is the phase that distinguishes the project. Spend real effort here.

Deliverables:
- `Idempotency-Key` header handling: same key + same payload returns the stored response without re-executing; same key + different payload returns 409.
- Optimistic concurrency via `RowVersion` on `Accounts`.
- Explicit transaction boundaries around transfers with a deliberate isolation level. Document the choice in `docs/decisions/`.
- Deterministic lock ordering (e.g. always lock accounts in ascending `AccountId`) to prevent deadlocks on reciprocal transfers.
- Deadlock retry policy with exponential backoff (Polly).
- Concurrency test suite:
  - 100 parallel withdrawals against one account → balance never goes negative, exactly the expected number succeed.
  - Two accounts transferring to each other in parallel → no deadlock escapes to the caller.
  - The same idempotency key fired 50 times in parallel → money moves exactly once.

Acceptance criteria:
- Concurrency tests run against a real SQL Server via Testcontainers, not an in-memory provider. In-memory providers do not reproduce locking behaviour and are not acceptable here.
- Tests pass 20 consecutive runs without flaking.
- `docs/decisions/0001-isolation-level.md` explains the chosen isolation level and the alternatives rejected.

**For the README:** capture a naive implementation failing these tests, then the fixed version passing. This before/after is the single most persuasive artifact in the repository.

---

### Phase 3 — Architecture (target: 1.5 weeks)

Deliverables:
- MediatR: commands (`TransferMoneyCommand`) go through EF Core inside a transaction; queries (`GetAccountStatementQuery`) go through Dapper against read models. Make the asymmetry explicit — this is the actual reason CQRS exists here.
- Pipeline behaviours: validation, logging, transaction, caching — each as a decorator.
- Outbox pattern: domain events written in the same transaction as the ledger entries; `LedgerCore.Worker` dispatches them. Explain in the README why dual-write without an outbox is unsafe.
- Strategy pattern for fee calculation (flat / percentage / tiered).
- Repository + Unit of Work on the write side only. Do **not** wrap Dapper queries in repositories; that is ceremony without benefit.

Acceptance criteria:
- `docs/architecture.md` contains a Mermaid diagram of the write path and the read path.
- Each pattern used is named in the README with one sentence on the problem it solves here. Patterns that cannot be justified in one sentence must be removed.

---

### Phase 4 — Performance (target: 1 week)

Deliverables:
- Seed script generating **10 million** ledger entries with Bogus, with realistic distribution (a few hot accounts, a long tail).
- `AccountBalanceSnapshots` table plus an incremental job, so balance reads do not scan full history. Reads combine the latest snapshot with entries after it.
- Stored procedures for statement and reporting queries.
- Index design pass: covering indexes for the statement query, filtered index on the outbox, correct clustered index choice on `LedgerEntries`.
- `docs/performance.md`: for each key query, the execution plan and timing **before and after** indexing, as a table. Include the actual plan screenshots.

Acceptance criteria:
- Statement query for a 30-day window on a hot account: under 50 ms at 10M rows.
- `docs/performance.md` shows measured numbers, not estimates. State the hardware the measurements were taken on.

---

### Phase 5 — Cloud (target: 1.5 weeks)

Deliverables:
- Bicep templates: App Service, Azure SQL, Storage Account, Function App, Key Vault.
- Deployment to Azure App Service + Azure SQL. Connection strings from Key Vault via managed identity — **never** in `appsettings.json` and never in the repository.
- Azure Function, timer-triggered: produces a daily reconciliation report (verifying that all transactions net to zero) and writes it to Blob Storage. Use a Function rather than Hangfire here because it scales independently of the API and runs while the API is idle — state this reasoning in the README.
- `azure-pipelines.yml` mirroring the GitHub Actions workflow, so both ecosystems are demonstrated.
- Health check endpoints (`/health/live`, `/health/ready`) wired to App Service.

Acceptance criteria:
- A live Swagger URL works and is linked at the top of the README.
- No secret appears anywhere in git history. Verify with a secret scan.
- A fresh deploy from clean Bicep succeeds end to end.

---

### Phase 6 — Data and BI (target: 2 weeks)

This phase is the database-developer half of the portfolio. Keep it clearly separated from the OLTP side.

Deliverables:
- Star schema in a separate database: `FactLedgerEntry`, `DimAccount`, `DimDate`, `DimTransactionType`. DDL in `data/warehouse/`.
- `DimAccount` implemented as Slowly Changing Dimension Type 2 — account status changes preserve history.
- SSIS package: nightly incremental load from OLTP to warehouse, using a watermark. Include error routing and a row-count audit table.
- Azure Data Factory pipeline performing the equivalent load in the cloud. Export the pipeline JSON to `data/adf/`.
- Power BI report: daily transaction volume, balance distribution by account, top accounts by volume, fee revenue trend. Commit the `.pbix` and screenshots.

Acceptance criteria:
- Incremental load is re-runnable without duplicating rows; a test proves it.
- The SCD2 dimension is demonstrably correct: changing an account's status produces a new row and closes the previous one.
- README explains why SSIS and ADF are both present (on-premise and cloud ecosystems).

**Explicitly out of scope:** SSRS. It is superseded by Power BI and adds no signal.

---

## 7. README requirements

A reviewer spends roughly 60 seconds on the README. It is as important as the code. Required order:

1. One-sentence description, **live demo link**, Swagger link, CI badge.
2. Architecture diagram (Mermaid, single image).
3. "Why I built this" — three sentences on the concurrency and correctness problems this project tackles.
4. Tech stack badges.
5. **Quick start:** `docker compose up`, then open `localhost:xxxx/swagger`. Must actually work — test it on a clean checkout.
6. The concurrency before/after from Phase 2.
7. The performance table from Phase 4.
8. Design decisions: each pattern and Azure service with one sentence of justification.
9. Screenshot of the Power BI report.

Write the README, all code comments, commit messages and documentation in **English**. This project targets EU applications.

---

## 8. Working agreement

- **Commit per meaningful unit of work**, not one giant commit per phase. A spread-out, readable history is itself a hiring signal. Conventional Commits format.
- **Never commit secrets.** Local config goes in user secrets or `.env` (gitignored).
- **Tests are written alongside the feature**, not retrofitted at the end of a phase.
- When something in this spec turns out to be wrong or impractical, **say so and propose an alternative** rather than silently working around it. Record the outcome as an ADR in `docs/decisions/`.
- Do not add a library, service or pattern that is not in this spec without asking first.
- If a phase's acceptance criteria cannot be met, report what failed instead of marking it done.
