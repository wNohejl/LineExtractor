using System.Reflection;
using Bunit;
using XLines.Core.Entities;
using XLines.Data;
using XLines.Data.CrossReference;
using XLines.Desk.Windowing;
using XLines.Ingestion.Services;
using XLines.Web.Components.Desk;
using XLines.Web.Components.Panels;
using XLines.Web.Tests.Support;
using XLines.Web.Windowing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XLines.Web.Services;

namespace XLines.Web.Tests;

/// <summary>
/// The Line watch workspace (ADR 0029): first in the catalogue, the Board beside Line movement with
/// the Journal a tab behind it, and — while it is the workspace on the desk — a press on the Board
/// shown in the Line movement window already open. Outside it the Board is as it always was.
/// </summary>
public class LineWatchTests : WagerTestContext
{
    public LineWatchTests()
    {
        Services.AddSingleton<IDbContextFactory<XLinesDbContext>>(new UnreachableFactory());
        Services.AddSingleton<RangePull>();
        Services.AddScoped<XLines.Desk.DeskJobRunner>();
    }

    private WindowManager Desk => Services.GetRequiredService<WindowManager>();

    private static Workspace LineWatchSpace => WindowCatalog.Workspaces[0];

    private WindowInstance Odds => Assert.Single(Desk.Windows, w => w.Definition.Key == WindowCatalog.Odds);

    // ---- The workspace -------------------------------------------------------------------

    [Fact]
    public void Line_watch_leads_the_workspaces_and_says_what_it_is()
    {
        var watch = LineWatchSpace;

        Assert.Equal("Line watch", watch.Name);
        Assert.Equal(LineWatch.Tag, watch.Tag);
        Assert.Equal([WindowCatalog.Board, WindowCatalog.Odds, WindowCatalog.Journal], watch.WindowKeys);
        Assert.Equal([0, 1, 1], watch.Columns);

        // Every workspace the application offers says in a sentence what it is for, and wears an icon.
        Assert.All(WindowCatalog.Workspaces, w =>
        {
            Assert.False(string.IsNullOrWhiteSpace(w.Description));
            Assert.EndsWith(".", w.Description);
            Assert.False(string.IsNullOrWhiteSpace(w.Icon));
        });
        Assert.Single(WindowCatalog.Workspaces, w => w.Tag == LineWatch.Tag);
    }

    [Fact]
    public void Applied_it_shows_the_board_beside_line_movement_with_the_journal_behind_it()
    {
        Assert.True(Desk.ApplyWorkspace(LineWatchSpace));

        var columns = Desk.Columns;
        Assert.Equal(2, columns.Count);
        Assert.Equal(WindowCatalog.Board, columns[0].Showing.Definition.Key);
        Assert.Equal(WindowCatalog.Odds, columns[1].Showing.Definition.Key);
        Assert.Equal([WindowCatalog.Odds, WindowCatalog.Journal], columns[1].Tabs.Select(t => t.Definition.Key));
        Assert.True(LineWatch.IsActive(Desk));
    }

    // ---- The rule, at the desk -----------------------------------------------------------

    [Fact]
    public void A_press_re_aims_the_line_movement_window_on_the_desk_and_brings_it_forward()
    {
        Desk.ApplyWorkspace(LineWatchSpace);
        var board = Desk.Columns[0].Showing;
        var journal = Assert.Single(Desk.Windows, w => w.Definition.Key == WindowCatalog.Journal);
        Desk.Focus(journal.Id);
        Assert.False(Desk.IsShowing(Odds));

        var shown = LineWatch.Show(Desk, 7, Markets.Spread, BoardRows.Away, "Bills at Chiefs", board.Id);

        Assert.Same(Odds, shown);
        Assert.Equal(3, Desk.Windows.Count);
        Assert.True(Desk.IsShowing(Odds));
        Assert.Equal((7, Markets.Spread, BoardRows.Away), (Odds.Parameters["GameId"], Odds.Parameters["Market"], Odds.Parameters["Outcome"]));
        Assert.Equal("Bills at Chiefs", Odds.Title);

        // The Board stays the window the operator is in.
        Assert.Equal(board.Id, Desk.FocusedId);

        // A row press names only the game: the window keeps the market and side it is on.
        LineWatch.Show(Desk, 8, null, null, "Jets at Dolphins", board.Id);
        Assert.Equal(8, Odds.Parameters["GameId"]);
        Assert.Null(Odds.Parameters["Market"]);
        Assert.Null(Odds.Parameters["Outcome"]);
    }

    [Fact]
    public void With_line_movement_closed_the_rule_ends_and_a_press_opens_nothing()
    {
        Desk.ApplyWorkspace(LineWatchSpace);
        Desk.Close(Odds.Id);

        // The Desk still calls Line watch the active workspace; with no Line movement on the desk
        // the rule does not hold, and a press opens no window of its own.
        Assert.Equal(LineWatch.Tag, Desk.ActiveWorkspace?.Tag);
        Assert.False(LineWatch.IsActive(Desk));
        Assert.Null(LineWatch.Show(Desk, 7, null, null, "Bills at Chiefs", null));
        Assert.DoesNotContain(Desk.Windows, w => w.Definition.Key == WindowCatalog.Odds);
    }

    // ---- The rule, on the Board ----------------------------------------------------------

    /// <summary>
    /// The Board with one row on it. Its first load fails here (no database), and the test places
    /// what a load would have read, as BoardStatsSourceTests does.
    /// </summary>
    private IRenderedComponent<BoardPanel> Board()
    {
        var panel = RenderComponent<BoardPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".empty--error")), Settle);

        var desk = typeof(DeskPanelBase);
        desk.GetProperty("LoadError", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel.Instance, null);
        desk.GetProperty("Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel.Instance, true);

        var rows = new List<BoardRow> { BoardRows.Row() };
        typeof(BoardPanel).GetField("_rows", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel.Instance, rows);
        typeof(BoardPanel).GetField("_visible", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel.Instance, rows);

        panel.Render();
        return panel;
    }

    private static void PressRow(IRenderedFragment board) => board.FindAll("tbody tr.mud-table-row")[0].Children[0].Click();

    /// <summary>The Chiefs' moneyline cell: the first market pair's home price.</summary>
    private static void PressChiefsMoneyline(IRenderedFragment board) => board.FindAll("button.price--take")[0].Click();

    [Fact]
    public void In_line_watch_the_board_says_what_a_press_does()
    {
        Desk.ApplyWorkspace(LineWatchSpace);

        var board = Board();

        // One line: the first clause is the disclosure's label, and nothing more shows until it is pressed.
        Assert.Equal("Line watch: pressing a game shows it in Line movement.", LineWatch.BoardHint);
        Assert.Equal(LineWatch.BoardHint, Hint(board)!);
        var summary = board.Find(".note .disclosure__summary");
        Assert.Equal("false", summary.GetAttribute("aria-expanded"));
        Assert.Empty(board.FindAll(".note .disclosure__content"));

        // The rest is one press away, on the summary — a <summary>, so Tab reaches it and Enter opens it.
        summary.Click();

        Assert.Equal("true", board.Find(".note .disclosure__summary").GetAttribute("aria-expanded"));
        Assert.Equal(LineWatch.BoardHintMore, board.Find(".note .disclosure__content").TextContent.Trim());
        Assert.Equal("Pressing a price shows that market and side. Place wager on the open row takes the price last pressed.",
            LineWatch.BoardHintMore);
    }

    [Fact]
    public void In_line_watch_a_row_press_shows_the_game_in_line_movement_and_opens_the_row()
    {
        Desk.ApplyWorkspace(LineWatchSpace);
        var board = Board();

        PressRow(board);

        Assert.Equal(BoardRows.GameId, Odds.Parameters["GameId"]);
        Assert.Equal(3, Desk.Windows.Count);
        // The row is open, its strip — and Place wager — a press away.
        Assert.NotEmpty(board.FindAll(".board__row--open"));
        Assert.NotEmpty(board.FindAll(".rowacts"));
    }

    [Fact]
    public void In_line_watch_a_price_press_shows_that_market_and_side_and_opens_no_take()
    {
        Desk.ApplyWorkspace(LineWatchSpace);
        var board = Board();

        PressChiefsMoneyline(board);

        Assert.Equal((BoardRows.GameId, Markets.Moneyline, BoardRows.Home),
            (Odds.Parameters["GameId"], Odds.Parameters["Market"], Odds.Parameters["Outcome"]));
        Assert.Single(Desk.Windows, w => w.Definition.Key == WindowCatalog.Odds);
        Assert.Empty(board.FindAll(".qtake"));
        Assert.NotEmpty(board.FindAll(".board__row--open"));
    }

    [Fact]
    public void Outside_line_watch_the_board_is_as_it_was()
    {
        // A Line movement window on the desk, but no workspace applied.
        Desk.Open(WindowCatalog.Find(WindowCatalog.Odds)!, new Dictionary<string, object> { ["GameId"] = 99 });
        var board = Board();

        Assert.Null(Hint(board));

        PressChiefsMoneyline(board);

        Assert.Equal(99, Odds.Parameters["GameId"]);
        Assert.NotEmpty(board.FindAll(".qtake"));
    }

    /// <summary>The Board's Line watch note as it reads closed (its disclosure's label), or null where it draws none.</summary>
    private static string? Hint(IRenderedFragment board)
        => board.FindAll(".note .disclosure__label").Select(n => n.TextContent.Trim())
            .FirstOrDefault(t => t.StartsWith("Line watch:", StringComparison.Ordinal));

    [Fact]
    public void With_line_movement_closed_the_board_is_the_plain_board_again()
    {
        Desk.ApplyWorkspace(LineWatchSpace);
        Desk.Close(Odds.Id);
        var board = Board();

        Assert.Null(Hint(board));

        PressChiefsMoneyline(board);

        Assert.NotEmpty(board.FindAll(".qtake"));
        Assert.DoesNotContain(Desk.Windows, w => w.Definition.Key == WindowCatalog.Odds);
    }

    /// <summary>The Bills' spread cell: the second market pair's away price, FanDuel -105 at +2.5.</summary>
    private static void PressBillsSpread(IRenderedFragment board) => board.FindAll("button.price--take")[3].Click();

    /// <summary>
    /// The pressed price is the one watched, and Place wager on the open row takes it — not the row's
    /// most value (the Chiefs' moneyline at DraftKings), which is where Place wager opens otherwise.
    /// </summary>
    [Fact]
    public void In_line_watch_place_wager_takes_the_price_last_pressed()
    {
        Desk.ApplyWorkspace(LineWatchSpace);
        var board = Board();

        PressBillsSpread(board);
        Assert.Empty(board.FindAll(".qtake"));

        board.FindAll(".rowacts button").First(b => b.TextContent.Trim() == WagerKey.PlaceLabel).Click();

        Assert.Equal("-105", board.Find(".qtake__price").TextContent.Trim());
        Assert.Contains("Bills", board.Find(".qtake__bet").TextContent);
        Assert.Equal("FanDuel", board.Find(".qtake__line .price-picker .popup__label").TextContent.Trim());
    }

    /// <summary>
    /// A take open on the row, then another price on the same row pressed: the press watches that
    /// price and closes the take, which would otherwise go on offering — and logging — the old one.
    /// </summary>
    [Fact]
    public void In_line_watch_a_price_press_on_the_rows_open_take_closes_it()
    {
        Desk.ApplyWorkspace(LineWatchSpace);
        var board = Board();

        PressChiefsMoneyline(board);
        board.FindAll(".rowacts button").First(b => b.TextContent.Trim() == WagerKey.PlaceLabel).Click();
        Assert.Equal("-128", board.Find(".qtake__price").TextContent.Trim());

        PressBillsSpread(board);

        Assert.Equal((BoardRows.GameId, Markets.Spread, BoardRows.Away),
            (Odds.Parameters["GameId"], Odds.Parameters["Market"], Odds.Parameters["Outcome"]));
        Assert.Empty(board.FindAll(".qtake"));
        Assert.NotEmpty(board.FindAll(".board__row--open"));

        // Place wager takes the price now watched, not the one the closed take held.
        board.FindAll(".rowacts button").First(b => b.TextContent.Trim() == WagerKey.PlaceLabel).Click();
        Assert.Equal("-105", board.Find(".qtake__price").TextContent.Trim());
    }

    [Fact]
    public void A_row_closed_and_opened_again_forgets_the_price_it_watched()
    {
        Desk.ApplyWorkspace(LineWatchSpace);
        var board = Board();

        PressBillsSpread(board);
        PressRow(board);
        PressRow(board);
        board.FindAll(".rowacts button").First(b => b.TextContent.Trim() == WagerKey.PlaceLabel).Click();

        // Back to the most value: the Chiefs' moneyline at DraftKings.
        Assert.Equal("-128", board.Find(".qtake__price").TextContent.Trim());
    }

    /// <summary>In one column with the Board, Line movement is aimed but left behind it: a press never hides the slate.</summary>
    [Fact]
    public void Sharing_the_boards_column_line_movement_is_aimed_and_the_board_stays_in_front()
    {
        Desk.ApplyWorkspace(LineWatchSpace with { Columns = [0, 0, 0] });
        var column = Assert.Single(Desk.Columns);
        var board = column.Showing;
        Assert.Equal(WindowCatalog.Board, board.Definition.Key);

        LineWatch.Show(Desk, 7, Markets.Total, "Over", "Bills at Chiefs", board.Id);

        Assert.True(Desk.IsShowing(board));
        Assert.False(Desk.IsShowing(Odds));
        Assert.Equal((7, Markets.Total, "Over"), (Odds.Parameters["GameId"], Odds.Parameters["Market"], Odds.Parameters["Outcome"]));
        Assert.Equal(board.Id, Desk.FocusedId);
    }

    [Fact]
    public void Every_press_is_a_new_aim_even_on_the_same_price()
    {
        Desk.ApplyWorkspace(LineWatchSpace);

        LineWatch.Show(Desk, 7, Markets.Spread, BoardRows.Away, "Bills at Chiefs", null);
        var first = Odds.Parameters["Press"];
        LineWatch.Show(Desk, 7, Markets.Spread, BoardRows.Away, "Bills at Chiefs", null);

        Assert.NotEqual(first, Odds.Parameters["Press"]);
    }

    // ---- Line movement, rendered against what a test hands it -------------------------

    private sealed class Moves : ILineMovementData
    {
        public List<OddsSnapshot> Snapshots { get; } = [];
        public List<string> MarketsRead { get; } = [];

        public Task<IReadOnlyList<Game>> GamesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Game>>([BoardRows.Game()]);

        public Task<Game?> GameAsync(int gameId, CancellationToken ct = default)
            => Task.FromResult<Game?>(BoardRows.Game());

        public Task<IReadOnlyList<OddsSnapshot>> SnapshotsAsync(int gameId, string market, CancellationToken ct = default)
        {
            MarketsRead.Add(market);
            return Task.FromResult<IReadOnlyList<OddsSnapshot>>(Snapshots.Where(s => s.Market == market).ToList());
        }

        public Task<bool> ScheduledAsync(CancellationToken ct = default) => Task.FromResult(false);

        public Task<MatchupForm?> FormAsync(int gameId, TimeSpan window, CancellationToken ct = default)
            => Task.FromResult<MatchupForm?>(null);

        public SlateOverview Slate { get; set; } = SlateOverview.Empty;

        public int SlateReads { get; private set; }

        public Task<SlateOverview> SlateAsync(CancellationToken ct = default)
        {
            SlateReads++;
            return Task.FromResult(Slate);
        }
    }

    private sealed class Jobs : XLines.Desk.IDeskJobs
    {
        public List<string> Ran { get; } = [];

        public Task<IReadOnlyList<XLines.Desk.DeskJob>> DescribeAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<XLines.Desk.DeskJob>>(
            [
                new(IngestionJobs.OddsLinesPrefix + "nfl", "Pull NFL lines", "Prices the NFL slate.", "2 credits", true, null,
                    Group: "Lines", Confirm: "Pull NFL lines for 2 credits? 498 are left this month.")
            ]);

        public Task<XLines.Desk.DeskJobOutcome> RunAsync(string key, CancellationToken ct = default)
        {
            Ran.Add(key);
            return Task.FromResult(new XLines.Desk.DeskJobOutcome(XLines.Desk.DeskJobResult.Done, "Pulled."));
        }
    }

    private static OddsSnapshot Snapshot(string market, string outcome, string book, int price, decimal? line = null, int minutesAgo = 30)
        => new()
        {
            GameId = BoardRows.GameId, Market = market, Outcome = outcome, Book = book, PriceAmerican = price, Line = line,
            CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo)
        };

    private (Moves Moves, Jobs Jobs) LineMovementFakes()
    {
        var moves = new Moves();
        moves.Snapshots.AddRange(
        [
            Snapshot(Markets.Moneyline, BoardRows.Home, "draftkings", -128),
            Snapshot(Markets.Moneyline, BoardRows.Away, "fanduel", 110),
            Snapshot(Markets.Total, "Over", "draftkings", -110, 47.5m),
            Snapshot(Markets.Total, "Under", "fanduel", -110, 47.5m)
        ]);

        var jobs = new Jobs();
        Services.AddSingleton<ILineMovementData>(moves);
        Services.AddSingleton<XLines.Desk.IDeskJobs>(jobs);
        Data.LinesJob = new IngestionJob(IngestionJobs.OddsLinesPrefix + "nfl", "Pull NFL lines", "Prices the NFL slate.", 2, true, null,
            EstimatedCredits: 2);
        return (moves, jobs);
    }

    private IRenderedComponent<OddsPanel> LineMovement(int press = 1)
    {
        var panel = RenderComponent<OddsPanel>(p => p
            .Add(x => x.GameId, BoardRows.GameId).Add(x => x.Market, Markets.Moneyline)
            .Add(x => x.Outcome, BoardRows.Home).Add(x => x.Press, press));
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".standing")), Settle);
        return panel;
    }

    private static string? Market(IRenderedFragment panel)
        => panel.FindAll("[role=radio][aria-checked=true]").Select(r => r.TextContent.Trim())
            .FirstOrDefault(t => t is "Moneyline" or "Spread" or "Total");

    [Fact]
    public void Line_movement_leads_with_where_the_side_stands_from_stored_data()
    {
        LineMovementFakes();
        var panel = LineMovement();

        var figures = Squash(panel.Find(".standing .metrics").TextContent);
        Assert.Contains("Best Chiefs", figures);
        Assert.Contains("DraftKings · +2.7% against fair", figures);
        Assert.Contains("-136", figures);

        // Nobody has moved: no movement columns, and no empty "Recent form" beside it.
        Assert.DoesNotContain("seen once", panel.Markup);
        Assert.DoesNotContain("Recent form", panel.Markup);
        Assert.Equal("Pull NFL lines · 2 credits", panel.Find(".standing__keys button").TextContent.Trim());
    }

    /// <summary>The pull is the job menu's: its confirm asks first, and nothing is spent until it is answered.</summary>
    [Fact]
    public void The_pull_runs_through_the_job_menu_and_asks_before_it_spends()
    {
        var (_, jobs) = LineMovementFakes();
        var panel = LineMovement();

        panel.Find(".standing__keys button").Click();

        panel.WaitForAssertion(() => Assert.Contains("Pull NFL lines for 2 credits?", panel.Find(".confirm__ask").TextContent), Settle);
        Assert.Empty(jobs.Ran);

        panel.FindAll(".confirm button").First(b => b.TextContent.Trim() == "Pull NFL lines").Click();

        panel.WaitForAssertion(() => Assert.Equal([IngestionJobs.OddsLinesPrefix + "nfl"], jobs.Ran), Settle);
    }

    [Fact]
    public void A_new_press_re_aims_line_movement_even_after_the_operator_looked_elsewhere_in_it()
    {
        var (moves, _) = LineMovementFakes();
        var panel = LineMovement(press: 1);
        Assert.Equal("Moneyline", Market(panel));

        // The operator turns to the total in the window itself.
        panel.FindAll("[role=radio]").First(r => r.TextContent.Trim() == "Total").Click();
        panel.WaitForAssertion(() => Assert.Equal("Total", Market(panel)), Settle);

        // The window redrawn with the same aim (focus, layout) leaves the operator's choice alone.
        panel.SetParametersAndRender(p => p.Add(x => x.Press, 1));
        Assert.Equal("Total", Market(panel));

        // The same price pressed again on the Board is a new press: back to the Chiefs' moneyline.
        panel.SetParametersAndRender(p => p.Add(x => x.Press, 2));
        panel.WaitForAssertion(() => Assert.Equal("Moneyline", Market(panel)), Settle);
        Assert.Contains("Best Chiefs", panel.Find(".standing .metrics").TextContent);
        Assert.Equal(Markets.Moneyline, moves.MarketsRead[^1]);
    }

    [Fact]
    public void A_press_naming_a_side_shows_that_side()
    {
        LineMovementFakes();
        var panel = LineMovement();

        panel.SetParametersAndRender(p => p.Add(x => x.Outcome, BoardRows.Away).Add(x => x.Press, 2));

        panel.WaitForAssertion(() => Assert.Contains("Best Bills", panel.Find(".standing .metrics").TextContent), Settle);
    }

    // ---- The take's Full wager form, typed -------------------------------------------

    [Fact]
    public void A_typed_take_opens_the_full_form_typed_as_it_was()
    {
        var door = TypedTake.Door(BoardRows.GameId, Markets.Spread, BoardRows.Away, "fanatics", 25m,
            new TypedTake("fanatics", 25m, -112m, 3m));

        Assert.Equal("-112", door["PriceAmerican"]);
        Assert.Equal("3", door["TakenLine"]);
        Assert.Equal("25", door["Stake"]);

        // The window as the desk hosts it: its parameters by name, as the door wrote them.
        var form = RenderComponent<BoardWager>(door.Select(d => ComponentParameter.CreateParameter(d.Key, d.Value)).ToArray());
        // Ready to log, so the price step is folded to what it carries; opened, the fields hold it.
        form.WaitForAssertion(() => Assert.Contains("your own price", Step(form, "Bet").TextContent), Settle);
        Change(form, "Bet");

        Assert.Equal("-112", form.Find("input[aria-label='Price']").GetAttribute("value"));
        Assert.Equal("3", form.Find("input[aria-label='Line']").GetAttribute("value"));
        Assert.Equal("fanatics", form.Find("input[aria-label='Book']").GetAttribute("value"));

        form.WaitForAssertion(() => Assert.False(form.Find("button.desk-btn--filled").HasAttribute("disabled")), Settle);
        form.Find("button.desk-btn--filled").Click();

        var logged = Assert.Single(Data.Straights);
        Assert.Equal((true, -112m, 3m, "fanatics", 25m), (logged.ManualPrice, logged.PriceTaken, logged.LineTaken, logged.Book, logged.Stake));
    }

    [Fact]
    public void The_overs_best_number_is_the_lowest_total()
    {
        var rows = OddsPanel.MovementRows(
        [
            new OddsSnapshot { Book = "fanduel", Market = Markets.Total, Outcome = "Over", PriceAmerican = -110, Line = 48m, CapturedAt = At },
            new OddsSnapshot { Book = "betmgm", Market = Markets.Total, Outcome = "Over", PriceAmerican = -110, Line = 47.5m, CapturedAt = At }
        ], "Over");

        Assert.Equal(["betmgm", "fanduel"], PriceStanding.Books(rows, null).Select(b => b.Book));

        var under = OddsPanel.MovementRows(
        [
            new OddsSnapshot { Book = "fanduel", Market = Markets.Total, Outcome = "Under", PriceAmerican = -110, Line = 48m, CapturedAt = At },
            new OddsSnapshot { Book = "betmgm", Market = Markets.Total, Outcome = "Under", PriceAmerican = -110, Line = 47.5m, CapturedAt = At }
        ], "Under");

        Assert.Equal(["fanduel", "betmgm"], PriceStanding.Books(under, null).Select(b => b.Book));
    }

    [Fact]
    public void Fair_on_a_handicap_says_the_line_it_is_fair_at()
    {
        var row = BoardRows.Row();
        var side = row.Spread.Second! with { Fair = new XLines.Core.Analytics.FairPrice(0.52, -108, XLines.Core.Analytics.FairValue.SharpBook, 1) };
        var rows = OddsPanel.MovementRows([new OddsSnapshot { Book = "fanduel", Market = Markets.Spread, Outcome = BoardRows.Away,
            PriceAmerican = -105, Line = 2.5m, CapturedAt = At }], BoardRows.Away);

        var cut = RenderComponent<PriceStanding>(p => p.Add(x => x.Rows, rows).Add(x => x.Side, side)
            .Add(x => x.SideName, "Bills").Add(x => x.Now, At));

        Assert.StartsWith("+2.5 -108", Squash(cut.FindAll(".metric")[0].TextContent));
    }

    private static string? Status(IRenderedFragment journal)
        => journal.FindAll("[role=radio][aria-checked=true]").Select(r => r.TextContent.Trim())
            .FirstOrDefault(t => t is "All" or "Pending" or "Settled" or "Won" or "Lost");

    [Fact]
    public void In_line_watch_the_journal_starts_on_pending_entries()
    {
        Services.AddSingleton(new JournalParlayTests.JournalOnHand());
        Desk.ApplyWorkspace(LineWatchSpace);
        var window = Assert.Single(Desk.Windows, w => w.Definition.Key == WindowCatalog.Journal);

        var journal = RenderComponent<JournalParlayTests.JournalUnderTest>(p => p.AddCascadingValue("WindowId", window.Id));

        journal.WaitForAssertion(() => Assert.Equal("Pending", Status(journal)), Settle);
    }

    [Fact]
    public void Outside_line_watch_the_journal_starts_on_every_entry()
    {
        Services.AddSingleton(new JournalParlayTests.JournalOnHand());
        var window = Desk.Open(WindowCatalog.Find(WindowCatalog.Journal)!);

        var journal = RenderComponent<JournalParlayTests.JournalUnderTest>(p => p.AddCascadingValue("WindowId", window.Id));

        journal.WaitForAssertion(() => Assert.Equal("All", Status(journal)), Settle);
    }

    [Fact]
    public void Line_watch_is_known_by_its_tag_not_its_name()
    {
        Desk.ApplyWorkspace(LineWatchSpace with { Name = "Prices", Description = "Renamed." });
        Assert.True(LineWatch.IsActive(Desk));

        Desk.ApplyWorkspace(WindowCatalog.Workspaces[1]);
        Assert.False(LineWatch.IsActive(Desk));
    }

    // ---- Where the line stands (Line movement, no moves yet) ----------------------------

    private static readonly DateTimeOffset At = new(2026, 11, 20, 18, 0, 0, TimeSpan.Zero);

    private static OddsSnapshot Snap(string book, int price, TimeSpan ago, string outcome = BoardRows.Home)
        => new() { Book = book, Market = Markets.Moneyline, Outcome = outcome, PriceAmerican = price, CapturedAt = At - ago };

    /// <summary>
    /// The standard board row's Chiefs moneyline, every book seen once: DraftKings -128 two hours
    /// ago, worth +2.7% against a Pinnacle fair -136; FanDuel -135 forty minutes ago.
    /// </summary>
    private IRenderedComponent<PriceStanding> Standing(bool closing = false, bool scheduled = false, string? pull = "Pull NFL lines · 2 credits",
        Action? onPull = null)
    {
        var row = BoardRows.Row();
        var side = row.Moneyline.First! with
        {
            Rungs =
            [
                BoardRows.Rung("draftkings", -128, ev: 0.027, capturedAt: At.AddHours(-2)),
                BoardRows.Rung("fanduel", -135, ev: -0.01, capturedAt: At.AddMinutes(-40))
            ]
        };

        var rows = OddsPanel.MovementRows([Snap("draftkings", -128, TimeSpan.FromHours(2)), Snap("fanduel", -135, TimeSpan.FromMinutes(40))],
            BoardRows.Home);

        return RenderComponent<PriceStanding>(p => p
            .Add(x => x.Rows, rows)
            .Add(x => x.Side, side)
            .Add(x => x.Other, row.Moneyline.Second)
            .Add(x => x.SideName, "Chiefs")
            .Add(x => x.OtherName, "Bills")
            .Add(x => x.ShowBooks, true)
            .Add(x => x.Closing, closing)
            .Add(x => x.Scheduled, scheduled)
            .Add(x => x.PullLabel, pull)
            .Add(x => x.OnPull, onPull ?? (() => { }))
            .Add(x => x.Now, At));
    }

    private static string Squash(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void A_side_nobody_has_moved_leads_with_fair_and_the_best_price_on_each_side()
    {
        var cut = Standing();
        var figures = Squash(cut.Find(".metrics").TextContent);

        Assert.Contains("-136", figures);
        Assert.Contains("Chiefs (Pinnacle, no-vig)", figures);
        Assert.Contains("Best Chiefs", figures);
        Assert.Contains("DraftKings · +2.7% against fair", figures);
        Assert.Contains("Best Bills", figures);
        Assert.Contains("+110", figures);
        Assert.Contains("Last change", figures);
        Assert.Contains("40m ago", figures);
    }

    [Fact]
    public void Each_book_says_its_distance_from_fair_and_how_long_it_has_held_its_number()
    {
        var cut = Standing();

        var rows = cut.FindAll("tbody tr.mud-table-row").Select(r => Squash(r.TextContent)).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("DraftKings Best -128 +2.7% 2h 0m", rows[0]);
        Assert.Equal("FanDuel -135 −1.0% 40 min", rows[1]);

        // Nothing here is a move: the movement columns are not drawn.
        Assert.DoesNotContain("unmoved", cut.Markup);
        Assert.DoesNotContain("seen once", cut.Markup);
    }

    [Fact]
    public void A_book_whose_number_has_no_fair_price_says_so_rather_than_an_edge()
    {
        var row = BoardRows.Row();
        var rows = OddsPanel.MovementRows([Snap("betmgm", -130, TimeSpan.FromHours(1))], BoardRows.Home);

        var books = PriceStanding.Books(rows, row.Moneyline.First);

        Assert.Null(Assert.Single(books).Ev);
    }

    [Fact]
    public void With_polling_manual_it_says_prices_move_only_on_a_pull_and_offers_it_with_its_cost()
    {
        var pulled = 0;
        var cut = Standing(onPull: () => pulled++);

        Assert.StartsWith("Line polling is Manual, so these numbers change only when lines are pulled",
            Squash(cut.Find(".standing__next").TextContent));

        var key = cut.Find(".standing__keys button");
        Assert.Equal("Pull NFL lines · 2 credits", key.TextContent.Trim());
        Assert.DoesNotContain("desk-btn--filled", key.ClassList);

        key.Click();
        Assert.Equal(1, pulled);
        Assert.Empty(Data.JobsRun);
    }

    [Fact]
    public void A_closing_line_says_it_will_not_move_and_offers_no_pull()
    {
        var cut = Standing(closing: true);

        Assert.StartsWith("These are closing lines", Squash(cut.Find(".standing__next").TextContent));
        Assert.Empty(cut.FindAll(".standing__keys"));
    }

    [Fact]
    public void A_reader_who_cannot_pull_reads_the_sentence_without_the_key()
    {
        var cut = Standing(pull: null);

        Assert.NotEmpty(cut.FindAll(".standing__next"));
        Assert.Empty(cut.FindAll(".standing__keys"));
    }

    [Fact]
    public void With_polling_scheduled_it_says_so()
        => Assert.StartsWith("Lines are scanned on a schedule",
            Squash(Standing(scheduled: true).Find(".standing__next").TextContent));

    // ---- Re-aimed faster than it reads ------------------------------------------------

    /// <summary>Two games each with a moneyline on file, whose snapshot reads can be held open (<see cref="FakeLineMovementData"/>).</summary>
    private FakeLineMovementData AimFakes()
    {
        var aims = new FakeLineMovementData();
        Services.AddSingleton<ILineMovementData>(aims);
        Services.AddSingleton<XLines.Desk.IDeskJobs>(new Jobs());
        return aims;
    }

    /// <summary>The side switch's labels: the clubs the window's rows are about.</summary>
    private static List<string> SideSwitch(IRenderedFragment panel)
        => panel.FindAll("[role=radio]").Select(r => r.TextContent.Trim())
            .Where(t => t is not ("Moneyline" or "Spread" or "Total")).ToList();

    [Fact]
    public void A_read_overtaken_by_a_newer_aim_hands_over_nothing_when_it_lands()
    {
        var aims = AimFakes();
        aims.SnapshotGates[BoardRows.GameId] = new TaskCompletionSource();

        var panel = RenderComponent<OddsPanel>(p => p.Add(x => x.GameId, BoardRows.GameId).Add(x => x.Press, 1));

        // Pressed on to the Dolphins before the Chiefs' read came back: the Dolphins are shown.
        panel.SetParametersAndRender(p => p.Add(x => x.GameId, 8).Add(x => x.Press, 2));
        panel.WaitForAssertion(() => Assert.Contains("Dolphins", SideSwitch(panel)), Settle);

        // The Chiefs' read was asked to stop; it lands anyway, last, and changes nothing.
        var (game, ct) = aims.SnapshotReads[0];
        Assert.Equal(BoardRows.GameId, game);
        Assert.True(ct.IsCancellationRequested);

        aims.SnapshotGates[BoardRows.GameId].SetResult();
        panel.Render();

        Assert.Contains("Dolphins", SideSwitch(panel));
        Assert.DoesNotContain("Chiefs", SideSwitch(panel));
    }

    [Fact]
    public void Opened_without_an_aim_it_shows_the_next_game_to_start_not_the_oldest()
    {
        var aims = AimFakes();
        aims.Games.Clear();
        aims.Games.AddRange(
        [
            BoardRows.Game(DateTimeOffset.UtcNow.AddDays(-200)),
            FakeLineMovementData.Dolphins(DateTimeOffset.UtcNow.AddHours(2)),
            new Game { Id = 9, HomeTeam = new Team { Name = "Dallas Cowboys" }, AwayTeam = new Team { Name = "New York Giants" },
                Sport = new Sport { Key = "nfl", Name = "NFL" }, StartsAt = DateTimeOffset.UtcNow.AddDays(1) }
        ]);

        var panel = RenderComponent<OddsPanel>();

        panel.WaitForAssertion(() => Assert.Contains("Dolphins", SideSwitch(panel)), Settle);
        Assert.Equal(8, aims.SnapshotReads[^1].Game);
    }

    [Fact]
    public void With_nothing_ahead_it_opens_on_the_game_played_last()
    {
        var now = DateTimeOffset.UtcNow;
        var games = new List<Game>
        {
            BoardRows.Game(now.AddDays(-200)),
            FakeLineMovementData.Dolphins(now.AddDays(-1)),
            new() { Id = 9, StartsAt = now.AddDays(-30) }
        };

        Assert.Equal(8, OddsPanel.NextUp(games, now)?.Id);
        Assert.Null(OddsPanel.NextUp([], now));
    }

    [Fact]
    public void The_picker_keeps_the_games_nearest_now_within_its_cap()
    {
        var now = DateTimeOffset.UtcNow;
        Game At(int id, double days) => new() { Id = id, StartsAt = now.AddDays(days) };

        var listed = LineMovementData.Nearest([At(1, -300), At(2, -1), At(3, 2), At(4, -10), At(5, 0.5), At(6, -200)], now, 4);

        // Every game ahead, soonest first, then the latest played: last season's are the ones cut.
        Assert.Equal([5, 3, 2, 4], listed.Select(g => g.Id));
    }

    [Fact]
    public void A_game_aimed_at_outside_the_list_is_still_the_one_shown()
    {
        var aims = AimFakes();
        aims.Unlisted.Add(8);   // past the picker's cap: the list leaves it out, the game read finds it

        var panel = RenderComponent<OddsPanel>(p => p.Add(x => x.GameId, 8));

        panel.WaitForAssertion(() => Assert.Contains("Dolphins", SideSwitch(panel)), Settle);
        Assert.Contains(panel.FindAll("select[aria-label=Game] option"), o => o.GetAttribute("value") == "8");
    }

    // ---- Line watch: nothing chosen until something is pressed --------------------------

    private SlateMove ChiefsSpreadMove()
    {
        var game = BoardRows.Game();
        var rows = OddsPanel.MovementRows(
        [
            Snapshot(Markets.Spread, BoardRows.Away, "fanduel", -110, 3m, minutesAgo: 300),
            Snapshot(Markets.Spread, BoardRows.Away, "fanduel", -105, 2.5m, minutesAgo: 20)
        ], BoardRows.Away);
        return new SlateMove(game, Markets.Spread, Assert.Single(rows));
    }

    [Fact]
    public void In_line_watch_line_movement_chooses_nothing_and_shows_the_slates_biggest_moves()
    {
        var (moves, _) = LineMovementFakes();
        moves.Slate = new SlateOverview([ChiefsSpreadMove()], []);
        Desk.ApplyWorkspace(LineWatchSpace);

        var panel = RenderComponent<OddsPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".slatewatch")), Settle);

        Assert.Equal("Press a game or a price on the Board to follow it here.", panel.Find(".slatewatch__prompt").TextContent.Trim());
        Assert.Equal("Biggest moves today", panel.Find(".slatewatch__title").TextContent.Trim());
        Assert.Contains("+3 -110 → +2.5 -105", Squash(panel.Find(".slatewatch__row").TextContent));

        // No game was read as if it were chosen, and none is named as followed.
        Assert.Empty(moves.MarketsRead);
        Assert.Empty(panel.FindAll(".standing"));
        Assert.Empty(panel.FindAll(".slatewatch__following"));
    }

    [Fact]
    public void A_move_pressed_in_the_overview_is_followed_and_the_overview_is_one_key_back()
    {
        var (moves, _) = LineMovementFakes();
        moves.Snapshots.Add(Snapshot(Markets.Spread, BoardRows.Away, "fanduel", -105, 2.5m));
        moves.Slate = new SlateOverview([ChiefsSpreadMove()], []);
        Desk.ApplyWorkspace(LineWatchSpace);
        var panel = RenderComponent<OddsPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".slatewatch__row")), Settle);

        panel.Find(".slatewatch__row").Click();

        panel.WaitForAssertion(() => Assert.Equal("Following Buffalo Bills at Kansas City Chiefs · Spread · Bills",
            Squash(panel.Find(".slatewatch__following-text").TextContent)), Settle);
        Assert.Empty(panel.FindAll(".slatewatch"));

        panel.FindAll(".slatewatch__following button").Single(b => b.TextContent.Trim() == "Overview").Click();

        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".slatewatch")), Settle);
        Assert.Empty(panel.FindAll(".slatewatch__following"));
    }

    /// <summary>A desk saved in an earlier run comes back with its old aim; that is not today's choice.</summary>
    [Fact]
    public void A_selection_restored_from_an_earlier_run_does_not_masquerade_as_the_choice()
    {
        var (moves, _) = LineMovementFakes();
        Desk.ApplyWorkspace(LineWatchSpace);

        var panel = RenderComponent<OddsPanel>(p => p
            .Add(x => x.GameId, BoardRows.GameId).Add(x => x.Market, Markets.Moneyline)
            .Add(x => x.Outcome, BoardRows.Home).Add(x => x.Press, "a-token-this-server-never-issued"));

        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".slatewatch")), Settle);
        Assert.Empty(moves.MarketsRead);
    }

    [Fact]
    public void A_press_made_now_is_followed_and_named()
    {
        LineMovementFakes();
        Desk.ApplyWorkspace(LineWatchSpace);
        LineWatch.Show(Desk, BoardRows.GameId, Markets.Moneyline, BoardRows.Home, "Bills at Chiefs", null);

        var panel = RenderComponent<OddsPanel>(Odds.Parameters.Where(p => p.Value is not null)
            .Select(p => ComponentParameter.CreateParameter(p.Key, p.Value)).ToArray());

        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".standing")), Settle);
        Assert.Equal("Following Buffalo Bills at Kansas City Chiefs · Moneyline · Chiefs",
            Squash(panel.Find(".slatewatch__following-text").TextContent));
        Assert.Empty(panel.FindAll(".slatewatch"));
    }

    [Fact]
    public void With_nothing_moved_the_overview_says_so_and_lists_the_next_games()
    {
        var (moves, _) = LineMovementFakes();
        var next = BoardRows.Game();
        moves.Slate = new SlateOverview([], [next]);
        Desk.ApplyWorkspace(LineWatchSpace);

        var panel = RenderComponent<OddsPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".slatewatch")), Settle);

        Assert.Equal("Nothing on the slate has moved today.", panel.Find(".slatewatch__none").TextContent.Trim());
        Assert.Equal("Next games", panel.Find(".slatewatch__title").TextContent.Trim());
        Assert.Contains("Buffalo Bills at Kansas City Chiefs", panel.Find(".slatewatch__row").TextContent);
    }

    /// <summary>Outside Line watch the standalone window keeps its default: the next game to start.</summary>
    [Fact]
    public void Outside_line_watch_line_movement_opens_on_the_next_game_as_before()
    {
        LineMovementFakes();

        var panel = RenderComponent<OddsPanel>();

        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".standing")), Settle);
        Assert.Empty(panel.FindAll(".slatewatch, .slatewatch__following"));
    }

    [Fact]
    public void The_biggest_moves_rank_a_line_move_over_any_price_move_and_keep_only_todays()
    {
        var since = DateTimeOffset.UtcNow.AddHours(-6);
        var games = new[] { BoardRows.Game() };
        var snapshots = new[]
        {
            // A ten-cent move on the moneyline, today.
            Snapshot(Markets.Moneyline, BoardRows.Home, "draftkings", -120, null, minutesAgo: 600),
            Snapshot(Markets.Moneyline, BoardRows.Home, "draftkings", -130, null, minutesAgo: 30),
            // Half a point on the spread, today.
            Snapshot(Markets.Spread, BoardRows.Away, "fanduel", -110, 3m, minutesAgo: 600),
            Snapshot(Markets.Spread, BoardRows.Away, "fanduel", -110, 2.5m, minutesAgo: 60),
            // A move that landed yesterday, and a book that never moved.
            Snapshot(Markets.Total, "Over", "betmgm", -110, 47m, minutesAgo: 3000),
            Snapshot(Markets.Total, "Over", "betmgm", -110, 48m, minutesAgo: 2000),
            Snapshot(Markets.Total, "Under", "fanduel", -110, 47.5m, minutesAgo: 30)
        };

        var biggest = SlateMoves.Biggest(snapshots, games, since);

        Assert.Equal([Markets.Spread, Markets.Moneyline], biggest.Select(m => m.Market));
    }

    // ---- Second review ------------------------------------------------------------------

    /// <summary>A book that moves a total moves both of its sides: the overview says it once.</summary>
    [Fact]
    public void A_books_move_is_said_once_not_once_a_side()
    {
        var since = DateTimeOffset.UtcNow.AddHours(-6);
        var snapshots = new[]
        {
            Snapshot(Markets.Total, "Over", "draftkings", -110, 47.5m, minutesAgo: 600),
            Snapshot(Markets.Total, "Over", "draftkings", -110, 48.5m, minutesAgo: 30),
            Snapshot(Markets.Total, "Under", "draftkings", -110, 47.5m, minutesAgo: 600),
            Snapshot(Markets.Total, "Under", "draftkings", -110, 48.5m, minutesAgo: 30),
            Snapshot(Markets.Spread, BoardRows.Home, "fanduel", -110, -3m, minutesAgo: 600),
            Snapshot(Markets.Spread, BoardRows.Home, "fanduel", -110, -2.5m, minutesAgo: 30),
            Snapshot(Markets.Spread, BoardRows.Away, "fanduel", -110, 3m, minutesAgo: 600),
            Snapshot(Markets.Spread, BoardRows.Away, "fanduel", -110, 2.5m, minutesAgo: 30)
        };

        var biggest = SlateMoves.Biggest(snapshots, [BoardRows.Game()], since);

        Assert.Equal(2, biggest.Count);
        Assert.Equal("Over", Assert.Single(biggest, m => m.Market == Markets.Total).Row.Now.Outcome);
        Assert.Equal(BoardRows.Away, Assert.Single(biggest, m => m.Market == Markets.Spread).Row.Now.Outcome);
    }

    /// <summary>
    /// Closing the workspace's Line movement window ends the watch. One opened later from the strip
    /// is a window of its own: not driven by the Board, and it opens on the next game, not the overview.
    /// </summary>
    [Fact]
    public void A_line_movement_window_opened_after_the_watch_ended_is_standalone()
    {
        LineMovementFakes();
        Desk.ApplyWorkspace(LineWatchSpace);
        Assert.True(LineWatch.IsActive(Desk));   // the Board, drawn, has asked
        Desk.Close(Odds.Id);

        var plain = Desk.Open(WindowCatalog.Find(WindowCatalog.Odds)!);

        Assert.False(LineWatch.IsActive(Desk));
        Assert.False(LineWatch.Drives(Desk, plain.Id));
        Assert.Null(LineWatch.Show(Desk, 7, null, null, "Bills at Chiefs", null));

        var panel = RenderComponent<OddsPanel>(p => p.AddCascadingValue("WindowId", plain.Id));
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".standing")), Settle);
        Assert.Empty(panel.FindAll(".slatewatch, .slatewatch__following"));

        // Applying Line watch again is a new watch.
        Desk.ApplyWorkspace(LineWatchSpace);
        Assert.True(LineWatch.IsActive(Desk));
    }

    /// <summary>A second Line movement window beside the watch's is standalone; the watch drives its own.</summary>
    [Fact]
    public void Only_the_workspaces_line_movement_window_is_driven()
    {
        Desk.ApplyWorkspace(LineWatchSpace);
        var watched = Odds;
        Assert.True(LineWatch.IsActive(Desk));

        // A second copy of the same window is not opened; one on a game of its own is.
        Assert.Same(watched, Desk.Open(WindowCatalog.Find(WindowCatalog.Odds)!));
        var second = Desk.Open(WindowCatalog.Find(WindowCatalog.Odds)!, new Dictionary<string, object> { ["GameId"] = 3 });
        Assert.NotSame(watched, second);

        Assert.True(LineWatch.Drives(Desk, watched.Id));
        Assert.False(LineWatch.Drives(Desk, second.Id));
        Assert.Same(watched, LineWatch.Show(Desk, 7, null, null, "Bills at Chiefs", null));
        Assert.Equal(3, second.Parameters["GameId"]);
    }

    [Fact]
    public void A_token_is_current_only_once_issued_today()
    {
        Assert.True(LineWatch.IsCurrent(LineWatch.Issue()));
        Assert.False(LineWatch.IsCurrent("0123456789abcdef0123456789abcdef"));
        Assert.False(LineWatch.IsCurrent(null));
    }


    /// <summary>The title bar says what is followed, and nothing once the overview is back.</summary>
    [Fact]
    public void The_window_title_and_its_saved_aim_follow_the_overview_and_what_is_followed()
    {
        var (moves, _) = LineMovementFakes();
        moves.Snapshots.Add(Snapshot(Markets.Spread, BoardRows.Away, "fanduel", -105, 2.5m));
        moves.Slate = new SlateOverview([ChiefsSpreadMove()], []);
        Desk.ApplyWorkspace(LineWatchSpace);
        LineWatch.Show(Desk, BoardRows.GameId, Markets.Moneyline, BoardRows.Home, "Bills at Chiefs", null);
        var odds = Odds;

        var panel = RenderComponent<OddsPanel>(Parameters(odds).Append(ComponentParameter.CreateCascadingValue("WindowId", odds.Id)).ToArray());
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".slatewatch__following")), Settle);

        panel.FindAll(".slatewatch__following button").Single(b => b.TextContent.Trim() == "Overview").Click();

        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".slatewatch__row")), Settle);
        Assert.Null(odds.TitleOverride);
        Assert.Null(odds.Parameters.GetValueOrDefault("GameId"));

        panel.Find(".slatewatch__row").Click();

        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".slatewatch__following")), Settle);
        Assert.Equal("Buffalo Bills at Kansas City Chiefs", odds.TitleOverride);
        Assert.Equal((BoardRows.GameId, Markets.Spread, BoardRows.Away),
            (odds.Parameters["GameId"], odds.Parameters["Market"], odds.Parameters["Outcome"]));
        Assert.True(LineWatch.IsCurrent(odds.Parameters["Press"]));
    }

    [Fact]
    public void A_desk_restored_from_an_earlier_day_drops_its_old_title_with_its_old_aim()
    {
        LineMovementFakes();
        Desk.ApplyWorkspace(LineWatchSpace);
        var odds = Odds;
        Assert.True(LineWatch.IsActive(Desk));
        WindowManager.Aim(odds.Parameters, new Dictionary<string, object>
        {
            ["GameId"] = BoardRows.GameId, ["Market"] = Markets.Moneyline, ["Press"] = "0123456789abcdef0123456789abcdef"
        });
        odds.TitleOverride = "Bills at Chiefs";

        var panel = RenderComponent<OddsPanel>(Parameters(odds).Append(ComponentParameter.CreateCascadingValue("WindowId", odds.Id)).ToArray());

        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".slatewatch")), Settle);
        Assert.Null(odds.TitleOverride);
        Assert.Null(odds.Parameters.GetValueOrDefault("GameId"));
    }

    private static IEnumerable<ComponentParameter> Parameters(WindowInstance window)
        => window.Parameters.Where(p => p.Value is not null).Select(p => ComponentParameter.CreateParameter(p.Key, p.Value));
}
