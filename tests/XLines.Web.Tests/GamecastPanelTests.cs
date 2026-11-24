using Bunit;
using XLines.Core.Entities;
using XLines.Data.CrossReference;
using XLines.Desk.Primitives;
using XLines.Desk.Windowing;
using XLines.Ingestion.Services;
using XLines.Web.Components.Panels;
using XLines.Web.Components.Snippets;
using XLines.Web.Services;
using XLines.Web.Tests.Support;
using XLines.Web.Windowing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace XLines.Web.Tests;

/// <summary>
/// The Gamecast window (spec 2026-10-24 §6.1, §6.4): it appends the plays after the last one it
/// holds rather than reloading, matches plays by id, never draws a play twice however its loads
/// overlap, hears only its own game, opens the drive in progress and folds the rest to their
/// result lines, draws the field from the home goal line, and is reached from a live NFL game's
/// window and the catalogue.
/// </summary>
public class GamecastPanelTests : DeskTestContext
{
    /// <summary>
    /// The window with no settle to wait out, and a clock the test can set: a notice is heard at
    /// once, and the box score's throttle can be stepped past without waiting half a minute.
    /// </summary>
    public sealed class Bench : GamecastPanel
    {
        public static DateTimeOffset? Clock { get; set; }

        protected override TimeSpan RefreshSettle => TimeSpan.Zero;

        protected override DateTimeOffset Now => Clock ?? DateTimeOffset.UtcNow;
    }

    private readonly FakeGamecastData _data = new();
    private readonly FakeGameWindowData _window = new();
    private readonly BunitJSModuleInterop _clock;

    public GamecastPanelTests()
    {
        Bench.Clock = null;
        Services.AddSingleton<IGamecastData>(_data);
        Services.AddSingleton<IGameWindowData>(_window);

        // The window hands the clock to the browser only once it is interactive; this bench is.
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));
        _clock = JSInterop.SetupModule("./js/gameclock.js");
        _clock.Mode = JSRuntimeMode.Loose;
    }

    private static string Id => BoardRows.GameId.ToString();

    private IRenderedComponent<Bench> Render(string? windowId = null)
    {
        var cut = RenderComponent<Bench>(p =>
        {
            p.Add(g => g.GameId, BoardRows.GameId);
            if (windowId is not null)
                p.AddCascadingValue("WindowId", windowId);
        });
        cut.WaitForAssertion(() => Assert.True(cut.FindAll(".ptitle").Count > 0 || cut.Markup.Contains("did not load")));
        return cut;
    }

    private void Publish(params string[] tokens)
        => Services.GetRequiredService<DeskSignals>().Publish(tokens.ToHashSet());

    private static string?[] Seqs(IRenderedFragment cut, string rows = ".gc__play")
        => cut.FindAll(rows).Select(r => r.GetAttribute("data-seq")).ToArray();

    private static string?[] Ids(IRenderedFragment cut)
        => cut.FindAll(".gc__play").Select(r => r.GetAttribute("data-play")).ToArray();

    /// <summary>Presses one of the window's sections, by its label.</summary>
    private static void Show(IRenderedFragment cut, string section)
        => cut.FindAll("[role='tab']").Single(t => t.TextContent.Trim() == section).Click();

    /// <summary>Drive 1 is done (PHI touchdown); drive 2 is on the field with plays 3 and 4.</summary>
    private void TwoDrives()
    {
        _data.Plays.AddRange([
            FakeGamecastData.Play(1, 1, "J.Hurts run for 4 yards"),
            FakeGamecastData.Play(2, 1, "J.Hurts pass to D.Smith for a touchdown", scoring: true),
            FakeGamecastData.Play(3, 2, "S.Barkley run for 3 yards"),
            FakeGamecastData.Play(4, 2, "J.Hurts pass incomplete")
        ]);
        _data.Drives.AddRange([
            FakeGamecastData.Drive(1, 1, 2, current: false, result: "TD", plays: 7, yards: 62, elapsed: "3:48", score: true),
            FakeGamecastData.Drive(2, 3, 4, current: true, plays: 2, yards: 3)
        ]);
    }

    /// <summary>Four plays and no drives: the log is one flat list of every play held.</summary>
    private void FourPlays()
    {
        for (var s = 1; s <= 4; s++)
            _data.Plays.Add(FakeGamecastData.Play(s, null, $"Play {s}"));
    }

    // ---- Append, not reload ----------------------------------------------------------------

    [Fact]
    public void A_plays_notice_for_this_game_appends_the_plays_after_the_last_one_held()
    {
        TwoDrives();
        var cut = Render();
        Assert.Equal(2, cut.FindAll("[data-drive='2'] ~ .gc__plays .gc__play").Count);
        var headers = _data.HeaderReads;

        _data.Plays.Add(FakeGamecastData.Play(5, 2, "S.Barkley run for 11 yards"));
        _data.Drives[1] = FakeGamecastData.Drive(2, 3, 5, current: true, plays: 3, yards: 14);
        Signal("plays", Id);

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".gc__plays .gc__play").Count), TimeSpan.FromSeconds(5));

        // The opening read asked for every play; the notice asked for what came after play 4,
        // and then the drive in progress again (from its first play) for corrections. Nothing
        // read the whole game again, and the header waits for its own notice.
        Assert.Null(_data.PlaysReadsAfter[0]);
        Assert.Equal(4, _data.PlaysReadsAfter[1]);
        Assert.Equal(2, _data.PlaysReadsAfter[2]);
        Assert.Single(_data.PlaysReadsAfter, after => after is null);
        Assert.Equal(headers, _data.HeaderReads);

        // Newest first, and the rows already drawn are the same rows, keyed by the play.
        Assert.Equal(new string?[] { "5", "4", "3" }, Seqs(cut, ".gc__plays .gc__play"));
    }

    [Fact]
    public void Each_play_row_is_keyed_by_its_play_so_an_append_inserts_rather_than_rewrites()
    {
        var source = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Web", "Components", "Snippets", "PlayLog.razor"));

        Assert.Contains("<li class=\"gc__play @(play.IsScoring ? \"gc__play--score\" : null)\" @key=\"play.Id\"", source);
    }

    [Fact]
    public void Plays_stored_at_sequence_zero_are_drawn_when_the_window_opens()
    {
        _data.Plays.Add(FakeGamecastData.Play(0, null, "Kickoff, no sequence sent"));
        _data.Plays.Add(FakeGamecastData.Play(1, null, "J.Hurts run for 4 yards"));

        var cut = Render();

        Assert.Equal(new string?[] { "1", "0" }, Seqs(cut));
    }

    [Fact]
    public void A_play_rewritten_in_place_replaces_its_row_on_the_next_notice()
    {
        TwoDrives();
        var cut = Render();

        // ESPN keeps the sequence across a review; only the text changes.
        _data.Plays[3] = FakeGamecastData.Play(4, 2, "J.Hurts pass short left to A.Brown for 12 yards (reversed on review)");
        Signal("plays", Id);

        cut.WaitForAssertion(() => Assert.Contains("reversed on review", cut.Find("[data-seq='4']").TextContent), TimeSpan.FromSeconds(5));
        Assert.Equal(2, cut.FindAll(".gc__plays .gc__play").Count);
    }

    /// <summary>
    /// GamecastWriter updates a stored play's sequence when ESPN renumbers it. The window matches
    /// by id: the play moves to its new place and is drawn once, never once per sequence.
    /// </summary>
    [Fact]
    public void A_play_whose_sequence_changes_is_matched_by_its_id_and_drawn_once()
    {
        FourPlays();
        var cut = Render();

        // Play 4 (id 1004) is renumbered to 6, and a new play 5 lands before it.
        _data.Plays[3] = FakeGamecastData.Play(6, null, "Play 4, renumbered", id: 1004);
        _data.Plays.Add(FakeGamecastData.Play(5, null, "Play 5"));
        Signal("plays", Id);

        cut.WaitForAssertion(() => Assert.Contains("renumbered", cut.Markup), TimeSpan.FromSeconds(5));
        Assert.Equal(new string?[] { "6", "5", "3", "2", "1" }, Seqs(cut));
        Assert.Equal(Ids(cut).Distinct().Count(), Ids(cut).Length);
    }

    /// <summary>
    /// The correction read takes the drive in progress again from its first play. A play in it
    /// the window did not hold — inserted late, at or below the last sequence held — is added
    /// in its place rather than skipped because its sequence is not new.
    /// </summary>
    [Fact]
    public void A_correction_read_adds_a_play_at_or_below_the_last_sequence_it_did_not_hold()
    {
        TwoDrives();
        var cut = Render();

        _data.Plays.Add(FakeGamecastData.Play(3, 2, "Timeout PHI, inserted late", id: 2000));
        Signal("plays", Id);

        cut.WaitForAssertion(() => Assert.Contains("inserted late", cut.Markup), TimeSpan.FromSeconds(5));
        Assert.Equal(new string?[] { "4", "3", "3" }, Seqs(cut, ".gc__plays .gc__play"));
    }

    [Fact]
    public void A_notice_for_another_game_does_nothing()
    {
        TwoDrives();
        var cut = Render();
        var (plays, drives, headers) = (_data.PlaysReadsAfter.Count, _data.DriveReads, _data.HeaderReads);

        Signal("plays", "4123");
        Signal("games", "4123");

        // A notice for this game afterwards is heard: once it has been read, the other game's
        // would have been too, had the window heard them.
        _data.Header = FakeGamecastData.Live() with { AwayScore = 14 };
        Signal("games", Id);
        cut.WaitForAssertion(() => Assert.Contains("14", cut.Find(".ptitle").TextContent), TimeSpan.FromSeconds(5));

        Assert.Equal((plays, drives, headers + 1), (_data.PlaysReadsAfter.Count, _data.DriveReads, _data.HeaderReads));
    }

    [Fact]
    public void A_games_notice_for_this_game_reads_the_header_alone()
    {
        TwoDrives();
        var cut = Render();
        var (plays, headers) = (_data.PlaysReadsAfter.Count, _data.HeaderReads);

        _data.Header = FakeGamecastData.Live() with { AwayScore = 14 };
        Signal("games", Id);

        cut.WaitForAssertion(() => Assert.Contains("14", cut.Find(".ptitle").TextContent), TimeSpan.FromSeconds(5));
        Assert.Equal(headers + 1, _data.HeaderReads);
        Assert.Equal(plays, _data.PlaysReadsAfter.Count);
    }

    // ---- Overlapping loads -----------------------------------------------------------------

    /// <summary>
    /// Try again pressed while a refresh's load is in flight: both loads are past their first
    /// await before either has its plays. Only the later applies, and every play is drawn once —
    /// a duplicate row key would end the circuit.
    /// </summary>
    [Fact]
    public async Task Try_again_during_a_refresh_draws_each_play_once()
    {
        FourPlays();
        _data.HeaderError = new InvalidOperationException("database restarting");
        var cut = Render();
        Assert.Contains("did not load", cut.Markup);

        _data.HeaderError = null;
        _data.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Signal("games", Id);
        cut.WaitForState(() => _data.Waiting == 1, TimeSpan.FromSeconds(5));

        var retry = cut.FindAll("button").First(b => b.TextContent.Contains("Try again"));
        var click = retry.ClickAsync(new MouseEventArgs());
        cut.WaitForState(() => _data.Waiting == 2, TimeSpan.FromSeconds(5));

        _data.Gate.SetResult();
        await click;

        cut.WaitForAssertion(() => Assert.Equal(new string?[] { "4", "3", "2", "1" }, Seqs(cut)), TimeSpan.FromSeconds(5));
        Assert.Equal(Ids(cut).Distinct().Count(), Ids(cut).Length);
    }

    /// <summary>Two appends in flight at once: the play both see is drawn once.</summary>
    [Fact]
    public void Two_appends_in_flight_at_once_draw_a_new_play_once()
    {
        FourPlays();
        var cut = Render();

        _data.Plays.Add(FakeGamecastData.Play(5, null, "Play 5"));
        _data.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Signal("plays", Id);
        cut.WaitForState(() => _data.Waiting == 1, TimeSpan.FromSeconds(5));
        Signal("plays", Id);
        cut.WaitForState(() => _data.Waiting == 2, TimeSpan.FromSeconds(5));

        _data.Gate.SetResult();

        cut.WaitForAssertion(() => Assert.Equal(new string?[] { "5", "4", "3", "2", "1" }, Seqs(cut)), TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// A plays notice that lands while a full load is in flight waits for it, then appends onto
    /// what it drew: the play it brings is drawn once, after the load.
    /// </summary>
    [Fact]
    public async Task An_append_during_a_full_load_waits_for_it_and_draws_each_play_once()
    {
        FourPlays();
        _data.HeaderError = new InvalidOperationException("database restarting");
        var cut = Render();

        _data.HeaderError = null;
        _data.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var retry = cut.FindAll("button").First(b => b.TextContent.Contains("Try again"));
        var click = retry.ClickAsync(new MouseEventArgs());
        cut.WaitForState(() => _data.Waiting == 1, TimeSpan.FromSeconds(5));

        _data.Plays.Add(FakeGamecastData.Play(5, null, "Play 5"));
        Signal("plays", Id);

        _data.Gate.SetResult();
        await click;

        cut.WaitForAssertion(() => Assert.Equal(new string?[] { "5", "4", "3", "2", "1" }, Seqs(cut)), TimeSpan.FromSeconds(5));
    }

    // ---- Independent reads ------------------------------------------------------------------

    [Fact]
    public void A_header_read_that_fails_still_appends_the_plays_and_says_the_refresh_failed()
    {
        TwoDrives();
        var window = Services.GetRequiredService<WindowManager>().Open(WindowCatalog.Find(WindowCatalog.Gamecast)!,
            new Dictionary<string, object> { ["GameId"] = BoardRows.GameId });
        var cut = Render(window.Id);

        _data.HeaderError = new InvalidOperationException("header timed out");
        _data.Plays.Add(FakeGamecastData.Play(5, 2, "S.Barkley run for 11 yards"));
        Publish(DeskSignals.Address("games", Id), DeskSignals.Address("plays", Id));

        cut.WaitForAssertion(() => Assert.Contains("S.Barkley run for 11 yards", cut.Markup), TimeSpan.FromSeconds(5));
        cut.WaitForAssertion(() => Assert.Equal(PulseState.Warn, window.Pulse), TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("did not load", cut.Markup);
    }

    [Fact]
    public void A_plays_read_that_fails_still_moves_the_header()
    {
        TwoDrives();
        var window = Services.GetRequiredService<WindowManager>().Open(WindowCatalog.Find(WindowCatalog.Gamecast)!,
            new Dictionary<string, object> { ["GameId"] = BoardRows.GameId });
        var cut = Render(window.Id);

        _data.PlaysError = new InvalidOperationException("plays timed out");
        _data.Header = FakeGamecastData.Live() with { AwayScore = 14 };
        Publish(DeskSignals.Address("games", Id), DeskSignals.Address("plays", Id));

        cut.WaitForAssertion(() => Assert.Contains("14", cut.Find(".ptitle").TextContent), TimeSpan.FromSeconds(5));
        cut.WaitForAssertion(() => Assert.Equal(PulseState.Warn, window.Pulse), TimeSpan.FromSeconds(5));
        Assert.Equal(2, cut.FindAll(".gc__plays .gc__play").Count);
    }

    // ---- The drive log ---------------------------------------------------------------------

    [Fact]
    public void The_drive_in_progress_is_open_and_the_previous_one_is_folded_to_its_result()
    {
        TwoDrives();
        var cut = Render();

        Assert.Equal("true", cut.Find("[data-drive='2'] button").GetAttribute("aria-expanded"));
        Assert.Equal("false", cut.Find("[data-drive='1'] button").GetAttribute("aria-expanded"));
        Assert.Contains("7 plays, 62 yds, 3:48 · TD", cut.Find("[data-drive='1']").TextContent);
        Assert.Single(cut.FindAll(".gc__plays"));

        // The current drive's plays, newest first; the folded drive's are not drawn.
        Assert.Equal(new string?[] { "4", "3" }, Seqs(cut, ".gc__plays .gc__play"));
    }

    [Fact]
    public void One_drive_is_open_at_a_time()
    {
        TwoDrives();
        var cut = Render();

        cut.Find("[data-drive='1'] button").Click();

        Assert.Equal("true", cut.Find("[data-drive='1'] button").GetAttribute("aria-expanded"));
        Assert.Equal("false", cut.Find("[data-drive='2'] button").GetAttribute("aria-expanded"));
        Assert.Single(cut.FindAll(".gc__plays"));
        Assert.Equal(new string?[] { "2", "1" }, Seqs(cut, ".gc__plays .gc__play"));

        // Pressed again, it closes, and nothing is open.
        cut.Find("[data-drive='1'] button").Click();
        Assert.Empty(cut.FindAll(".gc__plays"));
    }

    /// <summary>
    /// A play ESPN filed under no drive is still drawn: inside the drive whose sequence range
    /// holds it, or else in an "Other plays" group, as it is in the scoring and the chart.
    /// </summary>
    [Fact]
    public void A_play_with_no_drive_is_drawn_in_the_drive_that_holds_it_or_under_other_plays()
    {
        _data.Plays.AddRange([
            FakeGamecastData.Play(1, 1, "J.Hurts run for 4 yards"),
            FakeGamecastData.Play(3, 2, "S.Barkley run for 3 yards"),
            FakeGamecastData.Play(4, null, "Timeout Washington"),
            FakeGamecastData.Play(5, 2, "J.Hurts pass incomplete"),
            FakeGamecastData.Play(9, null, "Two-minute warning")
        ]);
        _data.Drives.AddRange([
            FakeGamecastData.Drive(1, 1, 1, current: false, result: "Punt"),
            FakeGamecastData.Drive(2, 3, 5, current: true)
        ]);

        var cut = Render();

        Assert.Equal(new string?[] { "5", "4", "3" }, Seqs(cut, ".gc__plays .gc__play"));

        var other = cut.Find("[data-drive='other']");
        Assert.Contains("Other plays", other.TextContent);
        other.QuerySelector("button")!.Click();
        Assert.Equal(new string?[] { "9" }, Seqs(cut, ".gc__plays .gc__play"));
    }

    [Fact]
    public void A_long_drive_virtualises_its_rows_with_list_item_spacers()
    {
        for (var s = 1; s <= GamecastPanel.VirtualizeAt + 5; s++)
            _data.Plays.Add(FakeGamecastData.Play(s, null, $"Play {s}"));

        var cut = Render();

        // Every child of the list is a list item, the spacers included.
        var log = cut.Find("ol.gc__log");
        Assert.NotEmpty(log.Children);
        Assert.All(log.Children, c => Assert.Equal("LI", c.TagName));
    }

    [Fact]
    public void A_drive_line_leaves_out_what_the_provider_did_not_send()
    {
        Assert.Equal("7 plays, 62 yds, 3:48 · TD",
            GamecastPanel.DriveLine(FakeGamecastData.Drive(1, 1, 2, false, "TD", 7, 62, "3:48")));
        Assert.Equal("2 plays, 3 yds · In progress",
            GamecastPanel.DriveLine(FakeGamecastData.Drive(2, 3, 4, true, null, 2, 3)));
        Assert.Equal("No plays yet · Punt", GamecastPanel.DriveLine(FakeGamecastData.Drive(3, 5, 5, false, "Punt")));
    }

    [Fact]
    public void The_drives_sit_under_a_heading_for_their_period()
    {
        TwoDrives();
        var cut = Render();

        var heading = Assert.Single(cut.FindAll(".gc__period"));
        Assert.Equal("2nd quarter", heading.TextContent.Trim());
        Assert.Equal("Overtime", XLines.Core.Analytics.Periods.Name("nfl", 5));
    }

    /// <summary>A game that is over reads as a league's play-by-play does: from the kickoff down.</summary>
    [Fact]
    public void A_final_reads_from_the_kickoff_down()
    {
        _data.Header = FakeGamecastData.Live(status: GameStatus.Final);
        TwoDrives();
        var cut = Render();

        Assert.Equal(new string?[] { "1", "2" }, cut.FindAll(".gc__drive").Select(d => d.GetAttribute("data-drive")).ToArray());

        cut.Find("[data-drive='1'] button").Click();
        Assert.Equal(new string?[] { "1", "2" }, Seqs(cut, ".gc__plays .gc__play"));
    }

    [Fact]
    public void Every_play_opens_each_drive_and_leaves_nothing_to_fold()
    {
        TwoDrives();
        var cut = Render();

        cut.Find("label[title^='Open every drive'] input").Change(true);

        Assert.Equal(new string?[] { "4", "3", "2", "1" }, Seqs(cut, ".gc__plays .gc__play"));
        Assert.Empty(cut.FindAll(".gc__drive button"));
        Assert.Contains("7 plays, 62 yds, 3:48 · TD", cut.Find("[data-drive='1']").TextContent);
    }

    [Fact]
    public void A_drive_row_says_the_score_it_left_away_side_first()
    {
        TwoDrives();
        var cut = Render();

        Assert.Equal("PHI 7–3 WSH", cut.Find("[data-drive='1'] .gc__tally").TextContent);
    }

    /// <summary>
    /// The field line is the strip's frame: away's goal on the left, yard lines counted from the
    /// home goal. A drive from the 75 with the ball now on the 34 ran from 25 to 66.
    /// </summary>
    [Fact]
    public void A_drives_field_line_runs_from_its_start_to_its_end_or_to_the_ball()
    {
        var current = FakeGamecastData.Drive(2, 3, 4, current: true);
        Assert.Equal((25d, 41d), GamecastPanel.DriveSpan(current, ballOn: 34));

        var done = FakeGamecastData.Drive(1, 1, 2, current: false) with { EndYardLine = 0 };
        Assert.Equal((25d, 75d), GamecastPanel.DriveSpan(done, ballOn: 34));

        // A drive that went nowhere still draws a mark; one with no end known draws none.
        Assert.Equal((25d, 2d), GamecastPanel.DriveSpan(done with { EndYardLine = 75 }, null));
        Assert.Null(GamecastPanel.DriveSpan(FakeGamecastData.Drive(1, 1, 2, current: false), ballOn: 34));
    }

    // ---- The line score --------------------------------------------------------------------

    [Fact]
    public void The_line_score_is_away_over_home_with_the_total_last_and_the_side_ahead_marked()
    {
        _data.Header = FakeGamecastData.Live() with { AwayLinescore = [7, 0], HomeLinescore = [0, 3] };
        var cut = Render();

        var rows = cut.FindAll(".linescore__table tbody tr");
        Assert.Equal(new string?[] { "PHI", "WSH" }, rows.Select(r => r.GetAttribute("data-line")).ToArray());
        Assert.Equal(new[] { "7", "0", "7" }, rows[0].QuerySelectorAll("td").Select(c => c.TextContent).ToArray());
        Assert.Equal(new[] { "0", "3", "3" }, rows[1].QuerySelectorAll("td").Select(c => c.TextContent).ToArray());
        Assert.Contains("linescore__ahead", rows[0].ClassName);
        Assert.DoesNotContain("linescore__ahead", rows[1].ClassName ?? string.Empty);
    }

    [Fact]
    public void A_game_not_yet_started_has_no_line_score()
    {
        _data.Header = FakeGamecastData.Live(status: GameStatus.Scheduled) with { AwayLinescore = [], HomeLinescore = [] };
        var cut = Render();

        Assert.Empty(cut.FindAll(".linescore"));
    }

    // ---- The field strip -------------------------------------------------------------------

    /// <summary>
    /// LAC (away) at its own 20 in LAC @ LAR read yardLine 80 (Game.YardLine): 20 yards from the
    /// left end zone's goal line, going right, needing 10 — the line of gain at its own 30 — and
    /// the red zone the home side's 20, on the right.
    /// </summary>
    [Fact]
    public void An_away_possession_is_drawn_from_the_left_going_right()
    {
        var marks = FieldGeometry.Marks(yardLine: 80, distance: 10, homeBall: false)!;

        Assert.Equal(30, marks.Spot);
        Assert.Equal(40, marks.Gain);
        Assert.Equal((90d, 110d), (marks.RedFrom, marks.RedTo));
        Assert.False(marks.Leftward);
        Assert.False(marks.InRedZone);
    }

    /// <summary>MIA (home) at its own 29 in NE @ MIA read 29: near the right end zone, going left.</summary>
    [Fact]
    public void A_home_possession_is_drawn_from_the_right_going_left()
    {
        var marks = FieldGeometry.Marks(yardLine: 29, distance: 7, homeBall: true)!;

        Assert.Equal(81, marks.Spot);
        Assert.Equal(74, marks.Gain);
        Assert.Equal((10d, 30d), (marks.RedFrom, marks.RedTo));
        Assert.True(marks.Leftward);
        Assert.False(marks.InRedZone);

        // At the away 15 (85 from home), inside the red zone; goal to go stops at the goal line.
        var close = FieldGeometry.Marks(yardLine: 85, distance: 15, homeBall: true)!;
        Assert.True(close.InRedZone);
        Assert.Equal(FieldGeometry.EndZone, close.Gain);
    }

    // The field's own marks — the ball, the line to gain, the red zone, no outline, the field
    // alone with nobody on the ball — are FieldGraphicTests'. Here: what the window hands it.

    [Fact]
    public void A_live_games_field_card_draws_the_field_with_the_situation_as_its_name()
    {
        var cut = Render();

        var card = cut.Find("section.wsection.gc__fieldcard");
        Assert.Equal("Field", card.QuerySelector(".wgroup__bar .wgroup__title")!.TextContent.Trim());

        var graphic = cut.FindComponent<FieldGraphic>().Instance;
        Assert.Equal("Field: PHI ball, 2nd and 6 at WSH 34", graphic.Label);
        Assert.Equal((34, 6, (bool?)false), (graphic.YardLine, graphic.Distance, graphic.HomeBall));
        Assert.Equal(("WSH", "PHI"), (graphic.HomeAbbrev, graphic.AwayAbbrev));
        Assert.Equal("img", card.QuerySelector("svg")!.GetAttribute("role"));
    }

    [Fact]
    public void The_field_is_handed_the_drive_on_the_field_and_only_its_plays()
    {
        TwoDrives();
        var cut = Render();

        var graphic = cut.FindComponent<FieldGraphic>().Instance;
        Assert.Equal(new long[] { 3, 4 }, graphic.DrivePlays.Select(p => p.Sequence).ToArray());
        Assert.Equal(75, graphic.DriveStartYardLine);
    }

    /// <summary>
    /// After a pick-six ESPN keeps the beaten drive current until the kickoff, with the ball the
    /// other side's: its plays are not drawn under that side's ball.
    /// </summary>
    [Fact]
    public void A_current_drive_that_is_not_the_side_with_the_balls_gives_the_field_no_plays()
    {
        TwoDrives();
        _data.Drives[1] = _data.Drives[1] with { TeamId = FakeGamecastData.AwayId };
        _data.Header = FakeGamecastData.Live() with { PossessionTeamId = FakeGamecastData.HomeId };
        var cut = Render();

        var graphic = cut.FindComponent<FieldGraphic>().Instance;
        Assert.Empty(graphic.DrivePlays);
        Assert.Null(graphic.DriveStartYardLine);
    }

    [Fact]
    public void With_no_drive_on_the_field_the_field_gets_no_plays()
    {
        FourPlays();
        var cut = Render();

        var graphic = cut.FindComponent<FieldGraphic>().Instance;
        Assert.Empty(graphic.DrivePlays);
        Assert.Null(graphic.DriveStartYardLine);
    }

    [Theory]
    [InlineData(GameStatus.Final)]
    [InlineData(GameStatus.Scheduled)]
    public void A_game_not_on_has_no_field_card(GameStatus status)
    {
        _data.Header = FakeGamecastData.Live(status: status);
        var cut = Render();

        Assert.Empty(cut.FindAll(".gc__fieldcard"));
        Assert.Empty(cut.FindComponents<FieldGraphic>());
    }

    // ---- Empty states and the box score ----------------------------------------------------

    [Fact]
    public void A_game_with_no_plays_yet_says_when_they_appear()
    {
        _data.Header = FakeGamecastData.Live(status: GameStatus.Scheduled);
        var cut = Render();

        Assert.Contains("Plays appear once the game kicks off.", cut.Markup);
        Show(cut, "Box score");
        Assert.Contains("The box score appears once the first stats are in.", cut.Markup);
    }

    [Fact]
    public void Another_league_says_the_gamecast_is_for_the_nfl_and_reads_no_plays()
    {
        _data.Header = FakeGamecastData.Live(sport: "mlb");
        var cut = Render();

        Assert.Contains("The gamecast covers NFL games.", cut.Markup);
        Assert.Empty(_data.PlaysReadsAfter);
    }

    [Fact]
    public void A_live_game_whose_plays_stopped_says_so_in_plain_words()
    {
        _data.Plays.Add(FakeGamecastData.Play(1, 1, "Kickoff") with { Wallclock = DateTimeOffset.UtcNow.AddMinutes(-9) });
        var cut = Render();

        var note = cut.Find(".gc__quiet");
        Assert.Contains("Plays are not arriving.", note.TextContent);
        Assert.Contains("The feed runs on the machine that runs the schedule.", note.TextContent);

        // The configuration keys are on the note, in a line of their own, not in the sentence.
        var keys = cut.Find(".gc__quiet .gc__quiet-keys");
        Assert.Contains("Ingestion:HostScheduler", keys.TextContent);
        Assert.DoesNotContain("Ingestion:", note.TextContent.Replace(keys.TextContent, ""));
        Assert.Null(note.GetAttribute("title"));

        // It is the field card's notice: under the card, never inside it.
        Assert.Empty(cut.FindAll(".gc__fieldcard .wgroup .gc__quiet"));
        Assert.Single(cut.FindAll(".gc__fieldcard > .gc__quiet"));
    }

    // ---- How the plays are arriving --------------------------------------------------------

    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(15);

    private static GamecastFeedReading Here(GamecastLane? lane, int inPlay = 7, int fast = 4, int slow = 3,
        int perPoll = 1, bool promoted = false)
        => new(true, lane, inPlay, fast, slow, Poll, perPoll, promoted);

    private static readonly Dictionary<string, GamecastFeedReading> Readings = new()
    {
        ["elsewhere"] = GamecastFeedReading.NotHere(Poll, 1),
        ["fast"] = Here(GamecastLane.Fast),
        ["slow"] = Here(GamecastLane.Slow),                    // 3 in turn, 1 a poll: every 45 s
        ["slow8"] = Here(GamecastLane.Slow, inPlay: 12, slow: 8), // every 2 minutes
        ["backedoff"] = Here(GamecastLane.BackedOff),
        ["nolane"] = Here(null)
    };

    public static TheoryData<GameStatus, string, int, bool, GamecastPanel.FeedState> States => new()
    {
        // By status, whatever the feed says.
        { GameStatus.Final, "fast", 5, false, GamecastPanel.FeedState.Final },
        { GameStatus.Final, "elsewhere", 6000, false, GamecastPanel.FeedState.Final },
        { GameStatus.Scheduled, "nolane", 6000, false, GamecastPanel.FeedState.NotStarted },
        { GameStatus.Postponed, "elsewhere", 6000, false, GamecastPanel.FeedState.NotStarted },

        // The feed elsewhere: the newest play's age is all there is to go by.
        { GameStatus.Live, "elsewhere", 30, false, GamecastPanel.FeedState.Live },
        { GameStatus.Live, "elsewhere", 119, false, GamecastPanel.FeedState.Live },
        { GameStatus.Live, "elsewhere", 120, false, GamecastPanel.FeedState.Quiet },
        { GameStatus.Live, "elsewhere", 900, true, GamecastPanel.FeedState.Live },

        // The fast lane here: live, unless nothing has come in outside a break.
        { GameStatus.Live, "fast", 30, false, GamecastPanel.FeedState.Live },
        { GameStatus.Live, "fast", 600, false, GamecastPanel.FeedState.Quiet },
        { GameStatus.Live, "fast", 600, true, GamecastPanel.FeedState.Live },

        // In turn every 45 s: quiet only past two minutes and two turns (210 s).
        { GameStatus.Live, "slow", 30, false, GamecastPanel.FeedState.Slow },
        { GameStatus.Live, "slow", 150, false, GamecastPanel.FeedState.Slow },
        { GameStatus.Live, "slow", 209, false, GamecastPanel.FeedState.Slow },
        { GameStatus.Live, "slow", 210, false, GamecastPanel.FeedState.Quiet },
        { GameStatus.Live, "slow", 900, true, GamecastPanel.FeedState.Slow },

        // In turn every two minutes: the threshold scales with the turn (360 s).
        { GameStatus.Live, "slow8", 300, false, GamecastPanel.FeedState.Slow },
        { GameStatus.Live, "slow8", 360, false, GamecastPanel.FeedState.Quiet },

        // Backing off, or here and reading the game in no lane: quiet however fresh the last play.
        { GameStatus.Live, "backedoff", 5, false, GamecastPanel.FeedState.Quiet },
        { GameStatus.Live, "backedoff", 5, true, GamecastPanel.FeedState.Quiet },
        { GameStatus.Live, "nolane", 5, false, GamecastPanel.FeedState.Quiet },
    };

    [Theory]
    [MemberData(nameof(States))]
    public void The_feed_state_follows_the_status_the_lane_and_the_newest_plays_age(
        GameStatus status, string reading, int seconds, bool isBreak, GamecastPanel.FeedState expected)
        => Assert.Equal(expected, GamecastPanel.StateOf(status, Readings[reading], TimeSpan.FromSeconds(seconds), isBreak));

    [Fact]
    public void A_game_in_turn_is_read_every_ceiling_of_the_games_in_turn_over_the_games_a_poll()
    {
        Assert.Equal(TimeSpan.FromSeconds(45), Here(GamecastLane.Slow, slow: 3).SlowEvery);
        Assert.Equal(TimeSpan.FromSeconds(60), Here(GamecastLane.Slow, slow: 7, perPoll: 2).SlowEvery);
        Assert.Null(Here(null, perPoll: 0).SlowEvery);
        Assert.Equal(TimeSpan.FromSeconds(210), GamecastPanel.SlowQuietAfter(Here(GamecastLane.Slow, slow: 3)));
    }

    /// <summary>A live game with a play landed just now, read by the feed as given.</summary>
    private IRenderedComponent<Bench> RenderFresh(GamecastFeedReading reading)
    {
        _data.Reading = reading;
        _data.Plays.Add(FakeGamecastData.Play(1, null, "Kickoff"));
        return Render();
    }

    [Fact]
    public void A_game_read_in_turn_says_how_often_under_the_field_and_that_it_moves_up()
    {
        var cut = RenderFresh(Here(GamecastLane.Slow, promoted: true));

        var foot = cut.Find(".gc__fieldcard > .wgroup__foot");
        Assert.Equal("7 games are on. This one is read about every 45 seconds; with this window open it moves to every 15 seconds.",
            foot.TextContent.Trim());
        Assert.Empty(cut.FindAll(".gc__quiet"));
    }

    [Fact]
    public void A_game_read_in_turn_that_will_not_move_up_does_not_say_it_will()
    {
        var cut = RenderFresh(Here(GamecastLane.Slow, inPlay: 12, slow: 8, promoted: false));

        Assert.Equal("12 games are on. This one is read about every 2 minutes.",
            cut.Find(".gc__fieldcard > .wgroup__foot").TextContent.Trim());
    }

    [Theory]
    [InlineData("fast")]
    [InlineData("elsewhere")]
    public void A_game_whose_plays_arrive_at_full_pace_says_nothing_extra(string reading)
    {
        var cut = RenderFresh(Readings[reading]);

        Assert.Empty(cut.FindAll(".gc__fieldcard .wgroup__foot"));
        Assert.Empty(cut.FindAll(".gc__quiet"));
    }

    [Fact]
    public void A_quiet_game_the_feed_here_is_reading_names_no_settings()
    {
        _data.Reading = Here(GamecastLane.Fast);
        _data.Plays.Add(FakeGamecastData.Play(1, 1, "Kickoff") with { Wallclock = DateTimeOffset.UtcNow.AddMinutes(-9) });
        var cut = Render();

        var note = cut.Find(".gc__quiet");
        Assert.Equal("Plays are not arriving. The newest is 9m old. The game is being read, and no new play has come in.",
            note.TextContent.Trim());
        Assert.Empty(cut.FindAll(".gc__quiet-keys"));
        Assert.DoesNotContain("Ingestion:", cut.Markup);
        Assert.DoesNotContain("machine that runs the schedule", cut.Markup);
    }

    [Fact]
    public void A_game_the_feed_here_is_backing_off_is_quiet_and_names_no_settings()
    {
        var cut = RenderFresh(Here(GamecastLane.BackedOff));

        var note = cut.Find(".gc__quiet");
        Assert.Equal("Plays are not arriving. The newest is under a minute old. The last reads of this game did not succeed; it is tried again shortly.",
            note.TextContent.Trim());
        Assert.Empty(cut.FindAll(".gc__quiet-keys"));
    }

    [Fact]
    public void A_game_the_feed_here_reads_in_no_lane_is_quiet()
    {
        var cut = RenderFresh(Here(null));

        Assert.Contains("This game is not being read.", cut.Find(".gc__quiet").TextContent);
        Assert.Empty(cut.FindAll(".gc__quiet-keys"));

        // With the turn switched off and the fast lane full, the note says so with its numbers.
        Assert.Equal(
            "Plays are not arriving. The newest is under a minute old. 7 games are on and 4 are read at a time; this one is not among them.",
            RenderFresh(Here(null, perPoll: 0)).Find(".gc__quiet").TextContent.Trim());
    }

    // ---- The empty plays tab ---------------------------------------------------------------

    [Fact]
    public void A_live_game_with_no_plays_says_none_has_landed_not_that_it_has_yet_to_kick_off()
    {
        var cut = Render();

        Assert.Contains("No plays have landed yet.", cut.Markup);
        Assert.DoesNotContain("Plays appear once the game kicks off.", cut.Markup);
        Assert.Contains("The game kicked off 40m ago and none has landed.", cut.Find(".gc__quiet").TextContent);
    }

    [Fact]
    public void A_final_with_no_plays_says_none_is_held()
    {
        _data.Header = FakeGamecastData.Live(status: GameStatus.Final);
        var cut = Render();

        Assert.Contains("No play-by-play is held for this game.", cut.Markup);
        Assert.Empty(cut.FindAll(".gc__quiet"));
    }

    [Fact]
    public void Each_empty_plays_tab_is_said_for_its_state()
    {
        Assert.Equal("Plays appear once the game kicks off.", GamecastPanel.NoPlays(GamecastPanel.FeedState.NotStarted));
        Assert.Equal("No play-by-play is held for this game.", GamecastPanel.NoPlays(GamecastPanel.FeedState.Final));
        Assert.All(new[] { GamecastPanel.FeedState.Live, GamecastPanel.FeedState.Slow, GamecastPanel.FeedState.Quiet },
            s => Assert.Equal("No plays have landed yet.", GamecastPanel.NoPlays(s)));
    }

    // ---- The cards -------------------------------------------------------------------------

    [Fact]
    public void A_live_game_flows_the_field_the_score_and_the_comparison_then_the_sections_across_the_row()
    {
        TwoDrives();
        var cut = Render();

        var flow = cut.Find(".wflow.gc__flow");
        var children = flow.Children.Select(c => c.ClassList).ToList();
        Assert.Equal(4, children.Count);
        Assert.Contains("gc__fieldcard", children[0]);
        Assert.Contains("gc__scorecard", children[1]);
        Assert.Contains("gc__comparecard", children[2]);
        Assert.Contains("wsection--span", children[3]);
        Assert.NotEmpty(flow.Children[3].QuerySelectorAll("[role='tab']"));

        Assert.Equal(new[] { "Field", "Score by quarter", "Team comparison" },
            cut.FindAll(".gc__flow .wgroup__title").Select(t => t.TextContent.Trim()).ToArray());
    }

    [Fact]
    public void The_line_score_sits_in_its_card_inset_as_the_game_window_places_it()
    {
        var cut = Render();

        var linescore = cut.Find(".gc__scorecard > .wgroup > .linescore");
        Assert.Contains("linescore--inset", linescore.ClassList);
    }

    [Fact]
    public void The_comparison_card_is_left_out_when_there_is_nothing_to_compare()
    {
        var cut = Render();

        Assert.Empty(cut.FindAll(".gc__comparecard"));
        Assert.Empty(cut.FindAll(".teamcmp"));
    }

    [Fact]
    public void The_comparison_card_compares_the_sides_from_the_drives_for_a_final_too()
    {
        _data.Header = FakeGamecastData.Live(status: GameStatus.Final);
        TwoDrives();
        var cut = Render();

        var card = cut.Find(".gc__comparecard");
        Assert.Contains(cut.FindComponent<TeamComparison>().Instance.Rows, r => r.Label == "Total yards");
        Assert.NotNull(card.QuerySelector(".teamcmp"));
    }

    [Fact]
    public void A_game_not_started_has_no_comparison_card_however_much_is_held()
    {
        _data.Header = FakeGamecastData.Live(status: GameStatus.Scheduled);
        TwoDrives();
        var cut = Render();

        Assert.Empty(cut.FindAll(".gc__comparecard"));
    }

    [Fact]
    public void The_comparison_is_rebuilt_when_the_drives_move_not_on_every_render()
    {
        TwoDrives();
        var cut = Render();
        var rows = cut.FindComponent<TeamComparison>().Instance.Rows;

        cut.Render();
        Assert.Same(rows, cut.FindComponent<TeamComparison>().Instance.Rows);

        _data.Drives[1] = FakeGamecastData.Drive(2, 3, 4, current: true, plays: 2, yards: 20);
        Signal("plays", Id);
        cut.WaitForAssertion(() => Assert.NotSame(rows, cut.FindComponent<TeamComparison>().Instance.Rows), TimeSpan.FromSeconds(5));
    }

    // ---- Telling the feed the window is open ------------------------------------------------

    [Fact]
    public void A_live_nfl_game_is_watched_once_when_the_window_opens()
    {
        Render();

        Assert.Equal(new[] { $"watch:{Id}" }, _data.Interest.ToArray());
    }

    [Fact]
    public void A_notice_renews_the_interest_without_watching_again()
    {
        var cut = Render();

        Signal("plays", Id);
        cut.WaitForAssertion(() => Assert.True(_data.Count("renew") >= 1), TimeSpan.FromSeconds(5));
        Signal("games", Id);
        cut.WaitForAssertion(() => Assert.True(_data.Count("renew") >= 2), TimeSpan.FromSeconds(5));

        Assert.Equal(1, _data.Count("watch"));
        Assert.Equal(0, _data.Count("unwatch"));
    }

    [Fact]
    public void Closing_the_window_lets_the_game_go_once()
    {
        Render();

        DisposeComponents();

        Assert.Equal(1, _data.Count("watch"));
        Assert.Equal(1, _data.Count("unwatch"));
        Assert.Equal($"unwatch:{Id}", _data.Interest[^1]);
    }

    [Fact]
    public void The_final_whistle_lets_the_game_go_and_nothing_renews_it_after()
    {
        var cut = Render();

        _data.Header = FakeGamecastData.Live(status: GameStatus.Final);
        Signal("games", Id);
        cut.WaitForAssertion(() => Assert.Equal(1, _data.Count("unwatch")), TimeSpan.FromSeconds(5));
        var calls = _data.Interest.Count;

        Signal("games", Id);
        cut.WaitForState(() => _data.HeaderReads >= 3, TimeSpan.FromSeconds(5));
        DisposeComponents();

        Assert.Equal(calls, _data.Interest.Count);
        Assert.Equal(1, _data.Count("watch"));
    }

    [Theory]
    [InlineData("nfl", GameStatus.Final)]
    [InlineData("mlb", GameStatus.Live)]
    [InlineData("nfl", GameStatus.Postponed)]
    public void A_game_not_on_is_not_watched(string sport, GameStatus status)
    {
        _data.Header = FakeGamecastData.Live(sport: sport, status: status);
        Render();
        DisposeComponents();

        Assert.Empty(_data.Interest);
    }

    [Fact]
    public void A_game_starting_within_the_hour_is_watched_and_one_further_off_is_not()
    {
        var now = DateTimeOffset.UtcNow;
        var soon = FakeGamecastData.Live(status: GameStatus.Scheduled) with { StartsAt = now.AddMinutes(40) };
        var later = soon with { StartsAt = now.AddHours(3) };

        Assert.True(GamecastPanel.Interested(soon, now));
        Assert.False(GamecastPanel.Interested(later, now));
        Assert.True(GamecastPanel.Interested(FakeGamecastData.Live(), now));
        Assert.False(GamecastPanel.Interested(null, now));

        _data.Header = soon;
        Render();
        Assert.Equal(1, _data.Count("watch"));
    }

    private static GameBoxScore Box(string yards) => new(new Game { Id = BoardRows.GameId },
        [new BoxScoreLine(7, "Jayden Daniels", "QB", true, new Dictionary<string, string> { ["YDS"] = yards })], []);

    /// <summary>
    /// The feed writes the box from the summary about once a minute while the game is live; a
    /// games notice reads it, at most once per half minute, and a final's notice always does.
    /// </summary>
    [Fact]
    public void A_live_games_notice_reads_the_box_score_at_most_once_per_half_minute()
    {
        var t0 = DateTimeOffset.UtcNow;
        Bench.Clock = t0;
        _data.Box = Box("120");
        var cut = Render();
        Show(cut, "Box score");
        Assert.Contains("Jayden Daniels", cut.Markup);
        Assert.Equal(1, _data.BoxReads);

        // Ten seconds on: the header moves, the box waits.
        Bench.Clock = t0.AddSeconds(10);
        _data.Box = Box("181");
        var headers = _data.HeaderReads;
        Signal("games", Id);
        cut.WaitForState(() => _data.HeaderReads == headers + 1, TimeSpan.FromSeconds(5));
        Assert.Equal(1, _data.BoxReads);

        // Past the half minute: read again.
        Bench.Clock = t0.AddSeconds(31);
        Signal("games", Id);
        cut.WaitForAssertion(() => Assert.Contains("181", cut.Markup), TimeSpan.FromSeconds(5));
        Assert.Equal(2, _data.BoxReads);

        // The final whistle reads it however recently it was read.
        Bench.Clock = t0.AddSeconds(33);
        _data.Header = FakeGamecastData.Live(status: GameStatus.Final);
        _data.Box = Box("240");
        Signal("games", Id);
        cut.WaitForAssertion(() => Assert.Contains("240", cut.Markup), TimeSpan.FromSeconds(5));
        Assert.Equal(3, _data.BoxReads);
    }

    // ---- Win probability -------------------------------------------------------------------

    private static double[] Values(DeskChart chart, string name)
        => chart.Series.Single(s => s.Name == name).Data.Values.ToArray();

    private void Probabilities()
    {
        _data.Plays.AddRange([
            FakeGamecastData.Play(1, null, "Kickoff", win: 0.40),
            FakeGamecastData.Play(2, null, "Run", win: 0.45) with { SpreadCoverPctHome = 0.60 },
            FakeGamecastData.Play(3, null, "Pass", win: 0.55) with { SpreadCoverPctHome = 0.70 }
        ]);
    }

    [Fact]
    public void The_chart_draws_the_pre_game_win_probability_as_a_flat_reference()
    {
        Probabilities();
        var cut = Render();
        Show(cut, "Win probability");
        var chart = cut.FindComponent<DeskChart>().Instance;

        Assert.Equal([40d, 45d, 55d], Values(chart, "WSH win"));
        Assert.Equal([40d, 40d, 40d], Values(chart, "WSH pre-game"));
        Assert.Equal("Closed PHI -3, total 44.5", chart.Takeaway);

        // No cover or over is drawn, so neither is their even line.
        Assert.DoesNotContain(chart.Series, s => s.Name == GamecastPanel.EvenSeries);
    }

    /// <summary>
    /// The cover line starts at its first real reading — DeskChart draws no gaps, so the chart
    /// starts there — and a flat 50% line is drawn as its reference. Nothing before the first
    /// reading is drawn at an invented 50%.
    /// </summary>
    [Fact]
    public void The_cover_line_starts_at_its_first_reading_with_an_even_line_beside_it()
    {
        Probabilities();
        var cut = Render();
        Show(cut, "Win probability");

        cut.Find("label[title^='Add the chance the home side covers'] input").Change(true);

        var chart = cut.FindComponent<DeskChart>().Instance;
        Assert.Equal([60d, 70d], Values(chart, "WSH cover"));
        Assert.Equal([50d, 50d], Values(chart, GamecastPanel.EvenSeries));
        Assert.Equal([45d, 55d], Values(chart, "WSH win"));
        Assert.Equal([40d, 40d], Values(chart, "WSH pre-game"));
        Assert.Equal(2, chart.Labels.Length);
    }

    [Fact]
    public void The_close_is_named_under_the_chart_for_the_favourite()
    {
        Assert.Equal("Closed PHI -3, total 44.5", GamecastPanel.CloseCaption(FakeGamecastData.Live()));
        Assert.Equal("Closed WSH -2.5", GamecastPanel.CloseCaption(FakeGamecastData.Live() with
        {
            Close = new XLines.Data.Gamecast.GamecastClose(-2.5m, null, true)
        }));
    }

    // ---- The clock -------------------------------------------------------------------------

    private static GamecastPanel.ClockReading Reading(int seconds, DateTimeOffset at, int period = 2) => new(period, seconds, at);

    [Fact]
    public void The_clock_runs_only_when_the_last_two_readings_show_it_moving()
    {
        var now = DateTimeOffset.UtcNow;
        var game = GamecastPanel.AsGame(FakeGamecastData.Live() with { LiveUpdatedAt = now.AddSeconds(-10) });

        // 267 fifteen seconds before 252: the clock ran with the wall clock. It counts on from
        // the reading less its age, for what is left of eighteen seconds past the reading.
        Assert.Equal((242, true, 8), GamecastPanel.ClockRun(game, Reading(267, now.AddSeconds(-25)), now));

        // One reading alone says nothing about whether the clock is running.
        Assert.False(GamecastPanel.ClockRun(game, null, now).Running);

        // The same seconds twice: stopped.
        Assert.Equal((252, false, 0), GamecastPanel.ClockRun(game, Reading(252, now.AddSeconds(-25)), now));

        // Two seconds off over fifteen: an incompletion between snaps, not a running clock.
        Assert.False(GamecastPanel.ClockRun(game, Reading(254, now.AddSeconds(-25)), now).Running);

        // Another period: the readings are not of one run.
        Assert.False(GamecastPanel.ClockRun(game, Reading(267, now.AddSeconds(-25), period: 1), now).Running);
    }

    [Fact]
    public void The_clock_never_runs_past_one_poll_and_its_slack()
    {
        var now = DateTimeOffset.UtcNow;
        var game = GamecastPanel.AsGame(FakeGamecastData.Live() with { LiveUpdatedAt = now.AddSeconds(-20) });

        Assert.False(GamecastPanel.ClockRun(game, Reading(267, now.AddSeconds(-35)), now).Running);
        Assert.Equal(TimeSpan.FromSeconds(18), GamecastPanel.ClockRunsFor);

        // Never past 0:00: a count of 5 runs five seconds at most.
        var late = GamecastPanel.AsGame(FakeGamecastData.Live() with { ClockSeconds = 5, DisplayClock = "0:05", LiveUpdatedAt = now });
        Assert.Equal((5, true, 5), GamecastPanel.ClockRun(late, Reading(20, now.AddSeconds(-15)), now));

        game.LiveUpdatedAt = now;
        game.StatusDetail = "Halftime";
        Assert.False(GamecastPanel.ClockRun(game, Reading(267, now.AddSeconds(-15)), now).Running);
    }

    [Fact]
    public void The_window_keeps_the_previous_reading_and_runs_the_clock_on_the_second()
    {
        var now = DateTimeOffset.UtcNow;
        Bench.Clock = now;
        _data.Header = FakeGamecastData.Live() with
        {
            ClockSeconds = 267,
            DisplayClock = "4:27",
            StatusDetail = "4:27 - 2nd",
            LiveUpdatedAt = now.AddSeconds(-15)
        };
        FourPlays();
        var cut = Render();

        cut.WaitForAssertion(() => Assert.NotEmpty(_clock.Invocations["startClock"]));
        Assert.False((bool)_clock.Invocations["startClock"][^1].Arguments[2]!);

        _data.Header = FakeGamecastData.Live() with { ClockSeconds = 252, DisplayClock = "4:12", LiveUpdatedAt = now };
        Signal("games", Id);
        cut.WaitForAssertion(() => Assert.True((bool)_clock.Invocations["startClock"][^1].Arguments[2]!), TimeSpan.FromSeconds(5));

        // The next reading repeats the seconds: the clock stopped.
        Bench.Clock = now.AddSeconds(15);
        _data.Header = FakeGamecastData.Live() with { ClockSeconds = 252, DisplayClock = "4:12", LiveUpdatedAt = now.AddSeconds(15) };
        Signal("games", Id);
        cut.WaitForAssertion(() => Assert.False((bool)_clock.Invocations["startClock"][^1].Arguments[2]!), TimeSpan.FromSeconds(5));
    }

    /// <summary>A live game whose title draws the clock tag, so there is a clock to hand the browser.</summary>
    private IRenderedComponent<Bench> RenderLiveClock()
    {
        var now = DateTimeOffset.UtcNow;
        Bench.Clock = now;
        _data.Header = FakeGamecastData.Live() with
        {
            ClockSeconds = 267,
            DisplayClock = "4:27",
            StatusDetail = "4:27 - 2nd",
            LiveUpdatedAt = now.AddSeconds(-15)
        };
        FourPlays();
        return Render();
    }

    /// <summary>An exception out of OnAfterRenderAsync ends the whole desk circuit; the renderer holds it here.</summary>
    private void AssertCircuitStands()
        => Assert.False(Renderer.UnhandledException.IsCompleted,
            Renderer.UnhandledException.IsCompleted ? Renderer.UnhandledException.Result.ToString() : null);

    /// <summary>The clock module's import, failing as a stale deployment's would.</summary>
    private sealed class FailingImport() : JSRuntimeInvocationHandlerBase<IJSObjectReference>(
        i => i.Identifier == "import" && i.Arguments.FirstOrDefault() as string == "./js/gameclock.js", false)
    {
        public void SetException(Exception exception) => SetExceptionBase(exception);
    }

    [Fact]
    public void A_clock_module_that_will_not_load_leaves_the_window_drawn_and_retries_once()
    {
        var import = new FailingImport();
        import.SetException(new JSException("Failed to fetch dynamically imported module"));
        JSInterop.AddInvocationHandler(import);

        var cut = RenderLiveClock();
        cut.WaitForAssertion(() => Assert.Single(import.Invocations));
        AssertCircuitStands();

        // Later renders may ask once more, and then the clock gives up quietly: a clock that
        // does not tick is acceptable, a window re-importing every pulse is not.
        for (var n = 0; n < 3; n++)
            cut.Render();

        cut.WaitForAssertion(() => Assert.Equal(2, import.Invocations.Count));
        AssertCircuitStands();
        Assert.Equal(4, cut.FindAll(".gc__play").Count);
        Assert.Contains("4:27", cut.Find(".gc__clock").TextContent);
    }

    [Fact]
    public void A_clock_the_browser_refuses_to_start_leaves_the_window_drawn()
    {
        _clock.SetupVoid("startClock", _ => true).SetException(new JSException("startClock is not a function"));

        var cut = RenderLiveClock();
        cut.WaitForAssertion(() => Assert.NotEmpty(_clock.Invocations["startClock"]));

        AssertCircuitStands();
        Assert.Equal(4, cut.FindAll(".gc__play").Count);
    }

    // ---- How it is reached -----------------------------------------------------------------

    [Fact]
    public void The_catalogue_holds_the_gamecast_and_opens_it_on_a_game()
    {
        var definition = WindowCatalog.Find(WindowCatalog.Gamecast);

        Assert.NotNull(definition);
        Assert.Equal(typeof(GamecastPanel), definition.ComponentType);
        Assert.True(definition.RequiresSubject);
        Assert.False(definition.Singleton);

        var manager = new WindowManager(new AppWindowCatalog());
        manager.OpenGamecast(BoardRows.GameId, "Philadelphia Eagles at Washington Commanders");

        var window = Assert.Single(manager.Windows, w => w.Definition.Key == WindowCatalog.Gamecast);
        Assert.Equal(BoardRows.GameId, window.Parameters["GameId"]);
        Assert.Equal("Gamecast · Philadelphia Eagles at Washington Commanders", window.Title);
    }

    [Theory]
    [InlineData("nfl", GameStatus.Live, true)]
    [InlineData("nfl", GameStatus.Final, true)]
    [InlineData("nfl", GameStatus.Scheduled, false)]
    [InlineData("mlb", GameStatus.Live, false)]
    public void The_gamecast_is_offered_for_an_nfl_game_once_it_has_kicked_off(string sport, GameStatus status, bool offered)
    {
        var game = BoardRows.Game();
        game.Sport = new Sport { Key = sport };
        game.Status = status;

        Assert.Equal(offered, GamecastPanel.Offered(game));
    }

    [Fact]
    public void A_live_nfl_game_window_has_a_gamecast_key_and_an_mlb_one_does_not()
    {
        var data = _window;

        var nfl = BoardRows.Game();
        nfl.Status = GameStatus.Live;
        data.Row = BoardRows.Row() with { Game = nfl };

        var cut = RenderComponent<GamePanel>(p => p.Add(g => g.GameId, BoardRows.GameId));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".ptitle")));
        Assert.Contains(cut.FindAll(".ptitle button"), b => b.TextContent.Trim() == "Gamecast");

        var mlb = BoardRows.Game();
        mlb.Status = GameStatus.Live;
        mlb.Sport = new Sport { Key = "mlb", Name = "MLB" };
        data.Row = BoardRows.Row() with { Game = mlb };

        var other = RenderComponent<GamePanel>(p => p.Add(g => g.GameId, BoardRows.GameId));
        other.WaitForAssertion(() => Assert.NotEmpty(other.FindAll(".ptitle")));
        Assert.DoesNotContain(other.FindAll(".ptitle button"), b => b.TextContent.Trim() == "Gamecast");
    }

    /// <summary>The Game window draws its box score with the Gamecast's component, not a copy of it.</summary>
    [Fact]
    public void The_game_window_draws_its_box_score_with_the_shared_side()
    {
        var final = BoardRows.Game();
        final.Status = GameStatus.Final;
        _window.Row = BoardRows.Row() with { Game = final };
        _window.Detail = GameWindowDetail.Empty with
        {
            Box = new GameBoxScore(final,
                [new BoxScoreLine(7, "Drake Maye", "QB", true, new Dictionary<string, string> { ["YDS"] = "281" })], [])
        };

        var cut = RenderComponent<GamePanel>(p => p.Add(g => g.GameId, BoardRows.GameId));
        // The box score is the Game window's one wide block, beneath the overview: no tab to press.
        cut.WaitForAssertion(() => Assert.Contains("Drake Maye", cut.Markup));
        Assert.Equal("Box score", cut.Find(".gameroster__block > .psection__head .panel-h").TextContent.Trim());
        Assert.Equal(2, cut.FindComponents<BoxScoreSide>().Count);
        var source = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Web", "Components", "Panels", "GamePanel.razor"));
        Assert.DoesNotContain("RenderBoxSide", source);
    }
}

/// <summary>
/// The clock module's import, held open: the window is closed before the browser answers, and
/// the module that arrives afterwards is disposed rather than leaked.
/// </summary>
public class GamecastClockImportTests : DeskTestContext
{
    private sealed class Module : IJSObjectReference
    {
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask DisposeAsync()
        {
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => ValueTask.FromResult(default(TValue)!);
    }

    /// <summary>
    /// The browser, held: the clock module's import waits for the test; every other call answers
    /// at once (bunit refuses to plan an import by hand, so this stands in for its interop).
    /// </summary>
    private sealed class HeldJs : IJSRuntime
    {
        public TaskCompletionSource<IJSObjectReference> Clock { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Asked { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "import" && args is ["./js/gameclock.js"])
            {
                Asked = true;
                return (TValue)await Clock.Task;
            }

            return typeof(TValue) == typeof(IJSObjectReference) ? (TValue)(object)new Module() : default!;
        }
    }

    [Fact]
    public async Task A_module_that_arrives_after_the_window_closed_is_disposed()
    {
        var data = new FakeGamecastData();
        var js = new HeldJs();
        Services.AddSingleton<IGamecastData>(data);
        Services.AddSingleton<IGameWindowData>(new FakeGameWindowData());
        Services.AddSingleton<IJSRuntime>(js);
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));

        var cut = RenderComponent<GamecastPanel>(p => p.Add(g => g.GameId, BoardRows.GameId));
        cut.WaitForAssertion(() => Assert.True(js.Asked), TimeSpan.FromSeconds(5));

        DisposeComponents();
        var module = new Module();
        js.Clock.SetResult(module);

        // The window is gone, so nothing renders again: wait on the module itself.
        await module.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
