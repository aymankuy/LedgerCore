# 0005. Idempotency keys claimed in the same transaction as the write

- **Status:** Accepted
- **Date:** 2026-11-12
- **Phase:** 2

## Context

Spec rule 6: every write endpoint is idempotent via an `Idempotency-Key` header. A client
that times out does not know whether its withdrawal ran, so it retries. Without a key, each
retry is a new withdrawal. The concurrency test showed it: fifty retries of one withdrawal
of 10 drained an account of 100.

The hard case is not a retry after the first request finished, but a retry that arrives
while the first is still running.

## Decision

- **Where:** a MediatR pipeline behavior, `IdempotencyBehavior`, for every command marked
  `IIdempotentCommand`: deposit, withdrawal, transfer, open account, register, freeze and
  unfreeze. It runs after validation and the account access check, so a caller cannot claim
  keys for an account they may not touch. The API only reads the header.
- **How:** the behavior opens the transaction. Its first statement inserts the key into
  `IdempotencyKeys` with a SHA-256 hash of the command type and payload. Then the handler
  runs, joining the same transaction, and the serialized response is saved on the key's row.
  Everything commits together.
- **A parallel duplicate** inserts the same key and waits on the first request's row lock.
  If the first commits, the insert fails as a duplicate, and the duplicate returns the stored
  response. If the first rolls back, for example on insufficient funds, the key is free and
  the duplicate runs normally. No caller ever sees a half-finished key.
- **Same key, different request:** the hashes differ, so the API returns 409.
- **Missing or longer than 64 characters:** 400. Login is the only write without a key,
  because it changes no state.
- **Scope:** keys are stored as `{userId}:{key}`, so two users who pick the same key do not
  collide. Sign-up has no user yet and uses the `anonymous` scope.

## Alternatives considered

- **An ASP.NET action filter that stores the HTTP response.** Simple, but the filter runs
  outside the handler's transaction. A crash between the money movement and saving the key
  would leave money moved and no key, and the retry would move it again.
- **Claim the key in its own transaction first, marked "in progress".** Parallel duplicates
  would get a 409 "in progress" instead of the real response, and a crash after the claim
  would leave the key stuck in progress.
- **Insert the key at the end of the transaction.** A duplicate would run the whole
  withdrawal first and only then hit the key. It could also fail on insufficient funds,
  because the original spent the money, before reaching the key: the client would get 422
  instead of its original response.

## Consequences

- The response is replayed at the application level: the stored value is the command's
  result, and the controller turns it into the same HTTP response again. The `StatusCode`
  column from the spec's schema is therefore left empty.
- A request that failed with a business error is not stored. Retrying it with the same key
  runs it again, which is safe because the failed attempt changed nothing.
- Keys are kept for 24 hours. Deleting older keys is a job for the Worker in Phase 3;
  until then they stay, which only means a very late retry still gets the old response.
