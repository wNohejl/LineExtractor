---
name: xlines-setup
description: Use when someone asks to set up, install, start, deploy locally, or "get running" XLines on a machine for the first time, or to add or fix their odds API key. Takes a fresh clone to a running Docker desk at https://localhost:9443 with The Odds API's free key as the default source of fixtures, prices and scores, asks before turning on ESPN (the optional extra for box scores; there is no data snapshot), and says what to ask the person before doing anything that costs credits or changes their machine.
---

# Setting up XLines on a new machine

The goal is the desk running in Docker at **https://localhost:9443**, with fixtures and prices
from the person's own free The Odds API key (the default source, ADR 0026), and ESPN only if
they choose it. This is the procedure in the README's "Quick
start", written for an agent: what to check, what to ask, and what never to do unasked.

This is for **anyone's** machine. `CLAUDE.md`'s owner's laptop bootstrap is the owner's own
flow: its `lineops-journal` repository is private and holds the owner's journal and database
snapshot, and nobody else needs it or can clone it. Skip it. There is no snapshot for anyone
else to restore.

## The key: The Odds API, and only that one

Two unrelated providers have near-identical names. The desk's fixtures, prices and final scores
come from **The Odds API** (`the-odds-api.com`). Its key goes in `THE_ODDS_API_KEY`, or in the
desk under **Ops → Odds feeds**.

| | |
|---|---|
| Sign up (free Starter plan: 500 credits a month, all sports, no card) | <https://the-odds-api.com/#get-access> → Starter **START**; the key arrives by email |
| Account, usage, the key again | <https://dash.the-odds-api.com/> |
| API documentation | <https://the-odds-api.com/liveapi/guides/v4/> |

**Not** odds-api.io (`odds-api.io`, variable `ODDS_API_IO_KEY`). It is a different company with
its own keys; a The Odds API key put there is refused with 401, and its free tier is licensed for
evaluation only (ADR 0024). Leave `ODDS_API_IO_*` blank unless the person asks for it by name.

Credits (500 a month, reset on the 1st):

| | Cost |
|---|---|
| Fixtures: the next eight days' schedule | free, and runs by itself |
| Pull lines, one league | a credit per market: 2 with the moneyline and spread `setup.ps1` writes; 3 if its `THE_ODDS_API_TOTALS=` line is deleted |
| Pull scores, one league | 2 credits; finals from the last three days only |

Nothing that costs a credit runs unless the person presses it or switches **Ops → Scanning** to
Automatic (`LinePolling:Mode` is `Manual`). On Automatic, lines are paced to the month and each
league's scores are pulled once a night, never into the 100-credit reserve: in November (NBA and
NFL) scores take about 120 a month and lines get the remaining ~280, one two-league scan about
every 10 hours. On request, pricing and scoring one league daily is about 120 a month.

## Procedure

Work from the repository root. PowerShell on every OS (`pwsh` on macOS and Linux).

1. **Check prerequisites, install nothing system-wide unasked.** `git --version`,
   `docker info` (the daemon must answer, not just the CLI), `dotnet --version` (10.x; `setup.ps1`
   uses it for the HTTPS certificate), and PowerShell. Report what is missing with its download
   link and stop: Docker Desktop <https://www.docker.com/products/docker-desktop/>, .NET 10 SDK
   <https://dotnet.microsoft.com/download/dotnet/10.0>, PowerShell
   <https://learn.microsoft.com/powershell/scripting/install/installing-powershell>.

2. **Get the code** if it is not already here, on the development branch:
   `git clone -b LineX_Development https://github.com/wNohejl/XLines.git XLines`.
   Already cloned: `git checkout LineX_Development` and `git pull`.

3. **Secrets, and the ESPN question.** ESPN is optional: without it the desk runs on The Odds
   API's fixtures and scores. ESPN adds box scores, player stats, play-by-play, injuries and UFC
   cards (the MLB Stats API follows it), and it is off until the person turns it on.
   `setup.ps1` asks at a console, but an agent's run cannot answer, so it writes
   `ESPN_ENABLED=false`. Before running it, show the person this notice, word for word (it is
   `EspnOptIn.Notice` in `src/XLines.Ingestion/Configuration/EspnOptIn.cs`), and ask whether
   to turn ESPN on:

   > ESPN's endpoints are unofficial and undocumented. ESPN's terms of use license its services
   > for personal, non-commercial use and prohibit automated access. Turning this on is your
   > decision; use it only to keep data in your own local database, never to host,
   > redistribute or sell it.

   Only on a clear yes run `.\scripts\setup.ps1 -EnableEspn`; otherwise `.\scripts\setup.ps1`.
   A yes that comes after `.env` exists: the person (or you, with their yes) sets
   `ESPN_ENABLED=true` in `.env`, and step 6's command is run again. Without a yes, ESPN stays
   off and the desk runs on the key; say so, and go on.

   `setup.ps1` writes `.env` (random database and certificate passwords) and exports the dev
   certificate; re-running leaves an existing `.env` alone. If scripts are disabled, run it as
   `powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1` (with `-EnableEspn` after a
   yes). Never change the machine's execution policy.

4. **The key.** Ask the person whether they have a The Odds API key. If not, give them the
   sign-up link above: it is what the default desk runs on. Without one the desk is empty unless
   ESPN is on (then games, scores and box scores, and no prices).

   **Preferred: they enter it in the desk**, after step 6: **Ops → Odds feeds**, paste it into
   **The Odds API key**, **Check and save**. The desk checks it against the provider for free
   and keeps it encrypted outside the database, and you never handle the key. Say this and
   move on.

   Otherwise it goes in `.env`, which they should paste themselves. `.env` needs both lines,
   because a key does nothing without its switch:

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

5. **Data.** This repository carries no data, and there is no snapshot to restore. Skip
   `restore-data.ps1`: it restores the owner's private snapshot and, without one, only says so.
   With ESPN off and a key, the next eight days of fixtures arrive by themselves within a minute
   of the key being saved (free); finals come from **Pull data → Pull scores** (2 credits a
   league, the person's press) and there is no history before the day they start. With neither,
   the desk is empty and Ops asks for a key. With ESPN on, today's slate arrives once the desk
   starts, and history is filled after step 6 by the
   person with the Board's **Date range** key, or (ask first: it can run for a long time) by
   one range run per league, at most 200 days each, spending no odds credits:

   ```powershell
   docker compose run --rm worker --job espn:range --sport nfl --from 2025-09-04 --to 2025-09-30
   ```

6. **Start:** `docker compose -f docker-compose.yml -f compose/dev.yml up -d --build`. The first
   build takes several minutes; run it in the background and wait on it rather than timing out.

7. **Verify, and show the person the output:**
   - `docker compose ps`: `xlines-postgres` healthy, `xlines-web` up.
   - `curl.exe -k https://localhost:9443/ready` (`curl -k` off Windows): `Healthy`.
   - `docker compose logs web --tail 50`: no startup exception.

   Then give them the URL and tell them: the browser warns about the certificate until they run
   `dotnet dev-certs https --trust` (their decision: it changes the machine's trust store); the
   key goes in under **Ops → Odds feeds** if it is not in `.env`, and fixtures follow on their
   own; prices arrive when they press **Pull data → Pull lines** for a league and finals with
   **Pull scores**, both under **Costs credits** with the price shown first; **Ops** states the
   stats source in force, credits left this month and what the settings will spend. ESPN is
   optional; Ops says how to turn it on and what it adds.

## Never, without the person's yes

- Turn ESPN on: `-EnableEspn`, `ESPN_ENABLED=true`, or `Ingestion:Espn:Enabled` anywhere. Show
  the notice in step 3 first; silence or "whatever" is not a yes.
- Pull lines or scores, switch Scanning to Automatic, or run anything else that spends The Odds
  API's credits. The key check and the fixtures (both free) are fine. Credits are theirs and
  monthly.
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
| The desk is empty; Ops says "No stats source" | No key is held and ESPN is off. They enter the key under **Ops → Odds feeds**; fixtures follow within a minute. |
| Games never finish | Scores are a pull (2 credits a league) unless Scanning is Automatic; a game more than three days old with no final cannot get one from The Odds API. Ask before pulling. |
| No box scores, player logs or UFC | Those are ESPN's. Only if the person says yes after reading the notice (step 3): `ESPN_ENABLED=true` in `.env`, then step 6's command again. |
| `--job espn:range` exits at once (code 5): "ESPN is off on this host" | The same switch. |
| Games but no prices | Nobody has pulled lines yet, or the month is spent; with ESPN on, perhaps no key: they enter one under **Ops → Odds feeds**, or `THE_ODDS_API_KEY` and `THE_ODDS_API_ENABLED=true` go in `.env` (then step 6's command again). **Ops → Odds feeds** names which. |
| Key check returns 401 | Wrong key, or an odds-api.io key. Get one from the sign-up link above. |
| `restore-data.ps1`: "No snapshot at …" | Expected: the snapshot is the owner's. Do not look for one; fill the database as in step 5. |
| `dotnet run` fails: "An Application Control policy has blocked this file" | Windows Smart App Control. Stay on Docker; the person decides about the setting, not you. |
| Web container restarts in a loop | `docker compose logs web`; usually the certificate: re-run `setup.ps1 -Force` only if the person agrees, since it also replaces the database password a volume was made with. |

Going on to change code after setup: `CLAUDE.md` holds the working rules (two branches, no
Claude trailers, verification before done).
