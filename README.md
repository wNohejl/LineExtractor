# XLines

**A multi-source sports-data ingestion and analytics operations platform.**
.NET 10 · Blazor (Interactive Server) · MudBlazor 9 · PostgreSQL 17 · Docker

> **XLines was called LineOps until 1.0.** Projects, namespaces, packages, images and commands
> say XLines. A few stored values keep the old spelling so nothing saved on a machine is lost:
> the database and role `lineops`, the compose project and its volumes, the connection string
> `ConnectionStrings:LineOps`, the Data Protection application name, the saved-key folder
> `%LOCALAPPDATA%\LineOps` and the desk's saved-layout key
> ([ADR 0031](docs/adr/0031-xlines-the-name-its-own-repository-and-where-1-0-runs.md), amended).

XLines ingests sports statistics and betting-market data from multiple external providers on
a schedule, stores line movement as partitioned time-series, computes performance analytics
(closing-line value, ROI, bankroll), and monitors its own ingestion health with operational
KPIs, alerting, and an incident log with written root-cause analyses.

> **Scope.** XLines does not place wagers and does not integrate with any wagering system.
> The journal records bets placed elsewhere so their performance can be analysed against
> stored market data. There is no bet-placement path anywhere in the codebase.

---

## Why it exists

Most side projects demonstrate that you can build a feature. This one is built to demonstrate
that you can **operate** one: the interesting part is not the odds, it is the layer that
notices when a feed silently breaks at 3am and produces a written RCA afterwards.

---

## What it does

**Ingestion**
- Odds and stats adapters behind two interfaces (`IOddsSource`, `IStatsSource`), so adding a
  provider is a registration change, not a code change; the MLB Stats API sits beside them as
  the identity spine for MLB games and players.
- Three integration styles: synchronous REST pulls, a scheduled bulk slate ingest, and
  post-final settlement processing.
- Resilience per source: retry with jittered backoff, circuit breaking, per-attempt timeouts.
- A budget guard that refuses a run rather than overrun a provider's free-tier ceiling.
- **Store-on-change**: a price that has not moved is not written, so re-running a window is
  idempotent and every stored row is a real line move.

**Analytics**
- Closing-line value: each journal entry is joined to the last price observed before kick-off.
- ROI, win rate against the break-even rate implied by price, bankroll curve, and breakdowns
  by market and book.
- Automatic grading for moneyline, spread and total — including the push cases.

**The interface**
- A **desk**, not a set of pages: windows on one screen, so source health, the incident and
  the runs behind it can be read together. Fixed header and footer; only the taskbar moves.
- **Always one horizontal row** of full-height columns. Each new window appends and the
  others give up the space. Drag the divider between two columns to move their split; the
  rest of the row stays put. Arbitrary placement is left to modals and popovers layered above.
- **The limit is the number of columns, not of windows.** Past it, a window opens as a tab in
  one of the columns and nothing closes; the header says which column before the click
  (ADR 0027).
- A **primary window** leads the row and takes a larger share, and is what opens on an empty
  desk.
- A **Window manager** for the window ceiling, primary window and its share, resolution
  (follow the browser or declare a size), and taskbar dock.
- **Workspaces** — named layouts stored as fractions of the desk, so one saved on a monitor
  still fits a laptop.
- Each window's title bar and taskbar chip carry a **live pulse** of that window's own state,
  so a minimised window still tells you something changed.

**Operations**
- KPIs: freshness, success rate, volume-vs-median anomaly detection, and budget burn.
- An alert engine that opens, updates and auto-resolves alerts, one per (rule, source).
- An incident log that **cannot be closed without a root cause and a corrective action**.
- Failure injection, so the detect → alert → incident → RCA loop can be drilled on demand
  instead of waiting for a real outage.

---

## Quick start

Clone, run setup, start Docker, enter your free key in the desk: fixtures and prices appear.
About ten minutes, most of it the first image build. The default runs on The Odds API alone
(ADR 0026); ESPN is an optional extra you can turn on for box scores and player stats.

**You need**

| | Why |
|---|---|
| [Git](https://git-scm.com/downloads) | to clone |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or Docker Engine with Compose v2), running | Postgres and the desk run in containers |
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | `setup.ps1` uses it to make the HTTPS development certificate |
| PowerShell | built into Windows; on macOS or Linux install [`pwsh`](https://learn.microsoft.com/powershell/scripting/install/installing-powershell) and run the same scripts |
| A free key from [The Odds API](https://the-odds-api.com/#get-access) | the default source: fixtures (free), prices and final scores (credits), 500 credits a month |
| Optionally, your decision about ESPN | an extra: box scores, player stats, play-by-play, injuries and UFC cards. Off unless you turn it on in step 2 |

**1. Clone, on the development branch.** `main` is the stable branch and trails it; the steps
below are written for `LineX_Development`.

```powershell
git clone -b LineX_Development https://github.com/wNohejl/XLines.git XLines
```

```powershell
cd XLines
```

**2. Generate the local secrets, and decide about ESPN.** Writes `.env` (a random database
password and certificate password; gitignored) and exports the HTTPS development certificate.
Safe to run again: it leaves an existing `.env` alone.

```powershell
.\scripts\setup.ps1
```

It asks one question first: whether to turn ESPN on. You do not need it: without it the desk
runs on The Odds API's fixtures and scores. ESPN adds box scores, player stats, play-by-play,
injuries and UFC cards, read through unofficial, undocumented endpoints; its terms of use license its services for
personal, non-commercial use and prohibit automated access. Turning it on is your decision, and
it is for keeping data in your own local database only, never for hosting, redistributing or
selling it. The answer defaults to No and is written to `.env` as `ESPN_ENABLED`; the MLB Stats
API follows it. To answer yes without the question (a script, an agent):

```powershell
.\scripts\setup.ps1 -EnableEspn
```

A run that cannot ask, and has no `-EnableEspn`, writes `ESPN_ENABLED=false`. Change your mind
later by editing that line in `.env` and starting the stack again (step 4).

If Windows says running scripts is disabled, bypass the policy for that one run rather than
changing the machine's: `powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1`.

**3. Get a free key.** Fixtures, prices and final scores come from **The Odds API**:

| | |
|---|---|
| Sign up: free **Starter** plan, 500 credits a month, all sports | <https://the-odds-api.com/#get-access> → Starter → **START**. The key arrives by email. |
| Your account, usage and the key again | <https://dash.the-odds-api.com/> |
| API documentation | <https://the-odds-api.com/liveapi/guides/v4/> |

> **Not odds-api.io.** `odds-api.io` is a different provider with a near-identical name and its
> own keys. A The Odds API key goes in `THE_ODDS_API_KEY`; put in `ODDS_API_IO_KEY` it is
> refused with 401.

**The short way: enter it in the desk.** Skip the rest of this step, start the desk (step 4),
open **Ops → Odds feeds**, paste the key into **The Odds API key** and press **Check and
save**. The desk asks the provider whether the key is good before keeping it (free, no credit
spent) and says how many credits are left. No restart: with ESPN off the key is also the stats
source, and fixtures for the next eight days arrive within a minute. The key is
kept encrypted on your machine, outside the database, so it is in no snapshot and no export.

**Or in `.env`**, which is also where a separately run Worker reads it. Set both lines; a key
there does nothing without its switch beside it. A key saved in the desk takes precedence.

```ini
THE_ODDS_API_KEY=your-key-here
THE_ODDS_API_ENABLED=true
```

Check the key without spending a credit (the sports list is free); it reads the key from
`.env`, so it never lands in your shell history:

```powershell
$key = (Select-String .env -Pattern '^THE_ODDS_API_KEY=(.+)$').Matches.Groups[1].Value.Trim(); $r = Invoke-WebRequest "https://api.the-odds-api.com/v4/sports?apiKey=$key" -UseBasicParsing; "status $($r.StatusCode), credits remaining $($r.Headers['x-requests-remaining'])"
```

**What a credit buys** (the free month is 500, reset on the 1st):

| Pull | Cost |
|---|---|
| Fixtures (the schedule, eight days ahead) | free; runs by itself |
| Pull lines — one league | a credit per market: 2 with `setup.ps1`'s moneyline and spread; 3 if you delete the `THE_ODDS_API_TOTALS=` line in `.env` to add over/unders |
| Pull scores — one league | 2 credits; finals from the last three days |

Nothing that costs a credit runs unless you press for it, or switch **Ops → Scanning** to
Automatic. On Automatic the scheduler paces lines to the month and pulls each league's scores once
a night, always keeping 100 credits back for your own pulls. The arithmetic for November, NBA and
NFL in season, at 2 markets: scores about 4 credits a day (120 a month), leaving 280 for lines,
about one two-league scan every 10 hours. On request only, pricing and scoring one league once a
day is 4 credits a day, 120 a month; three leagues so is 360. Ops shows the month's own numbers.

Skip this step and the desk starts with nothing on it; add the key later in the desk, or in
`.env` and run step 4's command again.

**4. Start it.**

```powershell
docker compose -f docker-compose.yml -f compose/dev.yml up -d --build
```

Open **<https://localhost:9443>**. The browser warns about the certificate until you trust it
(`dotnet dev-certs https --trust`, your call: it changes the machine's trust store).

`compose/dev.yml` only publishes Postgres on `127.0.0.1:5433`, which the data scripts and a
host-side `dotnet run` use. Plain `docker compose up -d` runs the desk without it.

**5. Fill the database.** A new desk starts empty; this repository carries no data. With a key,
the next eight days of fixtures arrive by themselves (free), and **Pull data → Pull scores**
fetches the last three days' finals for a league (2 credits). The Odds API holds no history before
that and no box scores. With ESPN on, ESPN is the stats source instead: today's slate arrives by
itself, and history is a pull — the Board's **Date range** key takes a league and a span of days
(up to 200), or the same job runs in the worker image:

```powershell
docker compose run --rm worker --job espn:range --sport nfl --from 2025-09-04 --to 2025-09-30
```

Both read ESPN only and spend no odds credits. With ESPN off they are refused, and say what The
Odds API can do instead. With neither ESPN nor a key the board stays empty and **Ops** says to
enter a key.

**6. Pull prices and scores.** Both are fetched when you ask, not on a timer (`LinePolling:Mode`
is `Manual`, so a metered key is never spent unattended). Use **Pull data → Pull lines** or
**Pull scores** for a league: the menu keeps every pull that costs credits under **Costs credits**,
states the price and what is left, asks before a pull that would dip into the reserve, and does
not offer one the month cannot cover. **Ops → Odds feeds** shows whether the key
was accepted and what is left of the month.

**Check it worked**

```powershell
docker compose ps                               # xlines-postgres healthy, xlines-web up
```

```powershell
curl.exe -k https://localhost:9443/ready        # Healthy once the database is migrated
```

```powershell
docker compose logs -f web                      # ingestion and alert activity
```

`docker compose down` stops it and keeps the data; `docker compose down -v` deletes the data.

| If | Then |
|---|---|
| `set POSTGRES_PASSWORD in .env` | Step 2 has not run in this folder. |
| Port 9443 is taken | Set `HTTPS_PORT` in `.env` and start again. |
| The board has games but no prices | No one has pulled lines yet (**Pull data → Pull lines**), or the month is spent: **Ops → Odds feeds** says which. |
| The board is empty, and Ops says "No stats source" | No key is held and ESPN is off. Enter your key under **Ops → Odds feeds**. |
| Games stay unfinished | Scores are a pull (2 credits a league) unless Scanning is Automatic. A game more than three days old with no final cannot get one from The Odds API; the scores pull says how many. |
| UFC is missing or unpriced | UFC needs ESPN: The Odds API keeps no MMA scores, and a fight is priced only against a bout ESPN holds. |
| The key check answers 401 | A mistyped key, or an odds-api.io key. Get one from <https://the-odds-api.com/#get-access>. |
| No box scores, player logs or play-by-play | Those are ESPN's (`ESPN_ENABLED=false` by default). If you decide to turn it on, set `ESPN_ENABLED=true` in `.env` and run step 4's command again. |
| `--job espn:range` exits at once: "ESPN is off on this host" | The same switch. The worker reads `.env` too. |
| `dotnet run` fails with "An Application Control policy has blocked this file" | Windows Smart App Control. Stay on the Docker path above; do not turn it off for this. |

### Hand it to an agent

The repository ships its setup procedure as a Claude Code skill,
[`.claude/skills/xlines-setup/SKILL.md`](.claude/skills/xlines-setup/SKILL.md): the steps above
plus what to check, what to ask you, the key check, and what never to do unasked. Any agent can
follow it; it is plain Markdown.

**Already cloned?** Open Claude Code in the `XLines` folder and say:

```text
/xlines-setup
```

**Starting from an empty folder?** Paste this into Claude Code, or any coding agent with a shell:

```text
Clone https://github.com/wNohejl/XLines.git into ./XLines on branch LineX_Development,
then read XLines/.claude/skills/xlines-setup/SKILL.md and follow it to get the desk running
in Docker on this machine. Work inside XLines. Check prerequisites before installing anything,
show me the ESPN notice and ask before turning ESPN on (it is optional), let me enter my The Odds
API key in the desk myself (or paste it into .env), never print .env, and do not pull lines or
scores, switch scanning to Automatic, or change security settings without asking. Done is docker compose ps healthy
and https://localhost:9443/ready answering Healthy: show me both, then give me the URL.
```

`CLAUDE.md` holds the repository's working rules (branches, authorship, verification) for an
agent that goes on to change code; `docs/INFRASTRUCTURE.md` and `docs/adr/` say why things are
the way they are.

---

## Running it

The web container waits for Postgres to report *healthy* (not merely started, since the app
migrates on boot), applies migrations, creates the monthly partitions, seeds reference data,
and, with ESPN on, runs one ingest at startup so the dashboard has data on first load.

**No API keys are needed to start, and one free key is all the default needs.** With ESPN off —
the default — The Odds API is the stats source as well as the prices (ADR 0026): its fixtures are
free and refresh on their own, its final scores cost 2 credits a league a pull. ESPN is the opt-in
extra (Quick start, step 2; `ESPN_ENABLED` in `.env`, or `Ingestion:Espn:Enabled` for a host run):
on, it is the stats source instead, needs no authentication, and adds box scores, player stats,
play-by-play, injuries, UFC cards and season history. With neither, nothing is pulled and **Ops**
says to enter a key. Until a key is supplied there is no odds source at all and price cells are
empty. **Ops → Odds feeds** names each provider and says why
it is not running, and the alert rules skip a source that has never run — an unconfigured feed
is not an outage.

Earlier versions filled that gap with an offline demo source that fabricated prices and rosters.
It has been removed. Fabricated rows land in the same tables as real ones under a different
source id, where every reader downstream treats them alike — and a fixture feed going stale
spent a critical alert slot on data that was never real. A database seeded before the removal
cleans itself on the next start: the demo sources and everything recorded against them are
deleted by `DatabaseInitializer`. Fixtures the old demo source invented — games ESPN never
confirmed — are left alone there, because removing a game can take a journal entry with it;
`scripts/sql/purge-demo-data.sql` clears those once, deliberately.

### The board

Open the **Board** window for today's slate with the best available number on each market and
the book holding it — moneyline, spread and total, across DraftKings, FanDuel and BetMGM, with
Pinnacle as the sharp price the others' value is measured against.

Under each price is a **spread rail**: one tick per book, placed by how good its price is.
Books that agree collapse to a single mark; a market worth shopping visibly spreads out, so a
column can be scanned for where the work repays before reading a number. The label says which
it is — a price gap in points, or `line varies`, which is the case a price comparison misses
entirely.

"Best" is judged per market. On a handicap the line outranks the price: +1.5 at -110 beats
+1.0 at -105. An over wants the lowest total, an under the highest.

**Click a row** and its actions appear on it — this is the workflow, board to bet:

| Action | Opens |
|---|---|
| More bets | Every book's number on every market for that game |
| Place wager | The journal entry, prefilled — you supply the stake |
| Form | Both rosters' recent form, read live from the stats history |

Each **adds a window** to the desk while there is room. Once the window limit is reached it
opens as a floating panel instead — so following something up never costs you the board you
were working from. Floating panels drag anywhere and are deliberately **not modal**: several
stay open at once so two games can be compared side by side.

**Every fixture that has no price says why.** A row of dashes is ambiguous between a broken
feed, a fixture nobody quoted and a game already under way, so the row states which and the
header reports coverage as a fraction. Prices are stored on change, so an old timestamp means
the line has not moved rather than that the data is stale — the header says `unmoved` for
exactly that reason.

### UFC

Fights sit on the same board as games (ADR 0021). A bout is a row between two fighters — the red
corner first in its markets, each fighter's record going in beneath their name — listed in card
order, main event first. A finished bout reads **W** and **L** with the method and where it ended
(`KO/TKO R1 3:14`). Moneyline and the rounds total are its markets; there is no spread.

**Form** on a fight row, and the Game window's **Tale of the tape**, put the two fighters side by
side: record, age, height, reach, stance and gym; ESPN's career rates; averages per fight from
ESPN's per-fight stat sheets (significant strikes, accuracy, knockdowns, takedowns, submission
attempts, finish rate, fight length); each fighter's last five; and opponents they share. A
fighter's name opens their profile with every recorded fight and its full stat sheet.

Cards come from ESPN's fight center, one request a card, and history reaches back to UFC 300.
**Pull data → Fighter profiles owed** reads the profiles of fighters on a card ahead or who have
just fought. Bets grade as books settle them: a draw pushes the moneyline, a no contest voids, and
a rounds total is graded on how long the fight lasted.

### Pulling data by hand

**Pull data** on the Board offers each fetch by name, with what it will cost:

| Pull | What it gets | Cost |
|---|---|---|
| Fixtures | The next eight days' schedule (ESPN off) | The Odds API: free |
| Pull scores — *league* | Finals from the last three days, then settles (ESPN off) | The Odds API: 2 credits |
| Today's games | Schedules, start times, live scores | ESPN, unmetered |
| Games in play | Live scores and state for games under way | ESPN, unmetered |
| Results owed | Finals and box scores for started games not yet settled | ESPN, unmetered |
| Play-by-play owed | Plays for finals that have none yet | ESPN, unmetered |
| Fighter profiles owed | Profiles of fighters on a card ahead or just fought | ESPN, unmetered |
| Closing lines owed | ESPN's close for finals no book market covered | ESPN, unmetered |
| Pull lines — *league* | One price scan across every book for that league's unstarted games | The Odds API: a credit per market |
| Settle finished games | Grades entries and resolves CLV | none — reads what is already stored |

These are the same jobs the scheduler runs, so the manual and automatic paths cannot drift.
The menu lists ESPN's pulls when ESPN is on and The Odds API's Fixtures and Pull scores when it
is off; everything that costs credits sits under **Costs credits** with its price, and free pulls
say "free". With ESPN off the Date range key is refused with a line naming the switch.

### Nothing runs on a fixed hour

- **Lines** are pulled on request by default (`Ingestion:LinePolling:Mode` is `Manual`), so a
  metered key is never spent unattended. With `Mode: Scheduled` they are scanned at a cadence
  *derived* from the provider's remaining allowance — what is left, minus a reserve, divided by
  the cost of one scan, spread over the time that remains. Ops shows the interval in force and
  what is pacing it.
- **ESPN runs around games:** the slate refreshes when the next start is close, and results are
  swept once a started game is old enough to have finished — then retried until it is, with
  settlement following immediately. A game finishing at 22:00 settles at 22:00.
- **The Odds API, with ESPN off:** fixtures are read every six hours, and every half hour while a
  game starts within three; free. Scores run unattended only on Automatic: once a night per league,
  after its last game has had time to finish, never into the 100-credit reserve, and set aside in
  the lines' pacing so the two share the month.
- A slate with nothing starting soon spends nothing at all.

### Turning on real odds

Stats come from The Odds API on that same key by default (ADR 0026), or from ESPN, free and
unauthenticated, once you have turned it on. **Odds need a key**, because a market needs several books and the free feeds that carry several want one. ESPN publishes odds too, but only
one book — a price rather than a market — so it stays the stats port.

The key can be entered in the desk itself, under **Ops → Odds feeds** (Quick start, step 3).
In Docker it can also go in `.env`. For a host run (`dotnet run`), copy
`src/XLines.Web/appsettings.Local.example.json` to `appsettings.Local.json` and fill in the
key. That file never enters an image; it is gitignored and loaded last, so it wins over everything else — the
environment-named files are *not* ignored, which is why a key put in the obvious place ends up in
the repository with it. The example file ships `Ingestion:Espn:Enabled` as false, with the
ESPN notice beside it, so copying it as it is leaves ESPN off. Setting that to true is the
opt-in for a host run; do it only once you have read the notice and decided to.

```jsonc
{
  "Ingestion": {
    "Sports": [ "mlb" ],
    "TheOddsApi": {
      "Enabled": true,
      "ApiKey": "your-key-here",
      "Markets": [ "moneyline", "spread" ]
    }
  }
}
```

Check **Ops → Odds feeds** to see which providers are live and, when one is not, why.

**Markets are the cost control.** The Odds API bills `markets x regions` per call, so its free
500 credits a month stretch a great deal further at two markets than three — adding totals is a
standing 50% surcharge on every scan for the rest of the month. Books, by contrast, cost nothing
extra: a region returns whoever it returns, which on `us` is nine.

In `Scheduled` mode the cadence is derived from whatever the provider actually meters — requests
over a day, credits over a month — so the schedule fits inside the allowance rather than relying
on the budget guard to refuse it. **Ops → Odds feeds** states the interval and what is left.

### Pulling lines on request

**Pull data → Pull lines — MLB** fetches the configured markets across every book for that
sport's unstarted games, and says what it will cost in credits before you press it. One entry per
sport, because that is how the provider bills and how the allowance is spent.

Manual and scheduled pulls draw on the same pool, so pulling by hand lengthens the scheduled
cadence rather than competing with it. Prices are stored on change, so a second pull minutes after
the first costs the same credits and stores almost nothing — worth knowing before reaching for it.

### Odds are kept for a day, not for ever

Odds live in two tiers. **Scans** hold the live market for games that have not started yet —
that is what the movement chart draws. Once a game starts, the last price before first pitch is
promoted to a **closing line**, one row per book, market and outcome, and the scans behind it
are deleted. The closing line is permanent; nothing else about the market is.

That is the whole cost model for odds: a bounded number of rows per game instead of a stream
that grows with poll frequency for ever. It also means **historical line movement is not
recoverable** — look at it while the game is still ahead of you. CLV is unaffected, because it
reads the promoted close and is denormalised onto the journal entry anyway.

Nothing captured before `OddsRetention:HistoryFloor` is kept at all, promoted or not.

### Cross-referencing odds against history

Open **Line movement** and the panel shows, under the market, the recent form of the players on
both rosters — appearances, and the stats they actually recorded. It is a lookup, not a copy:
the numbers are read live from the stats history by reference, so nothing goes stale and there
is no summary table to invalidate. Counting stats are totalled, rates are averaged and marked
`/g`, and a player who did not play in the window gets no row — the same rule storage follows.

### Gathering history

The schedule keeps you current but can only learn one day per day, so a fresh install has no
past to compute against. Open the **History** window and press *Gather history* to walk
backwards over previous days — schedules, scores and box scores, one request per sport per
day, and one more per finished game.

It runs against **unmetered sources only**, and that is enforced in code rather than
configuration: a source is eligible only when its row declares no rate limit and no credit
budget. Naming a metered provider in `Ingestion:Backfill:Sources` gets it refused, with the
reason shown in the window. Today that means ESPN, and it means a backfill cannot spend the
free-tier quota the live schedule depends on. With ESPN off there is no eligible source, so
there is nothing to gather.

```jsonc
"Backfill": {
  "Enabled": false,          // auto-start on boot; off so the first run is a decision
  "Sources": [ "espn" ],
  "Sports": [ "mlb", "nba", "nfl", "ufc" ],
  // Where each league's walk stops: its season start (UFC: UFC 300).
  "Seasons": { "mlb": "2026-03-25", "nba": "2026-10-20", "nfl": "2025-09-04", "ufc": "2024-04-13" },
  "Since": "2026-03-01",     // fallback for a league not named in Seasons
  "RequestDelay": "00:00:00.600",
  "RetryFailedDays": false
}
```

Each league walks back to its own date in `Seasons`; `Since` is the fallback for one that is not
named there. A rolling `Days` count covers a different stretch every time it runs — "180 days"
reaches opening day in July and misses it in September — so a season, which has a start rather
than a length, is expressed as a date.

Expect it to take hours rather than minutes — a full MLB slate is sixteen requests, not one.
It is built for that: the walk goes newest-first, records every completed day, and resumes
where it left off after a restart. Stop it whenever you like and press the button again later.

**Odds history cannot be backfilled.** Every provider that sells historical prices meters
them, which is the cost model working as intended rather than a gap. Line movement accumulates
forward from the pulls made before a game starts, so CLV against a book market is available on
games the platform priced; ESPN's close stands in where none did.

### Security posture

Deliberate choices, not defaults:

| Choice | Why |
|---|---|
| **HTTPS only, port 9443** | No HTTP listener exists to downgrade to or forget about. 9443 avoids the 8080/8443 crowd, so it is unlikely to collide with anything else you run. |
| **`127.0.0.1:9443:9443`** | Published to loopback only. A bare `9443:9443` binds `0.0.0.0` and exposes the app to every machine on your network — and because Docker writes its own iptables rules, the host firewall does not necessarily stop it. |
| **Postgres not published by the base file** | The app reaches it over the compose network, so it needs no host port at all. `compose/dev.yml`, which the Quick start uses for the data scripts, publishes it on `127.0.0.1:5433` — loopback only. |
| **Certificate volume-mounted read-only** | Never `COPY`ed into the image: that breaks dev/prod parity and risks shipping a private key in a layer. |
| **No credentials in the repo** | Passwords live in `.env` (gitignored); `.env.example` is the committed template. `*.pfx`/`*.pem`/`*.key` are gitignored regardless of location. |
| **Non-root, read-only rootfs, `cap_drop: ALL`, `no-new-privileges`** | The app writes only to `/keys` and `/tmp`, so everything else can be immutable. A compromise inside the container has very little to escalate with. |
| **SCRAM-SHA-256 required** | Set at initdb, so password auth is never negotiated down to md5 or trust. |
| **Memory limits** | A runaway query or leak degrades one container instead of the host. |

### Exposing it beyond localhost

To open the desk from your own laptop or phone, do not expose it at all: put it on your
tailnet with Tailscale Serve (`compose/tailnet.yml` or `scripts/serve-tailnet.ps1`; see
`docs/INFRASTRUCTURE.md`, "From another device", and ADR 0022). What follows is for a host
other people reach.

Do not simply change the port binding to `0.0.0.0`. Terminate TLS at a reverse proxy with a
real certificate and let it talk to the container over the compose network. `deploy/` is that
stack — Caddy with automatic certificates in front of the web container, the worker scheduling,
nightly backups, a gated desk — and `deploy/README.md` walks through it.

The dev certificate is `localhost`-only and must not be used for this — it is untrusted
everywhere except the machine that generated it, and for Kubernetes you would use a TLS
secret rather than a mounted PFX.

### Running the ingestion worker separately

The web app hosts the scheduler in-process by default, which is right for a personal
deployment. To split it out:

```bash
docker compose --profile worker up -d
```

Set `Ingestion__HostScheduler=false` and `Reliability__HostEvaluator=false` on the `web` service
at the same time, or both processes will poll the same providers and burn the free-tier budget
twice, and both will evaluate the alert rules.

### Developing against the code

Run Postgres in Docker and the hosts on the host, for hot reload and a debugger:

```powershell
.\scripts\setup.ps1                   # .env, and the connection string in both hosts' user-secrets
docker compose -f docker-compose.yml -f compose/dev.yml up -d postgres
dotnet run --project src/XLines.Web --launch-profile http
dotnet run --project src/XLines.Worker   # optional: ingestion on its own host
```

A host run does not read `.env`, so `ESPN_ENABLED` does not reach it. To turn ESPN on there,
put `"Ingestion": { "Espn": { "Enabled": true } }` in the host's `appsettings.Local.json`
(`src/XLines.Web/`, and `src/XLines.Worker/` for the Worker), or set
`Ingestion__Espn__Enabled=true` in the environment. The owner restores their private snapshot
between the first two lines instead of starting Postgres alone (`CLAUDE.md`, "Data").

Host-side runs read `ConnectionStrings:LineOps` from `dotnet user-secrets`, which setup writes
for both hosts from the password in `.env`; compose reads `.env` itself. Both launch profiles run
as Development, which is the environment user-secrets load in. Running the worker beside a web
host that also schedules ingestion polls the providers twice — set `Ingestion:HostScheduler` and
`Reliability:HostEvaluator` to false on the web host, as for the containers above.

The dev overlay is what publishes Postgres on `127.0.0.1:5433`; it is a separate file so an
exposed database is something you opt into rather than something you forget is on.

### Using real providers

In Docker, every provider is a key and a switch in `.env`; compose hands both to the web and
worker containers, so nothing secret enters the repo or an image:

```ini
THE_ODDS_API_KEY=your-key-here
THE_ODDS_API_ENABLED=true
THE_ODDS_API_TOTALS=          # blank drops over/unders (each market is billed on every scan); delete the line to add them

ODDS_API_IO_KEY=             # odds-api.io: an alternative feed, off; its free tier is evaluation-only
ODDS_API_IO_ENABLED=false
```

Any other configuration key can be overridden by environment variable with `__` as the
separator (`Ingestion__LinePolling__Mode`), added to the service in `docker-compose.yml`.

---

## Cost

**$0 to run.** Every provider is on a permanent free tier and the stack runs locally:

| Provider | Free tier | Notes |
|---|---|---|
| The Odds API | 500 credits/mo | The default source: fixtures (free), the book market — moneyline, spread, total, a credit per market a league — and final scores (2 credits a league). The feed the Quick start sets up |
| ESPN (unofficial) | Free, unauthenticated | The opt-in extra: schedules, scores, box scores, play-by-play, injuries, fighter profiles; can change without notice. Off by default |
| MLB Stats API | Free, unauthenticated | MLB game and player identity, probable pitchers. Opt-in, off by default: it follows ESPN's switch |
| odds-api.io | 100 req/h, 500 req/day | Alternative odds feed, off by default; free tier is for evaluation |

The budget guard enforces these ceilings rather than trusting the schedule to stay inside
them. Player props at volume are the first thing that would cost money — the schema is
already built for them (`market` is text, `PlayerId` is nullable), so it is an addition
rather than a migration.

### Whose data it is, and what you may do with it

XLines is a personal tool. It runs on your machine, pulls with your own key, and stores what
it pulls in your own Docker Postgres. Nothing is sent to the project or to anyone else.

- **The Odds API.** You sign up yourself and use your own key (Quick start, step 3); the
  project never supplies or shares one. By default it supplies the desk's fixtures and scores as
  well as its prices, so a desk without ESPN holds nothing but this provider's data and your own. Its
  [terms](https://the-odds-api.com/terms-and-conditions.html) allow storing its data and
  showing it in an application. They do not allow passing the raw data on as a data product,
  so do not publish a database or dump that holds it. This repository carries none: what your
  desk holds, it pulled itself.
- **ESPN.** These are unofficial, undocumented endpoints, read only so that an individual can
  keep games, scores and box scores in their own local database. ESPN's
  [terms of use](https://disneytermsofuse.com/english/) license its services for personal,
  non-commercial use and prohibit automated access; this notice does not change that, and
  whether to run the ESPN pull is your decision. So the desk ships with ESPN off and calls
  nothing until you turn it on: `.\scripts\setup.ps1 -EnableEspn` or `ESPN_ENABLED=true` in
  `.env` for Docker, `Ingestion:Espn:Enabled` in `appsettings.Local.json` for a host run. Do
  not host a desk that shows ESPN-derived data to other people, do not use it commercially,
  and do not redistribute it.
- **MLB Stats API.** MLB Advanced Media permits
  [individual, non-commercial, non-bulk use](http://gdx.mlb.com/components/copyright.txt) only.
  It is off whenever ESPN is.

XLines is not affiliated with or endorsed by ESPN, Disney, MLB, The Odds API or any
sportsbook. It takes no bets and is not betting advice. If you gamble, do it legally where
you are, at the legal age, and within your means.

---

## Layout

```
src/
  XLines.Core            Domain entities, odds maths, grading, analytics (no dependencies)
  XLines.Data            EF Core model, migrations, partition maintenance, seeding
  XLines.Reliability     Shared reliability library: KPIs, alert engine, incidents
  XLines.Ingestion       Provider adapters, scheduler, settlement (library)
  XLines.Observability   OpenTelemetry wiring and the KPI gauges
  XLines.Worker          Standalone ingestion host (optional; Web can host the schedule)
  XLines.Web             Blazor UI — every view is a window on one desk
  XLines.Desk            The desk itself as a reusable Razor package: windows, tokens, controls
  XLines.Desk.Hosting    Access gate, sign-in and edge headers for a Desk app
  XLines.Desk.Templates  `dotnet new deskapp`
  XLines.Desk.Testing    bUnit bench for Desk components
tests/
  XLines.Tests           Unit, fixture, and Testcontainers integration tests
  XLines.Web.Tests       The desk's windows and services
  XLines.Desk.Tests      The Desk package, its theme contrast and template
  XLines.Desk.Hosting.Tests
compose/                  Overlays on docker-compose.yml: dev (Postgres on 127.0.0.1:5433), tailnet, tailnet-db, telemetry
deploy/                   A hosted stack: Caddy, web, worker, Postgres, backups
site/                     The static landing page, published by hand to GitHub Pages (ADR 0031)
scripts/                  Setup, the owner's private snapshot and journal transfer, tailnet
  sql/                    Hand-run database scripts: purges and the test-bet seed
data/personal/            Gitignored: the owner's private clone (journal, snapshot); absent elsewhere
.claude/skills/           Agent skills: setup, and the Desk's design rules
docs/
  README.md               Which document answers which question, and which are only the record
  DESIGN.md               Why it is built this way
  INFRASTRUCTURE.md       What runs, where, and on what
  adr/                    Architecture decision records (0031: the name, the repository, hosting)
  runbook.md              What each alert means and how to triage it
  specs/                  Dated research and designs; the record, not the present
  plans/                  Implementation plans, as written before each build
  evidence/               What was measured, and the mock-ups the styling ADRs answer
  desk/                   The nuget.config the Desk template ships to a consumer
```

## Tests

```bash
dotnet test XLines.slnx
```

Integration tests need Docker running (Testcontainers). CI runs on pushes and pull requests to
`main` only, so run the suite before merging `LineX_Development`.

Unit tests cover the odds maths, grading and analytics. Adapter tests parse recorded provider
payloads — including nested shapes, string-encoded prices and malformed rows — so parsing is
verified offline and for free. Integration tests run against real PostgreSQL via
Testcontainers, because the features worth testing (native partitioning, `jsonb`,
`timestamptz` offsets) do not exist in an in-memory provider.

## Licence

The code is released under the [MIT License](LICENSE). The licence covers the code in this
repository and nothing else: it grants no rights in any provider's data, which stays under that
provider's own terms. Who those providers are, what their terms allow, and the fact that none
of them is affiliated with XLines are under
[Whose data it is, and what you may do with it](#whose-data-it-is-and-what-you-may-do-with-it).
