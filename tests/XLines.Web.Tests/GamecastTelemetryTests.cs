using XLines.Core.Entities;
using XLines.Data.CrossReference;
using XLines.Data.Gamecast;
using XLines.Web.Components.Snippets;
using XLines.Web.Tests.Support;

namespace XLines.Web.Tests;

/// <summary>
/// The numbers behind the Gamecast's field graphic and team comparison
/// (<see cref="GamecastTelemetry"/>), pinned without rendering: where each play of the drive
/// sits on the field's x (110 − yard line, on the home-goal frame), and each comparison row,
/// including the ones left out when the data cannot say them.
/// </summary>
public class GamecastTelemetryTests
{
    private const int Home = FakeGamecastData.HomeId;
    private const int Away = FakeGamecastData.AwayId;

    private static GamecastPlay Play(int? start, int? end, int? team = Away, string type = "Rush",
        bool penalty = false, bool scoring = false, bool turnover = false, long sequence = 1)
        => FakeGamecastData.Play(sequence, 1, "play") with
        {
            Type = type,
            TeamId = team,
            IsPenalty = penalty,
            IsScoring = scoring,
            IsTurnover = turnover,
            Start = start is null ? null : new GamecastSituation(1, 10, start, null, null, null, team),
            End = end is null ? null : new GamecastSituation(2, 6, end, null, null, null, team),
        };

    // ---- Drive segments ---------------------------------------------------------------------

    /// <summary>
    /// The away side at its own 25 is 75 yards from the home goal (x 35); four yards gained
    /// rightward puts it at 71 (x 39).
    /// </summary>
    [Fact]
    public void An_away_rush_from_its_own_25_for_four_runs_rightward()
    {
        var segment = Assert.Single(GamecastTelemetry.DriveSegments([Play(75, 71)], homeOffence: false));

        Assert.Equal(35, segment.From);
        Assert.Equal(39, segment.To);
        Assert.Equal(DriveSegmentKind.Gain, segment.Kind);
        Assert.Equal(35, segment.Left);
        Assert.Equal(4, segment.Width);
        Assert.True(segment.Latest);
    }

    /// <summary>The home side at its own 30 (x 80) gains twelve to its 42 (x 68): leftward.</summary>
    [Fact]
    public void A_home_gain_runs_leftward()
    {
        var segment = Assert.Single(GamecastTelemetry.DriveSegments([Play(30, 42, Home)], homeOffence: true));

        Assert.Equal(80, segment.From);
        Assert.Equal(68, segment.To);
        Assert.Equal(DriveSegmentKind.Gain, segment.Kind);
        Assert.Equal((68d, 12d), (segment.Left, segment.Width));
    }

    /// <summary>An away sack from the home 40 (60 out) back to its own 33 (67 out) is a loss of seven.</summary>
    [Fact]
    public void A_sack_is_a_loss()
    {
        var segment = Assert.Single(GamecastTelemetry.DriveSegments([Play(60, 67, type: "Sack")], homeOffence: false));

        Assert.Equal(DriveSegmentKind.Loss, segment.Kind);
        Assert.Equal((43d, 7d), (segment.Left, segment.Width));
    }

    [Fact]
    public void A_penalty_is_marked_as_one_whichever_way_it_went()
    {
        var segments = GamecastTelemetry.DriveSegments(
            [Play(50, 45, penalty: true), Play(45, 50, penalty: true, sequence: 2)], homeOffence: false);

        Assert.All(segments, s => Assert.Equal(DriveSegmentKind.Penalty, s.Kind));
    }

    [Fact]
    public void A_play_for_no_gain_still_leaves_a_mark()
    {
        var segment = Assert.Single(GamecastTelemetry.DriveSegments([Play(50, 50)], homeOffence: false));

        Assert.Equal(DriveSegmentKind.NoGain, segment.Kind);
        Assert.Equal(GamecastTelemetry.MinSegment, segment.Width);
    }

    [Fact]
    public void A_play_missing_either_end_is_skipped_as_are_timeouts_and_kickoffs()
    {
        var segments = GamecastTelemetry.DriveSegments(
        [
            Play(null, 40),
            Play(40, null),
            Play(40, 40, type: "Official Timeout"),
            Play(65, 75, type: "Kickoff"),
            Play(75, 75, type: "Two-minute warning"),
            Play(75, 75, type: "End Period"),
            Play(75, 70),
        ], homeOffence: false);

        var segment = Assert.Single(segments);
        Assert.Equal(35, segment.From);
    }

    /// <summary>From the away 45 (55 out, x 55) to the home 40 (x 70): across the fifty at x 60.</summary>
    [Fact]
    public void A_drive_crosses_midfield()
    {
        var segments = GamecastTelemetry.DriveSegments(
            [Play(75, 55), Play(55, 40, sequence: 2)], homeOffence: false);

        Assert.Equal(2, segments.Count);
        Assert.Equal((35d, 55d), (segments[0].From, segments[0].To));
        Assert.Equal((55d, 70d), (segments[1].From, segments[1].To));
        Assert.True(segments[1].From < GamecastTelemetry.Midfield && segments[1].To > GamecastTelemetry.Midfield);
        Assert.False(segments[0].Latest);
        Assert.True(segments[1].Latest);
    }

    [Fact]
    public void A_score_and_a_turnover_are_what_they_were_and_ends_are_clamped_to_the_field()
    {
        var segments = GamecastTelemetry.DriveSegments(
            [Play(8, -3, scoring: true), Play(30, 32, turnover: true, sequence: 2)], homeOffence: false);

        Assert.Equal(DriveSegmentKind.Score, segments[0].Kind);
        Assert.Equal(110, segments[0].To);
        Assert.Equal(DriveSegmentKind.Turnover, segments[1].Kind);
    }

    [Fact]
    public void Each_play_has_its_own_lane_inside_the_band()
    {
        var plays = Enumerable.Range(0, 12).Select(i => Play(75 - i * 3, 72 - i * 3, sequence: i)).ToList();
        var segments = GamecastTelemetry.DriveSegments(plays, homeOffence: false);

        Assert.Equal(12, segments.Count);
        for (var i = 1; i < segments.Count; i++)
            Assert.True(segments[i].Top >= segments[i - 1].Top + segments[i - 1].Thickness);

        Assert.True(segments[0].Top >= GamecastTelemetry.BandTop);
        Assert.True(segments[^1].Top + segments[^1].Thickness <= GamecastTelemetry.BandBottom + 1e-9);
    }

    [Fact]
    public void No_plays_is_no_segments()
    {
        Assert.Empty(GamecastTelemetry.DriveSegments([], homeOffence: true));
        Assert.Empty(GamecastTelemetry.DriveSegments(null, homeOffence: true));
    }

    [Fact]
    public void The_band_runs_from_the_drive_s_start_to_the_ball()
    {
        Assert.Equal(new DriveBand(35, 20), GamecastTelemetry.Band(75, 55));
        Assert.Equal(new DriveBand(80, GamecastTelemetry.MinSegment), GamecastTelemetry.Band(30, 30));
        Assert.Null(GamecastTelemetry.Band(null, 55));
        Assert.Null(GamecastTelemetry.Band(75, null));
    }

    [Fact]
    public void The_yard_numbers_read_ten_to_fifty_and_back()
    {
        Assert.Equal(new[] { 10, 20, 30, 40, 50, 40, 30, 20, 10 }, GamecastTelemetry.YardNumbers.Select(n => n.Number));
        Assert.Equal(60, GamecastTelemetry.YardNumbers[4].X);
        Assert.Equal(10, GamecastTelemetry.FiveYardTicks.Count);
        Assert.Equal(15, GamecastTelemetry.FiveYardTicks[0]);
    }

    // ---- The comparison --------------------------------------------------------------------

    private static GamecastDrive Drive(int? team, int? yards, string? elapsed, bool score = false, int sequence = 1)
        => FakeGamecastData.Drive(sequence, 1, 2, current: false, yards: yards, elapsed: elapsed, score: score) with { TeamId = team };

    private static GamecastHeader Header => FakeGamecastData.Live();

    [Fact]
    public void Time_of_possession_sums_each_side_s_drives_and_skips_a_time_that_does_not_read()
    {
        var rows = GamecastTelemetry.Compare(Header,
        [
            Drive(Away, 75, "3:48"), Drive(Away, 10, "10:45"), Drive(Away, 5, "garbage"), Drive(Away, 5, "1:75"),
            Drive(Home, 20, "0:59"), Drive(Home, 0, null),
        ], [], null);

        var top = rows.Single(r => r.Label == "Time of possession");
        Assert.Equal("14:33", top.AwayText);
        Assert.Equal("0:59", top.HomeText);
        Assert.Equal(100, top.AwayShare + top.HomeShare, 6);
        Assert.True(top.AwayShare > top.HomeShare);
    }

    [Fact]
    public void Seconds_reads_m_ss_and_nothing_else()
    {
        Assert.Equal(228, GamecastTelemetry.Seconds("3:48"));
        Assert.Equal(0, GamecastTelemetry.Seconds("0:00"));
        Assert.Null(GamecastTelemetry.Seconds("3:5"));
        Assert.Null(GamecastTelemetry.Seconds("3:60"));
        Assert.Null(GamecastTelemetry.Seconds("3.48"));
        Assert.Null(GamecastTelemetry.Seconds(""));
        Assert.Null(GamecastTelemetry.Seconds(null));
    }

    [Fact]
    public void The_rows_come_in_order_and_each_side_is_its_own_team()
    {
        var rows = GamecastTelemetry.Compare(Header,
            [Drive(Away, 75, "3:48", score: true), Drive(Home, -4, "1:12"), Drive(Away, 30, "2:00")],
            [Play(50, 45, turnover: true), Play(40, 45, Home, turnover: true), Play(40, 45, Home, turnover: true), Play(30, 35, Home)],
            Box(away: [Line(false, ("passing.YDS", "142"))], home: [Line(true, ("passing.YDS", "208"))]));

        Assert.Equal(
            new[] { "Time of possession", "Total yards", "Passing yards", "Drives", "Scoring drives", "Turnovers" },
            rows.Select(r => r.Label));

        var yards = rows.Single(r => r.Label == "Total yards");
        Assert.Equal(("105", "-4"), (yards.AwayText, yards.HomeText));
        // A side that has lost ground counts as none toward the bar; its figure still says so.
        Assert.Equal((100d, 0d), (yards.AwayShare, yards.HomeShare));

        Assert.Equal(("2", "1"), Texts(rows, "Drives"));
        Assert.Equal(("1", "0"), Texts(rows, "Scoring drives"));

        var turnovers = rows.Single(r => r.Label == "Turnovers");
        Assert.Equal(("1", "2"), (turnovers.AwayText, turnovers.HomeText));
        Assert.False(turnovers.HigherIsBetter);
        Assert.True(yards.HigherIsBetter);
    }

    [Fact]
    public void Passing_and_rushing_sum_the_box_score_s_qualified_keys_per_side()
    {
        var box = Box(
            away: [Line(false, ("passing.YDS", "289"), ("rushing.YDS", "17")), Line(false, ("rushing.YDS", "94")), Line(false, ("receiving.YDS", "120"))],
            home: [Line(true, ("passing.YDS", "150")), Line(true, ("rushing.YDS", "-2")), Line(true, ("rushing.YDS", "60"))]);

        var rows = GamecastTelemetry.Compare(Header, [], [], box);

        Assert.Equal(("289", "150"), Texts(rows, "Passing yards"));
        Assert.Equal(("111", "58"), Texts(rows, "Rushing yards"));
        Assert.Equal(new[] { "Passing yards", "Rushing yards" }, rows.Select(r => r.Label));
    }

    [Fact]
    public void A_box_score_row_one_side_lacks_or_cannot_read_is_left_out()
    {
        var oneSided = Box(away: [Line(false, ("passing.YDS", "142"))], home: [Line(true, ("CAR", "4"))]);
        Assert.Empty(GamecastTelemetry.Compare(Header, [], [], oneSided));

        var unreadable = Box(away: [Line(false, ("rushing.YDS", "12"))], home: [Line(true, ("rushing.YDS", "--"))]);
        Assert.Empty(GamecastTelemetry.Compare(Header, [], [], unreadable));

        // A bare YDS is not a passing or a rushing figure: the box-score read never hands one over for the NFL.
        var bare = Box(away: [Line(false, ("YDS", "142"))], home: [Line(true, ("YDS", "99"))]);
        Assert.Empty(GamecastTelemetry.Compare(Header, [], [], bare));
    }

    [Fact]
    public void Nothing_the_data_cannot_say_is_returned()
    {
        Assert.Empty(GamecastTelemetry.Compare(Header, [], [], null));
        Assert.Empty(GamecastTelemetry.Compare(Header, null, null, null));

        // Drives nobody can be charged with, and plays with no side, say nothing.
        Assert.Empty(GamecastTelemetry.Compare(Header, [Drive(null, 40, "3:00"), Drive(99, 40, "3:00")], [Play(40, 45, team: null)], null));

        // Drives with no yards and no readable time still count as drives.
        var rows = GamecastTelemetry.Compare(Header, [Drive(Away, null, "x"), Drive(Home, null, null)], [], null);
        Assert.Equal(new[] { "Drives", "Scoring drives" }, rows.Select(r => r.Label));
    }

    [Fact]
    public void Penalties_are_never_compared()
    {
        var rows = GamecastTelemetry.Compare(Header, [], [Play(40, 45, penalty: true), Play(40, 35, Home, penalty: true)], null);

        Assert.DoesNotContain(rows, r => r.Label.Contains("Penalt", StringComparison.Ordinal));
    }

    [Fact]
    public void Shares_never_split_nothing_down_the_middle()
    {
        Assert.Equal((0d, 0d), GamecastTelemetry.Shares(0, 0));
        Assert.Equal((25d, 75d), GamecastTelemetry.Shares(1, 3));
        Assert.Equal((100d, 0d), GamecastTelemetry.Shares(5, 0));

        var rows = GamecastTelemetry.Compare(Header, [Drive(Away, 10, "1:00"), Drive(Home, 10, "1:00")], [Play(40, 45)], null);
        var turnovers = rows.Single(r => r.Label == "Turnovers");
        Assert.Equal(("0", "0"), (turnovers.AwayText, turnovers.HomeText));
        Assert.Equal((0d, 0d), (turnovers.AwayShare, turnovers.HomeShare));
    }

    private static (string, string) Texts(IReadOnlyList<ComparisonRow> rows, string label)
    {
        var row = rows.Single(r => r.Label == label);
        return (row.AwayText, row.HomeText);
    }

    private static BoxScoreLine Line(bool home, params (string Key, string Value)[] stats)
        => new(1, "Player", null, home, stats.ToDictionary(s => s.Key, s => s.Value));

    private static GameBoxScore Box(IReadOnlyList<BoxScoreLine> away, IReadOnlyList<BoxScoreLine> home)
        => new(new Game { Id = BoardRows.GameId }, home, away);
}
