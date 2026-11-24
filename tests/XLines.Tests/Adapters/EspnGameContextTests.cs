using System.Text.Json;
using XLines.Core.Contracts;
using XLines.Ingestion.Adapters;
using XLines.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace XLines.Tests.Adapters;

/// <summary>
/// The circumstances of a game — venue, surface, weather, crowd, officials and ESPN's predictor —
/// read from the summary the box-score walk already fetches.
///
/// <para>
/// Every payload is a real one trimmed to what is read: Braves at Cubs (401816950, 15 September
/// 2026, final), Dolphins at Bills (401873070, read before kickoff on 21 November 2026), Bucks at
/// 76ers (401910023, read before tip-off the same day) and an NBA final (401909313, 5 November
/// 2026). A final has dropped the weather and the predictor; a scheduled game has neither the
/// crowd nor the officials yet.
/// </para>
/// </summary>
public class EspnGameContextTests
{
    private const string MlbFinal = """
    {
      "gameInfo": {
        "venue": {
          "id": "16",
          "fullName": "Wrigley Field",
          "shortName": "Wrigley Field",
          "address": { "city": "Chicago", "state": "Illinois", "zipCode": "60613" }
        },
        "attendance": 38516,
        "officials": [
          { "displayName": "Quinn Wolcott", "position": { "name": "Home Plate Umpire", "displayName": "Home Plate Umpire", "id": "206" }, "order": 1 },
          { "displayName": "Nate  Tomlinson", "position": { "name": "First Base Umpire", "displayName": "First Base Umpire", "id": "207" }, "order": 2 },
          { "displayName": "Ramon De Jesus", "position": { "name": "Second Base Umpire", "displayName": "Second Base Umpire", "id": "208" }, "order": 3 },
          { "displayName": "Paul  Clemons", "position": { "name": "Third Base Umpire", "displayName": "Third Base Umpire", "id": "209" }, "order": 4 }
        ],
        "gameDuration": "2:43"
      }
    }
    """;

    private const string NflPreGame = """
    {
      "gameInfo": {
        "venue": {
          "id": "11938",
          "fullName": "Highmark Stadium",
          "address": { "city": "Orchard Park", "state": "NY", "zipCode": "14127", "country": "USA" },
          "grass": true,
          "images": []
        },
        "weather": { "temperature": 65, "highTemperature": 65, "lowTemperature": 45, "conditionId": "3", "precipitation": 3 }
      },
      "predictor": {
        "header": "Matchup Predictor",
        "homeTeam": { "id": "2", "gameProjection": "94.8" },
        "awayTeam": { "id": "15", "gameProjection": "5.1" }
      }
    }
    """;

    private const string NbaPreGame = """
    {
      "gameInfo": {
        "venue": {
          "id": "1845",
          "fullName": "Xfinity Mobile Arena",
          "shortName": "Wells Fargo Center",
          "address": { "city": "Philadelphia", "state": "PA" },
          "grass": false
        }
      },
      "predictor": {
        "header": "Matchup Predictor",
        "homeTeam": { "id": "20", "gameProjection": "86.2", "teamChanceLoss": "13.8" },
        "awayTeam": { "id": "15", "gameProjection": "13.8", "teamChanceLoss": "86.2" }
      }
    }
    """;

    private const string NbaFinal = """
    {
      "gameInfo": {
        "venue": {
          "id": "1841",
          "fullName": "crypto.com Arena",
          "shortName": "Staples Center",
          "address": { "city": "Los Angeles", "state": "CA" },
          "grass": false
        },
        "attendance": 19057,
        "officials": [
          { "fullName": "John Goble", "displayName": "John Goble", "position": { "name": "Referee", "displayName": "Referee", "id": "40" }, "order": 1 },
          { "fullName": "Ashley Moyer-Gleich", "displayName": "Ashley Moyer-Gleich", "position": { "name": "Referee", "displayName": "Referee", "id": "40" }, "order": 2 },
          { "fullName": "Robert Hussey", "displayName": "Robert Hussey", "position": { "name": "Referee", "displayName": "Referee", "id": "40" }, "order": 3 }
        ]
      }
    }
    """;

    private static CanonicalGameContext? Parse(string json, string sport, string gameId)
    {
        using var doc = JsonDocument.Parse(json);

        return EspnStatsAdapter.ParseGameContext(doc.RootElement, sport, gameId);
    }

    [Fact]
    public void A_baseball_final_states_its_venue_crowd_and_umpires()
    {
        var context = Parse(MlbFinal, "mlb", "401816950");

        Assert.NotNull(context);
        Assert.Equal("401816950", context.SourceGameId);
        Assert.Equal("Wrigley Field", context.VenueName);
        Assert.Equal("Chicago", context.VenueCity);
        Assert.Equal("Illinois", context.VenueState);
        Assert.Equal(38516, context.Attendance);

        // MLB names an umpire by display name alone, some with a double space ESPN typed.
        Assert.Equal(
            [
                new CanonicalOfficial("Quinn Wolcott", "Home Plate Umpire", 1),
                new CanonicalOfficial("Nate Tomlinson", "First Base Umpire", 2),
                new CanonicalOfficial("Ramon De Jesus", "Second Base Umpire", 3),
                new CanonicalOfficial("Paul Clemons", "Third Base Umpire", 4)
            ],
            context.Officials);

        // A final has dropped the weather and the predictor, and baseball's summary names no
        // surface and no roof: all of it stays unstated.
        Assert.Null(context.Surface);
        Assert.Null(context.Indoor);
        Assert.Null(context.TemperatureF);
        Assert.Null(context.WeatherCondition);
        Assert.Null(context.PrecipitationPercent);
        Assert.Null(context.WindSpeedMph);
        Assert.Null(context.WindDirection);
        Assert.Null(context.HomeWinPercent);
        Assert.Null(context.AwayWinPercent);
        Assert.Null(context.MatchupQuality);
        Assert.Null(context.DayNight);
    }

    [Fact]
    public void A_football_game_before_kickoff_states_its_surface_weather_and_predictor()
    {
        var context = Parse(NflPreGame, "nfl", "401873070");

        Assert.NotNull(context);
        Assert.Equal("Highmark Stadium", context.VenueName);
        Assert.Equal("Orchard Park", context.VenueCity);
        Assert.Equal("NY", context.VenueState);
        Assert.Equal(GameSurfaces.Grass, context.Surface);
        Assert.Equal(65, context.TemperatureF);
        Assert.Equal(3, context.PrecipitationPercent);

        // The summary's weather gives a condition code and no words; no words are made up for it.
        Assert.Null(context.WeatherCondition);
        Assert.Null(context.WindSpeedMph);

        // The projection is a string, and stays a percent.
        Assert.Equal(94.8m, context.HomeWinPercent);
        Assert.Equal(5.1m, context.AwayWinPercent);
        Assert.Null(context.MatchupQuality);

        // Before kickoff there is no crowd and no crew yet.
        Assert.Null(context.Attendance);
        Assert.Null(context.Officials);
    }

    [Fact]
    public void An_artificial_field_is_turf_and_the_weather_in_words_is_read_where_written()
    {
        var json = NflPreGame
            .Replace("\"grass\": true", "\"grass\": false")
            .Replace("\"conditionId\": \"3\"", "\"displayValue\": \"Partly sunny\", \"conditionId\": \"3\"");

        var context = Parse(json, "nfl", "401873070");

        Assert.Equal(GameSurfaces.Turf, context!.Surface);
        Assert.Equal("Partly sunny", context.WeatherCondition);
    }

    /// <summary>ESPN says <c>grass: false</c> of a basketball arena too; a court is not turf.</summary>
    [Fact]
    public void A_basketball_arena_has_no_surface_however_ESPN_answers()
    {
        var pre = Parse(NbaPreGame, "nba", "401910023");

        Assert.NotNull(pre);
        Assert.Null(pre.Surface);
        Assert.Equal("Xfinity Mobile Arena", pre.VenueName);
        Assert.Equal(86.2m, pre.HomeWinPercent);
        Assert.Equal(13.8m, pre.AwayWinPercent);
        Assert.Null(pre.Attendance);

        var final = Parse(NbaFinal, "nba", "401909313");

        Assert.Null(final!.Surface);
        Assert.Equal(19057, final.Attendance);
        Assert.Equal(new CanonicalOfficial("Ashley Moyer-Gleich", "Referee", 2), final.Officials![1]);
        Assert.Equal(3, final.Officials.Count);
    }

    [Fact]
    public void A_matchup_quality_is_read_where_the_predictor_states_one()
    {
        var json = NflPreGame.Replace("\"header\": \"Matchup Predictor\",", "\"header\": \"Matchup Predictor\", \"matchupQuality\": 41.7,");

        Assert.Equal(41.7m, Parse(json, "nfl", "401873070")!.MatchupQuality);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "gameInfo": null, "predictor": null }""")]
    [InlineData("""{ "gameInfo": { "venue": { "images": [] }, "attendance": 0, "officials": [] } }""")]
    public void A_summary_that_states_nothing_has_no_context(string json)
    {
        Assert.Null(Parse(json, "nfl", "401873070"));
    }

    [Fact]
    public void A_partial_section_leaves_everything_it_does_not_state_null()
    {
        var context = Parse("""{ "gameInfo": { "venue": { "fullName": "Highmark Stadium" } } }""", "nfl", "1");

        Assert.Equal(new CanonicalGameContext("1") { VenueName = "Highmark Stadium" }, context);
    }

    /// <summary>
    /// A crew alone is a context: the parsers keep it. It is not a row's worth, so the writer writes
    /// the officials and no context row, and an empty list states nobody.
    /// </summary>
    [Fact]
    public void A_crew_alone_states_something_but_no_row_field()
    {
        var context = Parse(
            """{ "gameInfo": { "officials": [ { "displayName": "Quinn Wolcott", "order": 1 } ] } }""", "mlb", "1");

        Assert.NotNull(context);
        Assert.True(context.StatesAnything);
        Assert.False(context.StatesRowFields);

        var bare = new CanonicalGameContext("1") { Officials = [], HomeLineup = [], AwayLineup = [] };
        Assert.False(bare.StatesAnything);
        Assert.False(bare.StatesRowFields);
        Assert.True((bare with { AwayLineup = [new CanonicalLineupSlot(1, "1", "A Batter", null)] }).StatesRowFields);
    }

    /// <summary>The final's summary with the odds block beside its context, as a real final carries both.</summary>
    private const string MlbFinalWithOdds = """
    {
      "gameInfo": {
        "venue": { "fullName": "Wrigley Field", "address": { "city": "Chicago", "state": "Illinois" } },
        "attendance": 38516
      },
      "pickcenter": [
        {
          "provider": { "name": "DraftKings" },
          "moneyline": {
            "home": { "close": { "odds": "-144" }, "open": { "odds": "-141" } },
            "away": { "close": { "odds": "+120" }, "open": { "odds": "+117" } }
          }
        }
      ]
    }
    """;

    private static (EspnStatsAdapter Adapter, StubHttp Handler) Adapter(string summary)
    {
        var board = Fixtures.Text("espn.scoreboard.json");
        var handler = new StubHttp(url => url.Contains("/scoreboard") ? board : summary);
        var http = handler.Client("https://site.api.espn.com/apis/site/v2/sports/");

        return (new EspnStatsAdapter(http, NullLogger<EspnStatsAdapter>.Instance), handler);
    }

    /// <summary>
    /// The context and the open ride on the requests the walk made anyway: the board and one summary
    /// a final, and not one more.
    /// </summary>
    [Fact]
    public async Task FetchBoxScoreAsync_returns_the_finals_context_and_its_open_at_no_extra_request()
    {
        var (adapter, handler) = Adapter(MlbFinalWithOdds);

        var result = await adapter.FetchBoxScoreAsync("mlb", new DateOnly(2026, 9, 20), "401816200", CancellationToken.None);

        Assert.Equal(2, handler.Urls.Count);
        Assert.Equal(2, result.Cost.Requests);

        var context = Assert.Single(result.Contexts);
        Assert.Equal("401816200", context.SourceGameId);
        Assert.Equal("Wrigley Field", context.VenueName);
        Assert.Equal(38516, context.Attendance);

        Assert.Equal(2, result.Lines.Count);
        Assert.All(result.Lines, l => Assert.NotNull(l.Opening));
    }

    [Fact]
    public async Task FetchBoxScoresAsync_returns_one_context_a_final_that_states_one()
    {
        var (adapter, handler) = Adapter(MlbFinalWithOdds);

        var result = await adapter.FetchBoxScoresAsync("mlb", new DateOnly(2026, 9, 20), CancellationToken.None);

        var summaries = handler.Urls.Count(u => u.Contains("/summary?event="));
        Assert.True(summaries > 0);
        Assert.Equal(summaries, result.Contexts.Count);
        Assert.Equal(
            handler.Urls.Where(u => u.Contains("/summary?event=")).Select(u => u[(u.IndexOf("event=", StringComparison.Ordinal) + 6)..]),
            result.Contexts.Select(c => c.SourceGameId));
        Assert.DoesNotContain(handler.Urls, u => u.Contains("sports.core.api.espn.com"));
    }

    [Fact]
    public async Task A_summary_without_context_returns_no_contexts()
    {
        var (adapter, _) = Adapter("""{ "boxscore": { "players": [] } }""");

        var result = await adapter.FetchBoxScoreAsync("mlb", new DateOnly(2026, 9, 20), "401816200", CancellationToken.None);

        Assert.Empty(result.Contexts);
    }

    /// <summary>The live box carries the context of the summary the gamecast read, on its zero-cost read.</summary>
    [Fact]
    public async Task FetchGamecastWithBoxAsync_carries_the_summarys_context_on_the_box()
    {
        var (adapter, handler) = Adapter(NflPreGame);

        var read = await ((IGamecastSource)adapter).FetchGamecastWithBoxAsync("nfl", "401873070", CancellationToken.None);

        Assert.Single(handler.Urls);
        var context = Assert.Single(read!.Box.Contexts);
        Assert.Equal("401873070", context.SourceGameId);
        Assert.Equal(94.8m, context.HomeWinPercent);
        Assert.Equal(0, read.Box.Cost.Requests);
    }
}
