# The slip and the advisory checks — design

**Date:** 2026-09-26
**Status:** Design, ready for a plan. Written against `LineX_Improvements` at `d487e41`.
**Source:** the design study at https://claude.ai/artifact/JXkL49ZY8UwLgm85eTAWPy (five artboards:
the wager window as built, the proposed window, the slip, the doors, and a pattern map), which
took the package wizard review ("The Wizard's Circuit") and asked which of its patterns the
Place wager window should adopt. This document is the answer in a form a plan can be written
from. File:line references are to `d487e41`.

The one-sentence version: **the wager window keeps its single screen, gains a strip of advisory
checks that run before the key is pressed, and learns to bank a wager onto a slip that belongs to
the desk session — which is where parlays get logged from now on.**

---

## 1. Why these two, and not the rest of the wizard

The wizard's patterns were paid for by a page with a queue of drafts, three steps, heavy cards
and live prechecks. The wager window shares two of those forces and none of the others:

| Wizard force | Wager window has it? | So |
|---|---|---|
| Several things created in one sitting, from several doors | **Yes** — an operator prices a slate and logs more than one bet | The queue becomes the slip (§3) |
| Answers worth having before the button | **Yes** — a duplicate, a stale price, a closing line, the value | The precheck becomes the advisory strip (§4) |
| Several steps with different unknowns | No — a wager has one unknown, the stake | No step rail; the doors decide where the window opens (§5) |
| Heavy components that must not re-render | No — six chips and four tiles | No memo keys, no `ShouldRender` |

Two more findings from the study drive the scope:

- **Windows do not tell each other about writes.** The next-phases spec (§2.3) recorded that a
  wager logged in `BoardWager` does not reach an open Journal. `BoardWager.razor` is a plain
  `ComponentBase`; it does not inherit `PanelBase`, so it neither watches nor is watched.
- **Parlays are logged from a separate form** in `JournalPanel.razor:100-145` that duplicates the
  leg fields, the stake row and the book picker. The slip is the natural home for a multi-leg
  bet, so that form retires into it.

---

## 2. Scope

**In:**

1. `WagerSlip` — a Blazor-free, circuit-scoped class holding the wagers an operator has banked
   this session, and the rules that govern them (§3.1).
2. A **Slip window** (`WindowCatalog.Slip`) that lists the slip, switches it between straight
   bets and one parlay, and logs it (§3.2–3.4).
3. **Advisory checks** on the wager window: value, price age, duplicate, closing line — run
   when the selection changes, never when the stake does (§4).
4. **Add to slip** on the wager window, beside Log (§3.3).
5. **Doors:** the wager window accepts a market, side and book so a price cell and an Every
   book rung can open it on the right side (§5). The Board row door is unchanged.
6. `BoardWager` inherits `PanelBase` and watches the odds and journal topics (§6).
7. The Journal's **Log parlay** form retires; **Log entry** stays as the manual door (§5.4).
8. Two shared primitives, `SelectionChips` and `StakeRow`, so the selection and the stake row
   have one definition each (§7).
9. Tests as listed in §9, and a dev-only seed of test bets for exercising the UI (§10).

**Out, deliberately:**

- Persisting the slip. It is circuit memory, like the wizard's drafts: a reload loses an
  unlogged slip, and a slip takes seconds to rebuild. Revisit if that ever costs a real bet.
- A "your bets on this game" link from the Game window (next-phases Phase 4 item 3).
- Editing a wager's price on the wager window. The window logs the price the board shows; a
  price the board no longer shows is logged from the Journal's manual door (§5.4).
- Bankroll-relative stake advice. Performance owns the bankroll; the wager window does not.

---

## 3. The slip

### 3.1 `WagerSlip` — the class

`src/LineOps.Web/Services/WagerSlip.cs`, registered `AddScoped` beside `IDeskSearch` in
`Program.cs:61`. Scoped is circuit-scoped under Interactive Server, which is the lifetime the
wizard's queue has and the one the slip wants: one slip per operator session, shared by every
window on that desk, gone with the circuit.

No Blazor types. It raises a plain `event Action? Changed` that the Slip window and the Add to
slip badge subscribe to. Everything the wizard's queue guards did by scanning source, this class
does by being the only owner of the list.

```csharp
public sealed class WagerSlip
{
    public IReadOnlyList<SlipItem> Items { get; }
    public SlipMode Mode { get; }              // Straight | Parlay
    public decimal ParlayStake { get; }
    public int? ParlayPriceQuoted { get; }
    public string? ParlayBook { get; }

    public event Action? Changed;

    public SlipItem Add(SlipItem item);        // returns the item as stored (assigned id)
    public void Remove(Guid id);               // the one removal path
    public void Clear();
    public void SetMode(SlipMode mode);
    public void SetParlay(decimal stake, int? priceQuoted, string? book);
    public void Touch(Guid id, Action<SlipItem> edit); // stake or note, raises Changed
}

public sealed record SlipItem(
    Guid Id,
    int GameId, string Matchup, DateTimeOffset StartsAt, string SportKey,
    string Market, string Outcome, decimal? Line,
    string Book, int PriceAmerican, DateTimeOffset CapturedAt, bool IsClosing,
    decimal Stake, string? Note,
    FairPrice? Fair, double? Ev);
```

Rules the class owns, with a test each (§9):

- **A closing-line item cannot be logged.** `WagerSlip.CanLog(item)` is false when `IsClosing`.
  The item stays on the slip so the operator sees why; it is dropped with the same × as any
  other or logged from the manual door with the price actually taken.
- **A straight bet needs a stake.** In `Straight` mode `CanLog` also requires `Stake > 0`; a
  zero-stake card wears the warning tag *Needs a stake* and an inline stake field (§3.2). In
  `Parlay` mode legs carry no stake, so only the closing rule applies.
- **Parlay legs are the live items, one per game.** `Legs` is the first non-closing item for each
  game. Two items on one game are correlated (or the same bet twice); no book prices that as a
  parlay and settlement grades legs as independent. The others wear **Not a leg** with the
  remedy *Drop one, or log them as straight bets.* A parlay needs two legs.
- **Combined price** is the product of the legs' decimal prices, converted back to American,
  unless `ParlayPriceQuoted` is set. `OddsMath` has no such helper today; add
  `OddsMath.CombinedAmerican(IEnumerable<int>)` and point `JournalPanel`'s remaining use and
  `SettlementService`'s reprice-after-push at it, so the three agree.
- **Duplicates are allowed on the slip.** Two items on the same side are two bets; the advisory
  strip says so (§4), the slip does not refuse.

### 3.2 The Slip window

`WindowCatalog.Slip`, key `slip`, title **Slip**, group `Data`, icon `Icons.Material.Filled.Receipt`,
`Singleton = true`, `DefaultWeight = 0.9`, `MinWidth = 380`, `RequiresSubject = false`. Hosted
by `SlipPanel.razor` in `Components/Panels`, which inherits `PanelBase` and subscribes to
`WagerSlip.Changed` (unsubscribed in `OnDispose`).

Layout, top to bottom, as drawn on the study's third artboard:

1. **Header row:** a `DeskSwitch` labelled *Logged as* with **Straight bets** / **One parlay**;
   the sentence "Belongs to this desk session, not to a game." in secondary text.
2. **Cards**, one per item, newest last. Each card: selection in one line (`ML · Chiefs −128`),
   matchup, date and book beneath in secondary; the stake right-aligned in the numeric face,
   editable in place through `Touch` (hidden in parlay mode, where legs carry no stake); a ×
   that calls `Remove`. Below, a `Tag` and one sentence: **Ready** (positive) with the value
   reading; **Needs a stake** (warning) at zero stake in straight mode; **Worth knowing**
   (warning) for a duplicate; **Cannot log** (negative) for a closing line, the card wearing a 1px
   `--state-negative` border and a second line naming the remedy: *Drop it, or log it from the
   Journal with the price you got.* In parlay mode a closing-line card says *Not a leg: the
   game has started.*
3. **Parlay block** (parlay mode only): *Combined* price, the note "N legs, prices multiplied;
   a book's own parlay price can replace it", a stake field, a parlay-price field
   (`PriceQuoted`), the `BookPicker`, and Returns.
4. **Report block** (after a run): a positive `Note` with the headline *N logged · M left on
   the slip* and a **Show steps** link that expands one line per item, in the order they ran.
   The headline also goes to `DeskToasts.Success`.
5. **Footer:** `Start over` (Plain, confirms through `DeskDialog` when the slip holds more than
   one item, matching the wizard's "last package standing is Start over, which confirms") on the
   left; on the right the window's one Filled key: **Log N ready · $total** in straight mode,
   **Log parlay · +price** in parlay mode. Disabled with the reason under it: *Nothing on the
   slip can be logged as it stands* / *A parlay needs two legs the market still offers* /
   *Needs a stake above zero.*
6. The scope sentence from the Journal, once: *A logged wager becomes a journal row and leaves
   the slip. LineOps records bets placed elsewhere; nothing here reaches a sportsbook.*

Empty state: `EmptyState Kind="New"` — *Nothing on the slip. Add to slip from any Place wager
window banks the wager here.*

### 3.3 Add to slip

On the wager window, a Plain key **Add to slip** with a count badge sits left of Log. It builds
a `SlipItem` from the chosen offer, the stake and the note, calls `WagerSlip.Add`, shows the
same one-line confirmation the Log path shows (*Added $50 on ML · Chiefs at −128 to the slip.*)
and clears the stake. It does **not** open the Slip window: the wizard's "Add another" hands
the user back to the door they came from, and so does this. The badge count is live through
`WagerSlip.Changed`. Zero stake is allowed on the slip — the stake can be set on the card —
but Log on the window still needs one.

### 3.4 Logging the slip

`SlipPanel.LogAsync` opens one `LineOpsDbContext`, and in one transaction:

- **Straight mode:** one `JournalEntry` per loggable item, exactly as `BoardWager.PlaceAsync`
  writes one today (`BoardWager.razor:196-214`), `Result = Pending`, `PlacedAt = now`.
- **Parlay mode:** one `Parlay` row with `Stake`, `PriceQuoted`, `Book`, `Note`, and one
  `JournalEntry` leg per loggable item with `Stake = 0` and `ParlayGroupId` set — the shape
  `Journal.cs` documents and `JournalPanel.SaveParlayAsync` writes today.

After `SaveChangesAsync`: remove the logged items from the slip (closing-line items stay),
build the report, publish `DataTopics.Journal` through the same in-circuit path the Journal's
own writes use, and raise `OnPlaced`-equivalent signals. A failure leaves the slip intact and
shows *Not logged: {message}* as a warning `Note`, as the wager window does.

The panel's one Filled key is Log; Start over is Plain; × is Plain with `Role.Destructive`.

---

## 4. The advisory checks

### 4.1 What they are

A list of `Check` records rendered as a bordered list titled **Before you log**, between the
offer and the stake row. Each has a state (`Positive`, `Warning`, `Neutral`, `Negative`), one
sentence, and optionally a remedy key on the same row. **None of them disables Log** except the
closing line, which is not a check on the operator's judgment but a fact about the price.

```csharp
public sealed record Check(DeskState State, string Text, string? Remedy = null, Func<Task>? Act = null);
```

| Check | State and sentence | Remedy on the row | Keyed on |
|---|---|---|---|
| **Value** | Positive: *Value: +2.7% against fair −136 (Pinnacle, no-vig).* Warning: *No value: −7.6% against fair +136 (…).* Neutral when `Fair` is null: *No fair price on this number: fewer than two books quote it, so value is unread rather than zero.* (ADR 0003) | — | selection |
| **Price age** | Positive under 30 min: *Price captured 12 min ago.* Warning at or over 30 min: *Price captured 2h 14m ago; the line may have moved since.* | **Pull {LEAGUE} lines · 2 credits**, which runs the same job the Board row's pull action runs (`BoardPanel.razor:836`) and reloads the row on completion | selection, odds topic |
| **Duplicate** | Warning: *Already in the journal: $50 on ML · Chiefs at −130 (DraftKings), 11:02 today. Logging again records a second bet.* Positive: *Nothing logged on this side yet.* | **Open Journal** → `Manager.Open(WindowCatalog.Journal)` | selection |
| **Closing line** | Negative, replaces the price-age row when `Offer.IsClosing`: *Closing line: the game started 42 min ago, so this price cannot have been taken.* Log is disabled with *Log it from the Journal with the price you got* under the key; Add to slip still works and the card carries the same state. | **Open Journal** | selection |

The 30-minute threshold is a constant on the check, not configuration; it is the same order as
`PanelBase.Age`'s "just now" band and can move when someone has a reason.

### 4.2 When they run

Following the wizard's staleness rule, each check has its own key and re-runs only when its key
moves:

- **Side key** `(GameId, Market, Outcome)`, at any book: the duplicate check is one query — the
  newest straight `JournalEntry` on that side placed within the last 7 days. Not keyed on the
  book: a second bet on the Bills is the same exposure at DraftKings or Pinnacle, and the
  sentence names the book the earlier one was at (changed after the first desk walkthrough,
  where a Pinnacle price reported "nothing logged" over two DraftKings bets). Value, price age
  and closing line derive from the `BestOffer` already in hand and cost nothing. Add to slip
  carries the reading onto the card (`SlipItem.InJournal`), so the slip says it too without a
  query per card.
- **Odds topic:** when `DataTopics.Odds` fires and the panel reloads its `BoardRow`, price age
  and value recompute from the new offer, and the window says in a positive `Note` *Price moved
  since you opened this: −128 → −122 (DraftKings)* rather than swapping the number silently.
- **Journal topic:** when `DataTopics.Journal` fires, the duplicate check re-runs.
- **The stake** moves nothing but Returns and the *expected on $N* suffix of the value sentence,
  which is why the stake field stays `Immediate`.

The duplicate query runs inside the panel's `WithActivityAsync` wrapper so the title-bar pulse
shows it, and its result is a field the render reads — never a query from the render path
(the wizard's "never schedule work from a render").

### 4.3 Where they render

The offer block above the strip replaces today's four-tile `MetricRow`: the price large and
accent, the book, *best of N books*, and the capture age with a clock glyph on one line; the
value reading on the second. Returns moves to the right of the stake row with its profit note,
which is where the eye goes after typing. The four numbers are all still on screen; they are no
longer four equal tiles.

---

## 5. The doors

### 5.1 Parameters

`BoardWager` gains three optional parameters beside `GameId`:

```csharp
[Parameter] public string? Market { get; set; }   // Markets.Moneyline | Spread | Total
[Parameter] public string? Outcome { get; set; }
[Parameter] public string? Book { get; set; }
```

`OnParametersSetAsync` chooses the side as today when `Market` and `Outcome` are null (most
value, then widest edge). When both are given it chooses that side; when `Book` is also given
and that book is among the offer's `Rungs`, the offer shown is that rung — its price, its
capture time, its `Ev` — rather than the best. The chip strip still lists every side, so a door
that lands on the wrong one costs a click.

### 5.2 Door 1 — the Board row (unchanged)

`RowAction "wager"` in `BoardPanel.ActionsFor` keeps launching with `GameId` alone.

### 5.3 Door 2 — a price cell

`PriceCell` gains an `OnTake` callback rendered as the cell's click when the offer is live (not
`IsClosing`). `BoardPanel` wires it to `Launch(WindowCatalog.Wager, …)` with `GameId`, `Market`,
`Outcome` and `Book`. The cell's existing tooltip gains one line: *Click to log this price.*
`BoardAllBets` (Every book) gets the same on each rung, which is Door 3, passing that rung's book.

### 5.4 Door 4 — the Journal's manual entry

`JournalPanel`'s **Log entry** form stays, unchanged in fields, because it is the only way to
log a bet the board never priced (a future, a prop, a price the board no longer shows). Its
**Log parlay** form and the `ParlayForm` class are removed; the key becomes **Open slip**, which
opens `WindowCatalog.Slip`. The empty-state copy on the Journal names the slip for parlays.

The manual entry gains the duplicate check only, once a game and side are chosen, rendered with
the same `Check` row. Value and price age need a live offer the manual form does not have.

---

## 6. The wager window becomes a panel

`BoardWager` changes `@inherits` to `PanelBase` and declares:

```csharp
protected override IReadOnlySet<string> Watches { get; } =
    new HashSet<string> { DataTopics.Odds, DataTopics.Journal };

protected override Task RefreshAsync() => ReloadAsync();   // row, then checks (§4.2)
```

This is the fix for next-phases §2.3 on this window: a pull re-prices an open form, a settle
re-runs the duplicate check. `OnPlaced` stays for the Board's own refresh.

The loading state changes with it: while `GetRowAsync` is in flight the body shows a
`DeskSkeleton` of the chip strip and the offer block, not the *Pick a side to price it* empty
state. Empty and unread are different facts (the wizard's field note; ADR 0003).

---

## 7. Shared primitives

Two components in `Components/Desk`, beside `PriceCell`:

- **`SelectionChips`** — the six-chip strip: takes the `BoardRow`, the chosen `(Market, Outcome)`,
  and `OnChoose`. Tinted for the chosen side, Plain otherwise, as `BoardWager.razor:22-33` draws
  it today.
- **`StakeRow`** — the stake field (`Immediate`, `Mono`, `type=number step=5`), the three quick
  keys, and a Returns slot; takes `Stake`, `StakeChanged`, `PriceAmerican`. Used by the wager
  window, the slip's parlay block, and the Journal's manual entry, which drops its own stake
  field.

A guard test asserts each has one definition and that no panel under `Components/Panels`
declares its own `placeholder="Stake"` field — the wizard's "one control, one call site",
enforced the way `GridConventionTests` enforces grid rules today.

---

## 8. Copy and emphasis rules, restated for this feature

- One Filled key per window: **Log** on the wager window, **Log** on the slip. Add to slip and
  Start over are Plain; × is Plain and Destructive.
- A disabled key says what it is waiting for, under the key, in warning text.
- Check sentences state a fact and, where there is one, the consequence: *Logging again records
  a second bet.* They never instruct.
- The scope sentence (LineOps records bets placed elsewhere) appears once per window, at the
  bottom, as the Journal does it.

---

## 9. Tests

`tests/LineOps.Tests`:

- `WagerSlipTests` — add/remove/clear raise `Changed` once each; `Remove` of an unknown id is a
  no-op; `CanLog` false on a closing item; `Legs` excludes it; combined price for two known legs
  (−128 × −105 → +248) and with `PriceQuoted` overriding; `SetMode` keeps items.
- `AdvisoryChecksTests` — the pure check builder (a static in `Components/Desk/Checks.cs`) given
  an offer, a clock and a duplicate result: the four rows and their states at the threshold
  boundaries (29 vs 30 minutes; null fair; closing).

`tests/LineOps.Web.Tests` (bUnit, on `DeskTestContext`):

- `BoardWagerTests` — lands on the most-value side with `GameId` alone; lands on the given side
  with `Market`+`Outcome`; shows the rung's price with `Book`; Log disabled at zero stake with
  the reason; closing offer disables Log and keeps Add to slip; Add to slip increments the badge
  and clears the stake; a `DataTopics.Odds` signal reloads and shows the *Price moved* note.
- `SlipPanelTests` — empty state; three seeded items render three cards with the right tags and
  border; × removes; parlay switch hides stakes and shows the combined price; Log with one
  closing item logs two, leaves one, shows the headline and steps.
- `PriceCellTests` — a live cell exposes the take action; a closing cell does not.
- `JournalPanelTests` — Log parlay is gone; Open slip opens the Slip window.
- `SelectionGuardTests` — the §7 guard.

`tests/LineOps.Tests` (Testcontainers):

- `SlipLoggingIntegrationTests` — straight mode writes N entries; parlay mode writes one
  `Parlay` and N legs with zero stake and the group id; a failed save leaves the slip intact.

Verification before "done" is the CLAUDE.md list: both builds clean, `dotnet test` green, the
Worker host starts.

---

## 10. Test bets for exercising the UI

The journal is empty on every machine (next-phases §1), so the duplicate check, the report block
and the parlay grid have nothing to render against. `scripts/seed-test-bets.sql` inserts a small
set of journal rows the desk can be exercised on, chosen from games the database already holds:

- two pending straight bets on the next scheduled NFL game, same side, one hour apart, to make
  the duplicate check fire on that side and read as *two bets* on the slip;
- one settled bet (Win) on a Final game, so the Journal grid and Performance have one graded row;
- one pending parlay of two legs across two scheduled games, so the parlay grid renders.

Every row carries `Note = 'test bet (seed-test-bets.sql)'`, and the script's first statement
deletes rows carrying that note, so it is idempotent and reversible in one run. It is run by
hand with `psql`, never by the application, and never from a machine about to publish the data
snapshot (`publish-data.ps1`), because a test bet in the snapshot is a test bet on the laptop.

---

## 11. Decisions made here, and why

These were the study's open questions; they are settled in this document so the plan needs no
further input. Each can be reversed cheaply because the slip's rules live in one class.

1. **The slip is circuit memory, not a table.** Same trade the wizard made for its drafts. A
   reload loses seconds of work; a table would need reconciliation on every reopen.
2. **The closing line blocks; everything else advises.** A closing line is a price the operator
   cannot have taken from this window, so logging it here would be recording a bet that was not
   made at that number. Every other check is a fact the operator may reasonably act against.
3. **Add to slip does not open the slip.** The operator is pricing a slate; the Board is where
   they are working from (ADR 0013). The badge count is the acknowledgment.
4. **The parlay form retires now, not later.** Keeping two ways to log a parlay for a release
   would be the wizard's "one control, three call sites" mistake in advance.
5. **30 minutes is "stale".** A line scan costs credits and is run by hand; half an hour is the
   shortest interval at which telling the operator to spend two credits is reasonable.
6. **Duplicate window is 7 days.** Long enough to cover a bet placed early in the week on a
   Sunday game; short enough that last season's bet on the same fixture is not a "duplicate".
