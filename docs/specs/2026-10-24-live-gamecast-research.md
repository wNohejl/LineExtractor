# A live gamecast — research

**Date:** 24 October 2026 (NFL week 7; one final on the board, thirteen games tomorrow).
**Scope:** NFL first. The design is per-sport in its *content* and shared in its *plumbing*, so
MLB follows on the same pipe.
**Method:** a read of the code path from ESPN to the Game window; live probes of ESPN's
scoreboard, summary, core and CDN endpoints against NE @ CHI (event `401873010`); the
provider docs for The Odds API and MLB's stats API. Nothing here was changed in the code.

**Question asked:** the Game window is slow to move while a game is being played and moves in
blocks when it does. Why, and what would an interactive gamecast across the sports we track
look like?

---

## 1. What the desk shows today, and how it gets there

The "gamecast" is the Game window (`GamePanel.razor`) with its title line: away name, score,
"at", home name, score, and a `Live` tag. There is no clock, no quarter, no possession, no
down and distance, no last play, no drive, no win probability. The `Game` entity holds
exactly what the window shows — `Status`, `HomeScore`, `AwayScore` (`Reference.cs:120-123`) —
because that is all the scoreboard parser keeps (`EspnStatsAdapter.ParseScoreboard`).

The path from ESPN to the screen, while a game is on:

| Step | Where | Cadence / cost |
|---|---|---|
| 1. Scheduler tick asks "is any started game not final?" | `IngestionScheduler.LivePollIsDueAsync` | tick every `TickInterval`; the live pass runs at most every `LivePoll` = **90 s** |
| 2. Runs the whole **slate** job: one scoreboard call per sport per league-day in play | `IngestionJobs.RunSlateAsync` → `FetchScheduleAsync` | 1 request per sport, after a 250 ms `RequestDelay` |
| 3. Every event on the scoreboard is resolved; status and both scores are written only when they changed | `EntityResolver.ResolveGameAsync:249-299` | one `SaveChanges` per changed game |
| 4. The save fires `pg_notify('lineops_changes','games')` | `ChangeNotifier` | once per save, topic-coarse |
| 5. The web host's listener fans the topic to every circuit | `DataChangeListener` → `DeskSignals` | immediate |
| 6. Every panel watching `games` waits the 1 s **settle**, then reloads everything it draws | `PanelBase.RefreshSoonAsync`, `GamePanel.LoadAsync` | ~10 queries per Game window per refresh |
| 7. Blazor diffs the whole panel and ships the diff over the circuit | InteractiveServer (ADR 0004) | — |

Two facts about this path matter for what follows. First, step 3 is already change-only, so
the desk is not reloading on a no-op poll: the notice only fires when a score or status
actually moved. Second, nothing in steps 1–7 is wrong for a desk whose question is "did the
score change" — it was built for that (Phase 3, "a live desk") and it does it. It is the
wrong shape for a gamecast, whose question is "what is happening right now".

## 2. Why it is delayed

The lag between a play on the field and the score moving on the desk is the sum of:

| Source of lag | Worst case | Note |
|---|---|---|
| ESPN's own scoreboard latency | ~10–20 s | The scoreboard is served with `Cache-Control: max-age=10` (measured). ESPN's data is a few seconds behind the broadcast on top of that. |
| Our poll interval | 90 s | `GamePassOptions.LivePoll`. On average a score change waits 45 s to be seen. |
| Scheduler tick granularity | up to one tick | The pass is checked only on a tick; `TickInterval` default is a minute (DESIGN §4). |
| Panel settle | 1 s | `PanelBase.Settle`, deliberate coalescing. |
| Panel reload | ~0.1–1 s | Ten queries, several of them aggregates (`MatchupCrossReference.GetAsync`, two `GetTeamAsync`, `BoxScoreAsync`). |

So a touchdown reaches the title line **between 10 s and ~2.5 min** after it happens, with
the poll interval dominating. The board can be *further* behind than ESPN's free scoreboard
page, which refreshes every ~10 s.

Where the slate job and the live poll share one entry (`EspnSlate`), the live pass also does
slate work it does not need every 90 s: resolving every event on the day's scoreboard,
refreshing team identity for all of them, and (for MLB) the spine sync gate. On a Sunday with
thirteen NFL games plus an MLB postseason slate that is fine for a scoreboard and wasteful for
a gamecast.

## 3. Why it is blocky

"Blocky" is three things stacked:

1. **The data only has blocks.** With only a score to move, the window can only jump —
   0 → 7 → 7 → 10. There is no clock running down between jumps, no "2nd & 6 at CHI 34",
   nothing to show that time is passing. A gamecast feels continuous because its *state*
   changes every 30–40 s (a play) even when the score does not.
2. **Every change reloads the whole window.** `GamePanel.Watches` is `{Games, Odds, Journal}`
   and `RefreshAsync` is `LoadAsync`: a score change in *any* game — or any odds write, or a
   journal save — re-runs ten queries and re-renders the whole Game window, and does the same
   in every other open window watching `games` (Board, Team, Player, Head to head). On a
   fourteen-game Sunday the `games` topic fires every time any game's score moves. The pulse
   goes active, the grids repaint, and the operator sees the window "think" for a moment to
   change two digits.
3. **The notice has no address.** `pg_notify` carries a topic, not a game id. A panel cannot
   tell "my game moved" from "some game moved", so it has to assume the worst. (The Phase 3
   design proposed `'<kind>:<sport>'` payloads; what shipped is `kind` only.)

None of this is a Blazor Server limitation. A render that changes two text nodes ships a
diff of two text nodes. The cost is the *reload* before the render, and the fact that it
is a reload of everything.

## 4. What ESPN actually offers, measured

All free, unauthenticated, undocumented (ADR 0011's trade). Probed 24 Oct against the
Thursday final; a live game will carry the same shapes with `state: "in"`.

### 4.1 Scoreboard — `site.api.espn.com/apis/site/v2/sports/football/nfl/scoreboard`

`max-age=10`. One call covers the week (14 events, 254 KB). Beyond what we parse now, each
`competitions[0]` carries:

- `status.displayClock`, `status.period`, `status.type.{state,name,detail,shortDetail}` —
  the clock and quarter. Already in the payload we fetch and throw away.
- `competitors[].linescores[]` — score by quarter (measured: `[10,10,7,3]`).
- `situation` — present while `state == "in"`: `down`, `distance`, `yardLine`,
  `possession` (team id), `isRedZone`, `homeTimeouts`, `awayTimeouts`,
  `downDistanceText`, `possessionText`, `lastPlay{text,type,…}`. Null before and after
  (measured null on the final). The core `situation` endpoint confirmed the field set.
- `competitors[].statistics` and `leaders` — live team totals and passing/rushing/receiving
  leaders.
- `odds` — ESPN BET's current line while live, where present. One book; a reference only
  (ADR 0011), never the board's price.

**This is the cheapest upgrade in the document.** Clock, quarter, possession, down and
distance, last play and quarter-by-quarter scores for every live game, for the one request we
already make — just a wider parser, a wider `CanonicalGame` and a wider `Game`.

### 4.2 Summary — `…/summary?event={id}`

`max-age=10`, 583 KB for a finished game. Carries `drives.previous[]` (20 drives, each with
`plays[]`, `result`, `yards`, `timeElapsed`, `start`/`end` yard lines) and, while live,
`drives.current`; `scoringPlays[]` (10, with `clock`, `period`, `scoringType`, `text`);
`winprobability[]` (181 points, one per play: `homeWinPercentage`, `tiePercentage`,
`playId`); `boxscore` (what the results pass already parses); `leaders`; `pickcenter`;
`header.competitions[0].{liveAvailable, playByPlayAvailable, playByPlaySource}`.

Each play: `id`, `sequenceNumber`, `type{id,text}`, `text`, `awayScore`, `homeScore`,
`period`, `clock.displayValue`, `scoringPlay`, `isPenalty`, `isTurnover`, `statYardage`,
`start{down,distance,yardLine,yardsToEndzone,downDistanceText}`, `end{…}`, `wallclock`,
`modified`.

Everything a gamecast draws is in this one document. Its weight is the cost: polling the full
summary every 10–15 s per live game is ~0.6 MB a time, and on a thirteen-game Sunday
afternoon that is most of a gigabyte an hour against a courtesy endpoint. Pollable for *the
game the operator has open*, not for the slate.

### 4.3 Core API — `sports.core.api.espn.com/v2/sports/football/leagues/nfl/events/{id}/competitions/{id}/…`

Small, addressable resources, paged, each with its own cache window (measured):

| Resource | Size | `max-age` | Holds |
|---|---|---|---|
| `status` | 316 B | 150 s | clock, period, type |
| `situation` | 393 B | 160 s | down, distance, yardLine, isRedZone, lastPlay, timeouts |
| `plays?limit=N` | ~4 KB/play | 900 s | 181 plays, 25 a page, each with a `probability` ref |
| `probabilities?limit=N` | ~1 KB/row | 900 s | per play: home/away win %, `spreadCoverProbHome`, `totalOverProb`, `secondsLeft` |
| `drives` | 824 KB | 416 s | 20 drives with plays inline |
| `odds` | 6.8 KB | 333 s | DraftKings open/current/close per side |

The cache windows are measured on a final and are long because the game is over; on a live
game ESPN's own clients hit these more often. The shape is the point: the core API is a
*feed of plays* with stable ids and sequence numbers, which is what an incremental store
wants (§6.2). `probabilities` is notable — it carries spread-cover and total-over
probabilities per play, which is live closing-line context nothing else on the desk has.

### 4.4 CDN — `cdn.espn.com/core/nfl/playbyplay?xhr=1&gameId={id}`

604 KB, `max-age=182`. `gamepackageJSON` = drives, scoringPlays, winprobability, boxscore,
header. The same data as the summary, a different cache. No advantage over `summary` for
us.

### 4.5 FastCast — ESPN's own push channel

ESPN's gamecast page does not poll; it holds a websocket to FastCast. The handshake is public
and answered (measured: `GET https://fastcast.semfs.engsvc.go.com/public/websockethost` →
`{ip, token, port: 9571, securePort: 9573}`). The subscription protocol and topic names
are not documented anywhere I could find — two web searches turned up nothing — and would
have to be read off the browser's network tab during a live game. That is a reverse-engineering
project with no stability promise, on top of an endpoint family that already has none.
**Not recommended as a dependency**; worth one evening's look because it would turn our
latency floor from "ESPN's cache + our poll" into "ESPN's push", and it is the only route to
sub-10-second updates without paying for a feed.

### 4.6 What the odds side offers while a game is on

- **The Odds API `/odds`** includes in-play games — an event whose `commence_time` is past is
  live — at the same cost as pre-game: 1 credit per market per region. `/scores` adds live
  scores "approximately every 30 seconds" at 1 credit a call (2 with `daysFrom`). ESPN
  beats it on both latency and price for scores; its value is **in-play prices from four
  books**, which is the one thing a gamecast on *this* desk could show that ESPN's cannot.
  ADR 0010 deletes scans once a game starts; a live gamecast would need an in-play tier the
  retention policy does not yet have.
- **ESPN's own `odds`** (DraftKings, one book) is a reference only, per ADR 0011, and should
  be shown as "what the book says", muted, the way `PriceProvenance` already labels a close.

### 4.7 Paid push feeds, for the record

Sportradar and SportsDataIO sell NFL play-by-play with push delivery and a licence. They are
the "we are sure" option and the wrong one for a project that stays local and runs at $0
(DESIGN §0, going-public §10b). Named so the decision is visible, not as a recommendation.

### 4.8 The other sport we track

MLB's stats API, which `MlbStatsApiAdapter` already speaks for the spine, has
`api/v1.1/game/{gamePk}/feed/live` (the whole game state: linescore, current play, every
play with pitch-by-pitch) and `…/feed/live/diffPatch?startTimecode=` (changes since a
timecode), plus `…/feed/live/timestamps`. Official-ish, free, unauthenticated, and *built
for incremental polling*: a client holds the last timecode and asks for the diff. That is a
better live port for baseball than ESPN, and it maps onto the same store proposed in §6.2.
ESPN's MLB scoreboard also carries `situation` (balls, strikes, outs, runners, batter,
pitcher), so the §5 step covers both leagues at once.

## 5. The cheapest change that removes most of the complaint

Before any architecture: widen what the live poll keeps and tighten how often it runs.

1. **Parse what we already fetch.** Add to `CanonicalGame` and `Game`: `Period`,
   `DisplayClock`, `StatusDetail` (ESPN's `shortDetail`, e.g. "Q3 4:12", "Halftime",
   "Final/OT"), `PossessionTeamId`, `Down`, `Distance`, `YardLine`, `IsRedZone`,
   `HomeTimeouts`, `AwayTimeouts`, `LastPlay`, and a `Linescore` jsonb (per-period scores).
   Sport-specific fields (MLB's balls/strikes/outs/runners) go in a `Situation` jsonb rather
   than columns, the way `PlayerGameStat.StatLine` already does. One migration.
2. **Make the live pass its own job.** `espn:live` fetches the scoreboard for the league-days
   with a game in play and resolves *only events whose status is live or just-final* —
   not the whole slate — and skips the team-identity refresh and spine sync. `espn:slate`
   keeps the pre-game job it was built for. ADR 0012's rule holds: a job named, triggered
   by state, in the catalogue the Pull menu reads.
3. **Poll faster while anything is live, and only then.** `LivePoll` to **15 s** (ESPN's
   cache is 10 s; faster buys nothing), with the scheduler's tick dropping to match while a
   game is in play and returning to a minute when none is. Cost: one request per sport per
   15 s during games — 240/h for one league, well inside what ESPN's own pages do per
   visitor. `RequestDelay` stays.
4. **Address the notice.** `ChangeNotifier` payload becomes `topic` or `topic:id`
   (`games:4123`), and `PanelBase` gains an optional `WatchesIds`. The Game window then
   reloads for its game and ignores the other thirteen; the Board keeps watching the topic.
   Backwards compatible: a bare topic still matches everyone.
5. **Split the Game window's reload.** On a `games:{id}` notice, reload the *row* (one query)
   and re-render the title; reload bets, team form, rosters and the box score only on the
   topics that change them (`journal`, or `games` when the status flipped to Final). The box
   score while live is §6's job, not this step's.

Result: the title line reads `NE 17 – CHI 20 · Q3 4:12 · CHI ball, 2nd & 6 at NE 34 · Last:
D.Maye pass incomplete…`, moves within ~15–25 s of ESPN, and changing it costs one query and
a two-node diff. This step alone turns "delayed and blocky" into "a scoreboard that keeps
up". It touches: `EspnStatsAdapter`, `ISourceAdapter` (CanonicalGame), `EntityResolver`,
`Game` + migration, `IngestionJobs`, `IngestionScheduler`, `GamePassOptions`,
`ChangeNotifier`, `PanelBase`, `GamePanel`, and the `Scoreline`/`StatusLabel` helpers, plus
the `espn.scoreboard.json` fixture gaining a live event.

## 6. The gamecast proper

### 6.1 What it is, on this desk

A **Gamecast window**, opened from a Game window's keys (beside Movement and Head to head)
and from a Board row's actions while the game is live, following ADR 0020's anatomy:

- **Title:** the matchup and score, clock and period as tags, the situation line as `Detail`.
- **Field strip:** a flat 100-yard bar with the ball spot, line of gain (first-down marker),
  possession arrow and red-zone tint. Pure CSS/SVG from `YardLine`, `Distance`,
  `PossessionTeamId`; no JS. The one piece of the window that is new *kind* of primitive.
- **Drive log** (section): the current drive at the top — plays newest-first with clock,
  down & distance, text, yardage, a tag for score/turnover/penalty — then previous drives
  collapsed to their result line ("7 plays, 62 yds, 3:48 · TD"). Opening a drive expands it;
  one open at a time, like every other row on the desk (`RowActions`).
- **Win probability** (section): a `DeskChart` line over play sequence, with the spread-cover
  and total-over probabilities from `probabilities` as two more series the operator can
  toggle. Grounded in the market: the pre-game close from `ClosingLine` is drawn as a
  reference line, so "the market had CHI -3 and the model now has them covering 71%" is one
  glance.
- **Live market** (section, operator only): the four books' in-play prices from The Odds API
  when polling is on, beside ESPN's book muted as a reference. Each price opens Place wager
  on that side, as the Board's price cells already do (Door 2). Empty state names the switch
  that would fill it, as the Board's unpriced reason does.
- **Box score** (section): the live box score from the summary's `boxscore`, the same parser
  and grid the Game window uses for finals.
- **Scoring summary**: `scoringPlays`, one line each, as the compact view for someone who
  only wants to know how it got to 17–20.

Per sport the *field strip* and the *drive log* change — MLB has a diamond with runners,
balls-strikes-outs and an at-bat log; a half-inning is the drive. The title, chart, market,
box score and scoring summary are the same component with the same data shapes. That is
the "throughout the multiple sports we track" part: build the sport-neutral five once, and
the sport-specific two per league, with `EspnLeagues` already the place a league says what
it has.

### 6.2 The data model behind it

Plays are the unit, and they are append-mostly with stable provider ids. Store them that way:

```
game_play (id, game_id, source_id, source_play_id, sequence, period, clock_seconds,
           drive_seq, type, text, home_score, away_score, is_scoring, is_turnover,
           is_penalty, yardage, start_situation jsonb, end_situation jsonb,
           home_win_pct, spread_cover_pct_home, total_over_pct, wallclock, modified)
UNIQUE (game_id, source_id, source_play_id)
```

- `drive_seq` plus a `game_drive` table (or a derived view) gives the drive log; the MLB
  equivalent is `half_inning`.
- Win probability lives on the play that produced it rather than in its own table: the
  summary pairs them by `playId` and the core API by `$ref`.
- Upsert by `(game, source, source_play_id)`: ESPN *modifies* plays after the fact
  (`modified` is stamped; a review or a stat correction rewrites the text), so the writer
  compares and updates, which is `PlayerGameStat`'s pattern already.
- Retention: keep every play for the season. 180 plays × ~1 KB × ~270 NFL games is small;
  MLB at ~300 pitches a game × 2,400 games is a few hundred megabytes a season and is the
  one place to think about it. The game log readers get something they never had: a
  per-play record to build "how do they do on 3rd and long" from.

### 6.3 The live loop, per game

A `GamecastFeed` hosted service in the ingestion library, beside the scheduler, that:

1. Watches for games in play (the same predicate `LivePollIsDueAsync` uses).
2. For each, polls the **summary** every 15 s — or, cheaper, `status` + `situation` every
   15 s and `plays?limit=25&page=last` when the situation's `lastPlay.id` moves — and
   upserts new or modified plays. Summary first because it is one request; the core split
   is the optimisation if the summary's weight shows up in the logs.
3. Writes through `LineOpsDbContext` so `ChangeNotifier` announces `plays:{gameId}` for
   free, and the Gamecast window — watching exactly that — appends the new plays and moves
   the field strip. No full reload: `plays` since the last sequence the window holds.
4. Stops the moment the game is final, and runs the box-score parse once more so the
   final box is on the Game window without waiting for `espn:results`.

Which host runs it is the question already answered for the scheduler: the Worker in
Docker, the web host otherwise, never both (the `HostScheduler` switch and the memory note
about doubled credits apply as-is). Only ESPN is spent, and ESPN is unmetered. When the
operator has switched line polling on, the same loop asks The Odds API for in-play odds on
the games it is following — at the planner's cadence, not every 15 s, because those are
credits.

### 6.4 The window's rendering

Blazor Server can do this well if the component is written for it:

- **Append, do not reload.** The window holds `List<PlayRow>` and on a notice fetches
  `WHERE game_id = @g AND sequence > @last`, adds, and calls `StateHasChanged`. `@key` on
  every play row so the diff is an insert at the top, not a rewrite of the list.
- **Virtualize the drive log** (`<Virtualize>`) once it is 150 rows; ten drives collapsed
  keeps it under that in practice.
- **Let the clock run on the client.** Between polls the game clock should tick. A tiny JS
  interop in `windowing.js`'s style — `startClock(el, seconds, running)` — decrements a text
  node locally and is reset by each server render. Server-side `Timer` ticks across the
  circuit every second are exactly the pattern DESIGN §7 warns against for pointer moves.
  Baseball has no clock; the field keeps the strip static between pitches.
- **One circuit, many viewers.** Every open Gamecast on every circuit hears the same notice
  and runs the same append query. Cache the "plays since N" result in a short-lived
  singleton keyed by game for a few seconds so ten windows cost one query. Optional;
  measure first.
- **Minimised, the window still reports.** `Report(PulseState.Active, "Q3 4:12 · CHI ball")`
  on each append, so the rail chip is a mini-scoreboard — the Phase 3 promise, made specific.

## 7. Risks and the honest caveats

- **ESPN can change the shape without notice.** Everything in §4 is undocumented. The adapter
  must treat every field as optional (it already does) and the Gamecast must render with
  whatever subset arrived — a missing `situation` is "no situation", not a broken window.
  The fixtures gain a live event so the parser is tested on the shape we depend on.
- **Request volume.** §5 is one request per sport per 15 s. §6.3 is one more per *live game*
  per 15 s: thirteen NFL games at once is ~52 requests a minute, each up to 0.6 MB with the
  summary. ESPN serves far more than that to one busy browser tab, but a single IP doing it
  for hours is the profile that gets blocked. Mitigations in order: the core split (§6.3.2,
  ~1 KB a time for the common no-new-play case), following only games with an open Gamecast
  window plus any game with a journal entry riding on it, a hard ceiling in options, and the
  existing budget guard treating ESPN's request count as a soft budget.
- **In-play odds are credits.** On the free tier a 15 s in-play cadence is impossible (500
  credits a month); the planner's cadence applies and the window says how old the price is,
  as `PriceCell` already does.
- **MLB's live feed is a second port.** Cleaner than ESPN for baseball, but a second parser
  to keep alive. Decide when MLB's turn comes; the store does not care which port fed it.
- **The settle delay.** 1 s coalescing is right for the Board and wrong for a gamecast only
  if the feed delivers more than one notice a second, which a 15 s poll never will. Leave
  it.

## 8. Recommended order

1. **§5 in one sitting** — widen the parser and the `Game` row, split `espn:live` from
   `espn:slate`, 15 s cadence while live, addressed notices, the Game window reloading only
   its row. This is the fix for the complaint as stated. Verify on tomorrow's 1 pm slate:
   the title line should show the clock and situation and move within ~20 s of ESPN's own
   page, with the Board's pulse quiet except when a score changes.
2. **The play store and the feed (§6.2–6.3)**, NFL only, writing plays and probabilities
   with no window yet. Verify with the coverage queries' pattern: every final has a play
   count and a last play whose scores match the game's.
3. **The Gamecast window (§6.1, §6.4)** — title, field strip, drive log, win-probability
   chart, scoring summary, live box. Operator-only live market section when polling is on.
4. **MLB**: ESPN `situation` through §5 comes free; the diamond strip and at-bat log, and a
   decision on the MLB live feed as the play port.
5. **FastCast**, only as a spike with a time box, after the rest works on polling. If the
   protocol is readable it replaces the 15 s poll for the followed games and nothing above
   it changes, because the store and the window never knew how the plays arrived.

Each step is its own commit on `LineX_Development` and leaves the desk working without the
next. Nothing here needs a key or spends a credit until the operator switches in-play prices
on.

## Sources

Measured: ESPN scoreboard, summary, core `status`/`situation`/`plays`/`probabilities`/
`drives`/`odds`, CDN `playbyplay`, FastCast `websockethost`, all 24 Oct 2026 02:10–02:20 UTC.
Read: [The Odds API v4 guide](https://the-odds-api.com/liveapi/guides/v4/) (`/scores` cost
and cadence, in-play events in `/odds`, credit formula);
[Public-ESPN-API](https://github.com/pseudo-r/Public-ESPN-API) (core and CDN endpoint
catalogue; it documents no websocket); [pymlb-statsapi game
schemas](https://pymlb-statsapi.readthedocs.io/en/latest/schemas/game.html) (`feed/live`,
`diffPatch`, `timestamps`). Code: the files named inline, at `91ce20f`.
