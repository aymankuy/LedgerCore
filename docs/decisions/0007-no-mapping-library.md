# 0007. No object mapping library

- **Status:** Accepted
- **Date:** 2026-11-14
- **Phase:** 3

## Context

The spec pins Mapster for mapping. By Phase 3 every place that turns one shape into another is
one of these:

- **Read side:** Dapper maps SQL columns straight onto the response records
  (`AccountBalanceResponse`, statement lines). There is no entity in between to map from.
- **Write side:** a command's fields are passed to a domain factory method such as
  `LedgerTransaction.Transfer(...)`, which enforces the rules. Copying properties onto an entity
  would bypass those rules.
- **API:** controllers return the handler's record, or wrap an id in a one-line response record.

## Decision

Mapster is not added. Mapping stays explicit: constructor calls and SQL column aliases.

## Why

- There is nothing to configure a mapper for: no entity is ever turned into a DTO in code.
- A mapper on the write side would be actively harmful, because domain objects are built only
  through methods that check invariants.
- CLAUDE.md asks that every pattern be justifiable in one sentence; "we map nothing" is not one.

## Consequences

- A response that needs a field is changed in its SQL or its record, and the compiler finds
  every caller.
- This is a deviation from the pinned stack, recorded here as `CLAUDE.md` asks. If a later phase
  introduces real entity-to-DTO mapping, Mapster is reconsidered then.
