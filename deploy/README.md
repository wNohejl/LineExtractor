# Hosting the desk

One VM, four containers, Caddy in front. What this is for and why it is shaped this way:
[ADR 0019](../docs/adr/0019-one-vm-caddy-in-front-the-worker-schedules.md) and the
[going-public plan](../docs/superpowers/specs/2026-09-27-going-public-plan.md) §3.

| File | What it is |
|---|---|
| `compose.yml` | Caddy, web (the desk), worker (the only scheduler), Postgres, and a nightly backup |
| `Caddyfile` | TLS by Let's Encrypt, the WebSocket-aware proxy, HSTS |
| `.env.example` | Every secret and setting; copy to `.env` |
| `deploy.sh` | Pull the images, restart, and wait until `https://<host>/ready` answers |
| `restore.sh` | Load a dump: a nightly backup, or the development snapshot for a first load |
| `backup.sh` | What the backup container runs |

## A new server, from nothing

A 2 GB VM is the floor (web 512 MB, worker 384 MB, Postgres 512 MB, Caddy and backups
~200 MB). Ubuntu 24.04 LTS assumed.

1. **DNS.** Point an A (and AAAA) record for the desk's name at the server. Caddy asks
   Let's Encrypt for a certificate on first start, and that fails until DNS resolves.

2. **The machine.**

   ```bash
   # SSH keys only, then:
   sudo apt update && sudo apt -y upgrade && sudo apt -y install unattended-upgrades ufw
   curl -fsSL https://get.docker.com | sudo sh
   sudo usermod -aG docker "$USER"            # log out and back in

   sudo ufw default deny incoming
   sudo ufw allow OpenSSH
   sudo ufw allow 80,443/tcp && sudo ufw allow 443/udp
   sudo ufw enable
   ```

   Docker writes its own iptables rules, and a published port bypasses `ufw`. That is why
   `compose.yml` publishes only Caddy's 80 and 443. Keep it that way.

3. **The files.** Only `deploy/` is needed on the server, not the repository.

   ```bash
   sudo mkdir -p /opt/lineops && sudo chown "$USER" /opt/lineops && cd /opt/lineops
   for f in compose.yml Caddyfile .env.example deploy.sh restore.sh backup.sh; do
     curl -fsSLO "https://raw.githubusercontent.com/wNohejl/LineExtractor/main/deploy/$f"
   done
   chmod +x deploy.sh restore.sh
   cp .env.example .env && chmod 600 .env
   ```

   If the repository is private (plan §8, decision 2), copy the directory with `scp -r deploy/`
   from a clone instead.

4. **The secrets.** Fill in `.env`:
   - `POSTGRES_PASSWORD`: generate with `openssl rand -base64 30 | tr -d '+/='`.
   - `ACCESS_OPERATOR_CODE_HASH`, and `ACCESS_GUEST_CODE_HASH` for reviewers. Generate both
     on your own machine with `./scripts/new-access-code.ps1` (and `-Role Guest`). The codes
     are printed once; put them in a password manager. The server gets only the hashes.
   - `THE_ODDS_API_KEY`, with `THE_ODDS_API_ENABLED=true`, if the desk should price games.

5. **The registry.** A new GHCR package is private. Either make `lineops-web` and
   `lineops-worker` public (GitHub → Packages → Package settings), or log the server in
   with a token that can only read packages:

   ```bash
   echo "<fine-grained PAT, read:packages>" | docker login ghcr.io -u <github-user> --password-stdin
   ```

6. **Start it.**

   ```bash
   ./deploy.sh
   ```

7. **Load the data.** A fresh database has no history. Copy the development snapshot up and
   restore it. The web and worker stop while it loads.

   ```bash
   scp data/snapshots/lineops.dump server:/opt/lineops/     # from a clone
   ./restore.sh lineops.dump
   ```

8. **Watch it from outside.** Add a free uptime monitor (Better Stack, UptimeRobot) on
   `https://<host>/health` (the process) and `https://<host>/ready` (the database). A host
   cannot report its own absence.

## Day to day

| To | Run |
|---|---|
| Ship what is on `main` | `./deploy.sh` |
| Roll back | `IMAGE_TAG=sha-<commit> ./deploy.sh` (the tags are on the package page) |
| Read the logs | `docker compose logs -f web worker` |
| Open a database shell | `docker compose exec postgres psql -U lineops` |
| See the backups | `ls -lh backups/` and `docker compose logs backup` |
| Change a code | Make a new hash, put it in `.env`, `docker compose up -d web` |
| Let anyone read | `ACCESS_ALLOW_ANONYMOUS=true`, but only once stats come from a licensed source (plan §4) |

## Backups

The `backup` container writes `backups/lineops-<time>.dump` at start and every 24 hours after,
keeping 14 days. Those files sit on the same disk as the database, so they protect against a
bad migration or a deleted row, not a lost server. Copy them off the box too. A weekly cron
with `rclone` to Cloudflare R2 or Backblaze B2 costs nothing at this size:

```bash
# crontab -e
30 4 * * 0  rclone sync /opt/lineops/backups r2:lineops-backups --max-age 8d
```

**Restore one every quarter** into a scratch database (`./restore.sh backups/<file> --force`
on a spare VM, or a local Docker) and log it on the desk as an incident with its root cause
and corrective action. It is the same drill Ops runs for alerting, applied to the data.

## What is deliberately not here

- **An automatic deploy from GitHub.** Images build on every green `main` and wait. The
  server pulls when you run `deploy.sh`, so no key that can reach production lives in CI.
- **The Aspire dashboard.** It is anonymous. For telemetry, run `compose.telemetry.yml`
  locally, or send OTLP to a hosted backend (Grafana Cloud's free tier) by setting
  `Observability__Enabled=true` and `Observability__OtlpEndpoint` on web and worker.
- **A second web replica.** Blazor Server keeps each visitor's circuit in one process's
  memory. Two replicas need sticky sessions and shared Data Protection keys. One replica
  serves far more readers than a showcase will see.
