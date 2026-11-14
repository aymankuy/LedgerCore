# LedgerCore: progress and plan

Working notes for picking the project up again. The spec is [CLAUDE.md](CLAUDE.md); this file records where we are against it and how each phase was done.

Last updated: 2026-11-14. State: Phases 1 and 2 are merged into `main`; Phase 3 is complete on the `phase-3` branch and waits for its merge on [github.com/aymankuy/LedgerCore](https://github.com/aymankuy/LedgerCore).

## Status by phase

| Phase | Status |
|---|---|
| 1. Core | **Done.** All acceptance criteria verified (see below). |
| 2. Correctness under concurrency | **Done**, merged (PR #1). All acceptance criteria verified (see the checklist below). |
| 3. Architecture (CQRS, behaviours, outbox, fee strategy) | **Done** on `phase-3`. Acceptance criteria verified (see the Phase 3 outcome below). |
| 4. Performance (10M rows, snapshots, indexes) | Not started |
| 5. Cloud (Azure, Bicep, Key Vault, Function) | Not started |
| 6. Data and BI (star schema, SSIS, ADF, Power BI) | Not started |

## Phase 1: what was built

**Domain:** `Account` has no balance column, and its `Kind` is either customer or settlement. `LedgerTransaction` can only be built through factories that produce balanced entries: deposits and withdrawals go through a settlement account, and transfers only run between customer accounts. `Money` enforces `DECIMAL(19,4)` and `UtcTime` enforces UTC at millisecond precision.

**Database:** EF Core migrations build the §5 schema plus the following:
- `TR_LedgerEntries_AppendOnly`: rejects every UPDATE and DELETE.
- `TR_LedgerEntries_NetZero`: rejects any transaction whose entries do not net to zero.
- `usp_PostLedgerTransaction`: posts a whole transaction through a TVP in a single insert.
- A filtered unique index allowing one settlement account per currency.
- An application login, `ledgercore_app`, with no ALTER rights (so it cannot TRUNCATE) and with UPDATE and DELETE on `LedgerEntries` denied.

**Application:** MediatR commands are `OpenAccount`, `Deposit`, `Withdraw`, `TransferMoney`, `RegisterUser` and `Login`. Queries are `GetAccountBalance`, `GetAccountStatement` and `ListMyAccounts`; they run on Dapper with hand-written T-SQL, and the statement computes its running balance with `SUM() OVER`. Two pipeline behaviours run on every request, in this order:
- `ValidationBehavior` runs FluentValidation.
- `AccountAccessBehavior` enforces "only your own accounts" for every `IAccountScopedRequest`, with an exception for admins.

**API:**
- JWT auth, with the signing key validated at startup and never kept in appsettings.
- Controllers for auth, accounts and transfers.
- RFC 7807 ProblemDetails with a stable `code` and the request's `correlationId`. Another user's account returns 404, not 403.
- Swagger with bearer support.
- Serilog JSON logs, with a correlation id on every request.

**Startup:** `DatabaseInitializer` migrates as `sa`, provisions the app login, and seeds the system user, the EUR/USD/TRY settlement accounts, an admin, and two demo users. Each step is idempotent, and nothing runs unless it is configured.

**Delivery:**
- Multi-stage `Dockerfile`.
- `docker-compose.yml` (api, mssql on `localhost,14333`, redis).
- GitHub Actions workflow: restore → build → test → coverage gate (70%) → publish → docker build.
- README.

**Decisions recorded:**
- [ADR 0001](docs/decisions/0001-net-zero-enforcement.md): net-zero is enforced by a stored procedure plus a trigger.
- [ADR 0003](docs/decisions/0003-account-kind.md): an explicit account kind.
- [ADR 0004](docs/decisions/0004-local-compose-credentials.md): local-only default credentials in compose.
- ADR 0002 is reserved for the Phase 2 isolation-level decision.

### Phase 1 acceptance, as verified

- **Clean-machine start:** `docker compose up --build` from a fresh `git clone` serves Swagger at `http://localhost:8080/swagger` with seeded data. This was verified twice by hand, the last time on `c285aa3`.
- **Overdraft:** a transfer above the balance returns 422 `insufficient_funds`. An HTTP test covers it, and it was also checked by hand.
- **Access control:** a user cannot read or move money on another user's account. An HTTP test covers it.
- **Coverage:** unit coverage is Domain 94.8% and Application 71.2%, as reported by CI. CI fails if either drops below 70%.
- **Tests:** 78 unit and 29 integration tests, all against a real SQL Server 2022 through Testcontainers.

### Known gaps carried forward

- Withdraw and transfer are **deliberately naive**: the balance is read with no lock and no explicit transaction. Phase 2 exists to fix this, and the naive version is needed for the before/after.
- Not every write endpoint handles `Idempotency-Key` yet (Phase 2). The `IdempotencyKeys` table and its EF mapping already exist.
- `Accounts.RowVersion` exists in the schema, but nothing uses it yet (Phase 2).
- Redis runs in compose but nothing uses it until the caching behaviour in Phase 3. (Done in Phase 3.)
- The Phase 5 secret scan must allow-list the local compose defaults (ADR 0004).
- Commits are authored with a personal email, which is now public. We could switch to the GitHub noreply address.

## Phase 2 plan

Goal from the spec: prove that money cannot be lost, duplicated or overdrawn under concurrency, and do it with tests against a real SQL Server that pass 20 runs in a row.

Each step ends with all tests green and a commit. Steps marked **(together)** are where we decide or write code side by side.

### Step 1: Failing concurrency tests against the naive code (together)

Set up `tests/LedgerCore.ConcurrencyTests`. The project exists but is empty. It needs its own Testcontainers SQL Server fixture and must drive the real handlers in parallel. Then write the three tests the spec asks for:

1. **100 parallel withdrawals from one account.** Fund the account with exactly enough for N withdrawals and fire 100. Expected: exactly N succeed, the rest get `insufficient_funds`, and the final balance is never negative.
2. **Reciprocal transfers.** A→B and B→A fire in parallel, many times. Expected: no deadlock reaches the caller, and the total across both accounts is unchanged.
3. **Same `Idempotency-Key` sent 50 times in parallel.** Expected: money moves exactly once, and every caller gets the same response.

Run them against the current code and **save the failing output** (overdrawn balances, deadlock errors, duplicate movements) to put in the README.

→ verify: tests 1 and 2 fail on the naive code for the expected reason. Test 3 cannot run until the idempotency endpoint shape exists, so it is written first and is expected to fail.

### Step 2: Transaction boundary and isolation level (together, decision first)

The question to answer, and record in **ADR 0002**: how does a withdrawal read the balance and write the entries so that no other request can spend the same money in between?

Options to compare:
- **`READ COMMITTED` plus `UPDLOCK, HOLDLOCK` on the account row.** Each money movement locks the accounts it touches before reading the balance. Short locks, and the intent is explicit in the SQL. Likely choice.
- **`SERIALIZABLE` without hints.** Correct, but range locks on `LedgerEntries` are broad, and conversion deadlocks are likely under load.
- **`SNAPSHOT`.** Reads never block, but on its own it allows write skew: two withdrawals each see enough money. It needs an update conflict on a shared row to be safe.
- **Optimistic, using `Accounts.RowVersion`.** Bump the account's version on each movement and retry on conflict. It is also how the spec asks us to use `RowVersion`, but under heavy contention on one hot account it degrades into retry storms.

Implementation outline: add a unit-of-work method that runs a delegate inside an explicit transaction with the chosen isolation level. Inside it, the handler locks the accounts, reads the balance, builds the domain transaction and calls `usp_PostLedgerTransaction`, which already nests in an outer transaction (ADR 0001).

→ verify: test 1 passes, and the balance is never negative.

### Step 3: Deterministic lock ordering and deadlock retry

- **Lock ordering:** always lock the accounts of a transfer in ascending `AccountId` order, one statement per account. A single `WHERE Id IN (...)` does not guarantee the order in which locks are taken.
- **Deadlock retry:** wrap the whole transaction in a **Polly** retry with exponential backoff and jitter, for SQL error 1205 (deadlock victim) and possibly 1222 (lock timeout). Polly is named in the spec for this. Each attempt must start from a clean `ChangeTracker`.
- **`RowVersion`:** decide where optimistic concurrency belongs. It fits account state changes (freeze, close) racing with movements. Record the decision in ADR 0002 or in its own ADR.

→ verify: test 2 passes, and no deadlock reaches the caller. A test also shows that a deadlock does occur without the ordering.

### Step 4: Idempotency-Key (together, design first)

Decisions to make:
- Is the header **required** on every write endpoint (400 when missing), or optional? The spec says every write endpoint is idempotent, so the default is "required on money movements and account creation". Login and register need a separate decision.
- **Scope keys per user** (for example, store `userId:key`), so that two users choosing the same key cannot collide.
- **Concurrency of duplicates:** insert the key row in the *same* transaction as the business write. A concurrent duplicate then blocks on the primary key until the first request commits, gets a duplicate-key error, and returns the stored response. If the first request rolled back, the duplicate runs normally.
- **Same key, different payload:** compare against `RequestHash` (SHA-256 of the normalised request) and return 409.
- **Retention:** how long keys are kept, for example 24 hours, and who cleans them up (a Hangfire job locally).

Where it lives: an action filter or middleware on write endpoints, plus a store in Infrastructure.

→ verify: test 3 passes, and HTTP tests cover replay (same response), 409 on a different payload, and 400 on a missing header.

### Step 5: Stability and the before/after

- Run the concurrency suite **20 times in a row** with a small script, and fix any flakiness instead of retrying past it.
- Write `docs/decisions/0002-isolation-level.md` with the chosen level and the alternatives rejected.
- README "Concurrency: before and after": the failing output from step 1 next to the passing output, with a one-paragraph explanation of each fix.

### Phase 2 acceptance checklist

- [x] Concurrency tests run against a real SQL Server through Testcontainers, with no in-memory provider.
- [x] 100 parallel withdrawals: the balance never goes negative, and exactly the expected number succeed.
- [x] Reciprocal transfers: no deadlock reaches the caller.
- [x] Same idempotency key 50 times in parallel: money moves exactly once.
- [x] Suite passes 20 consecutive runs.
- [x] `docs/decisions/0002-isolation-level.md` written.
- [x] README shows the naive version failing and the fixed version passing.

### Phase 2 outcome (2026-11-13)

- All five steps are done, each with tests written before the fix. The README shows the before/after table.
- An unplanned find: the Phase 1 net-zero trigger deadlocked under parallel posts. Fixed with a covering index and a seek-only trigger (ADR 0001 amendment).
- Added beyond the original plan: an admin freeze/unfreeze endpoint, so `Accounts.RowVersion` has a real use (ADR 0002).
- Carried to Phase 3: a Worker job that deletes idempotency keys older than 24 hours. The `IdempotencyKeys.StatusCode` column stays empty by design (ADR 0005).
- Verified: 20 consecutive runs of the concurrency suite, and `docker compose up --build` from a fresh clone of `phase-2`, with Swagger, replay, 409 and 400 checked by hand.

## Phase 3: what was built (2026-11-14)

Six steps, each a commit on `phase-3` with tests written alongside:

1. **Transaction and logging behaviours.** `TransactionBehavior` opens the transaction for every `ICommand` (handlers no longer do); `LoggingBehavior` logs name, duration and outcome, never the payload.
2. **Fee strategy.** `IFeePolicy` with flat, percentage and tiered rules, chosen per currency from the `Fees` configuration. The fee is a separate `Fee` ledger transaction to a seeded fee revenue account (`AccountKind.FeeRevenue`), posted in the transfer's transaction.
3. **Outbox write.** `usp_PostLedgerTransaction` inserts a `LedgerTransactionPosted` row into `OutboxMessages` in the same transaction as the entries.
4. **Worker.** `OutboxWorker` claims batches with `UPDLOCK, READPAST, ROWLOCK`, retries failures up to five attempts; `IdempotencyCleanupWorker` deletes keys older than 24 hours (carried over from Phase 2). Its own Docker stage and compose service. Hosted services instead of Hangfire: ADR 0006.
5. **Redis balance cache.** `CachingBehavior` for `ICacheableQuery` (the balance query only), after the access check, falling back to SQL Server if Redis fails. The Worker's `BalanceCacheInvalidator` removes the balances a posting touched; entries also expire after one minute.
6. **Docs.** [docs/architecture.md](docs/architecture.md) with the write path, read path and outbox diagrams; README pattern table and the dual-write paragraph; ADR 0007 (no mapping library).

### Phase 3 acceptance, as verified

- [x] Commands go through EF Core inside a transaction, queries through Dapper; the asymmetry is drawn in `docs/architecture.md`.
- [x] Pipeline behaviours for validation, logging, transaction and caching, each a decorator; their order is tested.
- [x] Outbox events written in the same transaction as the entries (a test proves they roll back together), dispatched by the Worker; README explains why dual-write is unsafe.
- [x] Strategy pattern for flat, percentage and tiered fees.
- [x] Repository and Unit of Work on the write side only; Dapper queries are not wrapped.
- [x] `docs/architecture.md` has Mermaid diagrams of the write path and the read path.
- [x] Every pattern is named in the README with one sentence.
- [x] 132 unit, 46 integration and 6 concurrency tests green.
- [x] Checked in Docker: a transfer charges 0.50, every outbox event is processed, cached balances are cleared by the Worker, and with Redis stopped the balance still answers in about 0.2 s.

### Phase 3 findings and deviations

- Without the lock hints, four parallel outbox dispatchers deadlocked in each of three runs.
- With Redis down, the client queued each command for 5 seconds, so a balance read took about 12 s. `BacklogPolicy.FailFast` fixed it.
- The Worker starts before the API has created its SQL login; the dispatcher heals within seconds, and the clean-up job retries after a minute instead of an hour.
- Deviations from the pinned stack, each with an ADR: Hangfire deferred to Phase 4 (0006), Mapster not used (0007).
- Not added on purpose: an `AccountStatusChanged` event, because nothing consumes it yet. Phase 6 (SCD2) may want it.
- Cached balances can be up to about a second stale after a posting, until the Worker's next poll. This is tested and documented, and money movements never read the cache.

## How we work

- The spec in `CLAUDE.md` is authoritative. When it is wrong or impractical, we say so and record an ADR.
- Small steps, each ending with tests green and a Conventional Commit. Tests are written together with the feature.
- New libraries or services need approval first. Polly is named in the spec for Phase 2.
- Code, comments, commits and docs are in English. Discussion is in Turkish.
