using System.Text.Json;
using LineOps.Ingestion.Adapters;

namespace LineOps.Tests.Adapters;

/// <summary>
/// The ESPN port's boundary: which leagues it can address, and what identity it hands the
/// resolver for the teams it finds.
/// </summary>
public class EspnPortTests
{
    private static JsonElement Load(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    [Theory]
    [InlineData("mlb", "baseball/mlb")]
    [InlineData("nfl", "football/nfl")]
    [InlineData("nhl", "hockey/nhl")]
    [InlineData("MLB", "baseball/mlb")] // config casing should not decide whether a league works
    public void PathFor_MapsAKnownLeague(string sportKey, string expected)
        => Assert.Equal(expected, EspnLeagues.PathFor(sportKey));

    [Fact]
    public void PathFor_ReturnsNullForAnUnmappedLeague()
    {
        // Null rather than throwing: an unmapped sport is one to skip, not a reason to fail the
        // backfill day that asked for it. The old switch threw NotSupportedException here.
        Assert.Null(EspnLeagues.PathFor("kabaddi"));
        Assert.False(EspnLeagues.Supports("kabaddi"));
    }

    [Fact]
    public void ParseScoreboard_CarriesTheProvidersTeamIdAndOfficialAbbreviation()
    {
        var games = EspnStatsAdapter.ParseScoreboard(Load("espn.scoreboard.json"), "mlb").ToList();
        var game = games.Single(g => g.SourceGameId == "401816200");

        Assert.Equal("Detroit Tigers", game.HomeTeamName);
        Assert.Equal("Kansas City Royals", game.AwayTeamName);

        // The identity that makes resolution exact and stops abbreviations being invented.
        Assert.Equal("6", game.Home!.SourceTeamId);
        Assert.Equal("DET", game.Home.Abbrev);
        Assert.Equal("7", game.Away!.SourceTeamId);
        Assert.Equal("KC", game.Away.Abbrev);

        Assert.Equal("final", game.Status);
        Assert.Equal(2, game.HomeScore);
        Assert.Equal(3, game.AwayScore);
    }

    [Fact]
    public void ParseScoreboard_StillResolvesATeamWithNoIdOrAbbreviation()
    {
        var games = EspnStatsAdapter.ParseScoreboard(Load("espn.scoreboard.json"), "mlb").ToList();
        var game = games.Single(g => g.SourceGameId == "401816201");

        // A leaner provider gives a name and nothing else; the name is the one field that
        // resolution cannot do without, and it is enough.
        Assert.Equal("San Francisco Giants", game.Home!.Name);
        Assert.Null(game.Home.SourceTeamId);
        Assert.Null(game.Home.Abbrev);

        Assert.Equal("COL", game.Away!.Abbrev);
        Assert.Equal("scheduled", game.Status);
    }

    [Fact]
    public void ParseScoreboard_DropsAnEventWhoseTeamCannotBeNamed()
    {
        var games = EspnStatsAdapter.ParseScoreboard(Load("espn.scoreboard.json"), "mlb").ToList();

        // Half a matchup is worse than none: it would resolve to a game with one real team and
        // one invented one, and then quietly collect odds and stats against it.
        Assert.DoesNotContain(games, g => g.SourceGameId == "401816202");
    }

    [Fact]
    public void ParseScoreboard_DropsSpringTrainingButKeepsOpeningDay()
    {
        var games = EspnStatsAdapter.ParseScoreboard(Load("espn.scoreboard.json"), "mlb").ToList();

        // 24 March 2026 is spring training and 25 March is opening day. Both come back from the
        // same query, and ingesting the first puts a loss on Tampa Bay's record from a game that
        // never counted and a stat line in a player's form from an inning nobody recorded.
        Assert.DoesNotContain(games, g => g.SourceGameId == "401816203");
        Assert.Contains(games, g => g.SourceGameId == "401816204");
    }

    [Fact]
    public void ParseScoreboard_KeepsAnEventThatDeclaresNoSeasonTypeAtAll()
    {
        var games = EspnStatsAdapter.ParseScoreboard(Load("espn.scoreboard.json"), "mlb").ToList();

        // Absence of the field is not evidence of an exhibition. Excluding on a missing value
        // would silently drop every ordinary game, which is the more expensive mistake — the
        // filter has to name preseason to reject it.
        Assert.Contains(games, g => g.SourceGameId == "401816200");
        Assert.Equal(3, games.Count);
    }

    [Fact]
    public void ParseScoreboard_DropsTheAllStarGameDespiteItsRegularSeasonLabel()
    {
        var games = EspnStatsAdapter.ParseScoreboard(Load("espn.scoreboard.json"), "mlb").ToList();

        // Reported as season type 2 on an ordinary July date, so the preseason check waves it
        // through. Left in, it resolves normally and invents two franchises to hold itself —
        // "American All-Stars" and "National All-Stars", each with a one-game record, sitting in
        // the team list beside the thirty real ones.
        Assert.DoesNotContain(games, g => g.SourceGameId == "401816205");
        Assert.DoesNotContain(games, g => g.Home?.Name.Contains("All-Stars") == true);
    }
}
