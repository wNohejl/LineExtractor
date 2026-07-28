using System.Text.Json;
using LineOps.Ingestion.Adapters;

namespace LineOps.Tests.Adapters;

/// <summary>
/// The entity spine: MLB's own ids, and which games count.
///
/// Every other source names a game differently, so resolution across them has been team name
/// plus start time with a tolerance. That works until the two cases where it cannot: a
/// doubleheader is two games between the same teams on the same day, and an exhibition looks
/// exactly like a game that counted. MLB answers both in the schedule rather than leaving them
/// to be inferred.
///
/// The fixture is a real statsapi payload, trimmed, with an exhibition and a second game of a
/// doubleheader appended.
/// </summary>
public class MlbStatsApiAdapterTests
{
    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "mlb.schedule.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    private static List<MlbScheduledGame> Parse() => MlbStatsApiAdapter.Parse(Load()).ToList();

    [Fact]
    public void EveryGameCarriesMlbsOwnIdentity()
    {
        var game = Parse().Single(g => g.GamePk == 822868);

        // gamePk, and MLBAM team ids. These are the keys the sport itself runs on, so a source
        // that resolves into them once never has to be fuzzy-matched again.
        Assert.Equal("Texas Rangers", game.Home.Name);
        Assert.Equal("Seattle Mariners", game.Away.Name);
        Assert.True(game.Home.TeamId > 0);
        Assert.True(game.Away.TeamId > 0);
    }

    [Fact]
    public void TheAnnouncedStarterArrivesWithTheirPlayerId()
    {
        var game = Parse().Single(g => g.GamePk == 822868);

        // A scratch invalidates the whole game's pricing and every pitcher prop on it, and it is
        // knowable here hours before a book takes the market down.
        Assert.NotNull(game.Home.ProbablePitcher);
        Assert.Equal("Kumar Rocker", game.Home.ProbablePitcher!.FullName);
        Assert.Equal(677958, game.Home.ProbablePitcher.PlayerId);
    }

    [Fact]
    public void ADoubleHeaderIsTwoGamesTheScheduleCanTellApart()
    {
        var games = Parse();
        var pair = games.Where(g => g.Home.TeamId == games.Single(x => x.GamePk == 822868).Home.TeamId).ToList();

        // Same teams, same day. Name-and-date matching resolves both onto one game and silently
        // merges two slates' worth of prices; the game number is what makes them distinct, and
        // it is stated by the schedule rather than guessed at.
        Assert.Equal(2, pair.Count);
        Assert.Distinct(pair.Select(g => g.GamePk));
        Assert.Contains(pair, g => g.GameNumber == 2 && g.IsDoubleHeader);
    }

    [Theory]
    [InlineData(999001, "S")] // spring training
    [InlineData(999002, "A")] // the All-Star Game
    public void AnExhibitionIsMarkedAsNotCounting(long gamePk, string expectedType)
    {
        var game = Parse().Single(g => g.GamePk == gamePk);

        Assert.Equal(expectedType, game.GameType);

        // The two that would otherwise put results in a team's record and at-bats in a player's
        // form from games nobody counts. ESPN needs two separate signals to tell these apart —
        // a season type and a competition type — and MLB says it in one field.
        Assert.False(game.Counts);
    }

    [Fact]
    public void TheRegularSeasonCounts()
    {
        var games = Parse().Where(g => g.GameType == "R").ToList();

        Assert.NotEmpty(games);
        Assert.All(games, g => Assert.True(g.Counts));
    }

    [Fact]
    public void AnUnfamiliarPostseasonCodeIsKeptRatherThanDropped()
    {
        // Rejecting named exhibition types rather than accepting known good ones. The costs are
        // not symmetric: an exhibition let through is a row that can be found and removed, while
        // a whitelist silently loses every game of a round nobody thought to list.
        var wildCard = new MlbScheduledGame(
            GamePk: 1, StartsAt: DateTimeOffset.UtcNow, GameType: "F", GameNumber: 1,
            DoubleHeader: "N", DetailedState: "Scheduled",
            Home: new MlbSide(1, "Home", null, null, null),
            Away: new MlbSide(2, "Away", null, null, null));

        Assert.True(wildCard.Counts);
    }
}
