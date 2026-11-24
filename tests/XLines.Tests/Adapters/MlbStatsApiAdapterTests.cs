using System.Text.Json;
using XLines.Core.Contracts;
using XLines.Ingestion.Adapters;
using XLines.Tests.Support;

namespace XLines.Tests.Adapters;

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
    private static List<MlbScheduledGame> Parse() => MlbStatsApiAdapter.Parse(Fixtures.Json("mlb.schedule.json")).ToList();

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

    // --- The game's circumstances, from the hydrated schedule --------------------------------
    //
    // The fixture is the same call with lineups, weather, officials and venue(fieldInfo,location)
    // hydrated, trimmed from real payloads: 17 July 2026 (an open park by day, a retractable roof
    // closed, one open, and a postponed game), a dome game from 24 July 2026, and a game on
    // 1 April 2027, a season ahead, with nothing posted yet.

    private static List<MlbScheduledGame> ParseHydrated()
    {
        return MlbStatsApiAdapter.Parse(Fixtures.Json("mlb.schedule.hydrated.json")).ToList();
    }

    [Fact]
    public void TheScheduleAsksForTheVenuesFieldAndLocationByName()
    {
        // A bare 'venue' hydrates the id and the name and nothing else: the roof, the turf and the
        // city are only there when fieldInfo and location are asked for.
        Assert.Equal("probablePitcher,team,lineups,weather,officials,venue(fieldInfo,location)", MlbStatsApiAdapter.Hydrate);
    }

    [Fact]
    public void AGameAlreadyPlayedCarriesEverythingTheScheduleStates()
    {
        var context = ParseHydrated().Single(g => g.GamePk == 824766).Context;

        Assert.NotNull(context);
        Assert.Equal("824766", context!.SourceGameId);
        Assert.Equal("Fenway Park", context.VenueName);
        Assert.Equal("Boston", context.VenueCity);
        Assert.Equal("MA", context.VenueState);
        Assert.False(context.Indoor);
        Assert.Equal(GameSurfaces.Grass, context.Surface);
        Assert.Equal("Sunny", context.WeatherCondition);
        Assert.Equal(82, context.TemperatureF);
        Assert.Equal(10, context.WindSpeedMph);
        Assert.Equal("In From LF", context.WindDirection);
        Assert.Equal(GameDayNight.Day, context.DayNight);

        // The schedule states neither, so neither is invented.
        Assert.Null(context.Attendance);
        Assert.Null(context.PrecipitationPercent);
        Assert.Null(context.HomeWinPercent);
    }

    [Fact]
    public void TheUmpiresArriveWithTheBaseEachWorked()
    {
        var officials = ParseHydrated().Single(g => g.GamePk == 824766).Context!.Officials!;

        // In MLB's order, which is not the crew's: first base before home plate. The role is what
        // says who called balls and strikes.
        Assert.Equal(
            [
                new CanonicalOfficial("Louie Krupa", "First Base"),
                new CanonicalOfficial("Mike Estabrook", "Home Plate"),
                new CanonicalOfficial("Malachi Moore", "Second Base"),
                new CanonicalOfficial("Alfonso Márquez", "Third Base")
            ],
            officials);
    }

    [Fact]
    public void ABattingOrderIsReadLeadoffFirstWithTheDaysPositions()
    {
        var context = ParseHydrated().Single(g => g.GamePk == 824766).Context!;

        Assert.Equal(9, context.HomeLineup!.Count);
        Assert.Equal(9, context.AwayLineup!.Count);
        Assert.Equal(Enumerable.Range(1, 9), context.HomeLineup.Select(s => s.BattingOrder));

        Assert.Equal(new CanonicalLineupSlot(1, "678011", "Anthony Seigler", "2B"), context.HomeLineup[0]);
        Assert.Equal(new CanonicalLineupSlot(9, "691907", "Tsung-Che Cheng", "SS"), context.HomeLineup[8]);
        Assert.Equal(new CanonicalLineupSlot(1, "650490", "Yandy Díaz", "DH"), context.AwayLineup[0]);

        // The position is the one batted at in this game: Yoshida is the day's DH.
        Assert.Equal(new CanonicalLineupSlot(5, "807799", "Masataka Yoshida", "DH"), context.HomeLineup[4]);
    }

    [Fact]
    public void ADomeIsIndoors()
    {
        var context = ParseHydrated().Single(g => g.GamePk == 822952).Context!;

        Assert.Equal("Tropicana Field", context.VenueName);
        Assert.True(context.Indoor);
        Assert.Equal(GameSurfaces.Turf, context.Surface);
        Assert.Equal(0, context.WindSpeedMph);
        Assert.Null(context.WindDirection);
    }

    [Fact]
    public void ARetractableRoofIsIndoorsOnlyWhenMlbSaysItWasClosed()
    {
        var games = ParseHydrated();
        var closed = games.Single(g => g.GamePk == 822789).Context!;
        var open = games.Single(g => g.GamePk == 823115).Context!;

        // Rogers Centre: MLB's weather reads "Roof Closed", which is the roof's state stated.
        Assert.Equal("Rogers Centre", closed.VenueName);
        Assert.True(closed.Indoor);
        Assert.Equal("Roof Closed", closed.WeatherCondition);
        Assert.Equal(GameSurfaces.Turf, closed.Surface);

        // T-Mobile Park under a clear sky: probably open, never said to be, so not stated.
        Assert.Equal("T-Mobile Park", open.VenueName);
        Assert.Null(open.Indoor);
        Assert.Equal("Clear", open.WeatherCondition);
        Assert.Equal(GameDayNight.Night, open.DayNight);
    }

    [Fact]
    public void APostponedGameHasItsCrewAndWeatherButNoLineup()
    {
        var context = ParseHydrated().Single(g => g.GamePk == 824414).Context!;

        Assert.Equal(4, context.Officials!.Count);
        Assert.Equal(89, context.TemperatureF);
        Assert.Null(context.HomeLineup);
        Assert.Null(context.AwayLineup);
    }

    [Fact]
    public void AGameASeasonAheadHoldsOnlyWhatIsAlreadyKnown()
    {
        var game = ParseHydrated().Single(g => g.GamePk == 854284);
        var context = game.Context!;

        // The venue and the start are set; the weather object is empty, the crew list is empty and
        // no lineup is posted. Each is null — not an empty list, not a default.
        Assert.Equal("Oriole Park at Camden Yards", context.VenueName);
        Assert.Equal(GameDayNight.Day, context.DayNight);
        Assert.False(context.Indoor);
        Assert.Null(context.WeatherCondition);
        Assert.Null(context.TemperatureF);
        Assert.Null(context.WindSpeedMph);
        Assert.Null(context.WindDirection);
        Assert.Null(context.Officials);
        Assert.Null(context.HomeLineup);
        Assert.Null(context.AwayLineup);
        Assert.Null(game.Home.ProbablePitcher);
    }

    [Fact]
    public void AGameThatStatesNothingHasNoContext()
    {
        var game = JsonDocument.Parse("""
            {"gamePk": 1, "weather": {}, "officials": [], "venue": {"id": 3}}
            """).RootElement;

        Assert.Null(MlbStatsApiAdapter.ReadContext(game, 1));
    }

    [Fact]
    public void TheHydratedScheduleLeavesProbablesAndDoubleheadersAsTheyWere()
    {
        var games = ParseHydrated();
        var fenway = games.Single(g => g.GamePk == 824766);

        Assert.Equal(6, games.Count);
        Assert.Equal("Jake Bennett", fenway.Home.ProbablePitcher!.FullName);
        Assert.Equal(687562, fenway.Home.ProbablePitcher.PlayerId);
        Assert.Equal("Griffin Jax", fenway.Away.ProbablePitcher!.FullName);
        Assert.True(fenway.IsDoubleHeader);
        Assert.Equal(1, fenway.GameNumber);
        Assert.Equal("Postponed", games.Single(g => g.GamePk == 824414).DetailedState);
    }

    [Fact]
    public void TheOriginalFixtureStillParsesWithoutAContextItNeverHydrated()
    {
        // The unhydrated payload states no venue, weather, crew, lineup or day and night, so there
        // is nothing to carry — and nothing is made up to fill the space.
        var games = Parse();

        Assert.NotEmpty(games);
        Assert.All(games, g => Assert.Null(g.Context));
    }

    [Theory]
    [InlineData("8 mph, Out To LF", 8, "Out To LF")]
    [InlineData("0 mph, None", 0, null)]
    [InlineData("0 mph, Calm", 0, "Calm")]
    [InlineData("5 mph, Varies", 5, "Varies")]
    [InlineData("  12mph ,   L   To  R ", 12, "L To R")]
    [InlineData("14 MPH, In From CF", 14, "In From CF")]
    [InlineData("7 mph", 7, null)]
    [InlineData("7 mph,", 7, null)]
    [InlineData("Out To RF", null, "Out To RF")]
    [InlineData("None", null, null)]
    [InlineData("", null, null)]
    [InlineData(null, null, null)]
    public void WindIsASpeedAndADirectionInMlbsWords(string? wind, int? speed, string? direction)
    {
        var parsed = MlbStatsApiAdapter.ParseWind(wind);

        Assert.Equal(speed, parsed.SpeedMph);
        Assert.Equal(direction, parsed.Direction);
    }

    [Theory]
    [InlineData("Dome", "Dome", true)]
    [InlineData("Open", "Sunny", false)]
    [InlineData("Retractable", "Roof Closed", true)]
    [InlineData("Retractable", "Partly Cloudy", null)]
    [InlineData("Retractable", null, null)]
    [InlineData(null, "Roof Closed", null)]
    public void IndoorIsOnlyWhatTheRoofAndTheWeatherSay(string? roof, string? condition, bool? indoor)
        => Assert.Equal(indoor, MlbStatsApiAdapter.ReadIndoor(roof, condition));
}
