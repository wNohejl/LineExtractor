# ADR 0006 — Container security posture and HTTPS

**Status:** Accepted

## Context

The first containerisation pass got the app running and stopped there. Reviewing it turned up
a real vulnerability rather than a stylistic issue:

```yaml
ports:
  - "5433:5432"   # Postgres
  - "8080:8080"   # app
```

Docker's short port syntax binds `0.0.0.0`. That published PostgreSQL — holding every
ingested row, with a weak password committed to the repo (`lineops_dev`) — to every host on
the local network. Docker also writes its own iptables rules, so a host firewall does not
reliably mitigate this. The app was served over plaintext HTTP on 8080, a heavily contested
port.

## Decision

**Network exposure.** Every published port is bound explicitly to loopback
(`127.0.0.1:9443:9443`). Postgres is not published at all in the default compose file — the
app reaches it over the compose network, so there is no reason for a host port to exist.
Host access is a separate opt-in overlay (`compose.dev.yml`), so an exposed database is a
decision rather than a forgotten default.

**HTTPS only, on 9443.** There is no HTTP listener: nothing to downgrade to, and no
`UseHttpsRedirection` port-guessing warning. 9443 sidesteps the 8080/8443/3000/5000 crowd, so
collisions with other local work are unlikely.

**Certificate handling.** Kestrel reads a PFX from a read-only volume mount, with the path and
password supplied by environment variables from `.env`. The certificate is never `COPY`ed into
the image — Microsoft's guidance is explicit that this breaks dev/prod parity and risks
disclosing a private key inside a layer. `dotnet dev-certs` on .NET 10 issues SANs for
`host.docker.internal` and `127.0.0.1`, so the container case works without extra plumbing.

**Secrets.** No credential appears in any committed file. `.env` is gitignored with
`.env.example` as the template, and `*.pfx`/`*.p12`/`*.key`/`*.pem` are ignored globally.
`AddLineOpsData` now throws when no connection string is configured, rather than silently
falling back to a hardcoded development password — a fallback credential is a credential
that eventually reaches production.

**Runtime hardening.** Non-root user, `read_only: true` root filesystem with `tmpfs` for
`/tmp`, `cap_drop: ALL`, `no-new-privileges:true`, and memory limits. Postgres keeps only the
capabilities initdb genuinely needs (`CHOWN`, `SETUID`, `SETGID`, `DAC_OVERRIDE`, `FOWNER`)
and is initialised with `--auth-host=scram-sha-256` so password auth can never be negotiated
down to md5 or trust.

## Consequences

- Rotating `POSTGRES_PASSWORD` does not affect an existing volume, because it only applies at
  initdb. Changing it on a live database is `ALTER ROLE ... WITH PASSWORD`, not a compose edit
  — and destroying the volume to force a re-init would discard real data. This is documented
  because the failure mode (`SqlState 28P01`) is otherwise baffling.
- Host-side `dotnet run` now needs the connection string supplied out of band. That is the
  intended trade: mild inconvenience in exchange for no credential in the repository.
- The read-only filesystem means any future feature that writes to disk must be given an
  explicit volume or tmpfs. That is a feature — it makes filesystem writes a visible decision.
- Exposing the app beyond localhost requires a reverse proxy with a real certificate. The dev
  certificate is trusted only on the machine that generated it and must not be used for it.
