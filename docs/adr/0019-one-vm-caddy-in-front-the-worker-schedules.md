# ADR 0019 — One VM, Caddy in front, and the worker is the only scheduler

**Status:** Accepted

**Amends:** [ADR 0006](0006-container-security-and-https.md) for the hosted case only. The laptop
stack keeps HTTPS-only on loopback.

**Plan:** [Going public](../superpowers/specs/2026-09-27-going-public-plan.md) §3.

## Context

Blazor Server keeps each visitor's circuit in one process's memory over a WebSocket, so a
platform that scales to zero drops circuits, and a second replica needs sticky sessions. The
desk also runs a scheduler that spends metered odds credits, and two schedulers spend them twice.

## Decision

- **One VM running `deploy/compose.yml`**: Caddy, web, worker, Postgres and a backup container.
  Only Caddy publishes ports (80, 443). Caddy terminates TLS with Let's Encrypt.
- **Web listens on plain HTTP on the compose network.** ADR 0006's rule was "no HTTP listener
  anyone can reach", and a listener on an unpublished bridge is not one. With
  `Hosting:BehindProxy`, web trusts `X-Forwarded-For` and `-Proto` from any sender. That is
  safe only because nothing but Caddy can reach it; publishing web's port would break this.
- **The worker is the only scheduler and evaluator.** Web runs with both switched off.
- **Images are built by `.github/workflows/images.yml`** after CI passes on `main`, and are
  tagged `main` and `sha-<commit>`. The server pulls when the operator runs `deploy.sh`, so no
  production credential lives in GitHub. Rollback is `IMAGE_TAG=sha-<commit> ./deploy.sh`.
- **Backups**: a nightly `pg_dump -Fc` with 14 days kept, copied off the box by the host, and
  restored quarterly as a recorded drill.

## Consequences

- $6–12 a month hosts everything. There is one point of failure, which the uptime monitor and
  the backups cover.
- Verified locally on 2026-09-27 against `localhost`. Caddy's local CA served the certificate.
  Sign-in set a Secure cookie, the hub negotiated and upgraded to a WebSocket through Caddy
  (101), an anonymous negotiate was redirected, and `restore.sh` loaded the development
  snapshot.
