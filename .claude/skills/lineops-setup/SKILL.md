---
name: lineops-setup
description: Use when someone asks to set up, install, start, deploy locally, or "get running" LineOps on a machine for the first time, or to add or fix their odds API key. Takes a fresh clone to a running Docker desk at https://localhost:9443 with The Odds API's free key, and says what to ask the person before doing anything that costs credits or changes their machine.
---

# Setting up LineOps on a new machine

The goal is the desk running in Docker at **https://localhost:9443**, with prices if the person
has a key. This is the procedure in the README's "Quick start", written for an agent: what to
check, what to ask, and what never to do unasked.

This is for **anyone's** machine. `CLAUDE.md`'s "Laptop bootstrap" is the owner's own flow: its
`lineops-journal` repository is private, and nobody else needs it or can clone it. Skip it.

## The key: The Odds API, and only that one

Two unrelated providers have near-identical names. The desk's prices come from
**The Odds API** (`the-odds-api.com`). Its key goes in `THE_ODDS_API_KEY`.

| | |
|---|---|
| Sign up (free Starter plan: 500 credits a month, all sports, no card) | <https://the-odds-api.com/#get-access> → Starter **START**; the key arrives by email |
| Account, usage, the key again | <https://dash.the-odds-api.com/> |
| API documentation | <https://the-odds-api.com/liveapi/guides/v4/> |

**Not** odds-api.io (`odds-api.io`, variable `ODDS_API_IO_KEY`). It is a different company with
its own keys; a The Odds API key put there is refused with 401, and its free tier is licensed for
evaluation only (ADR 0024). Leave `ODDS_API_IO_*` blank unless the person asks for it by name.

Credits: one scan costs one credit per market per region. The `.env` that `setup.ps1` writes
asks for moneyline and spread, 2 credits a league per pull; deleting its `THE_ODDS_API_TOTALS=`
line adds totals and makes it 3. 500 a month is roughly 160–250 pulls; nothing pulls unless
asked (`LinePolling:Mode` is `Manual`).

## Procedure

Work from the repository root. PowerShell on every OS (`pwsh` on macOS and Linux).

1. **Check prerequisites, install nothing system-wide unasked.** `git --version`,
   `docker info` (the daemon must answer, not just the CLI), `dotnet --version` (10.x; `setup.ps1`
   uses it for the HTTPS certificate), and PowerShell. Report what is missing with its download
   link and stop: Docker Desktop <https://www.docker.com/products/docker-desktop/>, .NET 10 SDK
   <https://dotnet.microsoft.com/download/dotnet/10.0>, PowerShell
   <https://learn.microsoft.com/powershell/scripting/install/installing-powershell>.

2. **Get the code** if it is not already here, on the development branch:
   `git clone -b LineX_Development https://github.com/wNohejl/LineExtractor.git LineOps`.
   Already cloned: `git checkout LineX_Development` and `git pull`.

3. **Secrets:** `.\scripts\setup.ps1`. It writes `.env` (random database and certificate
   passwords) and exports the dev certificate; re-running leaves an existing `.env` alone. If
   scripts are disabled, run it as
   `powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1`. Never change the machine's
   execution policy.

4. **The key.** Ask the person whether they have a The Odds API key. If not, give them the
   sign-up link above and say the desk works without one (games, scores, box scores; no prices).
   Prefer that they paste it into `.env` themselves. Either way `.env` needs both lines, because
   a key does nothing without its switch:

   ```ini
   THE_ODDS_API_KEY=<their key>
   THE_ODDS_API_ENABLED=true
   ```

   Check the key without spending a credit (the sports list is free), reading it from `.env`
   so it is never echoed:

   ```powershell
   $key = (Select-String .env -Pattern '^THE_ODDS_API_KEY=(.+)$').Matches.Groups[1].Value.Trim()
   $r = Invoke-WebRequest "https://api.the-odds-api.com/v4/sports?apiKey=$key" -UseBasicParsing
   "status $($r.StatusCode), credits remaining $($r.Headers['x-requests-remaining'])"
   ```

   200 with a remaining count means it works. 401 means a wrong key, or an odds-api.io key.

5. **Data (ask first).** `.\scripts\restore-data.ps1` loads the committed snapshot of games, box
   scores and closing lines, so the desk opens with history. If it refuses because the database
   already holds games, that database is someone's: ask before re-running with `-Force`, which
   replaces it.

6. **Start:** `docker compose -f docker-compose.yml -f compose.dev.yml up -d --build`. The first
   build takes several minutes; run it in the background and wait on it rather than timing out.

7. **Verify, and show the person the output:**
   - `docker compose ps`: `lineops-postgres` healthy, `lineops-web` up.
   - `curl.exe -k https://localhost:9443/ready` (`curl -k` off Windows): `Healthy`.
   - `docker compose logs web --tail 50`: no startup exception.

   Then give them the URL and tell them: the browser warns about the certificate until they run
   `dotnet dev-certs https --trust` (their decision: it changes the machine's trust store); prices
   arrive when they press **Pull data → Pull lines** for a league, which shows the cost first;
   **Ops → Odds feeds** says whether the key is live and how many credits are left.

## Never, without the person's yes

- Pull lines or run anything that calls The Odds API beyond the free key check. Credits are
  theirs and monthly.
- Print, paste, commit or log `.env` or the key.
- Trust certificates, change execution policy, turn off Windows Smart App Control, or change
  any other machine security setting.
- `docker compose down -v`, `restore-data.ps1 -Force`, or anything else that deletes a database.
- Publish Postgres or the desk beyond `127.0.0.1`.

## When it goes wrong

| Symptom | Cause and fix |
|---|---|
| `set POSTGRES_PASSWORD in .env` | Step 3 has not run in this folder. |
| Port 9443 in use | Set `HTTPS_PORT` in `.env`, start again, use that port. |
| Games but no prices | Key or `THE_ODDS_API_ENABLED=true` missing (after fixing `.env`, run step 6's command again), or nobody has pulled yet. **Ops → Odds feeds** names which. |
| Key check returns 401 | Wrong key, or an odds-api.io key. Get one from the sign-up link above. |
| `restore-data.ps1`: "pg_restore: not found" | An older checkout; `git pull` and run it again. |
| `dotnet run` fails: "An Application Control policy has blocked this file" | Windows Smart App Control. Stay on Docker; the person decides about the setting, not you. |
| Web container restarts in a loop | `docker compose logs web`; usually the certificate: re-run `setup.ps1 -Force` only if the person agrees, since it also replaces the database password a volume was made with. |

Going on to change code after setup: `CLAUDE.md` holds the working rules (two branches, no
Claude trailers, verification before done).
