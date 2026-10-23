# 0004. Local-only default credentials in docker-compose.yml

- **Status:** Accepted
- **Date:** 2026-10-23
- **Phase:** 1

## Context

Two requirements in the spec pull against each other:

- Phase 1 acceptance: `docker compose up` on a clean machine brings up a working API with
  seeded data, with no setup step.
- Working agreement: never commit secrets; local config goes in user secrets or a
  gitignored `.env`.

A clean checkout has no `.env`, so if every credential lived only there, the first
`docker compose up` would fail.

## Decision

`docker-compose.yml` gives every credential a default through `${VAR:-default}`:
the SQL Server `sa` password, the application login password, the seeded admin and demo
passwords, and the JWT signing key. Any of them can be overridden in a gitignored `.env`
(`.env.example` lists them).

These defaults are not secrets in the sense the rule is about, because:

- they are used nowhere except a container stack on the developer's own machine;
- every published port is bound to `127.0.0.1`, so nothing is reachable from the network;
- every value is named `LocalOnly_…` or `local-only-…`, so it cannot be mistaken for a
  real credential;
- no deployed environment reads `docker-compose.yml`. Azure (Phase 5) gets its connection
  strings and signing key from Key Vault through managed identity.

## Alternatives rejected

- **Require `cp .env.example .env` first.** Keeps the repository free of any password
  string, but breaks the one-command quick start that the README promises.
- **Generate credentials on first start.** A script would need to run before compose and
  persist its output, which is the same setup step in another form.

## Consequences

- The Phase 5 secret scan will report these values. They are expected findings and are
  listed in the scanner's allow-list with a reference to this ADR; anything else it finds is
  a real leak.
- Changing `APP_DB_PASSWORD` after the first start requires `docker compose down -v`,
  because the login already exists in the database volume.
