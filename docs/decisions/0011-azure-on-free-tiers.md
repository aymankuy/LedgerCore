# 0011. Azure deployment on free tiers, passwordless, with nightly jobs in a Function

- **Status:** Accepted
- **Date:** 2026-11-16
- **Phase:** 5

## Context

Phase 5 asks for a live deployment: App Service, Azure SQL, Storage, a Function App and Key
Vault, all described in Bicep, with no secret in configuration or in git. It runs on an Azure
free trial subscription. That subscription has a spending limit, so nothing can be charged to
a card, but every design choice still has to stay inside the free tiers.

The free tier of Azure SQL Database decides most of the design. It gives 100,000 vCore-seconds
a month. The database pauses after 15 idle minutes and, when the allowance runs out, stays
paused until the next month. **Anything that touches the database every minute keeps it
awake and uses up the month's allowance in a few days.** In docker compose, the Worker does
exactly that: it polls the outbox every second and refreshes the balance snapshots every
minute.

## Decision

| Concern | Choice |
|---|---|
| Region | Sweden Central. For this subscription, the free SQL tier is blocked in North Europe and West Europe. |
| API | App Service on the **Free (F1)** Linux plan, .NET 9 |
| Database | Azure SQL serverless with the **free offer**, set to pause when the allowance runs out |
| Secrets | Key Vault with RBAC. App settings are Key Vault references, read with the app's managed identity. |
| Database sign-in | **Microsoft Entra only.** The API and the Function connect with their managed identities. No SQL password exists anywhere. |
| Storage | Shared keys **disabled**, so every client signs in with Entra |
| Nightly jobs | One Function App on **Flex Consumption**, at most one 512 MB instance, two timer functions at 02:00 UTC |
| Redis and Worker | **Not deployed.** They stay in docker compose, where they show the cache and the outbox at work. |
| Cost guard | A budget on the resource group that emails at the first cent spent |

### Why the Worker's jobs become nightly on Azure

`NightlyMaintenance` does once a night what the Worker does continuously: it refreshes the
balance snapshots, deletes idempotency keys older than 24 hours and drains the outbox.
`DailyReconciliation` writes the report the spec asks for. Both run at the same minute, so
the database wakes only once a night for them.

Nothing breaks when the jobs run less often:

- **Balances stay correct.** A balance is the snapshot plus the entries after the watermark
  (ADR 0009). With a refresh once a night, a read scans one day's entries instead of one
  minute's. On a demo database that is a handful of rows.
- **The outbox has no consumer on Azure.** Its only handler clears the Redis cache, and there
  is no Redis there. Draining it keeps the filtered index small, and a consumer added later
  finds nothing stale.

### Why a Function and not Hangfire, for the reconciliation report

The spec's own reasoning holds. The report runs at night, while the API sleeps on its free
plan (no Always On), so it must not depend on the API being awake. A Function scales on its
own schedule, scales to zero between runs, and fits inside the free monthly grant. Hangfire
would need a host that is always running, which is the thing the free plan does not offer.

### Why the reconciliation checks the raw ledger

The report does not trust the mechanisms it is meant to audit. It recomputes the following
from the stored entries:

- each day's debits against its credits, per currency;
- that every transaction nets to zero (the trigger enforces this only on insert);
- that every account's reported balance matches the sum of its entries, which catches
  snapshot drift;
- that each currency sums to zero across all accounts.

Tests corrupt a ledger on purpose, with the trigger switched off and with a snapshot edited
by hand, and assert that the report catches both. At 10M entries the report takes about
9 seconds.

### How a deploy migrates the database

The API never migrates on Azure. A deploy runs the same binary once with
`Database:MigrateOnly=true`, as an Entra administrator, and that run does three things:

- applies the migrations;
- creates the managed identities as database users `FROM EXTERNAL PROVIDER`
  (`Database:AppPrincipals`), with the same least-privilege grants as the local SQL login:
  read, write, execute, and `DENY UPDATE, DELETE` on `LedgerEntries`;
- seeds the reference and demo data.

The running app then has no DDL rights at all. The connection reaches the server through the
"Allow Azure services" firewall rule, because the free App Service plan has no virtual
network integration; every connection still has to authenticate with Entra. A deploy from
outside Azure opens a firewall rule for its own IP and removes it afterwards.

### How the pipelines sign in to Azure

Neither CI system stores an Azure credential:

- **GitHub Actions** uses OpenID Connect. Azure trusts a token that GitHub issues for this
  repository's `main` branch. The federated credential's subject has to use GitHub's
  id-based form (`repo:<owner>@<id>/<repo>@<id>:ref:refs/heads/main`); the name-based form
  was rejected with `AADSTS700213`.
- **Azure Pipelines** uses a service connection with workload identity federation, which is
  the same idea. It reuses the GitHub deploy identity: the app registration holds a second
  federated credential whose issuer and subject come from the service connection, so no
  second service principal or role assignment is needed. Verified on 2026-11-18: a run of
  `main` passed Build (tests green) and Deploy (migration, zip deploys, smoke test).

The deploy identity is an app registration with only two roles, Website Contributor and SQL
Server Contributor, both on the one resource group. To migrate the database it must also be
a SQL administrator, so the server's Entra admin is the group `ledgercore-sql-admins`
(the developer and the deploy identity) instead of a single user. A deploy opens a firewall
rule for the runner's IP, migrates, and removes the rule in a step that always runs.

## Alternatives rejected

- **Deploying Redis and the Worker as they are.** Azure Cache for Redis has no free tier. The
  Worker, besides needing an always-on host, would keep the free database from ever pausing.
- **SQL authentication with a password in Key Vault.** It would work, but a password is a
  secret that can leak and has to be rotated. Managed identities have neither problem.
- **Storage connection strings.** They contain an account key. With shared keys disabled,
  no such key can exist.
- **Running migrations on API startup, as in compose.** The app would need DDL rights for
  its whole lifetime, only to use them once per deploy.

## Consequences

- **The first request after an idle period is slow, but it does not fail.** The database
  pauses after 15 idle minutes. It was measured on the live deployment three times:

  | Setup | First requests while the database resumes |
  |---|---|
  | Default `Connect Timeout` (15 s) | 500, 500, then 200: about 35 s of errors |
  | Plus SqlClient open retry on transient errors | Still 500, 500, then 200 |
  | Plus `Connect Timeout=90` | **200 after 51 s**, then 0.5 s |

  App Service logs showed why the retry alone did not help. The server accepts the login
  and holds it for about half a minute while it resumes, so the client's 15-second timeout
  expires (error -2, which is not on the transient list). The longer timeout in the Key Vault
  connection string fixes that case. The open retry is kept for the other way a resuming
  database answers, error 40613 "not currently available", which the migration step hit.
  Only opening a connection is retried, never a command, so nothing in a transaction can
  run twice. The startup migration retry also recognises the error when EF Core wraps it.
- The free App Service plan allows 60 CPU minutes a day. That is enough for a demo, not for
  load testing.
- The free trial lasts 30 days. After that, the resources stop unless the subscription is
  upgraded; with the upgrade, this design costs cents a month (storage and Key Vault
  operations).
- Bicep cannot create Entra users in SQL, and should not hold secrets. The deploy script
  therefore generates the JWT signing key and the admin password once, directly into Key
  Vault, and runs the migration step.

## Verification: a fresh deploy from clean Bicep

On 2026-11-18 the templates were deployed into an empty resource group
(`rg-ledgercore-cleantest`) next to the live one and then deleted. The steps were the ones
above and nothing else: `az deployment sub create`, one generated JWT key written to Key
Vault, the migration step as the Entra administrator, a zip deploy of the API and the
Function. Result: the deployment succeeded, `/health/live`, `/health/ready` and Swagger
answered 200, and registering and logging in worked, which proves the Key Vault reference
and the managed identity's database access. The resource group was deleted afterwards and
the test Key Vault purged.
