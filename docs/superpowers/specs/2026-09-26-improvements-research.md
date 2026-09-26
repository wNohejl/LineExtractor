# Improvements after the six phases — research and what landed

**Date:** 2026-09-26
**Branch:** `LineX_Improvements`, from `LineX_Development` at `a427047`
**Method:** the snapshot of 2026-09-26 restored and measured read-only, a read of the odds,
settlement, board and history code, and ADRs 0010–0017. Every number below was measured today.

The six phases of the 2026-09-22 plan are done, and §12 of that plan names what they left. This
round looked for what would make the desk more *useful* rather than more complete, and found one
thing above the rest: **the board finds the best price and never says whether it is a good one.**
`OddsMath.NoVigProbability` existed and nothing called it; the Odds API adapter's own comment says
a DraftKings price is good relative to Pinnacle or it is not good, and nothing measured that. Most
of what landed follows from closing that gap.

---

## 1. What landed on the branch

| Commit | What |
|---|---|
| `fix(setup)` | A fresh clone's hosts can reach their database: `setup.ps1` writes the connection into both hosts' user-secrets, and the Worker has a Development launch profile. Before this the CLAUDE.md laptop bootstrap failed SCRAM on first boot, and `dotnet run --project src/LineOps.Worker` could not start on any machine. |
| `feat(odds)` | **Fair value.** `FairValue`/`FairMarket` pair each book's two sides on one number and de-margin them — Pinnacle's pair where it quotes, else the average of at least two books. Every offer carries EV, every book its hold. The price cell lights a +EV price; Every book shows hold and value per book; the wager form opens on the side with the most value. Pinnacle is in the committed book list (free: named books bill as one region). Also: unpriced rows no longer claim "no odds scan has run" when the league's scans were promoted away; a stale pull is named ("No lines pulled in 16 days"). |
| `feat(journal)` | **CLV at the fair close.** Settlement stores the closing market's no-vig chance of the side at the number taken (`FairClose` migration); `ClvResult.EvAtClose` values the price taken at it. Performance shows stake-weighted value at close beside ROI. |
| `chore(deps)` | Testcontainers 4.15 drops the high-severity SSH.NET; the AngleSharp advisory is suppressed in the test project with the reason (no patched version works with bUnit 1.x). Test warnings fixed. |
| `fix(odds)` | The Odds API writes totals "Over"/"Under"; everything else says "over". Switching totals on would have made every total from the feed invisible. Found by the adapter's first tests (12). |
| `feat(history)` | **Seasons table** in History — the seasons research §4.6 report: held against the league's schedule, finals, box scores, market vs reference closes. First reading: MLB 2026 holds 2,427 of 2,430. |
| `feat(board)` | **+EV filter** on the board, most value first. |
| `docs` | README host-side runs; DESIGN.md caught up. |

Tests: 505 + 296 = 801, from 746, all green. The Worker applied `FairClose` on start.

## 2. Findings that need a decision

### 2.1 Most "closing" lines are not closes (the biggest one)

A market close is the newest scan before first pitch (ADR 0010). Under manual polling that is
whenever someone last pressed Pull lines:

| Captured before the start | Games |
|---|---|
| under 1 hour | 8 |
| 1–6 hours | 24 |
| 6–24 hours | 54 |
| 1–3 days | 22 |
| over 3 days | 31 |

Of 139 market-closed games, 107 were closed more than six hours early — Chiefs–Broncos on 14 Sep
closed 5.4 days before kickoff. CLV measured against such a number, by price or by the new fair
close, measures the line's drift over days rather than the close. Settlement prefers the market
close over ESPN's single-book reference (ADR 0011) whatever its age, and **106 of those 107 games
also have ESPN's reference close, taken at first pitch.**

Options, cheapest first:

- **A. Age-aware choice (no credits).** Settlement and the board prefer the market close only when
  it was captured within N hours of the start (N = 1–3), and ESPN's first-pitch reference
  otherwise; the entry records which it used. A single-book close has no fair price, so fair CLV
  would read "no fair close" on those games rather than a stale one. Amends ADR 0011.
- **B. A pre-start pull.** One scan per league shortly before each slate's first start. MLB at 2
  credits × ~30 days ≈ 60 of the 500 monthly credits; later games on the slate still close early.
- **C. Automatic polling** (Phase 6 §4.2, priced in Ops) — the real fix, and the credit decision
  already on the table.

A is independent of B/C and worth doing now: the journal is empty, so it changes no number anyone
has read. Recommendation: A, then C when the credits allow.

### 2.2 bUnit 2

The AngleSharp mXSS advisory has no fix reachable from bUnit 1.40 (AngleSharp 1.5+ changes
`IHtmlCollection`'s indexer and bUnit 1.x fails at runtime against it). bUnit 2 renames
`TestContext`/`RenderComponent` across 296 tests. Test-only and unexploitable here; do it when
convenient.

### 2.3 Still open from the 2026-09-22 plan (unchanged)

Totals and automatic polling (§4.2); player props (§4.3); splitting players merged before
Phase 6; porting the desk changes to TicketMiser; `KpiDailies` computed and read by nothing;
`balldontlie` configured with no adapter.

## 3. Proposed next

1. §2.1 option A, with the settlement tests extended to an early market close.
2. An EV column on the board that sorts (the price cells are not sortable today), once live
   prices exist to verify it against.
3. Performance by CLV basis split by fair basis (Pinnacle vs consensus), once there are bets.

## 4. Testing this branch

```powershell
git fetch; git checkout LineX_Improvements
dotnet run --project src/LineOps.Web --launch-profile http   # applies FairClose on start
```

- **Fair value, without spending credits:** Ctrl+K → "chiefs broncos" → the 14 Sep game. Hover the
  moneyline: "Fair −136 (Pinnacle, no-vig)" and each book's value.
- **With a pull:** Board → Pull lines (costs credits). +EV prices light up in the cells; the "+EV"
  key narrows the slate; Place wager opens on the best-value side and shows its value.
- **Seasons:** open History → Seasons.
- **Settlement:** covered by `SettlementIntegrationTests` (no real bet needed).

The migration adds two nullable columns. A database opened by this branch works with
`LineX_Development`, which ignores them; `publish-data.ps1` from this branch would carry them into
the snapshot, so publish from `LineX_Development` until the branch is merged.
