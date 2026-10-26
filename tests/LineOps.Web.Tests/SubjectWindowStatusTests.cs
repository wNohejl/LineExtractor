using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Web.Components;
using LineOps.Web.Components.Panels;

namespace LineOps.Web.Tests;

/// <summary>
/// What a subject window's bar says after its title. The title already names the game or the
/// team; the status repeating it read "Kansas City Chiefs  Kansas City Chiefs". It says the
/// subject's state instead — where the game stands, what the team's record is.
/// </summary>
public class SubjectWindowStatusTests
{
    private sealed class Probe : PanelBase
    {
        public static string Of(Game game) => StateStatus(game);

        public static string Of(IReadOnlyList<TeamGameLogRow> season, string? seasonLabel)
            => StateStatus(season, seasonLabel);

        public static string Named(Game game) => Matchup(game);
    }

    private static Game MetsAtBraves(GameStatus status, int? away, int? home) => new()
    {
        AwayTeam = new Team { Name = "New York Mets", Abbrev = "NYM" },
        HomeTeam = new Team { Name = "Atlanta Braves", Abbrev = "ATL" },
        Status = status,
        AwayScore = away,
        HomeScore = home,
        StartsAt = new DateTimeOffset(2026, 6, 12, 23, 10, 0, TimeSpan.Zero)
    };

    [Fact]
    public void Game_status_is_state_not_the_matchup()
    {
        var game = MetsAtBraves(GameStatus.Final, 4, 2);

        var status = Probe.Of(game);

        Assert.Equal("Final · NYM 4–2 ATL", status);
        Assert.DoesNotContain(Probe.Named(game), status);
    }

    [Fact]
    public void A_live_game_says_live_and_the_score()
        => Assert.Equal("Live · NYM 1–0 ATL", Probe.Of(MetsAtBraves(GameStatus.Live, 1, 0)));

    [Fact]
    public void A_game_not_yet_played_says_when_it_starts()
    {
        // ESPN writes zeroes for an unplayed fixture; they are not a 0–0 result.
        var game = MetsAtBraves(GameStatus.Scheduled, 0, 0);

        Assert.Equal(DisplayTime.Stamp(game.StartsAt), Probe.Of(game));
    }

    [Fact]
    public void Team_status_is_the_record()
    {
        var season = Enumerable.Range(1, 5)
            .Select(i => new TeamGameLogRow(i, DateTimeOffset.UnixEpoch.AddDays(i), "ATL", true,
                5, 1, GameStatus.Final, null, null, null, null))
            .Append(new TeamGameLogRow(6, DateTimeOffset.UnixEpoch.AddDays(6), "ATL", true,
                0, 0, GameStatus.Scheduled, null, null, null, null))
            .ToList();

        Assert.Equal("5-0 · 2026", Probe.Of(season, "2026"));
    }

    [Fact]
    public void A_tied_final_reads_as_a_tie_not_a_loss()
    {
        // The window's log draws a level final as T; the bar above it must not call it a loss.
        var season = Enumerable.Range(1, 5)
            .Select(i => new TeamGameLogRow(i, DateTimeOffset.UnixEpoch.AddDays(i), "KC", true,
                24, 17, GameStatus.Final, null, null, null, null))
            .Append(new TeamGameLogRow(6, DateTimeOffset.UnixEpoch.AddDays(6), "KC", true,
                20, 20, GameStatus.Final, null, null, null, null))
            .ToList();

        Assert.Equal("5-0-1 · 2026", Probe.Of(season, "2026"));
    }

    /// <summary>
    /// A meeting between these sides, from the home side's point of view, graded as the service
    /// grades it: won is for &gt; against, so a level final arrives as "not won".
    /// </summary>
    private static HeadToHeadRow Meeting(int id, int home, int away, GameStatus status = GameStatus.Final)
        => new(id, DateTimeOffset.UnixEpoch.AddDays(id), "Kansas City Chiefs", "Denver Broncos",
            home, away, status, SubjectWasHome: true,
            SubjectWon: status == GameStatus.Final ? home > away : null,
            ClosingSpread: null, AtsResult: null, ClosingTotal: null, TotalResult: null);

    [Fact]
    public void A_tied_meeting_reads_as_a_tie_not_a_loss()
    {
        // The grid draws the level final as T; the record above it must not call it a loss.
        IReadOnlyList<HeadToHeadRow> meetings =
        [
            Meeting(1, 24, 17),
            Meeting(2, 20, 20),
            Meeting(3, 10, 13),
            Meeting(4, 0, 0, GameStatus.Scheduled)
        ];

        Assert.Equal("1-1-1", HeadToHeadPanel.Record(meetings));
    }

    [Fact]
    public void A_meeting_record_without_ties_keeps_two_numbers()
        => Assert.Equal("2-1", HeadToHeadPanel.Record([Meeting(1, 3, 1), Meeting(2, 5, 4), Meeting(3, 2, 6)]));
}
