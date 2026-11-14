# 0006. Background jobs as hosted services, Hangfire deferred

- **Status:** Accepted
- **Date:** 2026-11-14
- **Phase:** 3

## Context

The spec pins Hangfire for local background jobs. Phase 3 needs two of them in the Worker:

- the **outbox dispatcher**, which polls `OutboxMessages` every second and drains any backlog;
- the **idempotency key clean-up**, which deletes keys older than 24 hours once an hour.

Hangfire brings its own SQL Server schema (about ten tables), a server process, a dashboard
that needs an ASP.NET host, and its own retry and history model.

## Decision

Both jobs are plain .NET `BackgroundService`s in `LedgerCore.Worker`:

- `OutboxWorker` polls on a `PeriodicTimer` and claims batches with `UPDLOCK, READPAST`, so
  several Worker instances can run side by side. Retries and the give-up limit live on the
  outbox row itself (`Attempts`), where a test can see them.
- `IdempotencyCleanupWorker` runs once per interval and retries a failed run after a minute.

Hangfire is revisited in Phase 4 for the balance snapshot job, where a dashboard, run history
and manual re-runs would actually be used.

## Why

- The dispatcher is a tight loop, not a scheduled job. Hangfire would add a polling layer of
  its own on top of ours and still need the same `READPAST` claim inside the job.
- The outbox table already records what a job framework would: what is pending, how often it
  failed, and when it was done. A second record of the same work in Hangfire's tables adds
  nothing a reviewer or an operator would read.
- Two small hosted services are about 80 lines with no new dependency, and each one is tested
  directly against SQL Server.

## Consequences

- No dashboard: the Worker's structured logs are the only view of its runs. Each failed
  message is logged with its id and attempt count, and a message that gives up is logged as an
  error.
- This is a deviation from the pinned stack, recorded here as `CLAUDE.md` asks. Phase 4
  decides whether Hangfire earns its place there.
