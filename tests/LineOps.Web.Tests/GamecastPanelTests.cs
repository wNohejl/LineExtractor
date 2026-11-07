using Bunit;
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Desk.Primitives;
using LineOps.Desk.Windowing;
using LineOps.Web.Components.Panels;
using LineOps.Web.Components.Snippets;
using LineOps.Web.Services;
using LineOps.Web.Tests.Support;
using LineOps.Web.Windowing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace LineOps.Web.Tests;

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
        var source = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "Panels", "GamecastPanel.razor"));

        Assert.Contains("<li class=\"gc__play\" @key=\"play.Id\"", source);
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

    [Fact]
    public void The_strip_draws_the_marks_where_the_geometry_puts_them_and_says_the_situation()
    {
        var cut = RenderComponent<FieldStrip>(p => p
            .Add(s => s.HomeAbbrev, "WSH").Add(s => s.AwayAbbrev, "PHI")
            .Add(s => s.YardLine, 34).Add(s => s.Distance, 6).Add(s => s.HomeBall, false)
            .Add(s => s.Label, "Field: PHI ball, 2nd and 6 at WSH 34"));

        var svg = cut.Find("svg");
        Assert.Equal("img", svg.GetAttribute("role"));
        Assert.Equal("Field: PHI ball, 2nd and 6 at WSH 34", svg.GetAttribute("aria-label"));
        Assert.Equal("76", cut.Find(".fieldstrip__spot").GetAttribute("cx"));
        Assert.Equal("82", cut.Find(".fieldstrip__gain").GetAttribute("x1"));
        Assert.Contains("PHI", cut.Markup);
        Assert.Contains("WSH", cut.Markup);

        // At the WSH 34 the ball is outside the red zone, so none is drawn.
        Assert.Empty(cut.FindAll(".fieldstrip__red"));
    }

    [Fact]
    public void The_red_zone_is_tinted_only_once_the_ball_is_inside_the_defences_twenty()
    {
        var cut = RenderComponent<FieldStrip>(p => p
            .Add(s => s.HomeAbbrev, "WSH").Add(s => s.AwayAbbrev, "PHI")
            .Add(s => s.YardLine, 15).Add(s => s.Distance, 5).Add(s => s.HomeBall, false)
            .Add(s => s.Label, "Field: PHI ball, 1st and 5 at WSH 15"));

        Assert.Equal("90", cut.Find(".fieldstrip__red").GetAttribute("x"));
        Assert.Equal("20", cut.Find(".fieldstrip__red").GetAttribute("width"));
    }

    [Fact]
    public void The_strip_has_no_outline_of_its_own()
    {
        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "Snippets", "FieldStrip.razor.css"));

        Assert.DoesNotContain("border:", css);
    }

    [Fact]
    public void A_strip_with_nobody_on_the_ball_draws_the_field_alone()
    {
        var cut = RenderComponent<FieldStrip>(p => p.Add(s => s.YardLine, 34).Add(s => s.Label, "Field"));

        Assert.Empty(cut.FindAll(".fieldstrip__spot"));
        Assert.Empty(cut.FindAll(".fieldstrip__gain"));
        Assert.Empty(cut.FindAll(".fieldstrip__red"));
        Assert.Equal(2, cut.FindAll(".fieldstrip__zone").Count);
    }

    // ---- Empty states and the box score ----------------------------------------------------

    [Fact]
    public void A_game_with_no_plays_yet_says_when_they_appear()
    {
        _data.Header = FakeGamecastData.Live(status: GameStatus.Scheduled);
        var cut = Render();

        Assert.Contains("Plays appear once the game kicks off.", cut.Markup);
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
            Close = new LineOps.Data.Gamecast.GamecastClose(-2.5m, null, true)
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
            ClockSeconds = 267, DisplayClock = "4:27", StatusDetail = "4:27 - 2nd", LiveUpdatedAt = now.AddSeconds(-15)
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
            ClockSeconds = 267, DisplayClock = "4:27", StatusDetail = "4:27 - 2nd", LiveUpdatedAt = now.AddSeconds(-15)
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
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("[role=tab]"), t => t.TextContent.Contains("Box score")));
        cut.FindAll("[role=tab]").First(t => t.TextContent.Contains("Box score")).Click();

        cut.WaitForAssertion(() => Assert.Contains("Drake Maye", cut.Markup));
        Assert.Equal(2, cut.FindComponents<BoxScoreSide>().Count);
        var source = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "Panels", "GamePanel.razor"));
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
