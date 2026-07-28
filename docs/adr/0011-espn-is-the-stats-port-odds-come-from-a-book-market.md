# ADR 0011 — ESPN is the stats port; odds come from a book market

**Status:** Accepted

## Context

Two feeds, two different jobs, and until now both were half-wired. ESPN was a working stats
adapter with a stubbed roster method and a hard-coded four-league `switch` that threw on
anything else. Odds were a demo fixture generating fabricated prices, with two real adapters
that had never issued a request between them.

The immediate pressure is that only one sport is in season, more are coming, and the desk now
holds a full season of MLB history that the odds side has nothing real to sit beside.

## Decision

### ESPN is the stats port, and only the stats port

ESPN publishes odds — `sports.core.api.espn.com/.../odds` returns a full DraftKings line with
opens, currents and spreads. It is tempting and it is the wrong source, because it is *one*
book. A single book's number is a price; several books' numbers are a market, and the gap
between them is where a line being off actually shows up. Odds therefore come from a provider
that carries a book market, and ESPN stays what it is good at.

Three changes make it a port rather than an adapter that happens to work:

**Leagues are data.** `EspnLeagues` maps sport keys to ESPN's `{sport}/{league}` paths and
returns null for anything unmapped. Adding a league is one line, and an unmapped key is a sport
to skip rather than a `NotSupportedException` that takes down the backfill day that asked for
it. Nine leagues are mapped today against four in use.

**Team identity comes from the provider.** The scoreboard already carried a stable team id and
the official abbreviation next to the display name, on responses we were already fetching, and
the parser read only the name. The resolver then matched teams by wording and *invented*
abbreviations from it — the Colorado Rockies became "CR" and the Giants "SFG" where ESPN says
"COL" and "SF". Those invented identifiers reached the UI. `CanonicalTeamRef` carries id and
abbreviation through, resolution matches on the id first, and a derived abbreviation is now
only the fallback for a source that offers none.

**The roster method does nothing, on purpose.** It used to call `/teams` and discard the
response, which read as an implemented roster sync and was not one. Implementing it properly
would be worse: a roster is every player on the books, and storing a player who has not
appeared in a box score is exactly the allocation `StatsIngestionService` refuses. Players
enter through box scores because appearing in one is what makes a player worth a row.

### The demo source yields, per kind

Demo exists so a cold clone runs with no keys. It is not a supplement to a real feed — it
invents prices and rosters, and running it alongside one mixes fabricated rows into the same
tables under a different source id, where every downstream reader treats them alike. So it
stands down automatically: a real odds provider holding a key means no demo odds, ESPN enabled
means no demo stats. Nothing to remember to switch off.

### Odds readiness is visible

An odds provider fails to run for undramatic reasons — disabled, or enabled with an empty key —
and every one of them looks identical from outside: no prices, and no run rows to explain the
silence, because the adapter was never registered. The Ops window now reports each provider's
state and the reason, computed from the same options the container reads.

## Consequences

- **Odds still need a key.** The path is proven to the edge of the network — request shape,
  batching, book list and refusal-without-a-key are all covered by tests against a stub
  handler — but no live call has been made. Supply `Ingestion:OddsApiIo:ApiKey` out of band and
  the feed lights up; nothing else needs changing.
- One slate is two requests whatever the book list, so **books are free**: the ceiling on
  variety is the provider's tier, not our budget.
- ESPN team ids only fill in as games are re-walked. Identity is refreshed on games that
  already exist, not just new ones, so a backfill pass upgrades the whole table — but a
  database nobody re-walks keeps the old derived abbreviations.
- Nine mapped leagues do not mean nine working leagues: box-score shapes differ per sport, and
  only the four in use have been exercised against real payloads.

## What running it found

**A non-empty array default cannot be narrowed from configuration.** The binder *appends* to
an array property that already holds a default rather than replacing it. This bit three times —
backfill sources walked every day twice, bookmakers went out duplicated in the request URL,
and, worst, asking for one book would have silently kept the two defaults as well. Every list
option now defaults to empty and resolves its fallback in code. The failure is silent in both
directions, which is why it needed pinning with tests rather than a comment.

**The same source can reach entity resolution by two routes in one ingest**, and the weaker
route was overwriting the stronger. Game resolution carries the provider's team id; player
upsert knows only a team name. Writing the external id unconditionally meant the name-only
route clobbered the id microseconds after it was stored, so ids never survived a run and the
whole improvement looked like it had not shipped. Resolution now never downgrades: a real id
always wins, a name only fills a gap.

**`dotnet test` does not rebuild the web project's output.** It builds the test project and its
references, leaving `src/LineOps.Web/bin` stale, so the app under verification was an older
binary than the one that had just passed its tests. Combined with the content-root trap from
[ADR 0010](0010-odds-are-scans-until-first-pitch-then-one-closing-line.md), the rule is that
verifying a running app means building *that app* and checking what it actually loaded — twice
now, a "fix that did not work" was a fix that was never running.
