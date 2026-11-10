# Desk flow and pull fixes — 2026-11-09

Nine operator reports from one evening on the desk, mapped to their causes and split into
work packages that touch disjoint files. The shared schema (migration
`20261109230151_RunDetailInjuriesAndClosingLineMisses`) is already added: `IngestionRun.Detail`,
`Player.InjuryStatus/InjuryNote/InjuryUpdatedAt`, and the `ClosingLineMisses` table.

Conventions for every package: tests first where the package names them; `dotnet build` clean;
no new warnings; the Desk's rules (`apple-mudblazor` skill: Desk primitives, no raw MudBlazor in
panels, no new CSS classes outside the Desk); copy in the repository's voice; no `Co-Authored-By`
trailers; nothing committed by the package agents — the session commits once at the end.

## Findings

1. **Slip tab when empty.** `DeskTabs` has no disabled state; `Available=false` hides the tab,
   and with two tabs that collapses the whole bar. The user's own alternative — a Place wager
   button embedded in the empty state — is the right one. `EmptyState` already takes an `Action`
   fragment (precedent: `PerformancePanel.razor:24-40`). The slip shows in three places:
   Journal's Slip tab, Place wager's Slip tab, and the standalone Slip window.
2. **"Backport" and closing lines.** `GamecastBackport` writes plays/drives only, never odds.
   Closing lines come from `espn:closing-lines`, which writes only ESPN's `close` blocks and
   fabricates nothing (ADR 0011). That holds. What is wrong is *how it reports*: see 4.
3. **QB chip.** The Board derives the NFL starter from the last stored box score
   (`Starters.LastQuarterback`), and nothing reads injuries. Murray started the Vikings' last
   game on Nov 1; ESPN's league injuries endpoint
   (`site.api.espn.com/apis/site/v2/sports/football/nfl/injuries`, one request for the league)
   says Murray `Out` ("ruled out for Monday's game versus the Bills") and Wentz `Active`
   ("slated to start"). The depth-chart endpoint still lists Murray QB1, so it is not the signal.
4. **523 requests, Success in 340 ms.** One press of "Closing lines owed" runs one
   `RefillClosingLinesAsync` per configured sport, each writing its own run row: MLB (1 request,
   191 ms), NFL (2, 340 ms), UFC (520 requests, 2m16s). All three said Success, 0 rows, empty
   Detail. "Owed" means "Final with no closing *total*", so 520 UFC bouts (fights have no total)
   are re-asked on every press and never answered. The per-sport sentence exists only in the
   toast, and the Rows==0 toast drops it ("nothing new to store").
5. **Wizard loses the bet after Add to slip.** `AddToSlip` zeroes the stake and marks the window
   clean, so the Bet step folds and the stake empties; a clean window can also be evicted and
   reopened empty. Selection is otherwise kept.
6. **Payout 1,020 vs 1,021.74.** Every price is `int` end to end (`PriceTaken integer`,
   `WagerOffer.PriceAmerican`, `DeskField TValue="int?"`): "+104.35" cannot be typed (the field
   keeps 104 silently) or stored, and all money shows as `C0`. 500 × 2.04348 = 1,021.74.
7. **Wizard after Log.** A full reset happens only when the user presses "New bet".
8. **Toasts.** `DeskHost` pins the snackbar `BottomRight`; no CSS positions it; `#blazor-error-ui`
   is pinned to the same corner; `DeskWiringTests` asserts BottomRight.
9. **Journal order.** `JournalService.EntriesAsync/ParlaysAsync` order by `PlacedAt` and take 100
   *before* the client sort; the grid's `Placed` column has `InitialDirection Descending`.

## Packages (parallel, disjoint files)

### W — Wager wizard flow (`BoardWager.razor`, `SlipPanel.razor`, `WagerSlip.cs`, their tests)
- After **Add to slip** / **Add as leg**: keep side, price, book and stake exactly as left; the
  window stays marked as having input (no eviction). The review step shows a quiet "On the slip"
  tag (existing `_banked`) until the user changes something; pressing Add to slip again with a
  changed stake or price adds a second item. Update
  `Add_to_slip_counts_on_the_slip_tab_and_clears_the_stake` to the new rule and add a test that
  goes Wager → Slip → Wager and finds the selection, price and stake intact.
- After a successful **Log**: the wizard resets itself to step 1 (what `NewBetAsync` does, game
  kept), and the receipt becomes a compact line above the steps with "Same bet again" and
  "View in journal"; picking a side dismisses it. Test it.
- `WagerSlip.Logged(ids)` also clears `ParlayStake`, `ParlayPriceQuoted` and `ParlayBook`
  (today a logged parlay's +441 carries into the next one). Test it.
- Parlay footer: show the legs' decimals to 3 places and the exact product, e.g.
  `1.111 × 1.333 × 1.286 = 1.905, which is -111`.
- **Slip empty state** (`SlipPanel.razor:59-65`): `EmptyState` gains an `Action` button. New
  `[Parameter] EventCallback? OnPickBet` on `SlipPanel`: when the host sets it (Place wager
  window), the button reads "Pick a bet" and switches to the Wager tab; otherwise (Journal tab,
  standalone Slip window) it reads "Place wager" and opens `WindowCatalog.Wager` with no subject
  (the game search) — hidden when `!Access.CanOperate`. Keep the existing sentence; tests in
  `SlipPanelTests` pin it. Add tests for both button modes.
- Do **not** change price types in this package (that is package P, after the merge).

### J — Journal order (`JournalPanel.razor`, `JournalService.cs`, their tests)
- Entries sort by the game's start (`Game.StartsAt`), falling back to `PlacedAt` for untracked
  bets; parlays by the latest leg's game start, falling back to `PlacedAt`. Apply it on the
  server (before `Take`) and as the grid's initial sort.
- Column: title `Game` (the start, via `Starts(...)`), with the placed time as the dim second
  line; the `Placed within` filter keeps its meaning. `OnGameAsync` keeps PlacedAt order.
- Tests: an entry for a later game placed earlier sorts first; a parlay sorts by its last leg.

### T — Toasts top-right (`DeskHost.cs`, `mud-bridge.css`, `desk.css`, `DeskWiringTests`)
- `PositionClass = TopRight`; a `mud-bridge.css` rule offsets `.mud-snackbar-location-top-right`
  below the header (`calc(var(--titlebar-h) + var(--space-4) + var(--space-3))`, with the
  narrow-screen header in mind) and right of nothing (the rail is on the left). Keep newest on
  top. Move `#blazor-error-ui` to the same corner and fix both comments. Update the test.

### R — Closing-line runs that say what they did (`StatsIngestionService.cs`,
`IngestionJobs.cs` region `RunClosingLinesAsync`/`OwedClosesAsync`, `GamecastBackport.cs`,
`RunsPanel.razor`, `IngestionDeskJobs.cs`, tests)
- `ClosingLinesOwed(sport)`: a fight is owed its **moneyline**, a game its total
  (`FightSports.IsFight`).
- `ClosingLineMisses`: after a game is asked and ESPN returns no line, upsert a miss
  (`Attempts++`, `RetryAt = now + 14 days`, doubling to a cap of 90 days). A game whose miss is
  still ahead is not asked; the row is deleted when a close is written. The menu estimate
  (`OwedClosesAsync`) counts only games that will be asked; the menu label can say
  `523 without a close · 3 to ask` (keep the `EstimatedRequests` honest).
- Each run row gets `Detail`: `NFL: 2 asked, 0 answered; 2 set aside until Nov 23.` The
  job's `JobOutcome` detail stays the roll-up. `GamecastBackport` writes its own detail sentence
  onto its run row too.
- `RunsPanel` Detail column shows `Detail`, and the Error (when any) after it; a failed run's
  error keeps its tone.
- The Rows==0 toast carries the roll-up detail instead of "nothing new to store".
- Tests: owed-by-market per sport; a miss is written, skipped, retried, cleared; the run's
  Detail text; the toast text.

### Q — Injuries and the starter (`EspnStatsAdapter.Injuries.cs` new, `InjuryReportService.cs`
new, `IngestionJobs.cs` region `SyncSpineAsync` only, `Starters.cs`, `BoardService.cs`,
`BoardPanel.razor` starter title, `IngestionServiceCollectionExtensions.cs`, tests)
- Adapter: `FetchInjuriesAsync(sport)` reads the league injuries endpoint (one request):
  per athlete id → `displayName`, `status`, `shortComment`/`longComment`, team. NFL only for now
  (`EspnLeagues` says where it lives); fixture from a real response.
- Service: matches athletes to `Players` by ESPN external id (fallback: sport + full name +
  team), writes `InjuryStatus/InjuryNote/InjuryUpdatedAt`; clears a status for players no
  longer on the report (set to null, keep `InjuryUpdatedAt`). Runs inside `espn:slate` beside
  the MLB spine, throttled to once per 30 minutes, on its own run row (`espn:injuries`).
- `Starters`: `LastQuarterback` becomes `Quarterback(lines, injuries, teamId, before)` →
  `(Name, Basis)`: the last game's passer unless his status is Out / Injured Reserve /
  Doubtful, in which case the club's most recent *other* passer with attempts this season who is
  not out (Wentz), with basis text; none → `—` with the injury as the title. The Board title
  says which: "Murray is out (hamstring); Wentz started the last game the club played without him" or
  "Started at quarterback in the club's last game". Tests for both.

### P — Price precision (after W merges; `OddsMath`, `WagerOffer`, `WagerSlip`, `WagerPrefill`,
`StakeRow`, `BoardWager`, `SlipPanel`, `JournalEntry.PriceTaken`, `Parlay.PriceQuoted`,
`EntryDraft`, settlement/CLV readers, one migration)
- `PriceTaken` and `PriceQuoted` become `numeric(11,4)`; `ClosingPrice` stays int. `OddsMath`
  gains decimal overloads; `ToDecimal(104.35m) = 2.0435`. Four places, not two: the book's
  $1,021.74 on $500 is decimal 2.04348, i.e. +104.348, which a two-place price rounds to
  +104.35 and pays $1,021.75. Display rounds to two places; the stored price keeps four.
- The typed Price field accepts `+104.35`, `-104.35` and decimal odds `2.0435` (a value between
  1 and 99 is read as decimal odds and shown back as American to two places). Both wizard
  fields (price, book's parlay price) and the slip's closing-card field.
- Money shows cents when it has them: `$521.74 · $1,021.74 back with the stake`; whole dollars
  stay `$520`.
- Tests: 500 at +104.35 → 1,021.74; decimal-odds entry; a journal row round-trips 104.35.

## Checklist (verifier)

- [ ] Empty Slip tab (Journal, Place wager, Slip window) shows a button; it opens Place wager or
      switches to the Wager tab; hidden for a non-operator.
- [ ] Add to slip keeps side, price, book and stake; Wager → Slip → Wager shows them intact.
- [ ] After Log the wizard is on step 1 with the receipt line; a second bet logs without "New bet".
- [ ] Logged parlay does not leak its book price/stake into the next.
- [ ] 500 at decimal odds 2.04348 shows 521.74 to win and 1,021.74 back; 500 at +104.35 shows
      1,021.75; the journal stores the price to four places and shows it to two.
- [ ] Toasts top-right, under the header, not over the Reopen band; error UI same corner.
- [ ] Journal ordered by game start desc (parlay: last leg), server and grid.
- [ ] Closing-lines press: three run rows each with Detail; UFC owed by moneyline; second press
      asks only games outside their retry window; toast says the roll-up.
- [ ] Vikings–Bills shows QB Wentz with Murray's injury in the title after an injuries sync.
- [ ] `dotnet build` clean (Web + both test projects), `dotnet test` green, Worker host starts,
      `dotnet ef migrations has-pending-model-changes` clean.
