# The slip and the advisory checks — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Place wager window keeps its single screen, gains a strip of advisory checks that run before Log is pressed, and can bank a wager onto a circuit-scoped slip; a new Slip window logs the slip as straight bets or one parlay, and the Journal's Log parlay form retires into it.

**Architecture:** Blazor-free rules live in plain classes (`WagerSlip`, `WagerOffer`, `SelectionChoice`, `Checks`) that are unit-tested without rendering. The windows (`BoardWager`, new `SlipPanel`) inherit `PanelBase`, read and write through one small data seam (`IWagerData`, production implementation over `BoardService`, `JournalService` and `IngestionJobs`) so bUnit can render them against a fake, and share two new components (`SelectionChips`, `StakeRow`) plus a `CheckList`. Writes go through `JournalService`, whose single `SaveChangesAsync` per log is atomic and is announced to every window by the existing `ChangeNotifier` interceptor.

**Tech Stack:** .NET 10, Blazor Interactive Server, MudBlazor 9.7 behind the `LineOps.Desk` primitives, EF Core 10 + Npgsql, xunit 2.9, bUnit 1.40, Testcontainers.PostgreSql 4.15.

**Spec:** `docs/superpowers/specs/2026-09-26-slip-and-advisory-checks-design.md` (read it before starting; section numbers below refer to it).

## Global Constraints

- Two leagues only (MLB, NFL); no league, provider or configuration change is part of this work.
- Branch: work on `LineX_Improvements`, which is deliberate. Do not switch branches, merge into `LineX_Development` or `main`, delete the branch, or push without the operator saying so. Do not run `publish-data.ps1` from this branch.
- Commits are authored by the developer's own git identity. **Never** add a `Co-Authored-By: Claude …` trailer or commit with a Claude identity (CLAUDE.md, "Authorship"). Subjects are `type(scope): what changed, as a sentence`; the body says why. Read `git log -5` for the register.
- No agent worktrees, stashes, tags or extra branches left behind (`git worktree list` shows one entry at the end).
- One Filled key per window: **Log** on the wager window and on the slip. Add to slip, Start over and quick keys are Plain; the slip card's × is Plain with `Role="DeskRole.Destructive"`.
- A disabled key says what it is waiting for, under the key, in warning text (`<div class="warnc …">`).
- Check sentences state a fact and, where there is one, the consequence. They never instruct.
- The scope sentence (LineOps records bets placed elsewhere) appears once per window, at the bottom.
- The closing line is the only check that disables Log. Everything else advises.
- Stale threshold: **30 minutes** (`Checks.StaleAfter`). Duplicate window: **7 days** (`Checks.DuplicateWindow`). Constants, not configuration.
- Slip window: key `slip`, title **Slip**, group `Data`, icon `Icons.Material.Filled.Receipt`, `Singleton = true`, `DefaultWeight = 0.9`, `MinWidth = 380`, `RequiresSubject = false`.
- The slip is circuit memory (`AddScoped`), never persisted.
- Prices are formatted with `OddsMath.FormatPrice` (ASCII hyphen, "-128"); signed percentages and money use U+2212 "−" for negatives, as `PriceCell.EvLabel` already does. The spec's "−128" is typographic illustration only.
- Tests are written first and must be seen failing before the implementation is written.
- Do not modify `appsettings.json` of either host. Worker `appsettings.json` comment keys stay unique (`"//1"`, `"//2"` …).

## Where the spec and the code disagree, and what this plan does

The engineer does not need to re-litigate these; they are recorded so a reviewer knows they were deliberate.

1. **Test project for pure Web classes.** `tests/LineOps.Tests` does not reference `LineOps.Web`, so `WagerSlipTests` and `AdvisoryChecksTests` go in `tests/LineOps.Web.Tests` as plain xunit classes (no bUnit base). `SlipLoggingIntegrationTests` stays in `tests/LineOps.Tests` and exercises the `JournalService` methods the slip calls.
2. **Combined price.** `JournalPanel` computes no combined price, and the reprice-after-push lives in `ParlayGrading.Grade` (Core), which `SettlementService` calls. Rounding a payout multiplier to a whole American price would change settled payouts, so `OddsMath` gains `CombinedDecimal` (used by `ParlayGrading`) and `CombinedAmerican` on top of it (used by the slip).
3. **`WagerSlip.Touch`.** `SlipItem` is an immutable record, so `Action<SlipItem>` cannot edit it. The signature is `Touch(Guid id, Func<SlipItem, SlipItem> edit)`.
4. **"One transaction."** `ChangeNotifier` documents that the app uses no explicit transactions (retry-on-failure rules them out). A log is one `SaveChangesAsync`, which EF runs in its own implicit transaction: all rows or none.
5. **"Publish `DataTopics.Journal` through the in-circuit path" / "`OnPlaced`-equivalent signals."** There is no explicit publish; every save is announced by `ChangeNotifier`. Nothing consumes `BoardWager.OnPlaced` today. The parameter stays; the slip adds no extra signal.
6. **A rung's capture time.** `BookPrice` has none. It gains `DateTimeOffset? CapturedAt { get; init; }`, set by `BoardService`.
7. **`Manager.Open(WindowCatalog.Journal)`** takes a `WindowDefinition`, so calls read `Manager.Open(WindowCatalog.Find(WindowCatalog.Journal)!)`, as `OpsPanel` does.
8. **`DeskDialog` confirm** → the codebase's confirm idiom is `IDeskAlerts.ConfirmAsync`. **`Role.Destructive`** → `Role="DeskRole.Destructive"`.
9. **`BookPicker`** is a private `RenderFragment` in `JournalPanel`, not a component. The slip uses a `DeskSelect` over `JournalService.CommonBooks` plus the books on the slip.
10. **"One indexed query."** Only `PlacedAt` is indexed on `JournalEntries`. No migration is added; the journal is hundreds of rows. The query excludes parlay legs (stake 0) and compares outcome and book case-insensitively.
11. **Per-topic re-runs.** `PanelBase.RefreshAsync()` receives no topic, so any Odds or Journal signal reloads the row and re-reads the duplicate check. Both are cheap.
12. **Doors need several parameters.** `FollowUpLauncher.Launch` takes one id; it gains an overload taking a parameter dictionary. `BoardAllBets` is not a `PanelBase` and reads the database directly; it moves onto `IWagerData.RowAsync` and hosts its own `FollowUpLayer`, as `BoardPanel` does.
13. **bUnit and the database.** No Web test has a database. `BoardWager`, `SlipPanel` and `BoardAllBets` read through `IWagerData` so they can be rendered against `FakeWagerData`. `JournalPanel` loads from the database in `OnInitializedAsync`, so `JournalPanelTests` read its source (the way `GridConventionTests` does) and assert the catalog entry the new key opens.
14. **Slip card "Worth knowing".** Defined as another item on the slip on the same side (game, market, outcome). No per-card journal query.
15. **A parlay needs a book.** The spec's disabled reasons omit it; the plan adds *Needs the book the parlay was placed at.* and `SetMode(Parlay)` defaults the book to the legs' most common book.
16. **`Legs`.** "`Items.Where(CanLog)`" depends on mode; `Legs` is the non-closing items, which equals `CanLog` in parlay mode.
17. **"· 2 credits".** Read from `IngestionJob.EstimatedCredits` for `odds:lines:{sport}`, not hard-coded.
18. **After Log on the wager window** the old code cleared the chosen side. The plan clears the stake and note but keeps the side, so the duplicate check can say what was just logged; Log is disabled again at zero stake.
19. **Guard test timing.** "No panel declares `placeholder="Stake"`" can only pass once `BoardWager` (Task 7) and `JournalPanel` (Task 11) have moved to `StakeRow`, so that assertion is added in Task 11; the "one definition" half lands in Task 6.
20. **Seed script.** `scripts/seed-test-bets.sql` (already written, untracked) writes capitalised books, but `JournalService` stores books lower-case and the duplicate check matches on book; its header uses `<` redirection, which PowerShell does not support, and omits the password the container's SCRAM auth needs. Task 12 fixes all three and documents the one-line removal that makes it reversible.

## File map

| File | Status | Responsibility |
|---|---|---|
| `src/LineOps.Core/Analytics/OddsMath.cs` | modify | `CombinedDecimal`, `CombinedAmerican` |
| `src/LineOps.Core/Analytics/ParlayGrading.cs` | modify | reprice via `CombinedDecimal` |
| `src/LineOps.Data/CrossReference/BoardService.cs` | modify | `BookPrice.CapturedAt` |
| `src/LineOps.Ingestion/Services/JournalService.cs` | modify | `SaveEntriesAsync`, `LatestOnSideAsync` |
| `src/LineOps.Web/Services/SelectionText.cs` | create | "ML · Chiefs -2.5" wording, one definition |
| `src/LineOps.Web/Services/WagerSlip.cs` | create | `WagerSlip`, `SlipItem`, `SlipMode` |
| `src/LineOps.Web/Services/WagerOffer.cs` | create | the one price a wager is logged against |
| `src/LineOps.Web/Services/SelectionChoice.cs` | create | the six sides of a row, and where a door lands |
| `src/LineOps.Web/Services/WagerData.cs` | create | `IWagerData` seam + production `WagerData` |
| `src/LineOps.Web/Components/Desk/Checks.cs` | create | `Check`, `CheckActions`, pure `Checks` builder |
| `src/LineOps.Web/Components/Desk/CheckList.razor` | create | renders a list of `Check` rows |
| `src/LineOps.Web/Components/Desk/SelectionChips.razor` | create | the six-chip strip |
| `src/LineOps.Web/Components/Desk/StakeRow.razor` | create | stake field, quick keys, Returns |
| `src/LineOps.Web/Components/Desk/PriceCell.razor` | modify | `OnTake` (Door 2) |
| `src/LineOps.Web/Components/Panels/BoardWager.razor` | rewrite | panel, doors, checks, Add to slip |
| `src/LineOps.Web/Components/Panels/SlipPanel.razor` | create | the Slip window |
| `src/LineOps.Web/Components/Panels/BoardPanel.razor` | modify | price cells open the wager window |
| `src/LineOps.Web/Components/Panels/BoardAllBets.razor` | modify | rungs open the wager window (Door 3) |
| `src/LineOps.Web/Components/Panels/JournalPanel.razor` | modify | parlay form retires, Open slip, `StakeRow`, duplicate check |
| `src/LineOps.Web/Windowing/WindowCatalog.cs` | modify | `Slip` entry |
| `src/LineOps.Desk/Windowing/FollowUpLauncher.cs` | modify | multi-parameter `Launch` overload |
| `src/LineOps.Desk/wwwroot/css/lineops.css` | modify | offer block, checks, slip, badge, takeable price |
| `src/LineOps.Web/Program.cs` | modify | register `WagerSlip`, `IWagerData` |
| `scripts/seed-test-bets.sql` | fix + commit | dev-only test bets |
| `tests/LineOps.Tests/Analytics/OddsMathTests.cs` | modify | combined price tests |
| `tests/LineOps.Tests/Reliability/BoardServiceTests.cs` | modify | rung capture time |
| `tests/LineOps.Tests/Reliability/SlipLoggingIntegrationTests.cs` | create | Testcontainers |
| `tests/LineOps.Web.Tests/Support/*.cs` | create | `Repo`, `BoardRows`, `FakeWagerData`, `FakeAlerts`, `WagerTestContext` |
| `tests/LineOps.Web.Tests/WagerSlipTests.cs` | create | |
| `tests/LineOps.Web.Tests/AdvisoryChecksTests.cs` | create | |
| `tests/LineOps.Web.Tests/SharedControlsTests.cs` | create | `SelectionChips`, `StakeRow` |
| `tests/LineOps.Web.Tests/SelectionGuardTests.cs` | create | §7 guard |
| `tests/LineOps.Web.Tests/BoardWagerTests.cs` | create | |
| `tests/LineOps.Web.Tests/SlipPanelTests.cs` | create | |
| `tests/LineOps.Web.Tests/PriceCellTests.cs` | modify | take action |
| `tests/LineOps.Web.Tests/FollowUpLauncherTests.cs` | modify | overload |
| `tests/LineOps.Web.Tests/BoardAllBetsTests.cs` | create | Door 3 |
| `tests/LineOps.Web.Tests/JournalPanelTests.cs` | create | |

All commands are PowerShell, run from `C:\Deploy\LineOps`. Testcontainers tests need Docker Desktop running.

---

### Task 0: Commit the spec and this plan

**Files:** `docs/superpowers/specs/2026-09-26-slip-and-advisory-checks-design.md`, `docs/superpowers/plans/2026-09-26-slip-and-advisory-checks.md` (both untracked).

- [ ] **Step 1: Confirm the branch and a clean tree apart from these docs and the seed script**

Run: `git branch --show-current; git status --short`
Expected: `LineX_Improvements`; untracked are the spec, this plan and `scripts/seed-test-bets.sql` only.

- [ ] **Step 2: Commit**

```powershell
git add docs/superpowers/specs/2026-09-26-slip-and-advisory-checks-design.md docs/superpowers/plans/2026-09-26-slip-and-advisory-checks.md
git commit -m "docs(slip): the slip and advisory checks design, and the plan that builds it" -m "The wager window keeps one screen, gains checks that run before the key is pressed, and banks wagers onto a slip that belongs to the desk session, which is where parlays are logged from now on. The plan records where the design and the code disagreed and what was chosen."
```

---

### Task 1: A parlay's combined price has one definition

**Files:**
- Modify: `src/LineOps.Core/Analytics/OddsMath.cs` (append after `PayoutOnWin`, line 51)
- Modify: `src/LineOps.Core/Analytics/ParlayGrading.cs:55`
- Test: `tests/LineOps.Tests/Analytics/OddsMathTests.cs`

**Interfaces:**
- Produces: `public static double OddsMath.CombinedDecimal(IEnumerable<int> legsAmerican)`; `public static int OddsMath.CombinedAmerican(IEnumerable<int> legsAmerican)`. Both throw `ArgumentException` on no legs and `ArgumentOutOfRangeException` on a zero price (via `ToDecimal`).

- [ ] **Step 1: Write the failing tests** — append inside `OddsMathTests`:

```csharp
    // ---- A parlay's combined price ------------------------------------------------------
    //
    // The product of the legs' decimal prices. The slip quotes it as an American price; the
    // settlement reprices after a push on the unrounded product, so a payout is never paid on
    // a number rounded to the nearest cent of American odds.

    [Fact]
    public void CombinedAmerican_MultipliesTheLegsDecimalPrices()
        // 1.78125 × 1.95238 = 3.4777 → +248
        => Assert.Equal(248, OddsMath.CombinedAmerican([-128, -105]));

    [Fact]
    public void CombinedAmerican_TwoLegsAtMinus110IsPlus264()
        => Assert.Equal(264, OddsMath.CombinedAmerican([-110, -110]));

    [Fact]
    public void CombinedDecimal_IsTheUnroundedProduct()
        => Assert.Equal(1.78125 * (1.0 + 100.0 / 105.0), OddsMath.CombinedDecimal([-128, -105]), 10);

    [Fact]
    public void CombinedPrice_NeedsALeg()
        => Assert.Throws<ArgumentException>(() => OddsMath.CombinedAmerican([]));
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LineOps.Tests --filter "FullyQualifiedName~OddsMathTests"`
Expected: build error `'OddsMath' does not contain a definition for 'CombinedAmerican'`.

- [ ] **Step 3: Implement** — append to `OddsMath` after `PayoutOnWin`:

```csharp
    /// <summary>
    /// The legs' decimal prices multiplied: what one unit returns if every leg wins. Unrounded,
    /// because settlement pays on it (<see cref="ParlayGrading"/>) and a payout must not move
    /// with the rounding of a displayed price.
    /// </summary>
    public static double CombinedDecimal(IEnumerable<int> legsAmerican)
    {
        var product = 1.0;
        var any = false;

        foreach (var american in legsAmerican)
        {
            product *= ToDecimal(american);
            any = true;
        }

        if (!any)
            throw new ArgumentException("A combined price needs at least one leg.", nameof(legsAmerican));

        return product;
    }

    /// <summary>
    /// A parlay's price from its legs, as a whole American price. What the slip quotes when the
    /// book's own parlay price was not given. -128 and -105 make +248.
    /// </summary>
    public static int CombinedAmerican(IEnumerable<int> legsAmerican)
        => FromDecimal(CombinedDecimal(legsAmerican));
```

Then in `ParlayGrading.Grade`, replace line 55:

```csharp
        var multiplier = winners.Aggregate(1.0, (m, l) => m * OddsMath.ToDecimal(l.PriceTaken));
```

with:

```csharp
        var multiplier = OddsMath.CombinedDecimal(winners.Select(l => l.PriceTaken));
```

- [ ] **Step 4: Run to verify they pass, and that grading is unchanged**

Run: `dotnet test tests/LineOps.Tests --filter "FullyQualifiedName~OddsMathTests|FullyQualifiedName~ParlayGradingTests"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/LineOps.Core/Analytics/OddsMath.cs src/LineOps.Core/Analytics/ParlayGrading.cs tests/LineOps.Tests/Analytics/OddsMathTests.cs
git commit -m "feat(analytics): a parlay's combined price has one definition" -m "The slip quotes a parlay's price from its legs, and settlement reprices a parlay after a push the same way. Both now read OddsMath: CombinedDecimal is the unrounded product settlement pays on, CombinedAmerican the whole price the slip shows (-128 and -105 make +248)."
```

---

### Task 2: A rung carries when its book was captured

**Files:**
- Modify: `src/LineOps.Data/CrossReference/BoardService.cs` (the `BookPrice` record, line 538; the rung projection, lines 403-412)
- Test: `tests/LineOps.Tests/Reliability/BoardServiceTests.cs`

**Interfaces:**
- Produces: `BookPrice.CapturedAt` — `public DateTimeOffset? CapturedAt { get; init; }`. Null only on hand-built test values.

- [ ] **Step 1: Write the failing test** — append inside `BoardServiceTests`:

```csharp
    /// <summary>
    /// A door that opens the wager window on one book's price must be able to say how old that
    /// book's number is, not the best book's.
    /// </summary>
    [Fact]
    public async Task EveryRungCarriesWhenItsBookWasCaptured()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var early = DateTimeOffset.UtcNow.AddMinutes(-50);
        var late = DateTimeOffset.UtcNow.AddMinutes(-5);

        var dk = Price(s, "draftkings", Markets.Moneyline, s.Home.Name, 120);
        dk.CapturedAt = early;
        var fd = Price(s, "fanduel", Markets.Moneyline, s.Home.Name, 135);
        fd.CapturedAt = late;

        db.OddsSnapshots.AddRange(dk, fd);
        await db.SaveChangesAsync();

        var row = await LoadAsync(db, s);
        var rungs = row.Moneyline.First!.Rungs;

        Assert.Equal(early, rungs.Single(r => r.Book == "draftkings").CapturedAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(late, rungs.Single(r => r.Book == "fanduel").CapturedAt!.Value, TimeSpan.FromMilliseconds(1));
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/LineOps.Tests --filter "FullyQualifiedName~BoardServiceTests.EveryRungCarriesWhenItsBookWasCaptured"`
Expected: build error `'BookPrice' does not contain a definition for 'CapturedAt'`.

- [ ] **Step 3: Implement.** In `BookPrice` add, after `Hold`:

```csharp
    /// <summary>
    /// When this book's number was observed. The best offer's <see cref="BestOffer.CapturedAt"/>
    /// is the best book's; a wager logged at another book is only as fresh as that book's scan.
    /// </summary>
    public DateTimeOffset? CapturedAt { get; init; }
```

In `Best(...)`, the rung projection's initializer becomes:

```csharp
            {
                Ev = fair.Ev(p.Outcome, p.Line, p.PriceAmerican),
                Hold = fair.Hold(p.Book, p.Outcome, p.Line),
                CapturedAt = p.CapturedAt,
            })
```

- [ ] **Step 4: Run the Board tests**

Run: `dotnet test tests/LineOps.Tests --filter "FullyQualifiedName~BoardServiceTests"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/LineOps.Data/CrossReference/BoardService.cs tests/LineOps.Tests/Reliability/BoardServiceTests.cs
git commit -m "feat(board): every book's price carries when it was captured" -m "The wager window is about to open on one book's rung rather than always on the best offer, and a price-age check on that window has to read the age of the number shown. The best offer already carried its own capture time; each rung now carries its book's."
```

---

### Task 3: The journal logs a slip in one save, and names the last bet on a side

**Files:**
- Modify: `src/LineOps.Ingestion/Services/JournalService.cs` (add two methods after `SaveParlayAsync`, line 239)
- Test: `tests/LineOps.Tests/Reliability/SlipLoggingIntegrationTests.cs` (create)

**Interfaces:**
- Produces:
  - `Task<IReadOnlyList<JournalEntry>> JournalService.SaveEntriesAsync(IReadOnlyList<EntryDraft> drafts, CancellationToken ct = default)` — all or none, one `SaveChangesAsync`, `PlacedAt` shared, `Result = Pending`, books lower-cased by the existing `Apply`.
  - `Task<JournalEntry?> JournalService.LatestOnSideAsync(int gameId, string market, string outcome, string book, DateTimeOffset since, CancellationToken ct = default)` — newest straight bet (not a parlay leg) with `PlacedAt >= since` on that game, market, outcome and book; outcome and book case-insensitive.
  - Existing, unchanged, relied on by the slip: `SaveParlayAsync(ParlayDraft)`.

- [ ] **Step 1: Write the failing tests** — create `tests/LineOps.Tests/Reliability/SlipLoggingIntegrationTests.cs`:

```csharp
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LineOps.Tests.Reliability;

/// <summary>
/// What the slip writes, against real Postgres: straight bets in one save, a parlay as one row
/// with zero-stake legs in its group, a failure that writes nothing, and the lookup the wager
/// window's duplicate check reads.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SlipLoggingIntegrationTests(PostgresFixture fixture)
{
    private static JournalService Service(LineOpsDbContext db)
        => new(db, new SettlementService(db, NullLogger<SettlementService>.Instance));

    private static async Task<(Game Game, Team Home, Team Away)> ScheduledGameAsync(LineOpsDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sport = new Sport { Key = $"sl-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = $"Home {suffix}", Abbrev = "HOM" };
        var away = new Team { SportId = sport.Id, Name = $"Away {suffix}", Abbrev = "AWY" };
        db.Teams.AddRange(home, away);
        await db.SaveChangesAsync();

        var game = new Game
        {
            SportId = sport.Id, HomeTeamId = home.Id, AwayTeamId = away.Id,
            StartsAt = DateTimeOffset.UtcNow.AddHours(5), Status = GameStatus.Scheduled, SeasonYear = 2026
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return (game, home, away);
    }

    private static EntryDraft Bet(int? gameId, string outcome, decimal stake = 50m, string book = "draftkings", string? note = null)
        => new(gameId, Markets.Moneyline, null, outcome, null, -128, stake, book, note);

    [Fact]
    public async Task Straight_mode_writes_one_entry_per_bet_in_one_save()
    {
        await using var db = fixture.CreateContext();
        var (game, home, away) = await ScheduledGameAsync(db);
        var note = $"slip {Guid.NewGuid():N}";

        var written = await Service(db).SaveEntriesAsync(
        [
            Bet(game.Id, home.Name, 50m, note: note),
            Bet(game.Id, away.Name, 25m, "FanDuel", note)
        ]);

        Assert.Equal(2, written.Count);

        var rows = await db.JournalEntries.AsNoTracking().Where(e => e.Note == note).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(EntryResult.Pending, r.Result));
        Assert.All(rows, r => Assert.Null(r.ParlayGroupId));
        Assert.Single(rows.Select(r => r.PlacedAt).Distinct());
        Assert.Contains(rows, r => r.Book == "fanduel");   // written the way every reader matches it
    }

    [Fact]
    public async Task A_bet_that_cannot_be_written_takes_the_whole_slip_with_it()
    {
        await using var db = fixture.CreateContext();
        var (game, home, _) = await ScheduledGameAsync(db);
        var note = $"slip {Guid.NewGuid():N}";

        // No such game: the foreign key refuses the second row, and the first must not survive it.
        await Assert.ThrowsAsync<DbUpdateException>(() => Service(db).SaveEntriesAsync(
        [
            Bet(game.Id, home.Name, note: note),
            Bet(int.MaxValue, home.Name, note: note)
        ]));

        await using var fresh = fixture.CreateContext();
        Assert.Equal(0, await fresh.JournalEntries.CountAsync(e => e.Note == note));
    }

    [Fact]
    public async Task Parlay_mode_writes_one_parlay_and_zero_stake_legs_in_its_group()
    {
        await using var db = fixture.CreateContext();
        var (first, home1, _) = await ScheduledGameAsync(db);
        var (second, _, away2) = await ScheduledGameAsync(db);
        var note = $"slip {Guid.NewGuid():N}";

        var parlay = await Service(db).SaveParlayAsync(new ParlayDraft("BetMGM", 25m, null, note,
        [
            Bet(first.Id, home1.Name, 0m),
            Bet(second.Id, away2.Name, 0m)
        ]));

        var stored = await db.Parlays.AsNoTracking().Include(p => p.Legs).SingleAsync(p => p.Id == parlay.Id);
        Assert.Equal(25m, stored.Stake);
        Assert.Null(stored.PriceQuoted);
        Assert.Equal("betmgm", stored.Book);
        Assert.Equal(2, stored.Legs.Count);
        Assert.All(stored.Legs, l => Assert.Equal(0m, l.Stake));
        Assert.All(stored.Legs, l => Assert.Equal(parlay.Id, l.ParlayGroupId));
    }

    [Fact]
    public async Task The_newest_straight_bet_on_a_side_is_found_within_the_window()
    {
        await using var db = fixture.CreateContext();
        var (game, home, away) = await ScheduledGameAsync(db);
        var journal = Service(db);

        db.JournalEntries.AddRange(
            new JournalEntry { GameId = game.Id, Market = Markets.Moneyline, Outcome = home.Name, Book = "draftkings",
                               PriceTaken = -130, Stake = 50m, PlacedAt = DateTimeOffset.UtcNow.AddDays(-10) },
            new JournalEntry { GameId = game.Id, Market = Markets.Moneyline, Outcome = home.Name, Book = "draftkings",
                               PriceTaken = -128, Stake = 25m, PlacedAt = DateTimeOffset.UtcNow.AddHours(-2) },
            new JournalEntry { GameId = game.Id, Market = Markets.Moneyline, Outcome = home.Name, Book = "draftkings",
                               PriceTaken = -125, Stake = 40m, PlacedAt = DateTimeOffset.UtcNow.AddHours(-3) },
            new JournalEntry { GameId = game.Id, Market = Markets.Moneyline, Outcome = away.Name, Book = "draftkings",
                               PriceTaken = 110, Stake = 10m, PlacedAt = DateTimeOffset.UtcNow.AddHours(-1) });
        await db.SaveChangesAsync();

        var since = DateTimeOffset.UtcNow.AddDays(-7);

        var found = await journal.LatestOnSideAsync(game.Id, Markets.Moneyline, home.Name.ToUpperInvariant(), "DraftKings", since);
        Assert.NotNull(found);
        Assert.Equal(-128, found.PriceTaken);

        Assert.Null(await journal.LatestOnSideAsync(game.Id, Markets.Moneyline, home.Name, "fanduel", since));
        Assert.Null(await journal.LatestOnSideAsync(game.Id, Markets.Spread, home.Name, "draftkings", since));
    }

    [Fact]
    public async Task A_parlay_leg_is_not_a_prior_bet_on_its_side()
    {
        await using var db = fixture.CreateContext();
        var (first, home1, _) = await ScheduledGameAsync(db);
        var (second, home2, _) = await ScheduledGameAsync(db);
        var journal = Service(db);

        await journal.SaveParlayAsync(new ParlayDraft("draftkings", 10m, null, null,
            [Bet(first.Id, home1.Name, 0m), Bet(second.Id, home2.Name, 0m)]));

        Assert.Null(await journal.LatestOnSideAsync(first.Id, Markets.Moneyline, home1.Name, "draftkings",
            DateTimeOffset.UtcNow.AddDays(-7)));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LineOps.Tests --filter "FullyQualifiedName~SlipLoggingIntegrationTests"`
Expected: build error `'JournalService' does not contain a definition for 'SaveEntriesAsync'`.

- [ ] **Step 3: Implement** — add to `JournalService` after `SaveParlayAsync`:

```csharp
    /// <summary>
    /// Logs several straight bets as one save — the slip's straight mode. One
    /// <c>SaveChangesAsync</c> is one implicit transaction, so a slip is logged whole or not at
    /// all: a half-logged slip would leave the operator guessing which bets reached the journal.
    /// </summary>
    public async Task<IReadOnlyList<JournalEntry>> SaveEntriesAsync(IReadOnlyList<EntryDraft> drafts, CancellationToken ct = default)
    {
        if (drafts.Count == 0)
            return [];

        var placed = DateTimeOffset.UtcNow;
        var entries = new List<JournalEntry>(drafts.Count);

        foreach (var draft in drafts)
        {
            var entry = new JournalEntry { PlacedAt = placed, Result = EntryResult.Pending };
            Apply(entry, draft);
            entries.Add(entry);
        }

        db.JournalEntries.AddRange(entries);
        await db.SaveChangesAsync(ct);
        return entries;
    }

    /// <summary>
    /// The newest straight bet on one side at one book since a moment — what the wager window's
    /// duplicate check names. A parlay leg is left out: it has no stake of its own, and "already
    /// $0 on this side" is not a sentence. Outcome and book compare without case, because the
    /// manual form takes a typed team name and books were not always stored lower-case.
    /// </summary>
    public async Task<JournalEntry?> LatestOnSideAsync(
        int gameId, string market, string outcome, string book, DateTimeOffset since, CancellationToken ct = default)
    {
        var side = outcome.Trim().ToLower();
        var at = Normalise(book);

        return await db.JournalEntries.AsNoTracking()
            .Where(e => e.ParlayGroupId == null
                        && e.GameId == gameId
                        && e.Market == market
                        && e.PlacedAt >= since
                        && e.Outcome.ToLower() == side
                        && e.Book.ToLower() == at)
            .OrderByDescending(e => e.PlacedAt)
            .FirstOrDefaultAsync(ct);
    }
```

- [ ] **Step 4: Run to verify they pass, and the journal suite is unchanged**

Run: `dotnet test tests/LineOps.Tests --filter "FullyQualifiedName~SlipLoggingIntegrationTests|FullyQualifiedName~JournalServiceTests"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/LineOps.Ingestion/Services/JournalService.cs tests/LineOps.Tests/Reliability/SlipLoggingIntegrationTests.cs
git commit -m "feat(journal): a slip of straight bets is logged in one save, and a side's last bet can be named" -m "The slip logs several wagers with one press; SaveEntriesAsync writes them in a single SaveChanges so a failure leaves nothing half-logged. LatestOnSideAsync is the one query behind the wager window's duplicate check: the newest straight bet on a game, market, side and book within a window, legs left out."
```

---

### Task 4: `WagerSlip` — the slip and its rules

**Files:**
- Create: `src/LineOps.Web/Services/SelectionText.cs`
- Create: `src/LineOps.Web/Services/WagerSlip.cs`
- Modify: `src/LineOps.Web/Program.cs` (beside `AddScoped<…IDeskSearch, …AppDeskSearch>()`, line 61)
- Test: `tests/LineOps.Web.Tests/WagerSlipTests.cs` (create)

**Interfaces:**
- Consumes: `OddsMath.CombinedAmerican` (Task 1); `EntryDraft`, `ParlayDraft` (`LineOps.Ingestion.Services`).
- Produces (namespace `LineOps.Web.Services`):
  - `static class SelectionText { string MarketName(string market); string Short(string outcome); string Of(string market, string outcome, decimal? line); }` — `Of("h2h","Kansas City Chiefs",null)` = `"ML · Chiefs"`; `Of("spread","Buffalo Bills",2.5m)` = `"Spread · Bills +2.5"`.
  - `enum SlipMode { Straight, Parlay }`
  - `sealed record SlipItem(Guid Id, int GameId, string Matchup, DateTimeOffset StartsAt, string SportKey, string Market, string Outcome, decimal? Line, string Book, int PriceAmerican, DateTimeOffset CapturedAt, bool IsClosing, decimal Stake, string? Note, FairPrice? Fair, double? Ev)` with `string Selection` ("ML · Chiefs -128") and `EntryDraft ToDraft(decimal stake)`.
  - `sealed class WagerSlip` with `Items`, `Mode`, `ParlayStake`, `ParlayPriceQuoted`, `ParlayBook`, `event Action? Changed`, `SlipItem Add(SlipItem)`, `void Remove(Guid)`, `void Clear()`, `void SetMode(SlipMode)`, `void SetParlay(decimal, int?, string?)`, `void Touch(Guid, Func<SlipItem, SlipItem>)`, `bool CanLog(SlipItem)`, `IReadOnlyList<SlipItem> Ready`, `IReadOnlyList<SlipItem> Legs`, `int? CombinedPrice`, `bool HasTwin(SlipItem)`, `IReadOnlyList<EntryDraft> StraightDrafts()`, `ParlayDraft ToParlayDraft()`, `void Logged(IEnumerable<Guid> ids)`.

- [ ] **Step 1: Write the failing tests** — create `tests/LineOps.Web.Tests/WagerSlipTests.cs`:

```csharp
using LineOps.Core.Entities;
using LineOps.Web.Services;

namespace LineOps.Web.Tests;

/// <summary>
/// The slip's rules, owned by one class so no window has to remember them: a closing price is
/// never logged, a straight bet needs a stake, a parlay's legs are what the market still offers,
/// and the combined price is the legs multiplied unless the book quoted its own.
/// </summary>
public class WagerSlipTests
{
    private static SlipItem Item(int price = -110, bool closing = false, decimal stake = 50m,
        string outcome = "Kansas City Chiefs", string book = "draftkings", int gameId = 7)
        => new(Guid.Empty, gameId, "Buffalo Bills at Kansas City Chiefs", DateTimeOffset.UtcNow.AddHours(3), "nfl",
            Markets.Moneyline, outcome, null, book, price, DateTimeOffset.UtcNow, closing, stake, null, null, null);

    [Fact]
    public void Add_remove_and_clear_each_raise_Changed_once()
    {
        var slip = new WagerSlip();
        var changes = 0;
        slip.Changed += () => changes++;

        var stored = slip.Add(Item());
        Assert.Equal(1, changes);

        slip.Remove(stored.Id);
        Assert.Equal(2, changes);

        slip.Add(Item());
        slip.Clear();
        Assert.Equal(4, changes);
        Assert.Empty(slip.Items);
    }

    [Fact]
    public void Add_assigns_an_id()
    {
        var stored = new WagerSlip().Add(Item());
        Assert.NotEqual(Guid.Empty, stored.Id);
    }

    [Fact]
    public void Removing_an_unknown_id_is_a_no_op()
    {
        var slip = new WagerSlip();
        slip.Add(Item());
        var changes = 0;
        slip.Changed += () => changes++;

        slip.Remove(Guid.NewGuid());

        Assert.Single(slip.Items);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void A_closing_item_cannot_be_logged_and_is_not_a_leg()
    {
        var slip = new WagerSlip();
        var closing = slip.Add(Item(closing: true));
        slip.Add(Item());

        Assert.False(slip.CanLog(closing));
        Assert.DoesNotContain(closing, slip.Legs);
        Assert.Single(slip.Ready);
    }

    [Fact]
    public void A_straight_bet_needs_a_stake_and_a_leg_does_not()
    {
        var slip = new WagerSlip();
        var unstaked = slip.Add(Item(stake: 0m));

        Assert.False(slip.CanLog(unstaked));

        slip.SetMode(SlipMode.Parlay);
        Assert.True(slip.CanLog(unstaked));
    }

    [Fact]
    public void The_combined_price_multiplies_the_legs()
    {
        var slip = new WagerSlip();
        slip.Add(Item(-128));
        slip.Add(Item(-105, outcome: "Buffalo Bills"));
        slip.Add(Item(-200, closing: true));   // not a leg: it does not count

        Assert.Equal(248, slip.CombinedPrice);
    }

    [Fact]
    public void A_quoted_parlay_price_replaces_the_product()
    {
        var slip = new WagerSlip();
        slip.Add(Item(-128));
        slip.Add(Item(-105, outcome: "Buffalo Bills"));

        slip.SetParlay(25m, 260, "draftkings");

        Assert.Equal(260, slip.CombinedPrice);
    }

    [Fact]
    public void One_leg_has_no_combined_price()
    {
        var slip = new WagerSlip();
        slip.Add(Item(-128));
        Assert.Null(slip.CombinedPrice);
    }

    [Fact]
    public void Switching_mode_keeps_the_items()
    {
        var slip = new WagerSlip();
        slip.Add(Item());
        slip.Add(Item(outcome: "Buffalo Bills"));

        slip.SetMode(SlipMode.Parlay);
        slip.SetMode(SlipMode.Straight);

        Assert.Equal(2, slip.Items.Count);
    }

    [Fact]
    public void Switching_to_a_parlay_defaults_the_book_to_the_legs_book()
    {
        var slip = new WagerSlip();
        slip.Add(Item(book: "fanduel"));
        slip.Add(Item(book: "fanduel", outcome: "Buffalo Bills"));
        slip.Add(Item(book: "draftkings", gameId: 8));

        slip.SetMode(SlipMode.Parlay);

        Assert.Equal("fanduel", slip.ParlayBook);
    }

    [Fact]
    public void Touch_edits_in_place_and_keeps_the_id()
    {
        var slip = new WagerSlip();
        var stored = slip.Add(Item(stake: 0m));
        var changes = 0;
        slip.Changed += () => changes++;

        slip.Touch(stored.Id, i => i with { Stake = 75m, Id = Guid.NewGuid() });

        Assert.Equal(75m, slip.Items.Single().Stake);
        Assert.Equal(stored.Id, slip.Items.Single().Id);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Two_items_on_one_side_are_twins_and_both_stay()
    {
        var slip = new WagerSlip();
        var a = slip.Add(Item());
        slip.Add(Item(book: "fanduel"));
        var other = slip.Add(Item(outcome: "Buffalo Bills"));

        Assert.True(slip.HasTwin(a));
        Assert.False(slip.HasTwin(other));
        Assert.Equal(3, slip.Items.Count);
    }

    [Fact]
    public void Logged_items_leave_the_slip_in_one_change()
    {
        var slip = new WagerSlip();
        var a = slip.Add(Item());
        var b = slip.Add(Item(outcome: "Buffalo Bills"));
        var closing = slip.Add(Item(closing: true));
        var changes = 0;
        slip.Changed += () => changes++;

        slip.Logged([a.Id, b.Id]);

        Assert.Equal(new[] { closing.Id }, slip.Items.Select(i => i.Id));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void The_drafts_carry_the_stake_straight_and_none_on_a_leg()
    {
        var slip = new WagerSlip();
        slip.Add(Item(-128, stake: 50m));
        slip.Add(Item(-105, stake: 0m, outcome: "Buffalo Bills"));

        var straights = slip.StraightDrafts();
        Assert.Single(straights);
        Assert.Equal(50m, straights[0].Stake);

        slip.SetMode(SlipMode.Parlay);
        slip.SetParlay(25m, null, "draftkings");
        var parlay = slip.ToParlayDraft();

        Assert.Equal(25m, parlay.Stake);
        Assert.Null(parlay.PriceQuoted);
        Assert.Equal(2, parlay.Legs.Count);
        Assert.All(parlay.Legs, l => Assert.Equal(0m, l.Stake));
    }

    [Theory]
    [InlineData("h2h", "Kansas City Chiefs", null, "ML · Chiefs")]
    [InlineData("spread", "Buffalo Bills", 2.5, "Spread · Bills +2.5")]
    [InlineData("total", "Over", 47.5, "Total · Over o47.5")]
    public void A_selection_reads_the_same_everywhere(string market, string outcome, double? line, string expected)
        => Assert.Equal(expected, SelectionText.Of(market, outcome, (decimal?)line));
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LineOps.Web.Tests --filter "FullyQualifiedName~WagerSlipTests"`
Expected: build error `The type or namespace name 'WagerSlip' could not be found`.

- [ ] **Step 3: Implement.** Create `src/LineOps.Web/Services/SelectionText.cs` (the wording moves here from `BoardWager.Short`/`LineOf`, so a chip, a slip card and a check sentence name a side identically):

```csharp
using LineOps.Core.Analytics;
using LineOps.Core.Entities;

namespace LineOps.Web.Services;

/// <summary>
/// A side named the way the desk writes it: "ML · Chiefs", "Spread · Bills +2.5". One
/// definition, because the chip that picked it, the slip card that holds it and the sentence
/// that says it was already logged must all read the same.
/// </summary>
public static class SelectionText
{
    public static string MarketName(string market) => market switch
    {
        Markets.Moneyline => "ML",
        Markets.Spread => "Spread",
        Markets.Total => "Total",
        _ => market
    };

    /// <summary>Team names are long and these are buttons; the last word identifies a club.</summary>
    public static string Short(string outcome)
        => outcome.Contains(' ')
           && !outcome.Equals("over", StringComparison.OrdinalIgnoreCase)
           && !outcome.Equals("under", StringComparison.OrdinalIgnoreCase)
            ? outcome[(outcome.LastIndexOf(' ') + 1)..]
            : outcome;

    public static string Of(string market, string outcome, decimal? line)
        => $"{MarketName(market)} · {Short(outcome)}{(line is { } l ? " " + OddsMath.FormatLine(l, outcome) : "")}";
}
```

Create `src/LineOps.Web/Services/WagerSlip.cs`:

```csharp
using LineOps.Core.Analytics;
using LineOps.Ingestion.Services;

namespace LineOps.Web.Services;

public enum SlipMode { Straight, Parlay }

/// <summary>One wager banked on the slip: everything the wager window knew when it was added.</summary>
public sealed record SlipItem(
    Guid Id,
    int GameId, string Matchup, DateTimeOffset StartsAt, string SportKey,
    string Market, string Outcome, decimal? Line,
    string Book, int PriceAmerican, DateTimeOffset CapturedAt, bool IsClosing,
    decimal Stake, string? Note,
    FairPrice? Fair, double? Ev)
{
    /// <summary>"ML · Chiefs -128".</summary>
    public string Selection => $"{SelectionText.Of(Market, Outcome, Line)} {OddsMath.FormatPrice(PriceAmerican)}";

    public EntryDraft ToDraft(decimal stake)
        => new(GameId, Market, null, Outcome, Line, PriceAmerican, stake, Book, Note);
}

/// <summary>
/// The wagers an operator has banked this desk session, and the rules that govern them.
///
/// <para>
/// Circuit-scoped (registered <c>AddScoped</c>): one slip per desk, shared by every window on it,
/// gone with the circuit. Not persisted — a reload loses an unlogged slip, and a slip takes seconds
/// to rebuild (spec §11.1). No Blazor types: windows subscribe to <see cref="Changed"/>.
/// </para>
///
/// <para>
/// Being the only owner of the list is what enforces the rules: a closing price is never logged
/// (it stays so the operator sees why), a straight bet needs a stake, a parlay's legs are the
/// items the market still offers, and two items on one side are two bets — allowed, and said.
/// </para>
/// </summary>
public sealed class WagerSlip
{
    private readonly List<SlipItem> _items = [];

    public IReadOnlyList<SlipItem> Items => _items;
    public SlipMode Mode { get; private set; } = SlipMode.Straight;
    public decimal ParlayStake { get; private set; }
    public int? ParlayPriceQuoted { get; private set; }
    public string? ParlayBook { get; private set; }

    public event Action? Changed;

    /// <summary>Banks a wager. Returns it as stored, with the id the slip assigned.</summary>
    public SlipItem Add(SlipItem item)
    {
        var stored = item with { Id = Guid.NewGuid() };
        _items.Add(stored);
        Raise();
        return stored;
    }

    /// <summary>The one removal path. An id not on the slip is a no-op.</summary>
    public void Remove(Guid id)
    {
        if (_items.RemoveAll(i => i.Id == id) > 0)
            Raise();
    }

    public void Clear()
    {
        _items.Clear();
        ParlayStake = 0;
        ParlayPriceQuoted = null;
        ParlayBook = null;
        Raise();
    }

    /// <summary>
    /// Switches between straight bets and one parlay, keeping every item. A parlay is placed at
    /// one book, so switching to one proposes the book most of the legs came from.
    /// </summary>
    public void SetMode(SlipMode mode)
    {
        if (Mode == mode)
            return;

        Mode = mode;

        if (mode == SlipMode.Parlay && ParlayBook is null)
            ParlayBook = Legs.GroupBy(l => l.Book).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;

        Raise();
    }

    /// <summary>The parlay's money: stake, the book's own price if it gave one, and the book.</summary>
    public void SetParlay(decimal stake, int? priceQuoted, string? book)
    {
        ParlayStake = stake;
        ParlayPriceQuoted = priceQuoted is 0 ? null : priceQuoted;
        ParlayBook = string.IsNullOrWhiteSpace(book) ? null : book;
        Raise();
    }

    /// <summary>Edits one item in place — its stake or note. The id is the slip's and survives the edit.</summary>
    public void Touch(Guid id, Func<SlipItem, SlipItem> edit)
    {
        var index = _items.FindIndex(i => i.Id == id);
        if (index < 0)
            return;

        _items[index] = edit(_items[index]) with { Id = id };
        Raise();
    }

    /// <summary>
    /// Whether this item can be logged as the slip stands. Never a closing price: it is a number
    /// that could not have been taken from here. In straight mode, also a stake above zero; a
    /// parlay leg carries no stake of its own.
    /// </summary>
    public bool CanLog(SlipItem item)
        => !item.IsClosing && (Mode == SlipMode.Parlay || item.Stake > 0);

    /// <summary>What Log would write in straight mode.</summary>
    public IReadOnlyList<SlipItem> Ready => _items.Where(CanLog).ToList();

    /// <summary>What the market still offers: the items a parlay can be built from.</summary>
    public IReadOnlyList<SlipItem> Legs => _items.Where(i => !i.IsClosing).ToList();

    /// <summary>
    /// The parlay's price: the book's quote where it gave one, otherwise the legs' prices
    /// multiplied (<see cref="OddsMath.CombinedAmerican"/>). Null with fewer than two legs.
    /// </summary>
    public int? CombinedPrice
    {
        get
        {
            if (ParlayPriceQuoted is { } quoted)
                return quoted;

            var legs = Legs;
            return legs.Count >= 2 ? OddsMath.CombinedAmerican(legs.Select(l => l.PriceAmerican)) : null;
        }
    }

    /// <summary>Whether another item on the slip is on the same side of the same game.</summary>
    public bool HasTwin(SlipItem item)
        => _items.Any(o => o.Id != item.Id && o.GameId == item.GameId && o.Market == item.Market
                           && string.Equals(o.Outcome, item.Outcome, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<EntryDraft> StraightDrafts() => Ready.Select(i => i.ToDraft(i.Stake)).ToList();

    /// <summary>
    /// The parlay as the journal takes it. The quoted price only when the book gave one: without
    /// it settlement multiplies the legs, which is also how it reprices after a push.
    /// </summary>
    public ParlayDraft ToParlayDraft()
        => new(ParlayBook ?? string.Empty, ParlayStake, ParlayPriceQuoted, null, Legs.Select(l => l.ToDraft(0m)).ToList());

    /// <summary>Takes logged items off the slip, in one change. Whatever was not logged stays.</summary>
    public void Logged(IEnumerable<Guid> ids)
    {
        var gone = ids.ToHashSet();
        if (_items.RemoveAll(i => gone.Contains(i.Id)) > 0)
            Raise();
    }

    private void Raise() => Changed?.Invoke();
}
```

In `src/LineOps.Web/Program.cs`, after the `AddScoped<…IDeskSearch, …AppDeskSearch>()` line add:

```csharp
// The slip: one per desk session. Scoped is circuit-scoped under Interactive Server, so every
// window on this desk shares it and it goes with the circuit (spec §3.1).
builder.Services.AddScoped<LineOps.Web.Services.WagerSlip>();
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LineOps.Web.Tests --filter "FullyQualifiedName~WagerSlipTests"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/LineOps.Web/Services/SelectionText.cs src/LineOps.Web/Services/WagerSlip.cs src/LineOps.Web/Program.cs tests/LineOps.Web.Tests/WagerSlipTests.cs
git commit -m "feat(slip): a slip that belongs to the desk session, and the rules it owns" -m "WagerSlip holds the wagers banked this session and is the only owner of the list, so its rules need no window to remember them: a closing price stays but never logs, a straight bet needs a stake, a parlay's legs are what the market still offers, and the combined price is the legs multiplied unless the book quoted its own. Circuit-scoped and unpersisted by design."
```

---

### Task 5: The advisory checks, as a pure builder

**Files:**
- Create: `src/LineOps.Web/Services/WagerOffer.cs`
- Create: `src/LineOps.Web/Components/Desk/Checks.cs`
- Test: `tests/LineOps.Web.Tests/AdvisoryChecksTests.cs` (create)

**Interfaces:**
- Consumes: `SlipItem`, `SelectionText` (Task 4); `BookPrice.CapturedAt` (Task 2).
- Produces:
  - `LineOps.Web.Services.WagerOffer(string Market, string Outcome, decimal? Line, string Book, int PriceAmerican, DateTimeOffset CapturedAt, bool IsClosing, FairPrice? Fair, double? Ev, int BooksQuoting, bool IsBest)` with `Label`, `static WagerOffer Best(string market, BestOffer offer)`, `static WagerOffer? AtBook(string market, BestOffer offer, string book)`, `EntryDraft ToDraft(int gameId, decimal stake, string? note)`, `SlipItem ToSlipItem(Game game, string matchup, decimal stake, string? note)`.
  - `LineOps.Web.Components.Desk.Check(DeskState State, string Text, string? Remedy = null, Func<Task>? Act = null)`
  - `LineOps.Web.Components.Desk.CheckActions(string? PullLabel = null, Func<Task>? Pull = null, Func<Task>? OpenJournal = null)`
  - `static class Checks { TimeSpan StaleAfter; TimeSpan DuplicateWindow; const string OpenJournal; IReadOnlyList<Check> Build(WagerOffer offer, DateTimeOffset startsAt, DateTimeOffset now, bool priorRead, JournalEntry? prior, decimal stake, CheckActions? actions = null); Check Value(WagerOffer, decimal); Check Age(WagerOffer, DateTimeOffset, CheckActions); Check Closing(DateTimeOffset, DateTimeOffset, CheckActions); Check Duplicate(bool, JournalEntry?, CheckActions); string Span(TimeSpan); }`. `Build` returns three rows in order: value, price age (or closing line when `offer.IsClosing`), duplicate.

- [ ] **Step 1: Write the failing tests** — create `tests/LineOps.Web.Tests/AdvisoryChecksTests.cs`:

```csharp
using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Desk.Primitives;
using LineOps.Web.Components.Desk;
using LineOps.Web.Services;

namespace LineOps.Web.Tests;

/// <summary>
/// The checks the wager window runs before Log. Pure: an offer, a clock and a journal reading in,
/// rows out. Only the closing line blocks; every other row is a fact the operator may act against.
/// </summary>
public class AdvisoryChecksTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 17, 0, 0, TimeSpan.Zero);
    private static readonly FairPrice Pinnacle = new(0.576, -136, FairValue.SharpBook, 1);

    private static WagerOffer Offer(TimeSpan? age = null, bool closing = false, double? ev = 0.027, FairPrice? fair = null, bool noFair = false)
        => new(Markets.Moneyline, "Kansas City Chiefs", null, "draftkings", -128,
            Now - (age ?? TimeSpan.FromMinutes(12)), closing, noFair ? null : fair ?? Pinnacle, ev, 4, true);

    private static readonly CheckActions Actions = new("Pull NFL lines · 2 credits", () => Task.CompletedTask, () => Task.CompletedTask);

    [Fact]
    public void A_live_offer_reads_value_age_and_the_journal_in_that_order()
    {
        var rows = Checks.Build(Offer(), Now.AddHours(3), Now, priorRead: true, prior: null, stake: 0m, Actions);

        Assert.Equal(3, rows.Count);
        Assert.StartsWith("Value:", rows[0].Text);
        Assert.StartsWith("Price captured", rows[1].Text);
        Assert.Equal("Nothing logged on this side yet.", rows[2].Text);
    }

    [Fact]
    public void Value_against_a_fair_price_is_positive_and_names_what_it_is_measured_against()
    {
        var value = Checks.Value(Offer(), 0m);

        Assert.Equal(DeskState.Positive, value.State);
        Assert.Equal("Value: +2.7% against fair -136 (Pinnacle, no-vig).", value.Text);
    }

    [Fact]
    public void No_value_is_a_warning_not_a_block()
    {
        var value = Checks.Value(Offer(ev: -0.076, fair: new FairPrice(0.42, 136, FairValue.SharpBook, 1)), 0m);

        Assert.Equal(DeskState.Warning, value.State);
        Assert.Equal("No value: \u22127.6% against fair +136 (Pinnacle, no-vig).", value.Text);
    }

    [Fact]
    public void The_stake_adds_what_the_value_comes_to()
    {
        var value = Checks.Value(Offer(), 50m);

        Assert.EndsWith($" expected on {50m:C0}.", value.Text);
        Assert.Contains($"+{1.35m:C2}", value.Text);
    }

    [Fact]
    public void No_fair_price_is_unread_rather_than_zero()
    {
        var value = Checks.Value(Offer(ev: null, noFair: true), 50m);

        Assert.Equal(DeskState.Neutral, value.State);
        Assert.Equal("No fair price on this number: fewer than two books quote it, so value is unread rather than zero.", value.Text);
    }

    [Fact]
    public void Twenty_nine_minutes_is_fresh()
    {
        var age = Checks.Age(Offer(TimeSpan.FromMinutes(29)), Now, Actions);

        Assert.Equal(DeskState.Positive, age.State);
        Assert.Equal("Price captured 29 min ago.", age.Text);
        Assert.Null(age.Remedy);
    }

    [Fact]
    public void Thirty_minutes_is_stale_and_offers_the_pull()
    {
        var age = Checks.Age(Offer(TimeSpan.FromMinutes(30)), Now, Actions);

        Assert.Equal(DeskState.Warning, age.State);
        Assert.Equal("Price captured 30 min ago; the line may have moved since.", age.Text);
        Assert.Equal("Pull NFL lines · 2 credits", age.Remedy);
        Assert.NotNull(age.Act);
    }

    [Fact]
    public void Hours_read_as_hours_and_minutes()
    {
        var age = Checks.Age(Offer(new TimeSpan(2, 14, 0)), Now, Actions);
        Assert.Equal("Price captured 2h 14m ago; the line may have moved since.", age.Text);
    }

    [Fact]
    public void A_stale_price_without_a_pull_to_offer_has_no_remedy()
    {
        var age = Checks.Age(Offer(TimeSpan.FromHours(1)), Now, new CheckActions());
        Assert.Null(age.Remedy);
        Assert.Null(age.Act);
    }

    [Fact]
    public void A_closing_line_replaces_the_age_row_and_blocks()
    {
        var rows = Checks.Build(Offer(closing: true), Now.AddMinutes(-42), Now, true, null, 0m, Actions);

        Assert.Equal(3, rows.Count);
        Assert.Equal(DeskState.Negative, rows[1].State);
        Assert.Equal("Closing line: the game started 42 min ago, so this price cannot have been taken.", rows[1].Text);
        Assert.Equal(Checks.OpenJournal, rows[1].Remedy);
        Assert.DoesNotContain(rows, r => r.Text.StartsWith("Price captured", StringComparison.Ordinal));
    }

    [Fact]
    public void A_prior_bet_on_the_side_is_named_with_its_consequence()
    {
        var prior = new JournalEntry
        {
            GameId = 7, Market = Markets.Moneyline, Outcome = "Kansas City Chiefs", Book = "draftkings",
            PriceTaken = -130, Stake = 50m, PlacedAt = Now.AddHours(-1)
        };

        var duplicate = Checks.Duplicate(true, prior, Actions);

        Assert.Equal(DeskState.Warning, duplicate.State);
        Assert.StartsWith($"Already in the journal: {50m:C0} on ML · Chiefs at -130 (draftkings), ", duplicate.Text);
        Assert.EndsWith(". Logging again records a second bet.", duplicate.Text);
        Assert.Equal(Checks.OpenJournal, duplicate.Remedy);
    }

    [Fact]
    public void An_unread_journal_says_so_rather_than_clearing_the_side()
    {
        var duplicate = Checks.Duplicate(false, null, Actions);
        Assert.Equal(DeskState.Neutral, duplicate.State);
    }

    [Fact]
    public void A_rung_at_another_book_is_that_books_price_and_age()
    {
        var captured = Now.AddMinutes(-40);
        var best = new LineOps.Data.CrossReference.BestOffer("Kansas City Chiefs", "draftkings", -128, null, Now.AddMinutes(-12),
            null, false, false,
            [
                new LineOps.Data.CrossReference.BookPrice("draftkings", -128, null, 0.56) { Ev = 0.027 },
                new LineOps.Data.CrossReference.BookPrice("fanduel", -135, null, 0.574) { Ev = -0.01, CapturedAt = captured }
            ]) { Fair = Pinnacle, Ev = 0.027 };

        var rung = WagerOffer.AtBook(Markets.Moneyline, best, "FanDuel")!;

        Assert.Equal(-135, rung.PriceAmerican);
        Assert.Equal("fanduel", rung.Book);
        Assert.Equal(captured, rung.CapturedAt);
        Assert.Equal(-0.01, rung.Ev);
        Assert.False(rung.IsBest);
        Assert.Null(WagerOffer.AtBook(Markets.Moneyline, best, "caesars"));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LineOps.Web.Tests --filter "FullyQualifiedName~AdvisoryChecksTests"`
Expected: build error `The type or namespace name 'WagerOffer' could not be found`.

- [ ] **Step 3: Implement.** Create `src/LineOps.Web/Services/WagerOffer.cs`:

```csharp
using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Ingestion.Services;

namespace LineOps.Web.Services;

/// <summary>
/// The one price a wager is logged against: a side's best offer, or one book's rung of it when
/// a door named the book. Everything the checks read and the journal writes comes from here, so
/// the number on screen and the number logged cannot differ.
/// </summary>
public sealed record WagerOffer(
    string Market,
    string Outcome,
    decimal? Line,
    string Book,
    int PriceAmerican,
    DateTimeOffset CapturedAt,
    bool IsClosing,
    FairPrice? Fair,
    double? Ev,
    int BooksQuoting,
    bool IsBest)
{
    public string Label => SelectionText.Of(Market, Outcome, Line);

    public static WagerOffer Best(string market, BestOffer offer)
        => new(market, offer.Outcome, offer.Line, offer.Book, offer.PriceAmerican, offer.CapturedAt,
            offer.IsClosing, offer.Fair, offer.Ev, offer.Rungs.Count, IsBest: true);

    /// <summary>
    /// One book's price on this side, or null when that book does not quote it. Its own number,
    /// capture time and value; the side's fair price only when the book is on the same number.
    /// </summary>
    public static WagerOffer? AtBook(string market, BestOffer offer, string book)
    {
        var rung = offer.Rungs.FirstOrDefault(r => string.Equals(r.Book, book, StringComparison.OrdinalIgnoreCase));
        if (rung is null)
            return null;

        return new WagerOffer(market, offer.Outcome, rung.Line, rung.Book, rung.PriceAmerican,
            rung.CapturedAt ?? offer.CapturedAt, offer.IsClosing,
            rung.Line == offer.Line ? offer.Fair : null, rung.Ev, offer.Rungs.Count,
            IsBest: string.Equals(rung.Book, offer.Book, StringComparison.OrdinalIgnoreCase));
    }

    public EntryDraft ToDraft(int gameId, decimal stake, string? note)
        => new(gameId, Market, null, Outcome, Line, PriceAmerican, stake, Book,
            string.IsNullOrWhiteSpace(note) ? null : note);

    public SlipItem ToSlipItem(Game game, string matchup, decimal stake, string? note)
        => new(Guid.Empty, game.Id, matchup, game.StartsAt, game.Sport?.Key ?? string.Empty,
            Market, Outcome, Line, Book, PriceAmerican, CapturedAt, IsClosing, stake,
            string.IsNullOrWhiteSpace(note) ? null : note, Fair, Ev);
}
```

Create `src/LineOps.Web/Components/Desk/Checks.cs`:

```csharp
using System.Globalization;
using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Desk.Primitives;
using LineOps.Web.Services;

namespace LineOps.Web.Components.Desk;

/// <summary>One advisory row: a state, one sentence, and optionally a remedy key on the same row.</summary>
public sealed record Check(DeskState State, string Text, string? Remedy = null, Func<Task>? Act = null);

/// <summary>
/// What a remedy key does, supplied by the window that renders the strip. The builder is pure; it
/// cannot run a pull or open a window, only say which key belongs on which row.
/// </summary>
public sealed record CheckActions(string? PullLabel = null, Func<Task>? Pull = null, Func<Task>? OpenJournal = null);

/// <summary>
/// The checks the wager window runs before Log (spec §4). Each is keyed on what it reads — the
/// selection, the odds, the journal — and none reads the stake except to say what the value comes
/// to. Only the closing line blocks: it is a fact about the price, not a judgment the operator
/// may overrule. Sentences state a fact and its consequence; they never instruct.
/// </summary>
public static class Checks
{
    /// <summary>
    /// When a price stops being fresh. A line scan costs credits and is run by hand; half an hour
    /// is the shortest interval at which spending two credits is a reasonable thing to suggest.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    /// <summary>How far back a bet on the same side counts as already logged.</summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromDays(7);

    public const string OpenJournal = "Open Journal";

    /// <summary>Value, then price age (or the closing line, which replaces it), then the journal.</summary>
    public static IReadOnlyList<Check> Build(
        WagerOffer offer, DateTimeOffset startsAt, DateTimeOffset now,
        bool priorRead, JournalEntry? prior, decimal stake, CheckActions? actions = null)
    {
        actions ??= new CheckActions();

        return
        [
            Value(offer, stake),
            offer.IsClosing ? Closing(startsAt, now, actions) : Age(offer, now, actions),
            Duplicate(priorRead, prior, actions)
        ];
    }

    public static Check Value(WagerOffer offer, decimal stake)
    {
        if (offer.Ev is not { } ev)
            return new Check(DeskState.Neutral,
                "No fair price on this number: fewer than two books quote it, so value is unread rather than zero.");

        var against = offer.Fair is { } fair
            ? $" against fair {OddsMath.FormatPrice(fair.PriceAmerican)} ({Basis(fair)}, no-vig)"
            : " against the fair price";

        var expected = stake > 0
            ? $" · {Signed((decimal)ev * stake)} expected on {stake.ToString("C0", CultureInfo.CurrentCulture)}"
            : "";

        return ev > 0
            ? new Check(DeskState.Positive, $"Value: {Percent(ev)}{against}{expected}.")
            : new Check(DeskState.Warning, $"No value: {Percent(ev)}{against}{expected}.");
    }

    public static Check Age(WagerOffer offer, DateTimeOffset now, CheckActions actions)
    {
        var age = now - offer.CapturedAt;

        if (age < StaleAfter)
            return new Check(DeskState.Positive,
                age < TimeSpan.FromMinutes(1) ? "Price captured just now." : $"Price captured {Span(age)} ago.");

        var canPull = actions.PullLabel is not null && actions.Pull is not null;

        return new Check(DeskState.Warning,
            $"Price captured {Span(age)} ago; the line may have moved since.",
            canPull ? actions.PullLabel : null,
            canPull ? actions.Pull : null);
    }

    public static Check Closing(DateTimeOffset startsAt, DateTimeOffset now, CheckActions actions)
    {
        var since = now - startsAt;

        var text = since > TimeSpan.Zero
            ? $"Closing line: the game started {Span(since)} ago, so this price cannot have been taken."
            : "Closing line: this is where the market closed, so this price cannot have been taken.";

        return new Check(DeskState.Negative, text,
            actions.OpenJournal is not null ? OpenJournal : null, actions.OpenJournal);
    }

    public static Check Duplicate(bool read, JournalEntry? prior, CheckActions actions)
    {
        if (!read)
            return new Check(DeskState.Neutral, "Reading the journal for this side.");

        if (prior is null)
            return new Check(DeskState.Positive, "Nothing logged on this side yet.");

        var what = SelectionText.Of(prior.Market, prior.Outcome, prior.LineTaken);

        return new Check(DeskState.Warning,
            $"Already in the journal: {prior.Stake.ToString("C0", CultureInfo.CurrentCulture)} on {what} at "
            + $"{OddsMath.FormatPrice(prior.PriceTaken)} ({prior.Book}), {DisplayTime.Stamp(prior.PlacedAt)}. "
            + "Logging again records a second bet.",
            actions.OpenJournal is not null ? OpenJournal : null, actions.OpenJournal);
    }

    /// <summary>"12 min", "2h 14m" — whole minutes, so a threshold reads the same on screen as in a test.</summary>
    public static string Span(TimeSpan span)
        => span.TotalMinutes < 60
            ? $"{(int)span.TotalMinutes} min"
            : $"{(int)span.TotalHours}h {span.Minutes}m";

    private static string Basis(FairPrice fair)
        => fair.Basis == FairValue.SharpBook ? "Pinnacle" : $"{fair.Books}-book consensus";

    private static string Percent(double ev)
    {
        var percent = ev * 100;
        return (percent >= 0 ? "+" : "\u2212") + Math.Abs(percent).ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    private static string Signed(decimal amount)
        => (amount >= 0 ? "+" : "\u2212") + Math.Abs(amount).ToString("C2", CultureInfo.CurrentCulture);
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LineOps.Web.Tests --filter "FullyQualifiedName~AdvisoryChecksTests"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/LineOps.Web/Services/WagerOffer.cs src/LineOps.Web/Components/Desk/Checks.cs tests/LineOps.Web.Tests/AdvisoryChecksTests.cs
git commit -m "feat(wager): the checks worth having before Log, as one pure builder" -m "Value against the fair price, how old the price is, whether the side is already in the journal, and whether the price is a close that could not have been taken. Pure, so the thresholds are tested at their edges (29 against 30 minutes, no fair price, a closing line) without rendering anything. Only the closing line blocks; the rest advise. WagerOffer is the one price a wager is logged against, the best offer or a named book's rung."
```

---

### Task 6: Shared primitives — `SelectionChips` and `StakeRow`

**Files:**
- Create: `src/LineOps.Web/Services/SelectionChoice.cs`
- Create: `src/LineOps.Web/Components/Desk/SelectionChips.razor`
- Create: `src/LineOps.Web/Components/Desk/StakeRow.razor`
- Create: `tests/LineOps.Web.Tests/Support/Repo.cs`, `tests/LineOps.Web.Tests/Support/BoardRows.cs`
- Test: `tests/LineOps.Web.Tests/SharedControlsTests.cs`, `tests/LineOps.Web.Tests/SelectionGuardTests.cs` (create)

**Interfaces:**
- Consumes: `WagerOffer` (Task 5), `SelectionText` (Task 4).
- Produces:
  - `LineOps.Web.Services.SelectionChoice(string Market, BestOffer Offer)` with `Label`, `static IReadOnlyList<SelectionChoice> All(BoardRow? row)`, `static WagerOffer? Land(BoardRow? row, string? market, string? outcome, string? book)` — with `market`+`outcome` lands on that side (the named book's rung if `book` is quoted there, else the best); otherwise the most value, then the widest edge (today's `BoardWager` default).
  - `<SelectionChips Row Market Outcome OnChoose />` — `OnChoose` is `EventCallback<SelectionChoice>`. Renders `div.selection-chips` of small `DeskButton`s, the chosen one Tinted with `aria-pressed="true"`.
  - `<StakeRow Stake StakeChanged PriceAmerican />` — `decimal Stake`, `EventCallback<decimal> StakeChanged`, `int? PriceAmerican`. Renders the stake field (`placeholder="Stake"`, `Immediate`, `Mono`, `type=number step=5 min=0`), quick keys $25/$50/$100, and `span.stake-row__returns` when a price and a stake are set.
  - Test support: `Repo.Root()`; `BoardRows.Row(...)`, `BoardRows.Offer(...)`, `BoardRows.Rung(...)`, constants `GameId = 7`, `Home = "Kansas City Chiefs"`, `Away = "Buffalo Bills"`, `FairHomeMl`.

- [ ] **Step 1: Write the test support.** Create `tests/LineOps.Web.Tests/Support/Repo.cs`:

```csharp
namespace LineOps.Web.Tests.Support;

/// <summary>The repository root, for tests that read source rather than render it.</summary>
public static class Repo
{
    public static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LineOps.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("LineOps.slnx not found above the test output.");
    }
}
```

Create `tests/LineOps.Web.Tests/Support/BoardRows.cs`:

```csharp
using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;

namespace LineOps.Web.Tests.Support;

/// <summary>
/// A board row to render the wager window and the slip against. The standard row is an NFL game,
/// Bills at Chiefs, whose most valuable side is the Chiefs' moneyline at DraftKings, -128, worth
/// +2.7% against a Pinnacle fair price of -136; FanDuel also quotes it at -135, captured 40 minutes ago.
/// </summary>
public static class BoardRows
{
    public const int GameId = 7;
    public const string Home = "Kansas City Chiefs";
    public const string Away = "Buffalo Bills";

    public static readonly FairPrice FairHomeMl = new(0.576, -136, FairValue.SharpBook, 1);

    public static Game Game(DateTimeOffset? startsAt = null) => new()
    {
        Id = GameId,
        Sport = new Sport { Key = "nfl", Name = "NFL" },
        HomeTeam = new Team { Name = Home, Abbrev = "KC" },
        AwayTeam = new Team { Name = Away, Abbrev = "BUF" },
        StartsAt = startsAt ?? DateTimeOffset.UtcNow.AddHours(3),
        Status = GameStatus.Scheduled
    };

    public static BookPrice Rung(string book, int american, decimal? line = null, double? ev = null, DateTimeOffset? capturedAt = null)
        => new(book, american, line, OddsMath.ImpliedProbability(american)) { Ev = ev, CapturedAt = capturedAt };

    public static BestOffer Offer(string outcome, string book, int american, decimal? line = null,
        double? ev = null, FairPrice? fair = null, bool closing = false, TimeSpan? age = null, BookPrice[]? rungs = null)
    {
        var captured = DateTimeOffset.UtcNow - (age ?? TimeSpan.FromMinutes(12));
        return new BestOffer(outcome, book, american, line, captured, null, false, closing,
            rungs ?? [Rung(book, american, line, ev, captured)])
        {
            Fair = fair,
            Ev = ev
        };
    }

    public static BoardRow Row(int homeMl = -128, TimeSpan? age = null, bool closing = false, DateTimeOffset? startsAt = null)
    {
        var ageOf = age ?? TimeSpan.FromMinutes(12);

        var homeMlOffer = Offer(Home, "draftkings", homeMl, ev: 0.027, fair: FairHomeMl, closing: closing, age: ageOf,
            rungs:
            [
                Rung("draftkings", homeMl, ev: 0.027, capturedAt: DateTimeOffset.UtcNow - ageOf),
                Rung("fanduel", -135, ev: -0.01, capturedAt: DateTimeOffset.UtcNow.AddMinutes(-40))
            ]);

        return new BoardRow(
            Game(startsAt),
            new MarketPair(Markets.Moneyline, homeMlOffer,
                Offer(Away, "fanduel", 110, ev: -0.02, closing: closing, age: ageOf)),
            new MarketPair(Markets.Spread,
                Offer(Home, "draftkings", -110, line: -2.5m, closing: closing, age: ageOf),
                Offer(Away, "fanduel", -105, line: 2.5m, ev: 0.01, closing: closing, age: ageOf)),
            new MarketPair(Markets.Total,
                Offer("Over", "draftkings", -110, line: 47.5m, closing: closing, age: ageOf),
                Offer("Under", "fanduel", -110, line: 47.5m, closing: closing, age: ageOf)),
            BookCount: 2,
            PricedAt: DateTimeOffset.UtcNow - ageOf,
            PricesAreClosing: closing,
            Unpriced: null);
    }
}
```

- [ ] **Step 2: Write the failing tests.** Create `tests/LineOps.Web.Tests/SharedControlsTests.cs`:

```csharp
using Bunit;
using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Web.Components.Desk;
using LineOps.Web.Services;
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>The selection strip and the stake row: one definition each, shared by every window that logs.</summary>
public class SharedControlsTests : DeskTestContext
{
    [Fact]
    public void The_strip_lists_every_side_and_tints_the_chosen_one()
    {
        var cut = RenderComponent<SelectionChips>(p => p
            .Add(x => x.Row, BoardRows.Row())
            .Add(x => x.Market, Markets.Spread)
            .Add(x => x.Outcome, BoardRows.Away));

        var chips = cut.FindAll(".selection-chips button");
        Assert.Equal(6, chips.Count);

        var chosen = Assert.Single(cut.FindAll(".selection-chips button.desk-btn--tinted"));
        Assert.Equal("Spread · Bills +2.5", chosen.TextContent.Trim());
        Assert.Equal("true", chosen.GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Pressing_a_chip_reports_the_side()
    {
        SelectionChoice? picked = null;
        var cut = RenderComponent<SelectionChips>(p => p
            .Add(x => x.Row, BoardRows.Row())
            .Add(x => x.OnChoose, (SelectionChoice c) => picked = c));

        cut.FindAll(".selection-chips button").First(b => b.TextContent.Trim() == "Total · Over o47.5").Click();

        Assert.NotNull(picked);
        Assert.Equal(Markets.Total, picked.Market);
        Assert.Equal("Over", picked.Offer.Outcome);
    }

    [Fact]
    public void A_quick_key_sets_the_stake()
    {
        decimal? stake = null;
        var cut = RenderComponent<StakeRow>(p => p
            .Add(x => x.Stake, 0m)
            .Add(x => x.StakeChanged, (decimal s) => stake = s)
            .Add(x => x.PriceAmerican, -110));

        cut.FindAll("button").First(b => b.TextContent.Trim() == 50m.ToString("C0")).Click();

        Assert.Equal(50m, stake);
    }

    [Fact]
    public void Returns_sit_beside_the_stake_once_there_is_one()
    {
        var none = RenderComponent<StakeRow>(p => p.Add(x => x.Stake, 0m).Add(x => x.PriceAmerican, -110));
        Assert.Empty(none.FindAll(".stake-row__returns"));

        var some = RenderComponent<StakeRow>(p => p.Add(x => x.Stake, 100m).Add(x => x.PriceAmerican, -110));
        Assert.Contains(OddsMath.PayoutOnWin(-110, 100m).ToString("C0"), some.Find(".stake-row__returns").TextContent);
    }

    [Fact]
    public void The_stake_field_is_immediate_numeric_and_named()
    {
        var cut = RenderComponent<StakeRow>(p => p.Add(x => x.Stake, 0m));
        var input = cut.Find("input[placeholder='Stake']");

        Assert.Equal("number", input.GetAttribute("type"));
        Assert.Equal("5", input.GetAttribute("step"));
    }

    [Fact]
    public void A_door_lands_on_its_side_and_its_book()
    {
        var row = BoardRows.Row();

        Assert.Equal(-128, SelectionChoice.Land(row, null, null, null)!.PriceAmerican);
        Assert.Equal(-105, SelectionChoice.Land(row, Markets.Spread, BoardRows.Away, null)!.PriceAmerican);
        Assert.Equal(-135, SelectionChoice.Land(row, Markets.Moneyline, BoardRows.Home, "fanduel")!.PriceAmerican);
        // A book that does not quote the side falls back to the best, not to nothing.
        Assert.Equal(-128, SelectionChoice.Land(row, Markets.Moneyline, BoardRows.Home, "caesars")!.PriceAmerican);
    }
}
```

Create `tests/LineOps.Web.Tests/SelectionGuardTests.cs`:

```csharp
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// One control, one definition (spec §7). The selection strip and the stake row were drawn three
/// times — the wager window, the Journal's entry form, the Journal's parlay form — and drifted.
/// Read from the source, the way GridConventionTests enforces the grid's rules.
/// </summary>
public class SelectionGuardTests
{
    [Theory]
    [InlineData("SelectionChips")]
    [InlineData("StakeRow")]
    public void Each_shared_control_has_one_definition(string name)
    {
        var files = Directory.EnumerateFiles(Path.Combine(Repo.Root(), "src"), name + ".razor", SearchOption.AllDirectories).ToList();

        var only = Assert.Single(files);
        Assert.EndsWith(Path.Combine("LineOps.Web", "Components", "Desk", name + ".razor"), only);
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test tests/LineOps.Web.Tests --filter "FullyQualifiedName~SharedControlsTests|FullyQualifiedName~SelectionGuardTests"`
Expected: build error `The type or namespace name 'SelectionChips' could not be found`.

- [ ] **Step 4: Implement.** Create `src/LineOps.Web/Services/SelectionChoice.cs`:

```csharp
using LineOps.Data.CrossReference;

namespace LineOps.Web.Services;

/// <summary>One side of one market on a board row: what a selection chip stands for.</summary>
public sealed record SelectionChoice(string Market, BestOffer Offer)
{
    public string Label => SelectionText.Of(Market, Offer.Outcome, Offer.Line);

    /// <summary>Every priced side of the row, moneyline then spread then total, home/over first.</summary>
    public static IReadOnlyList<SelectionChoice> All(BoardRow? row)
    {
        if (row is null)
            return [];

        var choices = new List<SelectionChoice>(6);

        foreach (var pair in new[] { row.Moneyline, row.Spread, row.Total })
        {
            if (pair.First is { } first)
                choices.Add(new SelectionChoice(pair.Market, first));

            if (pair.Second is { } second)
                choices.Add(new SelectionChoice(pair.Market, second));
        }

        return choices;
    }

    /// <summary>
    /// Where a wager window opens. A door that names a side lands on it, and on the named book's
    /// price where that book quotes the side. Otherwise the side with the most value against the
    /// fair price; where nothing can be scored, the widest spread between books — they disagree,
    /// which is the next best reason the game is worth a look. A door that lands on the wrong side
    /// costs a click: every side is still on the strip.
    /// </summary>
    public static WagerOffer? Land(BoardRow? row, string? market, string? outcome, string? book)
    {
        var choices = All(row);

        if (market is not null && outcome is not null
            && choices.FirstOrDefault(c => c.Market == market
                                           && string.Equals(c.Offer.Outcome, outcome, StringComparison.OrdinalIgnoreCase)) is { } asked)
        {
            return (book is not null ? WagerOffer.AtBook(asked.Market, asked.Offer, book) : null)
                   ?? WagerOffer.Best(asked.Market, asked.Offer);
        }

        return choices
            .OrderByDescending(c => c.Offer.Ev ?? double.MinValue)
            .ThenByDescending(c => c.Offer.EdgePoints ?? 0)
            .Select(c => WagerOffer.Best(c.Market, c.Offer))
            .FirstOrDefault();
    }
}
```

Create `src/LineOps.Web/Components/Desk/SelectionChips.razor`:

```razor
@using LineOps.Data.CrossReference
@using LineOps.Web.Services

@*
    The six-chip selection strip: every priced side of a game, one chip each.

    The chosen side is Tinted rather than Filled: this is a selector reporting which side you are
    on, not the thing the window commits. The Filled key belongs to Log. One definition, shared by
    every window that logs a wager (SelectionGuardTests).
*@

<div class="row selection-chips">
    @foreach (var choice in SelectionChoice.All(Row))
    {
        var chosen = IsChosen(choice);
        <DeskButton Size="DeskKeySize.Small"
                    Emphasis="@(chosen ? DeskEmphasis.Tinted : DeskEmphasis.Plain)"
                    aria-pressed="@(chosen ? "true" : "false")"
                    OnClick="() => OnChoose.InvokeAsync(choice)">
            @choice.Label
        </DeskButton>
    }
</div>

@code {
    [Parameter] public BoardRow? Row { get; set; }

    /// <summary>The chosen side's market (<c>Markets.*</c>). Null: nothing chosen.</summary>
    [Parameter] public string? Market { get; set; }

    [Parameter] public string? Outcome { get; set; }

    [Parameter] public EventCallback<SelectionChoice> OnChoose { get; set; }

    private bool IsChosen(SelectionChoice choice)
        => choice.Market == Market && string.Equals(choice.Offer.Outcome, Outcome, StringComparison.OrdinalIgnoreCase);
}
```

Create `src/LineOps.Web/Components/Desk/StakeRow.razor`:

```razor
@using LineOps.Core.Analytics

@*
    The stake: a field, three quick keys, and what it returns.

    Immediate, because Returns beside it recomputes as you type — a field whose typing is the
    action, not one waiting to be validated. Returns sits to the right, which is where the eye goes
    after typing. One definition, shared by the wager window, the slip's parlay block and the
    Journal's manual entry (SelectionGuardTests).
*@

<div class="row stake-row">
    <DeskField TValue="decimal" Mono="true" Immediate="true"
               type="number" step="5" min="0" style="max-width:130px"
               placeholder="Stake" Value="Stake" ValueChanged="SetAsync" />

    @foreach (var quick in Quick)
    {
        <DeskButton Size="DeskKeySize.Small" OnClick="() => SetAsync(quick)">@quick.ToString("C0")</DeskButton>
    }

    @if (PriceAmerican is { } price && price != 0 && Stake > 0)
    {
        <span class="stake-row__returns num">
            Returns <strong>@OddsMath.PayoutOnWin(price, Stake).ToString("C0")</strong>
            <span class="dim">· @OddsMath.ProfitOnWin(price, Stake).ToString("C0") profit</span>
        </span>
    }
</div>

@code {
    private static readonly decimal[] Quick = [25m, 50m, 100m];

    [Parameter] public decimal Stake { get; set; }

    [Parameter] public EventCallback<decimal> StakeChanged { get; set; }

    /// <summary>The price the stake would be paid at. Null: no Returns (nothing to price it at yet).</summary>
    [Parameter] public int? PriceAmerican { get; set; }

    private Task SetAsync(decimal stake) => StakeChanged.InvokeAsync(stake);
}
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test tests/LineOps.Web.Tests --filter "FullyQualifiedName~SharedControlsTests|FullyQualifiedName~SelectionGuardTests"`
Expected: all PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/LineOps.Web/Services/SelectionChoice.cs src/LineOps.Web/Components/Desk/SelectionChips.razor src/LineOps.Web/Components/Desk/StakeRow.razor tests/LineOps.Web.Tests/Support/Repo.cs tests/LineOps.Web.Tests/Support/BoardRows.cs tests/LineOps.Web.Tests/SharedControlsTests.cs tests/LineOps.Web.Tests/SelectionGuardTests.cs
git commit -m "feat(desk): the selection strip and the stake row have one definition each" -m "The wager window, the slip and the Journal's manual entry each drew their own stake field, and the wager window its own chip strip. SelectionChips and StakeRow are the one copy of each, with a guard test that they stay single. SelectionChoice.Land is where a door opens the wager window: the side it names at the book it names, or the most value."
```

---

### Task 7: The wager window becomes a panel, opened on the side a door names

**Files:**
- Create: `src/LineOps.Web/Services/WagerData.cs`
- Rewrite: `src/LineOps.Web/Components/Panels/BoardWager.razor`
- Modify: `src/LineOps.Web/Program.cs` (next to the `WagerSlip` registration from Task 4)
- Modify: `src/LineOps.Desk/wwwroot/css/lineops.css` (append)
- Create: `tests/LineOps.Web.Tests/Support/FakeWagerData.cs`, `Support/FakeAlerts.cs`, `Support/WagerTestContext.cs`
- Test: `tests/LineOps.Web.Tests/BoardWagerTests.cs` (create)

**Interfaces:**
- Consumes: `SelectionChoice.Land`, `SelectionChips`, `StakeRow` (Task 6); `WagerOffer` (Task 5); `JournalService.SaveEntriesAsync`, `LatestOnSideAsync` (Task 3).
- Produces:
  - `LineOps.Web.Services.IWagerData`:
    ```csharp
    Task<BoardRow?> RowAsync(int gameId, CancellationToken ct = default);
    Task<JournalEntry?> LatestOnSideAsync(int gameId, string market, string outcome, string book, DateTimeOffset since, CancellationToken ct = default);
    Task LogStraightsAsync(IReadOnlyList<EntryDraft> bets, CancellationToken ct = default);
    Task LogParlayAsync(ParlayDraft parlay, CancellationToken ct = default);
    Task<IngestionJob?> LinesJobAsync(string sportKey, CancellationToken ct = default);
    Task<JobOutcome> RunJobAsync(string key, CancellationToken ct = default);
    ```
  - `BoardWager` parameters: `int GameId` (required), `string? Market`, `string? Outcome`, `string? Book`, `EventCallback OnPlaced`. Watches `DataTopics.Odds` and `DataTopics.Journal`.
  - Markup hooks used by tests: `.wager-offer__price`, `.wager-offer__book`, `.wager-why`, `.wager-logged`, the Log key is the only `button.desk-btn--filled`.
  - Test support: `FakeWagerData`, `FakeAlerts`, `WagerTestContext` (registers the fake as `IWagerData`, `WagerSlip`, `DeskToasts`, `IDeskAlerts`).

- [ ] **Step 1: Write the test support.** Create `tests/LineOps.Web.Tests/Support/FakeWagerData.cs`:

```csharp
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Ingestion.Services;
using LineOps.Web.Services;

namespace LineOps.Web.Tests.Support;

/// <summary>The data seam, in memory: a row to price, a journal reading, and a record of what was written.</summary>
public sealed class FakeWagerData : IWagerData
{
    public BoardRow? Row { get; set; } = BoardRows.Row();

    /// <summary>Set to hold <see cref="RowAsync"/> open, so a test can see the loading state.</summary>
    public TaskCompletionSource? RowGate { get; set; }

    public int RowReads { get; private set; }
    public JournalEntry? Latest { get; set; }
    public int LatestReads { get; private set; }
    public IngestionJob? LinesJob { get; set; }
    public List<string> JobsRun { get; } = [];
    public List<EntryDraft> Straights { get; } = [];
    public List<ParlayDraft> Parlays { get; } = [];

    /// <summary>Set to make the next writes fail with this.</summary>
    public Exception? FailWith { get; set; }

    public async Task<BoardRow?> RowAsync(int gameId, CancellationToken ct = default)
    {
        RowReads++;

        if (RowGate is { } gate)
            await gate.Task;

        return Row;
    }

    public Task<JournalEntry?> LatestOnSideAsync(int gameId, string market, string outcome, string book, DateTimeOffset since, CancellationToken ct = default)
    {
        LatestReads++;
        return Task.FromResult(Latest);
    }

    public Task LogStraightsAsync(IReadOnlyList<EntryDraft> bets, CancellationToken ct = default)
    {
        if (FailWith is { } ex)
            return Task.FromException(ex);

        Straights.AddRange(bets);
        return Task.CompletedTask;
    }

    public Task LogParlayAsync(ParlayDraft parlay, CancellationToken ct = default)
    {
        if (FailWith is { } ex)
            return Task.FromException(ex);

        Parlays.Add(parlay);
        return Task.CompletedTask;
    }

    public Task<IngestionJob?> LinesJobAsync(string sportKey, CancellationToken ct = default)
        => Task.FromResult(LinesJob);

    public Task<JobOutcome> RunJobAsync(string key, CancellationToken ct = default)
    {
        JobsRun.Add(key);
        return Task.FromResult(new JobOutcome(key, true, 12, 0, null));
    }
}
```

Create `tests/LineOps.Web.Tests/Support/FakeAlerts.cs`:

```csharp
using LineOps.Desk.Primitives;

namespace LineOps.Web.Tests.Support;

/// <summary>A confirm that answers as told and counts the questions.</summary>
public sealed class FakeAlerts : IDeskAlerts
{
    public bool Answer { get; set; } = true;
    public List<string> Asked { get; } = [];

    public Task<bool> ConfirmAsync(string heading, string? message = null, string confirmLabel = "OK",
        string? cancelLabel = "Cancel", bool destructive = false)
    {
        Asked.Add(heading);
        return Task.FromResult(Answer);
    }
}
```

Create `tests/LineOps.Web.Tests/Support/WagerTestContext.cs`:

```csharp
using LineOps.Desk.Primitives;
using LineOps.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests.Support;

/// <summary>The desk bench plus what a window that logs wagers needs: the data seam, the slip, toasts and confirms.</summary>
public abstract class WagerTestContext : DeskTestContext
{
    protected FakeWagerData Data { get; } = new();
    protected FakeAlerts Alerts { get; } = new();

    protected WagerTestContext()
    {
        Services.AddSingleton<IWagerData>(Data);
        Services.AddScoped<WagerSlip>();
        Services.AddScoped<DeskToasts>();
        Services.AddSingleton<IDeskAlerts>(Alerts);
    }

    protected WagerSlip Slip => Services.GetRequiredService<WagerSlip>();
}
```

- [ ] **Step 2: Write the failing tests.** Create `tests/LineOps.Web.Tests/BoardWagerTests.cs`:

```csharp
using AngleSharp.Dom;
using Bunit;
using LineOps.Core.Entities;
using LineOps.Web.Components.Panels;
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// The Place wager window: opened on the side a door names, the price that side's book shows, a
/// Log key that says what it is waiting for, and a loading state that is not an empty one.
/// </summary>
public class BoardWagerTests : WagerTestContext
{
    private IRenderedComponent<BoardWager> Render(string? market = null, string? outcome = null, string? book = null)
        => RenderComponent<BoardWager>(p =>
        {
            p.Add(x => x.GameId, BoardRows.GameId);
            if (market is not null) p.Add(x => x.Market, market);
            if (outcome is not null) p.Add(x => x.Outcome, outcome);
            if (book is not null) p.Add(x => x.Book, book);
        });

    private static IElement Key(IRenderedFragment cut, string startsWith)
        => cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(startsWith, StringComparison.Ordinal));

    private static IElement Log(IRenderedFragment cut) => cut.Find("button.desk-btn--filled");

    [Fact]
    public void With_only_a_game_it_lands_on_the_side_with_the_most_value()
    {
        var cut = Render();

        Assert.Equal("-128", cut.Find(".wager-offer__price").TextContent.Trim());
        Assert.Equal("ML · Chiefs", cut.Find(".selection-chips .desk-btn--tinted").TextContent.Trim());
    }

    [Fact]
    public void A_door_that_names_a_side_lands_on_it()
    {
        var cut = Render(Markets.Spread, BoardRows.Away);

        Assert.Equal("-105", cut.Find(".wager-offer__price").TextContent.Trim());
        Assert.Equal("Spread · Bills +2.5", cut.Find(".selection-chips .desk-btn--tinted").TextContent.Trim());
    }

    [Fact]
    public void A_door_that_names_a_book_shows_that_books_price()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home, "fanduel");

        Assert.Equal("-135", cut.Find(".wager-offer__price").TextContent.Trim());
        Assert.Equal("fanduel", cut.Find(".wager-offer__book").TextContent.Trim());
    }

    [Fact]
    public void Log_is_disabled_at_zero_stake_and_says_why()
    {
        var cut = Render();

        Assert.True(Log(cut).HasAttribute("disabled"));
        Assert.Equal("Needs a stake above zero.", cut.Find(".wager-why").TextContent.Trim());
    }

    [Fact]
    public void While_the_row_loads_the_body_is_a_skeleton_not_an_empty_state()
    {
        Data.RowGate = new TaskCompletionSource();
        var cut = Render();

        Assert.NotEmpty(cut.FindAll(".skel__line"));
        Assert.DoesNotContain("Pick a side", cut.Markup);

        Data.RowGate.SetResult();
        cut.WaitForAssertion(() => Assert.Equal("-128", cut.Find(".wager-offer__price").TextContent.Trim()));
    }

    [Fact]
    public void Logging_writes_one_straight_bet_through_the_journal()
    {
        var cut = Render();

        Key(cut, 50m.ToString("C0")).Click();
        Log(cut).Click();

        var bet = Assert.Single(Data.Straights);
        Assert.Equal(BoardRows.GameId, bet.GameId);
        Assert.Equal(Markets.Moneyline, bet.Market);
        Assert.Equal(BoardRows.Home, bet.Outcome);
        Assert.Equal("draftkings", bet.Book);
        Assert.Equal(-128, bet.PriceTaken);
        Assert.Equal(50m, bet.Stake);

        // The stake clears so the lit key cannot log the same wager twice; the side stays.
        Assert.True(Log(cut).HasAttribute("disabled"));
        Assert.Contains($"Logged {50m:C0} on ML · Chiefs at -128 (draftkings).", cut.Find(".wager-logged").TextContent);
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test tests/LineOps.Web.Tests --filter "FullyQualifiedName~BoardWagerTests"`
Expected: build error `The type or namespace name 'IWagerData' could not be found` (and `BoardWager` has no `Market` parameter).

- [ ] **Step 4: Implement the seam.** Create `src/LineOps.Web/Services/WagerData.cs`:

```csharp
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Ingestion.Services;

namespace LineOps.Web.Services;

/// <summary>
/// What the windows that log wagers read and write, in one seam: a board row, the journal's last
/// bet on a side, the writes, and the lines pull a stale price offers. An interface so the wager
/// window and the slip can be rendered in tests without a database.
/// </summary>
public interface IWagerData
{
    Task<BoardRow?> RowAsync(int gameId, CancellationToken ct = default);

    Task<JournalEntry?> LatestOnSideAsync(int gameId, string market, string outcome, string book,
        DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Straight bets, all in one save (<see cref="JournalService.SaveEntriesAsync"/>).</summary>
    Task LogStraightsAsync(IReadOnlyList<EntryDraft> bets, CancellationToken ct = default);

    Task LogParlayAsync(ParlayDraft parlay, CancellationToken ct = default);

    /// <summary>The lines pull for one league (<c>odds:lines:{sport}</c>) as the pull menu describes it.</summary>
    Task<IngestionJob?> LinesJobAsync(string sportKey, CancellationToken ct = default);

    Task<JobOutcome> RunJobAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// The seam over the real services. A scope per call, as the panels did before it, so a window
/// never holds a DbContext across renders. Every write goes through <see cref="JournalService"/>,
/// whose saves the change notifier announces to every open window.
/// </summary>
public sealed class WagerData(IServiceScopeFactory scopes, IngestionJobs jobs) : IWagerData
{
    public async Task<BoardRow?> RowAsync(int gameId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BoardService>().GetRowAsync(gameId, ct);
    }

    public async Task<JournalEntry?> LatestOnSideAsync(int gameId, string market, string outcome, string book,
        DateTimeOffset since, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<JournalService>()
            .LatestOnSideAsync(gameId, market, outcome, book, since, ct);
    }

    public async Task LogStraightsAsync(IReadOnlyList<EntryDraft> bets, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<JournalService>().SaveEntriesAsync(bets, ct);
    }

    public async Task LogParlayAsync(ParlayDraft parlay, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<JournalService>().SaveParlayAsync(parlay, ct);
    }

    public async Task<IngestionJob?> LinesJobAsync(string sportKey, CancellationToken ct = default)
        => (await jobs.DescribeAsync(ct)).FirstOrDefault(j => j.Key == IngestionJobs.OddsLinesPrefix + sportKey);

    public Task<JobOutcome> RunJobAsync(string key, CancellationToken ct = default) => jobs.RunAsync(key, ct);
}
```

In `src/LineOps.Web/Program.cs`, below the `WagerSlip` registration:

```csharp
builder.Services.AddScoped<LineOps.Web.Services.IWagerData, LineOps.Web.Services.WagerData>();
```

- [ ] **Step 5: Rewrite `src/LineOps.Web/Components/Panels/BoardWager.razor`** in full:

```razor
@using LineOps.Core.Analytics
@using LineOps.Data.CrossReference
@using LineOps.Web.Services
@inherits PanelBase
@inject IWagerData Data
@inject DeskToasts Toasts

@*
    Logging a wager from the board.

    The point of placing from here is that everything except the stake is already known — the
    game, the market, the side, the price and the book that has it. Retyping any of it is an
    opportunity to record a bet you did not make, so the form starts filled and the operator
    only supplies what the board cannot: how much.

    A panel (spec §6): it watches the odds and the journal, so a pull re-prices an open form
    instead of leaving it quoting a number the board has moved off. A door can name the side and
    the book (§5): a price cell opens it on that price, an Every book rung on that book's.
*@

<div class="stack" style="gap:10px">
    @if (_loading)
    {
        @* Unread is not empty (ADR 0003): the shape of what is coming, not "pick a side". *@
        <div class="stack wager-loading" style="gap:10px" aria-busy="true">
            <DeskSkeleton Lines="1" />
            <DeskSkeleton Shape="DeskSkeletonShape.Block" Height="64px" />
        </div>
    }
    else
    {
        <div>
            <div class="panel-h" style="margin-bottom:6px">Selection</div>
            <SelectionChips Row="Row" Market="@_offer?.Market" Outcome="@_offer?.Outcome" OnChoose="ChooseAsync" />
        </div>

        @if (_offer is { } offer)
        {
            @* The price large, then the book, how many books quote it and how old it is; the value
               reading beneath. The four numbers the tiles carried are all still here. *@
            <div class="wager-offer">
                <div class="wager-offer__top">
                    <span class="wager-offer__price num">@Price(offer.PriceAmerican)</span>
                    <span class="wager-offer__book">@offer.Book</span>
                    <span class="dim">@(offer.IsBest ? "best" : "one") of @offer.BooksQuoting books</span>
                    <span class="dim wager-offer__age">
                        <Glyph Icon="@Icons.Material.Filled.Schedule" /> @Captured(offer)
                    </span>
                </div>
                <div class="wager-offer__value">@ValueReading(offer)</div>
            </div>

            <StakeRow Stake="_stake" StakeChanged="SetStake" PriceAmerican="offer.PriceAmerican" />

            <DeskField TValue="string" placeholder="Note (optional)" @bind-Value="_note" />

            @if (_error is not null)
            {
                <Note State="DeskState.Warning" Style="margin:0">@_error</Note>
            }

            @if (_logged is not null)
            {
                <Note State="DeskState.Positive" Style="margin:0" Class="wager-logged">@_logged</Note>
            }

            <div class="row row--end">
                <DeskButton Emphasis="DeskEmphasis.Filled"
                            Icon="@Icons.Material.Filled.Bolt"
                            Busy="@_saving"
                            Disabled="@(!CanLog)"
                            OnClick="PlaceAsync">
                    Log @(_stake > 0 ? _stake.ToString("C0") : "wager")
                </DeskButton>
            </div>

            @if (Waiting is { } why)
            {
                <div class="warnc wager-why">@why</div>
            }

            <p class="empty" style="padding:0">
                Logged against the price shown; CLV resolves once the game starts and the close is
                recorded. LineOps records bets placed elsewhere; nothing here reaches a sportsbook.
            </p>
        }
        else
        {
            <EmptyState Kind="EmptyStateKind.New">Pick a side to price it.</EmptyState>
        }
    }
</div>

@code {
    /// <summary>
    /// The game this view is about. Taken as an id rather than a row so the view can be hosted
    /// as a desk window, which is rebuilt from its parameters and cannot carry an object.
    /// </summary>
    [Parameter, EditorRequired] public int GameId { get; set; }

    /// <summary>The side a door names (<c>Markets.*</c>). With <see cref="Outcome"/>; otherwise the most value.</summary>
    [Parameter] public string? Market { get; set; }

    [Parameter] public string? Outcome { get; set; }

    /// <summary>The book a door names. Where it quotes the side, the window shows its price, not the best.</summary>
    [Parameter] public string? Book { get; set; }

    [Parameter] public EventCallback OnPlaced { get; set; }

    protected override IReadOnlySet<string> Watches { get; } =
        new HashSet<string> { DataTopics.Odds, DataTopics.Journal };

    protected override Task RefreshAsync() => ReloadAsync();

    private BoardRow? Row;
    private WagerOffer? _offer;
    private string? _pinnedBook;
    private bool _loading;
    private decimal _stake;
    private string? _note;
    private string? _error;
    private string? _logged;
    private bool _saving;

    protected override async Task OnParametersSetAsync()
    {
        // A first read shows the skeleton; a door re-aiming a loaded window does not flash it.
        _loading = Row is null;
        _pinnedBook = Book;

        try
        {
            Row = await Data.RowAsync(GameId);
        }
        finally
        {
            _loading = false;
        }

        _offer = SelectionChoice.Land(Row, Market, Outcome, _pinnedBook);
    }

    /// <summary>After a change the window watches: the same side, re-priced.</summary>
    private async Task ReloadAsync()
    {
        var before = _offer;
        Row = await Data.RowAsync(GameId);

        _offer = before is null
            ? SelectionChoice.Land(Row, Market, Outcome, _pinnedBook)
            : SelectionChoice.Land(Row, before.Market, before.Outcome, _pinnedBook);
    }

    private Task ChooseAsync(SelectionChoice choice)
    {
        // A chip is the operator choosing, so the door's book no longer applies.
        _pinnedBook = null;
        _offer = WagerOffer.Best(choice.Market, choice.Offer);
        return Task.CompletedTask;
    }

    private void SetStake(decimal stake) => _stake = stake;

    private bool CanLog => _offer is { IsClosing: false } && _stake > 0 && Row is not null;

    /// <summary>What the Log key is waiting for, said under it.</summary>
    private string? Waiting
        => _offer is { IsClosing: true } ? "Log it from the Journal with the price you got."
           : _stake <= 0 ? "Needs a stake above zero."
           : null;

    private static string Captured(WagerOffer offer)
    {
        if (offer.IsClosing)
            return "closing price";

        var age = Age((DateTimeOffset.UtcNow - offer.CapturedAt).TotalMinutes);
        return age == "just now" ? "captured just now" : $"captured {age} ago";
    }

    /// <summary>Against what the bet is worth, not only what it pays. Absent is not zero (ADR 0003).</summary>
    private static string ValueReading(WagerOffer offer)
        => offer.Ev is { } ev
            ? PriceCell.EvLabel(ev) + (offer.Fair is { } fair ? " · " + PriceCell.FairLabel(fair) : "")
            : "No fair price on this number";

    private async Task PlaceAsync()
    {
        if (!CanLog || _offer is not { } offer || Row is null)
            return;

        _saving = true;
        _error = null;

        try
        {
            await Data.LogStraightsAsync([offer.ToDraft(Row.Game.Id, _stake, _note)]);

            // Hosted as a window there is no host to close the form, so it says what it did and
            // clears the stake: a form left exactly as it was, key still lit, logged the same
            // wager twice on the next press. The side stays, so the journal check can name it.
            _logged = $"Logged {_stake:C0} on {offer.Label} at {Price(offer.PriceAmerican)} ({offer.Book}).";
            Toasts.Success(_logged);
            _stake = 0;
            _note = null;

            await OnPlaced.InvokeAsync();
        }
        catch (Exception ex)
        {
            _error = $"Not logged: {ex.Message}";
        }
        finally
        {
            _saving = false;
        }
    }
}
```

Note: the old private `Price(int)` and `Short`/`LineOf` helpers are gone — `PanelBase.Price` and `SelectionText` replace them.

- [ ] **Step 6: Append the offer-block styles** to the end of `src/LineOps.Desk/wwwroot/css/lineops.css`:

```css
/* ---- The wager window, its advisory checks, and the slip ------------------------------ */

.wager-offer { display: grid; gap: 4px; padding: 10px 12px; background: var(--surface-2); border: 1px solid var(--separator); border-radius: 6px; }
.wager-offer__top { display: flex; align-items: baseline; gap: 10px; flex-wrap: wrap; }
.wager-offer__price { font-size: 22px; font-weight: 600; color: var(--accent); }
.wager-offer__book { font-weight: 500; }
.wager-offer__age { display: inline-flex; align-items: center; gap: 4px; margin-left: auto; }
.wager-offer__value { font-size: 12px; color: var(--text-secondary); }
.wager-why, .slip-why { font-size: 12px; text-align: right; }
.stake-row { align-items: center; }
.stake-row__returns { margin-left: auto; font-size: 13px; }
```

- [ ] **Step 7: Run to verify they pass, and nothing else in the Web suite broke**

Run: `dotnet test tests/LineOps.Web.Tests`
Expected: all PASS.

- [ ] **Step 8: Commit**

```powershell
git add src/LineOps.Web/Services/WagerData.cs src/LineOps.Web/Components/Panels/BoardWager.razor src/LineOps.Web/Program.cs src/LineOps.Desk/wwwroot/css/lineops.css tests/LineOps.Web.Tests/Support/FakeWagerData.cs tests/LineOps.Web.Tests/Support/FakeAlerts.cs tests/LineOps.Web.Tests/Support/WagerTestContext.cs tests/LineOps.Web.Tests/BoardWagerTests.cs
git commit -m "feat(wager): the wager window is a panel, and opens on the side a door names" -m "BoardWager was a plain component: a wager logged in it never reached an open Journal, and a pull never re-priced it. It now inherits PanelBase and watches odds and the journal. It takes a market, side and book so a price cell or a book's rung can open it on that price, and while the row loads it shows the shape of what is coming rather than 'pick a side'. Reads and writes go through IWagerData, which is what lets it be rendered in tests."
```

---

### Task 8: The advisory strip and Add to slip on the wager window

**Files:**
- Create: `src/LineOps.Web/Components/Desk/CheckList.razor`
- Rewrite: `src/LineOps.Web/Components/Panels/BoardWager.razor` (final version below)
- Modify: `src/LineOps.Desk/wwwroot/css/lineops.css` (append)
- Test: `tests/LineOps.Web.Tests/BoardWagerTests.cs` (append)

**Interfaces:**
- Consumes: `Checks`, `CheckActions`, `Check` (Task 5); `WagerSlip`, `WagerOffer.ToSlipItem` (Tasks 4–5); `IWagerData.LatestOnSideAsync`, `LinesJobAsync`, `RunJobAsync` (Task 7).
- Produces: `<CheckList Items Title />` — `IReadOnlyList<Check> Items`, `string Title = "Before you log"`; renders `.checks` with one `.check.check--{state}` row per item and a small Plain remedy key when both `Remedy` and `Act` are set. Markup hooks: `.slip-badge`, `.wager-moved`.

- [ ] **Step 1: Write the failing tests** — append inside `BoardWagerTests`:

```csharp
    // ---- Before you log -------------------------------------------------------------------

    [Fact]
    public void The_strip_reads_value_age_and_the_journal()
    {
        var cut = Render();

        var rows = cut.FindAll(".check").Select(r => r.TextContent).ToList();
        Assert.Equal(3, rows.Count);
        Assert.Contains("Value: +2.7% against fair -136 (Pinnacle, no-vig).", rows[0]);
        Assert.Contains("Price captured 12 min ago.", rows[1]);
        Assert.Contains("Nothing logged on this side yet.", rows[2]);
    }

    [Fact]
    public void A_prior_bet_on_the_side_is_named_and_Log_stays_available()
    {
        Data.Latest = new JournalEntry
        {
            GameId = BoardRows.GameId, Market = Markets.Moneyline, Outcome = BoardRows.Home, Book = "draftkings",
            PriceTaken = -130, Stake = 50m, PlacedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        var cut = Render();
        Key(cut, 25m.ToString("C0")).Click();

        var warning = cut.Find(".check--warning").TextContent;
        Assert.Contains($"Already in the journal: {50m:C0} on ML · Chiefs at -130 (draftkings)", warning);
        Assert.Contains("Logging again records a second bet.", warning);
        Assert.False(Log(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void A_closing_offer_disables_Log_and_keeps_Add_to_slip()
    {
        Data.Row = BoardRows.Row(closing: true, startsAt: DateTimeOffset.UtcNow.AddMinutes(-42));
        var cut = Render();

        Key(cut, 50m.ToString("C0")).Click();

        Assert.True(Log(cut).HasAttribute("disabled"));
        Assert.Equal("Log it from the Journal with the price you got.", cut.Find(".wager-why").TextContent.Trim());
        Assert.Contains("Closing line: the game started 42 min ago", cut.Find(".check--negative").TextContent);

        Key(cut, "Add to slip").Click();

        var item = Assert.Single(Slip.Items);
        Assert.True(item.IsClosing);
    }

    [Fact]
    public void Add_to_slip_counts_on_the_badge_and_clears_the_stake()
    {
        var cut = Render();
        Assert.Equal("0", cut.Find(".slip-badge").TextContent.Trim());

        Key(cut, 50m.ToString("C0")).Click();
        Key(cut, "Add to slip").Click();

        Assert.Equal("1", cut.Find(".slip-badge").TextContent.Trim());
        Assert.Equal(50m, Slip.Items.Single().Stake);
        Assert.Contains($"Added {50m:C0} on ML · Chiefs at -128 to the slip.", cut.Find(".wager-logged").TextContent);
        Assert.True(Log(cut).HasAttribute("disabled"));   // the stake is cleared
    }

    [Fact]
    public void The_badge_follows_the_slip_when_another_window_changes_it()
    {
        var cut = Render();

        cut.InvokeAsync(() => Slip.Add(new LineOps.Web.Services.SlipItem(Guid.Empty, 8, "x", DateTimeOffset.UtcNow, "nfl",
            Markets.Moneyline, "x", null, "draftkings", -110, DateTimeOffset.UtcNow, false, 10m, null, null, null)));

        cut.WaitForAssertion(() => Assert.Equal("1", cut.Find(".slip-badge").TextContent.Trim()));
    }

    [Fact]
    public void An_odds_change_reloads_and_says_the_price_moved()
    {
        var cut = Render();
        Data.Row = BoardRows.Row(homeMl: -122);

        Services.GetRequiredService<LineOps.Desk.Windowing.DeskSignals>()
            .Publish(new HashSet<string> { LineOps.Data.Changes.DataTopics.Odds });

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("-122", cut.Find(".wager-offer__price").TextContent.Trim());
            Assert.Contains("Price moved since you opened this: -128 → -122 (draftkings).", cut.Find(".wager-moved").TextContent);
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_stale_price_offers_the_leagues_pull_at_its_cost_and_runs_it()
    {
        Data.Row = BoardRows.Row(age: TimeSpan.FromMinutes(45));
        Data.LinesJob = new LineOps.Ingestion.Services.IngestionJob(
            "odds:lines:nfl", "NFL lines", "Lines for NFL", 1, true, null, EstimatedCredits: 2);

        var cut = Render();
        var reads = Data.RowReads;

        Key(cut, "Pull NFL lines · 2 credits").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(new[] { "odds:lines:nfl" }, Data.JobsRun);
            Assert.True(Data.RowReads > reads);
        });
    }
```

Add `using Microsoft.Extensions.DependencyInjection;` to the file's usings.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LineOps.Web.Tests --filter "FullyQualifiedName~BoardWagerTests"`
Expected: the six new tests FAIL (no `.check`, no `.slip-badge`, no "Add to slip" key, no `.wager-moved`); the Task 7 tests still pass.

- [ ] **Step 3: Implement `CheckList`.** Create `src/LineOps.Web/Components/Desk/CheckList.razor`:

```razor
@*
    A bordered list of advisory checks: a mark, one sentence, and the remedy key where there is
    one. The mark's hue is the second channel; the sentence carries the meaning.
*@

<div class="checks">
    <div class="checks__title panel-h">@Title</div>
    <div role="list" aria-label="@Title">
        @foreach (var check in Items)
        {
            <div class="check check--@(check.State.ToString().ToLowerInvariant())" role="listitem">
                <span class="check__mark" aria-hidden="true"></span>
                <span class="check__text">@check.Text</span>
                @if (check.Remedy is { } remedy && check.Act is { } act)
                {
                    <DeskButton Size="DeskKeySize.Small" OnClick="() => act()">@remedy</DeskButton>
                }
            </div>
        }
    </div>
</div>

@code {
    [Parameter, EditorRequired] public IReadOnlyList<Check> Items { get; set; } = [];

    [Parameter] public string Title { get; set; } = "Before you log";
}
```

- [ ] **Step 4: Rewrite `BoardWager.razor`** with its final content (supersedes Task 7's):

```razor
@using LineOps.Core.Analytics
@using LineOps.Core.Entities
@using LineOps.Data.CrossReference
@using LineOps.Ingestion.Services
@using LineOps.Web.Services
@inherits PanelBase
@inject IWagerData Data
@inject WagerSlip Slip
@inject DeskToasts Toasts

@*
    Logging a wager from the board.

    The point of placing from here is that everything except the stake is already known — the
    game, the market, the side, the price and the book that has it. Retyping any of it is an
    opportunity to record a bet you did not make, so the form starts filled and the operator
    only supplies what the board cannot: how much.

    A panel (spec §6): it watches the odds and the journal, so a pull re-prices an open form
    instead of leaving it quoting a number the board has moved off. A door can name the side and
    the book (§5). Before Log, a strip of checks says what is worth knowing (§4); only a closing
    line blocks. Add to slip banks the wager on the desk's slip and hands the operator back to
    the board (§3.3).
*@

<div class="stack" style="gap:10px">
    @if (_loading)
    {
        @* Unread is not empty (ADR 0003): the shape of what is coming, not "pick a side". *@
        <div class="stack wager-loading" style="gap:10px" aria-busy="true">
            <DeskSkeleton Lines="1" />
            <DeskSkeleton Shape="DeskSkeletonShape.Block" Height="64px" />
        </div>
    }
    else
    {
        <div>
            <div class="panel-h" style="margin-bottom:6px">Selection</div>
            <SelectionChips Row="Row" Market="@_offer?.Market" Outcome="@_offer?.Outcome" OnChoose="ChooseAsync" />
        </div>

        @if (_offer is { } offer)
        {
            <div class="wager-offer">
                <div class="wager-offer__top">
                    <span class="wager-offer__price num">@Price(offer.PriceAmerican)</span>
                    <span class="wager-offer__book">@offer.Book</span>
                    <span class="dim">@(offer.IsBest ? "best" : "one") of @offer.BooksQuoting books</span>
                    <span class="dim wager-offer__age">
                        <Glyph Icon="@Icons.Material.Filled.Schedule" /> @Captured(offer)
                    </span>
                </div>
                <div class="wager-offer__value">@ValueReading(offer)</div>
            </div>

            @if (_moved is not null)
            {
                @* Said, not swapped silently: the operator was looking at the old number. *@
                <Note State="DeskState.Positive" Style="margin:0" Class="wager-moved">@_moved</Note>
            }

            <CheckList Items="Strip" />

            <StakeRow Stake="_stake" StakeChanged="SetStake" PriceAmerican="offer.PriceAmerican" />

            <DeskField TValue="string" placeholder="Note (optional)" @bind-Value="_note" />

            @if (_error is not null)
            {
                <Note State="DeskState.Warning" Style="margin:0">@_error</Note>
            }

            @if (_logged is not null)
            {
                <Note State="DeskState.Positive" Style="margin:0" Class="wager-logged">@_logged</Note>
            }

            <div class="row row--end">
                @* Plain: the window's one Filled key is Log. The badge is the acknowledgment;
                   the slip does not open (spec §11.3). *@
                <DeskButton Icon="@Icons.Material.Filled.Receipt" OnClick="AddToSlip"
                            Title="Bank this wager on the desk's slip">
                    Add to slip
                    <span class="slip-badge num" aria-label="@($"{Slip.Items.Count} on the slip")">@Slip.Items.Count</span>
                </DeskButton>

                <DeskButton Emphasis="DeskEmphasis.Filled"
                            Icon="@Icons.Material.Filled.Bolt"
                            Busy="@_saving"
                            Disabled="@(!CanLog)"
                            OnClick="PlaceAsync">
                    Log @(_stake > 0 ? _stake.ToString("C0") : "wager")
                </DeskButton>
            </div>

            @if (Waiting is { } why)
            {
                <div class="warnc wager-why">@why</div>
            }

            <p class="empty" style="padding:0">
                Logged against the price shown; CLV resolves once the game starts and the close is
                recorded. LineOps records bets placed elsewhere; nothing here reaches a sportsbook.
            </p>
        }
        else
        {
            <EmptyState Kind="EmptyStateKind.New">Pick a side to price it.</EmptyState>
        }
    }
</div>

@code {
    /// <summary>
    /// The game this view is about. Taken as an id rather than a row so the view can be hosted
    /// as a desk window, which is rebuilt from its parameters and cannot carry an object.
    /// </summary>
    [Parameter, EditorRequired] public int GameId { get; set; }

    /// <summary>The side a door names (<c>Markets.*</c>). With <see cref="Outcome"/>; otherwise the most value.</summary>
    [Parameter] public string? Market { get; set; }

    [Parameter] public string? Outcome { get; set; }

    /// <summary>The book a door names. Where it quotes the side, the window shows its price, not the best.</summary>
    [Parameter] public string? Book { get; set; }

    [Parameter] public EventCallback OnPlaced { get; set; }

    protected override IReadOnlySet<string> Watches { get; } =
        new HashSet<string> { DataTopics.Odds, DataTopics.Journal };

    /// <summary>
    /// Odds or journal: the refresh does not say which, and both reads are cheap, so the row is
    /// re-priced and the journal re-read either way.
    /// </summary>
    protected override Task RefreshAsync() => ReloadAsync();

    private BoardRow? Row;
    private WagerOffer? _offer;
    private string? _pinnedBook;
    private bool _loading;
    private decimal _stake;
    private string? _note;
    private string? _error;
    private string? _logged;
    private string? _moved;
    private bool _saving;

    // The duplicate check's reading: a field the render reads, never a query from the render.
    private JournalEntry? _prior;
    private bool _priorRead;

    private IngestionJob? _linesJob;
    private bool _pulling;

    protected override void OnInitialized() => Slip.Changed += OnSlipChanged;

    protected override void OnDispose() => Slip.Changed -= OnSlipChanged;

    private void OnSlipChanged() => _ = InvokeAsync(StateHasChanged);

    protected override async Task OnParametersSetAsync()
    {
        // A first read shows the skeleton; a door re-aiming a loaded window does not flash it.
        _loading = Row is null;
        _pinnedBook = Book;
        _moved = null;

        try
        {
            Row = await Data.RowAsync(GameId);
            _linesJob = Row?.Game.Sport is { } sport ? await Data.LinesJobAsync(sport.Key) : null;
        }
        finally
        {
            _loading = false;
        }

        _offer = SelectionChoice.Land(Row, Market, Outcome, _pinnedBook);
        await ReadPriorAsync();
    }

    /// <summary>After a change the window watches: the same side, re-priced, and the journal re-read.</summary>
    private async Task ReloadAsync()
    {
        var before = _offer;
        Row = await Data.RowAsync(GameId);

        _offer = before is null
            ? SelectionChoice.Land(Row, Market, Outcome, _pinnedBook)
            : SelectionChoice.Land(Row, before.Market, before.Outcome, _pinnedBook);

        if (before is not null && _offer is not null
            && _offer.Market == before.Market && _offer.Outcome == before.Outcome
            && (_offer.PriceAmerican != before.PriceAmerican || _offer.Line != before.Line))
        {
            _moved = $"Price moved since you opened this: {LinePrice(before)} → {LinePrice(_offer)} ({_offer.Book}).";
        }

        await ReadPriorAsync();
    }

    /// <summary>
    /// The duplicate check's one query, keyed on the selection (game, market, side, book), inside
    /// the activity wrapper so the title bar pulses while it runs.
    /// </summary>
    private async Task ReadPriorAsync()
    {
        if (_offer is not { } offer || Row is null)
        {
            _prior = null;
            _priorRead = false;
            return;
        }

        _priorRead = false;

        await WithActivityAsync(async () =>
        {
            _prior = await Data.LatestOnSideAsync(Row.Game.Id, offer.Market, offer.Outcome, offer.Book,
                DateTimeOffset.UtcNow - Checks.DuplicateWindow);
            _priorRead = true;
        }, () => PulseState.Healthy);
    }

    private async Task ChooseAsync(SelectionChoice choice)
    {
        // A chip is the operator choosing, so the door's book no longer applies.
        _pinnedBook = null;
        _moved = null;
        _offer = WagerOffer.Best(choice.Market, choice.Offer);
        await ReadPriorAsync();
    }

    private void SetStake(decimal stake) => _stake = stake;

    private bool CanLog => _offer is { IsClosing: false } && _stake > 0 && Row is not null;

    /// <summary>What the Log key is waiting for, said under it.</summary>
    private string? Waiting
        => _offer is { IsClosing: true } ? "Log it from the Journal with the price you got."
           : _stake <= 0 ? "Needs a stake above zero."
           : null;

    private IReadOnlyList<Check> Strip
        => _offer is { } offer && Row is not null
            ? Checks.Build(offer, Row.Game.StartsAt, DateTimeOffset.UtcNow, _priorRead, _prior, _stake,
                new CheckActions(PullLabel, _pulling ? null : (Func<Task>)PullAsync, OpenJournalAsync))
            : [];

    /// <summary>"Pull NFL lines · 2 credits": the same job the Board row's pull runs, at the cost the menu quotes.</summary>
    private string? PullLabel
    {
        get
        {
            if (_linesJob is not { Available: true } job || Row?.Game.Sport is not { } sport)
                return null;

            var league = sport.Key.ToUpperInvariant();
            return job.EstimatedCredits > 0 ? $"Pull {league} lines · {job.EstimatedCredits} credits" : $"Pull {league} lines";
        }
    }

    private async Task PullAsync()
    {
        if (_linesJob is not { Available: true } job || _pulling)
            return;

        _pulling = true;

        try
        {
            var outcome = await Data.RunJobAsync(job.Key);

            if (outcome.Succeeded)
                Toasts.Success(outcome.Detail ?? $"{job.Label}: {outcome.Rows:N0} rows.");
            else
                Toasts.Warn(outcome.Detail ?? $"{job.Label}: {outcome.Failures} source failed.");

            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Toasts.Fail($"{job.Label} failed: {ex.Message}");
        }
        finally
        {
            _pulling = false;
        }
    }

    private Task OpenJournalAsync()
    {
        Manager.Open(WindowCatalog.Find(WindowCatalog.Journal)!);
        return Task.CompletedTask;
    }

    private static string Captured(WagerOffer offer)
    {
        if (offer.IsClosing)
            return "closing price";

        var age = Age((DateTimeOffset.UtcNow - offer.CapturedAt).TotalMinutes);
        return age == "just now" ? "captured just now" : $"captured {age} ago";
    }

    /// <summary>Against what the bet is worth, not only what it pays. Absent is not zero (ADR 0003).</summary>
    private static string ValueReading(WagerOffer offer)
        => offer.Ev is { } ev
            ? PriceCell.EvLabel(ev) + (offer.Fair is { } fair ? " · " + PriceCell.FairLabel(fair) : "")
            : "No fair price on this number";

    private static string LinePrice(WagerOffer offer)
        => (offer.Line is { } line ? OddsMath.FormatLine(line, offer.Outcome) + " " : "") + Price(offer.PriceAmerican);

    /// <summary>
    /// Banks the wager on the slip. Zero stake is allowed — it can be set on the card — and a
    /// closing price is allowed too: the card carries the same block the Log key does here.
    /// </summary>
    private void AddToSlip()
    {
        if (_offer is not { } offer || Row is null)
            return;

        Slip.Add(offer.ToSlipItem(Row.Game, Matchup(Row.Game), _stake, _note));

        _error = null;
        _logged = $"Added {_stake:C0} on {offer.Label} at {Price(offer.PriceAmerican)} to the slip.";
        Toasts.Success(_logged);
        _stake = 0;
        _note = null;
    }

    private async Task PlaceAsync()
    {
        if (!CanLog || _offer is not { } offer || Row is null)
            return;

        _saving = true;
        _error = null;

        try
        {
            await Data.LogStraightsAsync([offer.ToDraft(Row.Game.Id, _stake, _note)]);

            // Hosted as a window there is no host to close the form, so it says what it did and
            // clears the stake: a form left exactly as it was, key still lit, logged the same
            // wager twice on the next press. The side stays, so the journal check can name it.
            _logged = $"Logged {_stake:C0} on {offer.Label} at {Price(offer.PriceAmerican)} ({offer.Book}).";
            Toasts.Success(_logged);
            _stake = 0;
            _note = null;

            await OnPlaced.InvokeAsync();

            // The change notice will re-read the journal; this reads it now rather than a second later.
            await ReadPriorAsync();
        }
        catch (Exception ex)
        {
            _error = $"Not logged: {ex.Message}";
        }
        finally
        {
            _saving = false;
        }
    }
}
```

- [ ] **Step 5: Append the check and badge styles** to `lineops.css` (below Task 7's block):

```css
.checks { border: 1px solid var(--separator); border-radius: 6px; }
.checks__title { padding: 8px 12px 0; }
.check { display: flex; align-items: center; gap: 8px; padding: 8px 12px; border-top: 1px solid var(--separator); font-size: 13px; }
.check:first-child { border-top: 0; }
.check__mark { flex: none; width: 8px; height: 8px; border-radius: 50%; background: var(--text-tertiary); }
.check--positive .check__mark { background: var(--state-positive); }
.check--warning .check__mark { background: var(--state-warning); }
.check--negative .check__mark { background: var(--state-negative); }
.check__text { flex: 1; }
.slip-badge { display: inline-block; min-width: 18px; margin-left: 6px; padding: 0 5px; border-radius: 9px; background: var(--accent-wash); color: var(--accent); font-size: 11px; line-height: 18px; text-align: center; }
```

- [ ] **Step 6: Run to verify they pass**

Run: `dotnet test tests/LineOps.Web.Tests --filter "FullyQualifiedName~BoardWagerTests|FullyQualifiedName~PanelRefreshTests"`
Expected: all PASS (the odds test takes about a second: `PanelBase` settles before reloading).

- [ ] **Step 7: Commit**

```powershell
git add src/LineOps.Web/Components/Desk/CheckList.razor src/LineOps.Web/Components/Panels/BoardWager.razor src/LineOps.Desk/wwwroot/css/lineops.css tests/LineOps.Web.Tests/BoardWagerTests.cs
git commit -m "feat(wager): the checks before Log, and a slip to bank a wager on" -m "Between the offer and the stake the window now says what is worth knowing: the value against the fair price, how old the price is (with the league's pull and its cost when it is stale), whether the side is already in the journal, and whether the price is a close that cannot have been taken, which alone disables Log. A price that moves while the window is open is said, not swapped. Add to slip banks the wager on the desk's slip and counts it on a badge without leaving the board."
```

---

### Task 9: The Slip window

**Files:**
- Create: `src/LineOps.Web/Components/Panels/SlipPanel.razor`
- Modify: `src/LineOps.Web/Windowing/WindowCatalog.cs` (constant beside `Wager`, entry after the `Wager` definition)
- Modify: `src/LineOps.Desk/wwwroot/css/lineops.css` (append)
- Test: `tests/LineOps.Web.Tests/SlipPanelTests.cs` (create)

**Interfaces:**
- Consumes: `WagerSlip` (Task 4), `IWagerData.LogStraightsAsync`/`LogParlayAsync` (Task 7), `StakeRow` (Task 6), `IDeskAlerts`, `DeskToasts`.
- Produces: `WindowCatalog.Slip = "slip"`; `SlipPanel` (no parameters). Markup hooks: `.slip-card`, `.slip-card--blocked`, `.slip-card__stake`, `.slip-parlay__price`, `.slip-report`, `.slip-steps li`, `.slip-why`.

- [ ] **Step 1: Write the failing tests.** Create `tests/LineOps.Web.Tests/SlipPanelTests.cs`:

```csharp
using AngleSharp.Dom;
using Bunit;
using LineOps.Core.Entities;
using LineOps.Desk.Windowing;
using LineOps.Web.Components.Panels;
using LineOps.Web.Services;
using LineOps.Web.Tests.Support;
using LineOps.Web.Windowing;

namespace LineOps.Web.Tests;

/// <summary>
/// The slip: cards that say whether each wager can be logged and why not, a switch between
/// straight bets and one parlay, and a Log that writes what it can and reports what it left.
/// </summary>
public class SlipPanelTests : WagerTestContext
{
    private static SlipItem Item(int price = -110, decimal stake = 50m, bool closing = false,
        string outcome = BoardRows.Home, int gameId = BoardRows.GameId)
        => new(Guid.Empty, gameId, "Buffalo Bills at Kansas City Chiefs", DateTimeOffset.UtcNow.AddHours(3), "nfl",
            Markets.Moneyline, outcome, null, "draftkings", price, DateTimeOffset.UtcNow, closing, stake, null, null, null);

    private static IElement Key(IRenderedFragment cut, string startsWith)
        => cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(startsWith, StringComparison.Ordinal));

    [Fact]
    public void The_slip_is_a_singleton_window_that_needs_no_subject()
    {
        var definition = WindowCatalog.Find(WindowCatalog.Slip)!;

        Assert.Equal("Slip", definition.Title);
        Assert.Equal(typeof(SlipPanel), definition.ComponentType);
        Assert.True(definition.Singleton);
        Assert.False(definition.RequiresSubject);
        Assert.Equal(380, definition.MinWidth);
    }

    [Fact]
    public void An_empty_slip_says_where_wagers_come_from()
    {
        var cut = RenderComponent<SlipPanel>();

        Assert.Contains("Nothing on the slip. Add to slip from any Place wager window banks the wager here.", cut.Find(".empty").TextContent);
        Assert.Empty(cut.FindAll(".slip-card"));
    }

    [Fact]
    public void Each_card_says_whether_it_can_be_logged()
    {
        Slip.Add(Item(stake: 50m));
        Slip.Add(Item(stake: 0m, outcome: BoardRows.Away));
        Slip.Add(Item(closing: true, gameId: 9));

        var cut = RenderComponent<SlipPanel>();

        var tags = cut.FindAll(".slip-card .tag").Select(t => t.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Ready", "Needs a stake", "Cannot log" }, tags);

        var blocked = Assert.Single(cut.FindAll(".slip-card--blocked"));
        Assert.Contains("Drop it, or log it from the Journal with the price you got.", blocked.TextContent);
    }

    [Fact]
    public void The_cross_drops_a_card()
    {
        Slip.Add(Item());
        var cut = RenderComponent<SlipPanel>();

        cut.Find(".slip-card button.desk-btn--destructive").Click();

        Assert.Empty(Slip.Items);
        Assert.Empty(cut.FindAll(".slip-card"));
    }

    [Fact]
    public void The_parlay_switch_hides_the_stakes_and_shows_the_combined_price()
    {
        Slip.Add(Item(-128));
        Slip.Add(Item(-105, outcome: BoardRows.Away, gameId: 8));
        var cut = RenderComponent<SlipPanel>();
        Assert.Equal(2, cut.FindAll(".slip-card__stake").Count);

        cut.FindAll("button.gate__opt").First(b => b.TextContent.Trim() == "One parlay").Click();

        Assert.Empty(cut.FindAll(".slip-card__stake"));
        Assert.Equal("+248", cut.Find(".slip-parlay__price").TextContent.Trim());
    }

    [Fact]
    public void Log_with_a_closing_item_logs_the_rest_and_reports_what_it_left()
    {
        Slip.Add(Item(stake: 50m));
        Slip.Add(Item(stake: 25m, outcome: BoardRows.Away));
        Slip.Add(Item(closing: true, gameId: 9));
        var cut = RenderComponent<SlipPanel>();

        cut.Find("button.desk-btn--filled").Click();

        Assert.Equal(2, Data.Straights.Count);
        Assert.True(Assert.Single(Slip.Items).IsClosing);
        Assert.Contains("2 logged · 1 left on the slip", cut.Find(".slip-report").TextContent);

        Key(cut, "Show steps").Click();
        Assert.Equal(3, cut.FindAll(".slip-steps li").Count);
    }

    [Fact]
    public void A_parlay_logs_one_parlay_with_zero_stake_legs()
    {
        Slip.Add(Item(-128, stake: 0m));
        Slip.Add(Item(-105, stake: 0m, outcome: BoardRows.Away, gameId: 8));
        Slip.SetMode(SlipMode.Parlay);
        Slip.SetParlay(25m, null, "draftkings");
        var cut = RenderComponent<SlipPanel>();

        cut.Find("button.desk-btn--filled").Click();

        var parlay = Assert.Single(Data.Parlays);
        Assert.Equal(25m, parlay.Stake);
        Assert.Equal(2, parlay.Legs.Count);
        Assert.Empty(Slip.Items);
    }

    [Fact]
    public void A_failed_save_leaves_the_slip_intact()
    {
        Slip.Add(Item());
        Slip.Add(Item(outcome: BoardRows.Away));
        Data.FailWith = new InvalidOperationException("database is down");
        var cut = RenderComponent<SlipPanel>();

        cut.Find("button.desk-btn--filled").Click();

        Assert.Equal(2, Slip.Items.Count);
        Assert.Contains("Not logged: database is down", cut.Find(".note--warn").TextContent);
    }

    [Fact]
    public void Nothing_loggable_disables_Log_and_says_why()
    {
        Slip.Add(Item(closing: true));
        var cut = RenderComponent<SlipPanel>();

        Assert.True(cut.Find("button.desk-btn--filled").HasAttribute("disabled"));
        Assert.Equal("Nothing on the slip can be logged as it stands.", cut.Find(".slip-why").TextContent.Trim());
    }

    [Fact]
    public void Start_over_confirms_when_the_slip_holds_more_than_one()
    {
        Slip.Add(Item());
        Slip.Add(Item(outcome: BoardRows.Away));
        var cut = RenderComponent<SlipPanel>();

        Key(cut, "Start over").Click();

        Assert.Single(Alerts.Asked);
        Assert.Empty(Slip.Items);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LineOps.Web.Tests --filter "FullyQualifiedName~SlipPanelTests"`
Expected: build error `'WindowCatalog' does not contain a definition for 'Slip'`.

- [ ] **Step 3: Add the catalog entry.** In `WindowCatalog`, after `public const string Form = "form";` add:

```csharp
    /// <summary>The desk session's slip: wagers banked from any Place wager window, logged together.</summary>
    public const string Slip = "slip";
```

and in `All`, directly after the `Wager` definition:

```csharp
        new()
        {
            Key = Slip,
            Title = "Slip",
            Icon = Icons.Material.Filled.Receipt,
            Group = "Data",
            ComponentType = typeof(SlipPanel),
            Description = "Wagers banked this session, logged as straight bets or one parlay.",
            // One slip per desk session; a second window onto it would be the same list twice.
            Singleton = true,
            DefaultWeight = 0.9,
            MinWidth = 380
        },
```

- [ ] **Step 4: Create `src/LineOps.Web/Components/Panels/SlipPanel.razor`:**

```razor
@using LineOps.Ingestion.Services
@using LineOps.Web.Services
@inherits PanelBase
@inject WagerSlip Slip
@inject IWagerData Data
@inject DeskToasts Toasts
@inject IDeskAlerts Alerts

@*
    The slip: the wagers banked this desk session, and the one place a parlay is logged.

    Each card says whether it can be logged and, where it cannot, what would fix it. The rules
    are WagerSlip's; this window only draws them. Log writes everything it can in one save and
    reports what it left, so a closing price never blocks the rest of the slip (spec §3).
*@

<div class="stack slip" style="gap:10px">
    <div class="row" style="flex-wrap:wrap">
        <DeskSwitch Options="Modes" Label="Logged as" Size="DeskKeySize.Small"
                    Value="Slip.Mode" ValueChanged="@((SlipMode m) => Slip.SetMode(m))" />
        <span class="dim" style="font-size:12px">Belongs to this desk session, not to a game.</span>
    </div>

    @if (_report is { } report)
    {
        <Note State="DeskState.Positive" Style="margin:0" Class="slip-report">
            <strong>@report.Headline</strong>
            <DeskButton Size="DeskKeySize.Small" OnClick="() => _showSteps = !_showSteps">
                @(_showSteps ? "Hide steps" : "Show steps")
            </DeskButton>
            @if (_showSteps)
            {
                <ol class="slip-steps">
                    @foreach (var step in report.Steps)
                    {
                        <li>@step</li>
                    }
                </ol>
            }
        </Note>
    }

    @if (_error is not null)
    {
        <Note State="DeskState.Warning" Style="margin:0">@_error</Note>
    }

    @if (Slip.Items.Count == 0)
    {
        <EmptyState Kind="EmptyStateKind.New">
            Nothing on the slip. Add to slip from any Place wager window banks the wager here.
        </EmptyState>
    }
    else
    {
        @foreach (var item in Slip.Items)
        {
            var state = StateOf(item);

            <div class="slip-card @(item.IsClosing ? "slip-card--blocked" : null)" @key="item.Id">
                <div class="slip-card__top">
                    <div>
                        <div class="slip-card__sel">@item.Selection</div>
                        <div class="dim slip-card__meta">@item.Matchup · @Day(item.StartsAt) · @item.Book</div>
                    </div>

                    @if (Slip.Mode == SlipMode.Straight)
                    {
                        @* A leg carries no stake, so the field goes in parlay mode. *@
                        <DeskField TValue="decimal" Mono="true" Immediate="true" Class="slip-card__stake"
                                   type="number" step="5" min="0" aria-label="Stake on this wager"
                                   Value="item.Stake"
                                   ValueChanged="@((decimal s) => Slip.Touch(item.Id, i => i with { Stake = s }))" />
                    }

                    <DeskButton Icon="@Icons.Material.Filled.Close" Role="DeskRole.Destructive"
                                Title="Drop from the slip" aria-label="Drop from the slip"
                                OnClick="() => Slip.Remove(item.Id)" />
                </div>

                <div class="slip-card__state">
                    <Tag State="state.State">@state.Word</Tag>
                    <span>@state.Sentence</span>
                </div>

                @if (state.Remedy is { } remedy)
                {
                    <div class="dim slip-card__remedy">@remedy</div>
                }
            </div>
        }

        @if (Slip.Mode == SlipMode.Parlay)
        {
            <div class="slip-parlay stack" style="gap:8px">
                <div class="row">
                    <span class="panel-h">Combined</span>
                    <span class="num slip-parlay__price">@(Slip.CombinedPrice is { } combined ? Price(combined) : "—")</span>
                </div>
                <div class="dim" style="font-size:12px">
                    @Slip.Legs.Count legs, prices multiplied; a book's own parlay price can replace it.
                </div>

                <StakeRow Stake="Slip.ParlayStake"
                          StakeChanged="@(s => Slip.SetParlay(s, Slip.ParlayPriceQuoted, Slip.ParlayBook))"
                          PriceAmerican="Slip.CombinedPrice" />

                <div class="row">
                    @* The book's own price for the parlay, when it gave one: it is what a clean win
                       pays. Without it the legs are multiplied, which is also how a push reprices. *@
                    <DeskField TValue="int?" Mono="true" type="number" style="max-width:120px"
                               placeholder="Parlay price" Value="Slip.ParlayPriceQuoted"
                               ValueChanged="@((int? p) => Slip.SetParlay(Slip.ParlayStake, p, Slip.ParlayBook))" />
                    <DeskSelect TValue="string" style="max-width:140px" Value="Slip.ParlayBook"
                                ValueChanged="@((string? b) => Slip.SetParlay(Slip.ParlayStake, Slip.ParlayPriceQuoted, b))">
                        <option value="">Book…</option>
                        @foreach (var book in Books)
                        {
                            <option value="@book">@book</option>
                        }
                    </DeskSelect>
                </div>
            </div>
        }

        <div class="row slip-footer">
            <DeskButton OnClick="StartOverAsync">Start over</DeskButton>
            <span style="flex:1"></span>
            <DeskButton Emphasis="DeskEmphasis.Filled" Icon="@Icons.Material.Filled.Bolt"
                        Busy="@_saving" Disabled="@(Waiting is not null)" OnClick="LogAsync">
                @LogLabel
            </DeskButton>
        </div>

        @if (Waiting is { } why)
        {
            <div class="warnc slip-why">@why</div>
        }
    }

    <p class="empty" style="padding:0">
        A logged wager becomes a journal row and leaves the slip. LineOps records bets placed
        elsewhere; nothing here reaches a sportsbook.
    </p>
</div>

@code {
    private static readonly IReadOnlyList<DeskSwitchOption<SlipMode>> Modes =
    [
        new(SlipMode.Straight, "Straight bets", "Each wager is its own bet"),
        new(SlipMode.Parlay, "One parlay", "Every wager the market still offers is a leg")
    ];

    private sealed record CardState(DeskState State, string Word, string Sentence, string? Remedy = null);

    private sealed record SlipReport(string Headline, IReadOnlyList<string> Steps);

    private SlipReport? _report;
    private bool _showSteps;
    private string? _error;
    private bool _saving;

    protected override void OnInitialized() => Slip.Changed += OnSlipChanged;

    protected override void OnDispose() => Slip.Changed -= OnSlipChanged;

    private void OnSlipChanged() => _ = InvokeAsync(StateHasChanged);

    private IEnumerable<string> Books
        => JournalService.CommonBooks
            .Concat(Slip.Items.Select(i => i.Book))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);

    private CardState StateOf(SlipItem item)
    {
        if (item.IsClosing)
            return Slip.Mode == SlipMode.Parlay
                ? new CardState(DeskState.Negative, "Cannot log", "Not a leg: the game has started.",
                    "Drop it, or log it from the Journal with the price you got.")
                : new CardState(DeskState.Negative, "Cannot log", "Closing line: this price cannot have been taken.",
                    "Drop it, or log it from the Journal with the price you got.");

        if (Slip.Mode == SlipMode.Straight && item.Stake <= 0)
            return new CardState(DeskState.Warning, "Needs a stake", "A straight bet is logged with a stake above zero.");

        if (Slip.HasTwin(item))
            return new CardState(DeskState.Warning, "Worth knowing", "Another wager on the slip is on this side; logging both records two bets.");

        return new CardState(DeskState.Positive, "Ready", ValueReading(item));
    }

    private static string ValueReading(SlipItem item)
        => item.Ev is { } ev
            ? PriceCell.EvLabel(ev) + (item.Fair is { } fair ? " · " + PriceCell.FairLabel(fair) : "")
            : "No fair price on this number, so value is unread.";

    /// <summary>What the Log key is waiting for, said under it.</summary>
    private string? Waiting
        => Slip.Mode == SlipMode.Straight
            ? Slip.Ready.Count == 0 ? "Nothing on the slip can be logged as it stands." : null
            : Slip.Legs.Count < 2 ? "A parlay needs two legs the market still offers."
            : Slip.ParlayStake <= 0 ? "Needs a stake above zero."
            : string.IsNullOrWhiteSpace(Slip.ParlayBook) ? "Needs the book the parlay was placed at."
            : null;

    private string LogLabel
        => Slip.Mode == SlipMode.Straight
            ? $"Log {Slip.Ready.Count} ready · {Slip.Ready.Sum(i => i.Stake):C0}"
            : $"Log parlay · {(Slip.CombinedPrice is { } combined ? Price(combined) : "—")}";

    private async Task LogAsync()
    {
        if (Waiting is not null || _saving)
            return;

        _saving = true;
        _error = null;
        _report = null;
        _showSteps = false;

        try
        {
            var parlay = Slip.Mode == SlipMode.Parlay;
            var logged = parlay ? Slip.Legs : Slip.Ready;
            var ids = logged.Select(i => i.Id).ToHashSet();

            if (parlay)
                await WithActivityAsync(() => Data.LogParlayAsync(Slip.ToParlayDraft()), () => PulseState.Healthy);
            else
                await WithActivityAsync(() => Data.LogStraightsAsync(Slip.StraightDrafts()), () => PulseState.Healthy);

            // One line per item, in the order the slip holds them.
            var steps = Slip.Items.Select(i => !ids.Contains(i.Id)
                    ? $"Left on the slip: {i.Selection} — {(i.IsClosing ? "closing line" : "no stake")}."
                    : parlay
                        ? $"Leg: {i.Selection} ({i.Book})."
                        : $"Logged {i.Stake:C0} on {i.Selection} ({i.Book}).")
                .ToList();

            if (parlay)
                steps.Add($"Parlay: {Slip.ParlayStake:C0} at {(Slip.CombinedPrice is { } c ? Price(c) : "—")} ({Slip.ParlayBook}).");

            Slip.Logged(ids);

            var headline = $"{ids.Count} logged · {Slip.Items.Count} left on the slip";
            _report = new SlipReport(headline, steps);
            Toasts.Success(headline);
        }
        catch (Exception ex)
        {
            // The slip is untouched: nothing was written (one save), so nothing leaves it.
            _error = $"Not logged: {ex.Message}";
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task StartOverAsync()
    {
        if (Slip.Items.Count > 1
            && !await Alerts.ConfirmAsync("Start the slip over?",
                $"{Slip.Items.Count} wagers leave the slip. Nothing is logged.",
                confirmLabel: "Start over", destructive: true))
            return;

        Slip.Clear();
        _report = null;
        _error = null;
    }
}
```

- [ ] **Step 5: Append the slip styles** to `lineops.css`:

```css
.slip-card { display: grid; gap: 6px; padding: 10px 12px; border: 1px solid var(--separator); border-radius: 6px; }
.slip-card--blocked { border-color: var(--state-negative); }
.slip-card__top { display: flex; align-items: flex-start; gap: 10px; }
.slip-card__top > div:first-child { flex: 1; min-width: 0; }
.slip-card__meta { font-size: 12px; }
.slip-card__stake { max-width: 96px; text-align: right; }
.slip-card__state { display: flex; align-items: center; gap: 8px; font-size: 13px; }
.slip-card__remedy { font-size: 12px; }
.slip-parlay { padding: 10px 12px; background: var(--surface-2); border-radius: 6px; }
.slip-parlay__price { font-size: 18px; font-weight: 600; margin-left: auto; }
.slip-footer { align-items: center; }
.slip-steps { margin: 6px 0 0; padding-left: 18px; font-size: 12px; }
```

- [ ] **Step 6: Run to verify they pass, and the catalog-wide tests still do**

Run: `dotnet test tests/LineOps.Web.Tests --filter "FullyQualifiedName~SlipPanelTests|FullyQualifiedName~WindowBarTests|FullyQualifiedName~CommandPaletteTests"`
Expected: all PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/LineOps.Web/Components/Panels/SlipPanel.razor src/LineOps.Web/Windowing/WindowCatalog.cs src/LineOps.Desk/wwwroot/css/lineops.css tests/LineOps.Web.Tests/SlipPanelTests.cs
git commit -m "feat(slip): a Slip window that logs the session's wagers as straight bets or one parlay" -m "Each card says whether its wager can be logged and what would fix it: ready, needs a stake, worth knowing (two on one side), or cannot log (a closing price, with the remedy). The switch makes the slip one parlay, whose price is the legs multiplied unless the book's own is typed. Log writes everything it can in one save, reports what it left and why, and leaves the slip untouched if the save fails."
```

---

### Task 10: The doors — a price cell and a book's rung open the wager window

**Files:**
- Modify: `src/LineOps.Desk/Windowing/FollowUpLauncher.cs` (`Launch`, lines 77-116)
- Modify: `src/LineOps.Web/Components/Desk/PriceCell.razor`
- Modify: `src/LineOps.Web/Components/Panels/BoardPanel.razor` (the six `PriceCell`s, lines 290-327; a `Take` method beside `Launch`, line 857)
- Modify: `src/LineOps.Web/Components/Panels/BoardAllBets.razor`
- Modify: `src/LineOps.Desk/wwwroot/css/lineops.css` (append)
- Test: `tests/LineOps.Web.Tests/PriceCellTests.cs`, `tests/LineOps.Web.Tests/FollowUpLauncherTests.cs` (append); `tests/LineOps.Web.Tests/BoardAllBetsTests.cs` (create)

**Interfaces:**
- Consumes: `BoardWager` parameters `GameId`, `Market`, `Outcome`, `Book` (Task 7); `IWagerData.RowAsync` (Task 7).
- Produces:
  - `FollowUpLauncher.Launch(WindowManager manager, string windowKey, IReadOnlyDictionary<string, object> parameters, int subjectId, string subtitle, string width = "640px")`. The existing single-id overload delegates to it. Re-launching the same key and subject while floating updates that follow-up's parameters and raises it.
  - `PriceCell.OnTake` — `EventCallback`. When set and the offer is live, `.price` gets `role="button"`, `tabindex="0"`, class `price--take`, and a tooltip line *Click to log this price.*; click or Enter/Space invokes it.
  - `BoardAllBets` live rungs render as `button.take`.

- [ ] **Step 1: Write the failing tests.** Append inside `PriceCellTests`:

```csharp
    // ---- Door 2: a live price opens the wager window on itself --------------------------

    [Fact]
    public void A_live_cell_offers_the_take_and_says_so()
    {
        var taken = 0;
        var cut = RenderComponent<PriceCell>(p => p
            .Add(x => x.Offer, Offer(rungs: Rung("draftkings", -110, 0.524)))
            .Add(x => x.OnTake, () => taken++));

        var cell = cut.Find(".price");
        Assert.Equal("button", cell.GetAttribute("role"));
        Assert.EndsWith("Click to log this price.", cell.GetAttribute("title"));

        cell.Click();
        Assert.Equal(1, taken);
    }

    [Fact]
    public void A_closing_cell_offers_no_take()
    {
        var taken = 0;
        var cut = RenderComponent<PriceCell>(p => p
            .Add(x => x.Offer, Offer(isClosing: true, rungs: Rung("draftkings", -110, 0.524)))
            .Add(x => x.OnTake, () => taken++));

        var cell = cut.Find(".price");
        Assert.Null(cell.GetAttribute("role"));

        cell.Click();
        Assert.Equal(0, taken);
    }

    [Fact]
    public void Without_a_take_handler_the_cell_is_not_a_button()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer, Offer(rungs: Rung("draftkings", -110, 0.524))));

        Assert.Null(cut.Find(".price").GetAttribute("role"));
        Assert.DoesNotContain("Click to log", cut.Find(".price").GetAttribute("title") ?? "");
    }
```

Append inside `FollowUpLauncherTests`:

```csharp
    /// <summary>A door names more than the game: the side and the book travel with it.</summary>
    [Fact]
    public void A_follow_up_can_carry_several_parameters()
    {
        var manager = EmptyDesk();
        var launcher = new FollowUpLauncher();

        launcher.Launch(manager, WindowCatalog.Wager,
            new Dictionary<string, object> { ["GameId"] = 7, ["Market"] = "h2h", ["Outcome"] = "Kansas City Chiefs", ["Book"] = "fanduel" },
            7, "Bills at Chiefs");

        var window = Assert.Single(manager.Windows);
        Assert.Equal("fanduel", window.Parameters["Book"]);
        Assert.Equal(7, window.Parameters["GameId"]);
    }

    [Fact]
    public void Relaunching_a_floating_follow_up_re_aims_it_rather_than_stacking_another()
    {
        var launcher = new FollowUpLauncher();
        var desk = FullDesk();

        launcher.Launch(desk, WindowCatalog.Wager, new Dictionary<string, object> { ["GameId"] = 7, ["Market"] = "h2h" }, 7, "x");
        launcher.Launch(desk, WindowCatalog.Wager, new Dictionary<string, object> { ["GameId"] = 7, ["Market"] = "spread" }, 7, "x");

        var only = Assert.Single(launcher.Open);
        Assert.Equal("spread", only.Parameters["Market"]);
    }
```

Create `tests/LineOps.Web.Tests/BoardAllBetsTests.cs`:

```csharp
using Bunit;
using LineOps.Desk.Windowing;
using LineOps.Web.Components.Panels;
using LineOps.Web.Tests.Support;
using LineOps.Web.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>Door 3: a book's live price in Every book opens the wager window on that book.</summary>
public class BoardAllBetsTests : WagerTestContext
{
    [Fact]
    public void A_live_rung_opens_the_wager_window_on_its_book()
    {
        var cut = RenderComponent<BoardAllBets>(p => p.Add(x => x.GameId, BoardRows.GameId));

        cut.FindAll("button.take").First(b => b.TextContent.Contains("-135")).Click();

        var manager = Services.GetRequiredService<WindowManager>();
        var window = Assert.Single(manager.Windows, w => w.Definition.Key == WindowCatalog.Wager);
        Assert.Equal("fanduel", window.Parameters["Book"]);
        Assert.Equal(BoardRows.Home, window.Parameters["Outcome"]);
        Assert.Equal("h2h", window.Parameters["Market"]);
    }

    [Fact]
    public void A_closing_rung_is_not_a_door()
    {
        Data.Row = BoardRows.Row(closing: true);
        var cut = RenderComponent<BoardAllBets>(p => p.Add(x => x.GameId, BoardRows.GameId));

        Assert.Empty(cut.FindAll("button.take"));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LineOps.Web.Tests --filter "FullyQualifiedName~PriceCellTests|FullyQualifiedName~FollowUpLauncherTests|FullyQualifiedName~BoardAllBetsTests"`
Expected: build error `'PriceCell' does not contain a definition for 'OnTake'` and no `Launch` overload taking a dictionary.

- [ ] **Step 3: `FollowUpLauncher` overload.** Replace the body of the existing `Launch` and add the overload:

```csharp
    public void Launch(
        WindowManager manager,
        string windowKey,
        string parameterName,
        int subjectId,
        string subtitle,
        string width = "640px")
        => Launch(manager, windowKey, new Dictionary<string, object> { [parameterName] = subjectId }, subjectId, subtitle, width);

    /// <summary>
    /// Opens a follow-up with several parameters — a game and the side and book a door names.
    /// Re-launching the same view on the same subject while it floats re-aims it with the new
    /// parameters and raises it, rather than stacking a second dialog on top.
    /// </summary>
    public void Launch(
        WindowManager manager,
        string windowKey,
        IReadOnlyDictionary<string, object> parameters,
        int subjectId,
        string subtitle,
        string width = "640px")
    {
        if (manager.Catalog.Find(windowKey) is not { } definition)
            return;

        var copy = new Dictionary<string, object>(parameters);

        if (!manager.AtCapacity)
        {
            manager.Open(definition, copy, $"{definition.Title} · {subtitle}");
            return;
        }

        if (_open.FirstOrDefault(f => f.Key == windowKey && f.SubjectId == subjectId) is { } existing)
        {
            existing.Parameters.Clear();
            foreach (var (key, value) in copy)
                existing.Parameters[key] = value;

            Raise(existing);
            return;
        }

        _open.Add(new FollowUp
        {
            Key = windowKey,
            SubjectId = subjectId,
            Definition = definition,
            Parameters = copy,
            Subtitle = subtitle,
            Width = width,
            Order = ++_raiseCounter
        });

        Notify();
    }
```

Keep the existing XML doc comment above the first overload.

- [ ] **Step 4: `PriceCell` take.** In `PriceCell.razor`, change the opening `<div class="price …">` of `Body` to:

```razor
        <div class="price @(closing ? "price--closing" : null) @(Takeable ? "price--take" : null)"
             title="@(closing ? null : Tooltip)"
             role="@(Takeable ? "button" : null)"
             tabindex="@(Takeable ? "0" : null)"
             @onclick="TakeAsync"
             @onclick:stopPropagation="Takeable"
             @onkeydown="KeyAsync">
```

Add to `@code`:

```csharp
    /// <summary>
    /// Opens the wager window on this price (spec §5.3). Only a live price is a door: a close is
    /// a number that can no longer be taken, so it stays a reading.
    /// </summary>
    [Parameter] public EventCallback OnTake { get; set; }

    private bool Takeable => OnTake.HasDelegate && Offer is { IsClosing: false };

    private Task TakeAsync() => Takeable ? OnTake.InvokeAsync() : Task.CompletedTask;

    private Task KeyAsync(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
        => e.Key is "Enter" or " " ? TakeAsync() : Task.CompletedTask;
```

and change the `Tooltip` getter's return to:

```csharp
            var text = FairLine.Length == 0 ? books : $"{FairLine}\n{books}";
            return Takeable ? $"{text}\nClick to log this price." : text;
```

- [ ] **Step 5: `BoardPanel` wiring.** Add `OnTake` to each of the six `PriceCell`s, passing the pair and the offer; for example the first:

```razor
                <PriceCell Offer="context.Item.Moneyline.First" Selection="@Side(context.Item, true)"
                           StartsAt="context.Item.Game.StartsAt"
                           OnTake="() => Take(context.Item, context.Item.Moneyline, context.Item.Moneyline.First)" />
```

and likewise `Moneyline.Second`, `Spread.First`, `Spread.Second`, `Total.First`, `Total.Second` with their own pair and offer. Add beside `Launch` at the end of `@code`:

```csharp
    /// <summary>
    /// Door 2: a live price opens the wager window on that side at that book. The row's Place
    /// wager action is Door 1 and still opens on the most value.
    /// </summary>
    private Task Take(BoardRow row, MarketPair pair, BestOffer? offer)
    {
        if (offer is null)
            return Task.CompletedTask;

        _followUps.Launch(Manager, WindowCatalog.Wager, new Dictionary<string, object>
        {
            ["GameId"] = row.Game.Id,
            ["Market"] = pair.Market,
            ["Outcome"] = offer.Outcome,
            ["Book"] = offer.Book
        }, row.Game.Id, Matchup(row.Game), "480px");

        return Task.CompletedTask;
    }
```

- [ ] **Step 6: `BoardAllBets` (Door 3).** Replace the header usings/injections:

```razor
@using LineOps.Core.Analytics
@using LineOps.Data.CrossReference
@using LineOps.Desk.Primitives
@using LineOps.Web.Services
@inject IWagerData Data
@inject WindowManager Manager
```

Add at the very end of the markup, after the closing `}` of the `else` block:

```razor
@* Door 3 opens the wager window above the desk when it is full, as the Board's doors do. *@
<FollowUpLayer Launcher="_followUps" OnChanged="StateHasChanged" />
```

Change the two `Cell` call sites to pass the market: `@Cell(market, market.First, context.Item)` and `@Cell(market, market.Second, context.Item)`. In `@code`, replace `OnParametersSetAsync` and `Cell`, and add `Take`:

```csharp
    private readonly FollowUpLauncher _followUps = new();

    protected override async Task OnParametersSetAsync()
        => Row = await Data.RowAsync(GameId);

    private RenderFragment Cell(MarketPair market, BestOffer? offer, string book) => builder =>
    {
        var rung = offer?.Rungs.FirstOrDefault(r => r.Book == book);

        if (offer is null || rung is null)
        {
            builder.AddContent(0, "—");
            return;
        }

        // A live rung is a door (spec §5.3): it opens the wager window on this book's price.
        var live = !offer.IsClosing;

        if (live)
        {
            builder.OpenElement(10, "button");
            builder.AddAttribute(11, "type", "button");
            builder.AddAttribute(12, "class", "take");
            builder.AddAttribute(13, "title", "Click to log this price.");
            builder.AddAttribute(14, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () => Take(market, offer, book)));
        }

        var isBest = book == offer.Book;

        builder.OpenElement(1, "span");
        builder.AddAttribute(2, "class", isBest ? "up" : "dim");

        var line = rung.Line is { } l ? OddsMath.FormatLine(l, offer.Outcome) + " " : "";
        builder.AddContent(3, line + OddsMath.FormatPrice(rung.PriceAmerican));

        builder.CloseElement();

        // Value against the fair price, where this book's number has one. Positive is lit in
        // the same tone as the best price: both are reasons to bet here.
        if (rung.Ev is { } ev && live)
        {
            builder.OpenElement(4, "span");
            builder.AddAttribute(5, "class", ev > 0 ? "up" : "dim");
            builder.AddAttribute(6, "style", "font-size:10px;margin-left:6px");
            builder.AddContent(7, PriceCell.EvLabel(ev));
            builder.CloseElement();
        }

        if (live)
            builder.CloseElement();
    };

    private void Take(MarketPair market, BestOffer offer, string book)
    {
        if (Row is null)
            return;

        var matchup = $"{Row.Game.AwayTeam?.Name ?? "Away"} at {Row.Game.HomeTeam?.Name ?? "Home"}";

        _followUps.Launch(Manager, WindowCatalog.Wager, new Dictionary<string, object>
        {
            ["GameId"] = Row.Game.Id,
            ["Market"] = market.Market,
            ["Outcome"] = offer.Outcome,
            ["Book"] = book
        }, Row.Game.Id, matchup, "480px");
    }
```

Add `@using Microsoft.AspNetCore.Components.Web` if `MouseEventArgs` does not resolve (it is imported in `Components/_Imports.razor`, so normally it does). `ScopeFactory` and the `BoardService` resolution are no longer used and are removed.

- [ ] **Step 7: Append the take styles** to `lineops.css`:

```css
.price--take { cursor: pointer; }
.price--take:hover .price__odds, .price--take:focus-visible .price__odds { color: var(--accent); }
.price--take:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.take { all: unset; cursor: pointer; }
.take:hover span { color: var(--accent); }
.take:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
```

- [ ] **Step 8: Run to verify they pass, and the whole Web suite**

Run: `dotnet test tests/LineOps.Web.Tests`
Expected: all PASS.

`BoardAllBetsTests` is the first bUnit test to render a `DeskGrid` (MudDataGrid). If it fails for a missing MudBlazor provider rather than for the behaviour under test, render the provider first in the test (`RenderComponent<MudBlazor.MudPopoverProvider>();`) and re-run; do not weaken the assertions.

- [ ] **Step 9: Commit**

```powershell
git add src/LineOps.Desk/Windowing/FollowUpLauncher.cs src/LineOps.Web/Components/Desk/PriceCell.razor src/LineOps.Web/Components/Panels/BoardPanel.razor src/LineOps.Web/Components/Panels/BoardAllBets.razor src/LineOps.Desk/wwwroot/css/lineops.css tests/LineOps.Web.Tests/PriceCellTests.cs tests/LineOps.Web.Tests/FollowUpLauncherTests.cs tests/LineOps.Web.Tests/BoardAllBetsTests.cs
git commit -m "feat(board): a live price opens the wager window on itself" -m "The wager window only opened from a row, on the side with the most value, so taking any other side or book cost a hunt through the chips. A live price cell on the Board, and a book's live price in Every book, now open it on that side at that book; a close stays a reading, since it can no longer be taken. The follow-up launcher carries several parameters, and re-aims a floating window rather than stacking another."
```

---

### Task 11: The Journal's parlay form retires into the slip

**Files:**
- Modify: `src/LineOps.Web/Components/Panels/JournalPanel.razor`
- Test: `tests/LineOps.Web.Tests/JournalPanelTests.cs` (create); `tests/LineOps.Web.Tests/SelectionGuardTests.cs` (append)

**Interfaces:**
- Consumes: `WindowCatalog.Slip` (Task 9), `StakeRow` (Task 6), `CheckList`, `Checks.Duplicate`, `Checks.DuplicateWindow` (Tasks 5, 8), `IWagerData.LatestOnSideAsync` (Task 7).
- Produces: nothing new for later tasks.

- [ ] **Step 1: Write the failing tests.** Create `tests/LineOps.Web.Tests/JournalPanelTests.cs`:

```csharp
using LineOps.Desk.Windowing;
using LineOps.Web.Components.Panels;
using LineOps.Web.Tests.Support;
using LineOps.Web.Windowing;

namespace LineOps.Web.Tests;

/// <summary>
/// The Journal keeps Log entry as the manual door and gives parlays to the slip. Read from the
/// source: the Journal loads from the database the moment it renders, and this project has none.
/// </summary>
public class JournalPanelTests
{
    private static string Source()
        => File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "Panels", "JournalPanel.razor"));

    [Fact]
    public void Log_parlay_is_gone()
    {
        var source = Source();

        Assert.DoesNotContain("Log parlay", source);
        Assert.DoesNotContain("ParlayForm", source);
        Assert.DoesNotContain("SaveParlayAsync", source);
        Assert.DoesNotContain("FormMode.Parlay", source);
    }

    [Fact]
    public void Open_slip_opens_the_slip_window()
    {
        var source = Source();

        Assert.Contains("Open slip", source);
        Assert.Contains("Manager.Open(WindowCatalog.Find(WindowCatalog.Slip)!)", source);

        var window = new WindowManager(new AppWindowCatalog()).Open(WindowCatalog.Find(WindowCatalog.Slip)!);
        Assert.Equal(typeof(SlipPanel), window.Definition.ComponentType);
    }

    [Fact]
    public void The_manual_entry_uses_the_shared_stake_row_and_the_duplicate_check()
    {
        var source = Source();

        Assert.Contains("<StakeRow", source);
        Assert.Contains("Checks.Duplicate(", source);
    }
}
```

Append inside `SelectionGuardTests`:

```csharp
    [Fact]
    public void No_panel_declares_its_own_stake_field()
    {
        var panels = Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "Panels");

        var offenders = Directory.EnumerateFiles(panels, "*.razor")
            .Where(f => File.ReadAllText(f).Contains("placeholder=\"Stake\""))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Declare the stake with <StakeRow>, not a field of its own: " + string.Join(", ", offenders));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LineOps.Web.Tests --filter "FullyQualifiedName~JournalPanelTests|FullyQualifiedName~SelectionGuardTests"`
Expected: FAIL — "Log parlay" is found, and `JournalPanel.razor` declares `placeholder="Stake"` twice.

- [ ] **Step 3: Edit `JournalPanel.razor`.**

(a) Header — add after `@inject IDeskAlerts Alerts`:

```razor
@using LineOps.Web.Services
@inject IWagerData Wagers
```

(b) Replace the Log parlay key (lines 24-28):

```razor
    @* Parlays are built on the slip now (spec §5.4): the form that duplicated the leg fields,
       the stake row and the book picker retired into it. *@
    <DeskButton Icon="@Icons.Material.Filled.Receipt" OnClick="OpenSlip">
        Open slip
    </DeskButton>
```

(c) In the entry form, replace the stake row (lines 76-82):

```razor
        <StakeRow Stake="_entry.Stake" StakeChanged="@(s => _entry.Stake = s)"
                  PriceAmerican="@(_entry.PriceTaken == 0 ? null : _entry.PriceTaken)" />

        <div class="row">
            @BookPicker(_entry.Book, SetEntryBookAsync)
            <DeskField TValue="string" style="flex:1; min-width:160px"
                       placeholder="Note (optional)" @bind-Value="_entry.Note" />
        </div>

        @* The duplicate check only: value and price age need a live offer this form does not have. *@
        @if (_entryCheck is { } check)
        {
            <CheckList Items="@(new[] { check })" Title="Before you save" />
        }
```

and change `@LegFields(_entry, true)` (line 74) to `@LegFields(_entry)`.

(d) Delete the whole `@if (_mode == FormMode.Parlay) { … }` block (lines 96-145).

(e) Replace the empty state (lines 147-152):

```razor
@if (_entries.Count == 0 && _parlays.Count == 0)
{
    <EmptyState Kind="@(Filtered ? EmptyStateKind.Filtered : EmptyStateKind.New)">
        @(Filtered
            ? "No entries match these filters."
            : "No entries logged yet. Log entry records a bet by hand; parlays are built on the slip.")
    </EmptyState>
}
```

(f) In `@code`:
- `private enum FormMode { None, Entry }`
- Delete the `ParlayForm` class, the `_legs` and `_parlay` fields, `ParlayValid`, `ShowParlayForm` and `SaveParlayAsync`.
- Add the field `private Check? _entryCheck;`
- In `ShowEntryForm`, `EditEntry` and `CloseForm` add `_entryCheck = null;`.
- Add:

```csharp
    private void OpenSlip() => Manager.Open(WindowCatalog.Find(WindowCatalog.Slip)!);

    private async Task SetEntryBookAsync(string book)
    {
        _entry.Book = book;
        await SideChangedAsync();
    }

    /// <summary>
    /// Re-reads the duplicate check when the side it is keyed on moves — game, market, outcome,
    /// book — and never from the render. Not for an edit: the entry being edited is its own match.
    /// </summary>
    private async Task SideChangedAsync()
    {
        if (_editing is not null
            || _entry.GameId is not { } gameId
            || _entry.Market == FreeText
            || string.IsNullOrWhiteSpace(_entry.Outcome)
            || string.IsNullOrWhiteSpace(_entry.Book))
        {
            _entryCheck = null;
            return;
        }

        var prior = await Wagers.LatestOnSideAsync(gameId, _entry.Market, _entry.Outcome.Trim(), _entry.Book,
            DateTimeOffset.UtcNow - Checks.DuplicateWindow);

        _entryCheck = Checks.Duplicate(true, prior, new CheckActions());
    }
```

(g) Replace `LegFields` so the side's fields re-read the check when they change:

```razor
    private RenderFragment LegFields(Draft d) => @<text>
        <GamePicker Value="d.GameId" Selected="d.Game"
                    ValueChanged="@(async (int? id) => { d.GameId = id; await SideChangedAsync(); })" />

        <div class="row">
            <DeskSelect TValue="string" Value="d.Market"
                        ValueChanged="@(async (string? m) => { d.Market = m ?? Markets.Spread; await SideChangedAsync(); })">
                <option value="@Markets.Moneyline">Moneyline</option>
                <option value="@Markets.Spread">Spread</option>
                <option value="@Markets.Total">Total</option>
                <option value="@FreeText">Other (describe)</option>
            </DeskSelect>

            @if (d.Market == FreeText)
            {
                <DeskField TValue="string" style="flex:1; min-width:200px"
                           placeholder="e.g. Player X over 1.5 receiving TDs" @bind-Value="d.FreeTextMarket" />
            }
            else
            {
                <DeskField TValue="string" style="max-width:190px"
                           placeholder="@(d.Market == Markets.Total ? "over / under" : "Team")"
                           Value="d.Outcome"
                           ValueChanged="@(async (string? o) => { d.Outcome = o ?? string.Empty; await SideChangedAsync(); })" />
            }

            @if (d.Market is Markets.Spread or Markets.Total)
            {
                <DeskField TValue="decimal?" Mono="true" style="max-width:90px" type="number" step="0.5"
                           placeholder="Line" @bind-Value="d.LineTaken" />
            }

            <DeskField TValue="int" Mono="true" style="max-width:90px" type="number"
                       placeholder="Price" @bind-Value="d.PriceTaken" />
        </div>
    </text>;
```

(h) Replace `BookPicker` so it can await:

```razor
    private RenderFragment BookPicker(string current, Func<string, Task> set) => @<text>
        <DeskSelect TValue="string" style="max-width:140px" Value="current"
                    ValueChanged="@(async (string? b) => await set(b ?? string.Empty))">
            <option value="">Book…</option>
            @foreach (var book in _books)
            {
                <option value="@book">@book</option>
            }
        </DeskSelect>
    </text>;
```

The Parlays grid, its settle menu and delete stay: logged parlays are still read and settled here.

- [ ] **Step 4: Run to verify they pass, then the whole Web suite**

Run: `dotnet test tests/LineOps.Web.Tests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/LineOps.Web/Components/Panels/JournalPanel.razor tests/LineOps.Web.Tests/JournalPanelTests.cs tests/LineOps.Web.Tests/SelectionGuardTests.cs
git commit -m "feat(journal): parlays are built on the slip, and the manual entry checks for a prior bet" -m "The Journal's Log parlay form duplicated the leg fields, the stake row and the book picker; keeping it beside the slip would be two ways to log a parlay from the first day. The key is Open slip now. Log entry stays as the door for a bet the board never priced, uses the shared stake row, and says when the side it names is already in the journal."
```

---

### Task 12: Test bets for exercising the UI

**Files:**
- Modify and commit: `scripts/seed-test-bets.sql` (currently untracked)

**Interfaces:** none. Dev-only; never run by the application; never on a machine about to run `publish-data.ps1`.

- [ ] **Step 1: Fix the script.**
  1. Books lower-case, as `JournalService` stores them and the duplicate check matches: `'DraftKings'` → `'draftkings'`, `'FanDuel'` → `'fanduel'`, `'BetMGM'` → `'betmgm'` (five occurrences).
  2. Replace the header's run line (line 17) with the PowerShell form — PowerShell has no `<` redirection, and the container's SCRAM auth needs the password from `.env`:

```sql
--   $pw = (Select-String -Path .env -Pattern '^POSTGRES_PASSWORD=(.+)$').Matches[0].Groups[1].Value.Trim()
--   Get-Content scripts/seed-test-bets.sql -Raw | docker exec -i -e "PGPASSWORD=$pw" lineops-postgres psql -U lineops -d lineops -X -v ON_ERROR_STOP=1
--
-- To take the test bets out again without adding them back:
--   'DELETE FROM "JournalEntries" WHERE "Note" = ''test bet (seed-test-bets.sql)''; DELETE FROM "Parlays" WHERE "Note" = ''test bet (seed-test-bets.sql)'';' |
--     docker exec -i -e "PGPASSWORD=$pw" lineops-postgres psql -U lineops -d lineops -X
```

- [ ] **Step 2: Exercise it (only if the local Postgres holds data and this machine is not about to publish)**

Run the seed line from the header twice.
Expected: both runs print the `-- seeded` listing with the same rows (idempotent): two pending `h2h` rows at `draftkings` on one NFL game, one `Win` at `fanduel`, two parlay legs at `betmgm`. Then open the desk, open Place wager on that NFL game's home moneyline at DraftKings and confirm the strip reads *Already in the journal …*. Finally run the removal line and confirm `select count(*) from "JournalEntries" where "Note" = 'test bet (seed-test-bets.sql)'` returns 0.

- [ ] **Step 3: Commit**

```powershell
git add scripts/seed-test-bets.sql
git commit -m "chore(scripts): test bets to exercise the wager window, the slip and the Journal against" -m "The journal is empty on every machine, so the duplicate check, the slip's report and the parlay grid have nothing to render against. The script seeds two bets on one side of the next NFL game, one graded win and one pending parlay, all carrying one note it deletes first, so it is idempotent and reversible. Books are lower-case as the journal stores them. Run by hand, never before publish-data.ps1."
```

---

### Task 13: Verification before "done" (CLAUDE.md)

- [ ] **Step 1: Builds are clean** (no errors, and read the output for warnings this work introduced)

Run:
```powershell
dotnet build src/LineOps.Web
dotnet build tests/LineOps.Tests
dotnet build tests/LineOps.Web.Tests
```
Expected: `Build succeeded` with `0 Error(s)` for each.

- [ ] **Step 2: Every test is green** (Docker Desktop running for Testcontainers)

Run: `dotnet test`
Expected: `Passed!` for both test projects, `Failed: 0`.

- [ ] **Step 3: The Worker host starts**

Run: `dotnet run --project src/LineOps.Worker` in the background, watch for the host's startup log (no configuration exception about duplicate `"//"` keys), then stop it with Ctrl+C.
Expected: the host reaches `Application started` (or the Worker's equivalent hosted-service startup line) with no exception.

- [ ] **Step 4: The desk, by hand** — `dotnet run --project src/LineOps.Web --launch-profile http`, then:
  - Board → a priced row → Place wager: lands on the most value; the strip shows three rows; Log disabled with *Needs a stake above zero.*
  - Click a different price cell on the Board: the wager window opens on that side and book.
  - Every book → a non-best book's price: the window opens on that book's price, "one of N books".
  - Add to slip twice; open Slip from the window menu; switch to One parlay; the combined price shows; Log parlay; the Journal's Parlays grid shows it (refreshed by the change notice, not a click).
  - A game in play: its price cells are not clickable; its wager window disables Log with the closing-line row.
  - Journal: no Log parlay key; Open slip opens the Slip window; Log entry has the shared stake row.

- [ ] **Step 5: Data coverage.** Not applicable: this work changes no ingestion and no stored data (the seed script is dev-only and not run by the app). If the seed script was run on this machine, run its removal line before any `publish-data.ps1`.

- [ ] **Step 6: Repository hygiene**

Run: `git worktree list; git stash list; git branch --list; git status --short`
Expected: one worktree; no stashes; branches `main`, `LineX_Development`, `LineX_Improvements` only; a clean tree. Do not push or merge: `LineX_Improvements` is pending the operator's test.
