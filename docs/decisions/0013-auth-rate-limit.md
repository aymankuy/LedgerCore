# 0013: A rate limit on register and login

Status: accepted

## Context

`POST /api/auth/login` and `POST /api/auth/register` are the only endpoints that need no token, so a script can call them as fast as the network allows. Passwords are hashed with PBKDF2, which makes each guess expensive for the attacker too, but nothing capped how many guesses one client could make, and every attempt cost the server a hash and a database round trip.

## Decision

The built-in ASP.NET Core rate limiter (no extra package) caps register and login together, per client address: 10 requests per 60 seconds by default, configurable under `RateLimiting:Auth`. Both successful and failed attempts count, because telling the two apart is exactly what the attacker is after. Past the limit the API answers 429 as RFC 7807 ProblemDetails with the correlation id, code `rate_limited` and a `Retry-After` header.

Alternatives considered:

- **Account lockout after N failed logins.** It stops guessing against one account but lets anyone lock out anyone else by failing on purpose, and it needs state in the database. A per-address limit has neither problem. It does not stop a distributed attack from many addresses; that needs a service in front of the API (a WAF), which is out of scope for a free-tier demo.
- **A fixed window.** It lets a client send the limit at the end of one window and again at the start of the next. A sliding window with six segments counts that burst once.
- **Limiting every endpoint.** Authenticated endpoints already require a token that only a successful login produces, so the limit on login protects them indirectly, and they are the ones where legitimate clients are fast.

## Consequences

- **The address has to be the caller's.** Behind App Service the connection comes from the platform's front end. `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` is set in the Bicep template so the app reads the caller's address from `X-Forwarded-For`; the front end appends it, and ASP.NET Core takes the last entry, so a client cannot choose its own by sending the header. The API must therefore never be exposed directly without such a proxy. Locally (docker compose) there is no proxy and the limit applies to the connecting address.
- **Callers behind one NAT share a budget.** An office of 20 people logging in within a minute would hit 10. For this API that is acceptable; the limit is a setting.
- **State is in memory, per instance.** With more than one instance each has its own counters. The free tier runs one instance; a scaled-out deployment would need a shared store.
- The limiter's sliding window does not report when a slot frees up, so `Retry-After` is one segment of the window (10 seconds by default).
