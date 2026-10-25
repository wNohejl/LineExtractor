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
}
