# LineOps

**A multi-source sports-data ingestion and analytics operations platform.**
.NET 10 · Blazor (Interactive Server) · MudBlazor 9 · PostgreSQL 17 · Docker

LineOps ingests sports statistics and betting-market data from multiple external providers on
a schedule, stores line movement as partitioned time-series, computes performance analytics
(closing-line value, ROI, bankroll), and monitors its own ingestion health with operational
KPIs, alerting, and an incident log with written root-cause analyses.

> **Scope.** LineOps does not place wagers and does not integrate with any wagering system.
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
- Adapters for four providers behind two interfaces (`IOddsSource`, `IStatsSource`), so
  adding a provider is a registration change, not a code change.
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
- **Opening past the window limit closes the least recently used** — named in the launcher
  before it happens and in the footer after.
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

## Running it

### First time

```powershell
./scripts/setup.ps1              # generates .env secrets + the HTTPS dev certificate
dotnet dev-certs https --trust   # optional, removes browser warnings
docker compose up -d
```

Open **<https://localhost:9443>**. That's it — no .NET SDK required to run it.

The web container waits for Postgres to report *healthy* (not merely started, since the app
migrates on boot), applies migrations, creates the monthly partitions, seeds reference data,
and runs one ingest at startup so the dashboard has data on first load.

```bash
docker compose logs -f web     # follow ingestion and alert activity
docker compose down            # stop, keeping the database volume
docker compose down -v         # stop and delete all data
```

**No API keys are needed to start.** ESPN is on by default and needs no authentication, so real
schedules, scores, box scores and season history flow in for free. Odds are the part that
wants a key: until one is supplied, an offline demo source generates a plausible slate with
drifting prices so a cold clone is fully functional — line-movement charts, CLV, and the
on-call drills all work. It stands down automatically once a real provider is configured, so
demo and real data never mix.

Standing down stops it writing, but does not remove what it already wrote — fabricated prices sit
in the same table as real ones under a different source id, where every reader downstream treats
them alike. `scripts/purge-demo-data.sql` clears them, once, when a real key goes in.

### The board

Open the **Board** window for today's slate with the best available number on each market and
the book holding it — moneyline, spread and total, across DraftKings, FanDuel, bet365 and
BetMGM.

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

### Pulling data by hand

**Pull data** on the Slate window offers each fetch by name, with what it will cost:

| Pull | What it gets | Cost |
|---|---|---|
| Today's games | Schedules, start times, live scores | 1 request per sport |
| Yesterday's results | Final scores and box scores | 1 per sport, plus 1 per finished game |
| Fresh lines | One price scan across every book | 2 per sport per odds source |
| Settle finished games | Grades entries and resolves CLV | none — reads what is already stored |

These are the same jobs the scheduler runs, so the manual and automatic paths cannot drift.

### Nothing runs on a fixed hour

- **Lines** are scanned at a cadence *derived* from the provider's remaining daily allowance —
  what is left, minus a reserve, divided by the cost of one scan, spread over the hours that
  remain. The allotment gets used without being exceeded, and the cadence adjusts itself as the
  day is spent. Ops shows the interval in force and what is pacing it.
- **ESPN runs around games:** the slate refreshes when the next start is close, and results are
  swept once a started game is old enough to have finished — then retried until it is, with
  settlement following immediately. A game finishing at 22:00 settles at 22:00.
- A slate with nothing starting soon spends nothing at all.

### Turning on real odds

Stats come from ESPN, free and unauthenticated. **Odds need a key**, because a market needs
several books and the free feeds that carry several want one. ESPN publishes odds too, but only
one book — a price rather than a market — so it stays the stats port.

Copy `src/LineOps.Web/appsettings.Local.example.json` to `appsettings.Local.json` and fill in the
key. That file is gitignored and loaded last, so it wins over everything else — the
environment-named files are *not* ignored, which is why a key put in the obvious place ends up in
the repository with it.

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

The demo source stands down on its own the moment a real provider has a key, so there is nothing
to switch off. Check **Ops → Odds feeds** to see which providers are live and, when one is not,
why.

**Markets are the cost control.** The Odds API bills `markets x regions` per call, so its free
500 credits a month stretch a great deal further at two markets than three — adding totals is a
standing 50% surcharge on every scan for the rest of the month. Books, by contrast, cost nothing
extra: a region returns whoever it returns, which on `us` is nine.

The cadence is derived from whatever the provider actually meters — requests over a day, credits
over a month — so the schedule fits inside the allowance rather than relying on the budget guard
to refuse it. **Ops → Odds feeds** states the interval and what is left.

### Pulling lines on request

**Pull data → Pull lines — MLB** fetches moneyline and spread across every book for that sport's
unstarted games, and says what it will cost in credits before you press it. One entry per sport,
because that is how the provider bills and how the allowance is spent.

Manual and automatic pulls draw on the same pool, so pulling by hand lengthens the automatic
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
free-tier quota the live schedule depends on.

```jsonc
"Backfill": {
  "Enabled": false,          // auto-start on boot; off so the first run is a decision
  "Since": "2026-03-01",     // a fixed target beats a rolling day count for a season
  "Sources": [ "espn" ],
  "Sports": [ "mlb" ],       // the only sport in season; add leagues as they start
  "RequestDelay": "00:00:00.600",
  "RetryFailedDays": false
}
```

`Since` pins the walk to a date. A rolling `Days` count covers a different stretch every time
it runs — "180 days" reaches opening day in July and misses it in September — so a season, which
has a start rather than a length, is expressed as a date.

Expect it to take hours rather than minutes — a full MLB slate is sixteen requests, not one.
It is built for that: the walk goes newest-first, records every completed day, and resumes
where it left off after a restart. Stop it whenever you like and press the button again later.

**Odds history cannot be backfilled.** Every provider that sells historical prices meters
them, which is the cost model working as intended rather than a gap. Line movement accumulates
forward from the intraday poll, so CLV is available on games the platform watched live.

### Security posture

Deliberate choices, not defaults:

| Choice | Why |
|---|---|
| **HTTPS only, port 9443** | No HTTP listener exists to downgrade to or forget about. 9443 avoids the 8080/8443 crowd, so it is unlikely to collide with anything else you run. |
| **`127.0.0.1:9443:9443`** | Published to loopback only. A bare `9443:9443` binds `0.0.0.0` and exposes the app to every machine on your network — and because Docker writes its own iptables rules, the host firewall does not necessarily stop it. |
| **Postgres not published** | The app reaches it over the compose network, so it needs no host port at all. Opt in with `compose.dev.yml` when you want `psql` access. |
| **Certificate volume-mounted read-only** | Never `COPY`ed into the image: that breaks dev/prod parity and risks shipping a private key in a layer. |
| **No credentials in the repo** | Passwords live in `.env` (gitignored); `.env.example` is the committed template. `*.pfx`/`*.pem`/`*.key` are gitignored regardless of location. |
| **Non-root, read-only rootfs, `cap_drop: ALL`, `no-new-privileges`** | The app writes only to `/keys` and `/tmp`, so everything else can be immutable. A compromise inside the container has very little to escalate with. |
| **SCRAM-SHA-256 required** | Set at initdb, so password auth is never negotiated down to md5 or trust. |
| **Memory limits** | A runaway query or leak degrades one container instead of the host. |

### Exposing it beyond localhost

Do not simply change the port binding to `0.0.0.0`. Terminate TLS at a reverse proxy with a
real certificate and let it talk to the container over the compose network:

```yaml
# Caddy handles ACME/Let's Encrypt automatically
caddy:
  image: caddy:2-alpine
  ports: ["443:443", "80:80"]
  # Caddyfile: lineops.example.com { reverse_proxy web:9443 { transport http { tls } } }
```

The dev certificate is `localhost`-only and must not be used for this — it is untrusted
everywhere except the machine that generated it, and for Kubernetes you would use a TLS
secret rather than a mounted PFX.

### Running the ingestion worker separately

The web app hosts the scheduler in-process by default, which is right for a personal
deployment. To split it out:

```bash
docker compose --profile worker up -d
```

Set `Ingestion__HostScheduler=false` on the `web` service at the same time, or both processes
will poll the same providers and burn the free-tier budget twice.

### Developing against the code

Run Postgres in Docker and the app on the host, for hot reload and a debugger:

```powershell
docker compose -f docker-compose.yml -f compose.dev.yml up -d postgres
$env:ConnectionStrings__LineOps = "Host=localhost;Port=5433;Database=lineops;Username=lineops;Password=<POSTGRES_PASSWORD from .env>"
dotnet run --project src/LineOps.Web
```

The dev overlay is what publishes Postgres on `127.0.0.1:5433`; it is a separate file so an
exposed database is something you opt into rather than something you forget is on.

### Using real providers

Put keys in `.env` and enable the source. Any configuration key can be overridden by
environment variable using `__` as the separator, so nothing secret needs to enter the repo:

```bash
ODDS_API_IO_KEY=your-key-here
```

```yaml
# then, on the web service
Ingestion__OddsApiIo__Enabled: "true"
```

---

## Cost

**$0 to run.** Every provider is on a permanent free tier and the stack runs locally:

| Provider | Free tier | Notes |
|---|---|---|
| odds-api.io | 100 req/h, 500 req/day | Moneyline/spread/total, 2 books, no card |
| The Odds API | 500 credits/mo | Billed as markets × regions; used only for reconciliation |
| balldontlie | Free tier | Players and stats |
| ESPN (unofficial) | Free, unauthenticated | Schedules, scores, box scores; can change without notice |

The budget guard enforces these ceilings rather than trusting the schedule to stay inside
them. Player props at volume are the first thing that would cost money — the schema is
already built for them (`market` is text, `PlayerId` is nullable), so it is an addition
rather than a migration.

---

## Layout

```
src/
  LineOps.Core          Domain entities, odds maths, grading, analytics (no dependencies)
  LineOps.Data          EF Core model, migrations, partition maintenance, seeding
  LineOps.Reliability   Shared reliability library: KPIs, alert engine, incidents
  LineOps.Ingestion     Provider adapters, scheduler, settlement (library)
  LineOps.Worker        Standalone ingestion host (optional; Web can host the schedule)
  LineOps.Web           Blazor UI — a window manager; every view is a panel on one desk
tests/
  LineOps.Tests         Unit, fixture, and Testcontainers integration tests
docs/
  adr/                  Architecture decision records
  runbook.md            What each alert means and how to triage it
```

## Tests

```bash
dotnet test
```

Unit tests cover the odds maths, grading and analytics. Adapter tests parse recorded provider
payloads — including nested shapes, string-encoded prices and malformed rows — so parsing is
verified offline and for free. Integration tests run against real PostgreSQL via
Testcontainers, because the features worth testing (native partitioning, `jsonb`,
`timestamptz` offsets) do not exist in an in-memory provider.
