using XLines.Core.Contracts;
using XLines.Ingestion.Adapters;
using XLines.Ingestion.Configuration;
using XLines.Ingestion.Services;
using XLines.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace XLines.Tests.Adapters;

/// <summary>
/// The default stats source (ADR 0026) — The Odds API's events and scores — through its real entry
/// points. The fixtures <c>the-odds-api.events.json</c> and <c>the-odds-api.scores.json</c> follow the
/// response shapes in the provider's published v4 guide (https://the-odds-api.com/liveapi/guides/v4/,
/// read 2026-11-20): field names, a score as a string, <c>scores</c> and <c>last_update</c> null before
/// the start. They are not a live capture — no call was made with a real key — so they pin the
/// documented shape, not whatever the live API sends that the guide does not say.
/// </summary>
public class TheOddsApiScheduleAdapterTests
{
    private static (TheOddsApiScheduleAdapter Adapter, StubHttp Handler) Build(
        string fixture, int? billed, string? key = "test-key", Action<IngestionOptions>? configure = null, RuntimeOddsKey? runtime = null)
    {
        var handler = new StubHttp(Fixtures.Text(fixture)) { RequestsLast = billed, RequestsRemaining = 431 };

        var options = new IngestionOptions();
        options.TheOddsApi.Enabled = key is not null;
        options.TheOddsApi.ApiKey = key;
        configure?.Invoke(options);

        var http = handler.Client("https://api.the-odds-api.com/v4/");
        return (new TheOddsApiScheduleAdapter(http, Options.Create(options), NullLogger<TheOddsApiScheduleAdapter>.Instance, runtime), handler);
    }

    // ---- Events: the free schedule -------------------------------------------------------------

    [Fact]
    public async Task Events_become_games_by_full_name_with_the_desks_abbreviations()
    {
        var (adapter, handler) = Build("the-odds-api.events.json", billed: 0);

        var result = await adapter.FetchScheduleAsync("mlb", new DateOnly(2026, 9, 26), CancellationToken.None);

        var asked = Assert.Single(handler.Asked);
        Assert.Contains("/sports/baseball_mlb/events", asked.AbsolutePath);
        Assert.Contains("apiKey=test-key", asked.Query);
        Assert.Contains("commenceTimeFrom=2026-09-26T04:00:00Z", asked.Query);   // midnight Eastern, in UTC
        Assert.Contains("commenceTimeTo=2026-10-04T04:00:00Z", asked.Query);     // eight days on
        Assert.DoesNotContain("scores", asked.AbsolutePath);
        // The whole request, exactly: `events` has never had a trailing slash.
        Assert.Equal(
            "https://api.the-odds-api.com/v4/sports/baseball_mlb/events?apiKey=test-key&dateFormat=iso"
            + "&commenceTimeFrom=2026-09-26T04:00:00Z&commenceTimeTo=2026-10-04T04:00:00Z",
            asked.AbsoluteUri);

        // Four fixtures: the one with no start and the one with no id are not fixtures.
        Assert.Equal(4, result.Games.Count);

        var seattle = result.Games.Single(g => g.SourceGameId == "sea30000000000000000000000000003");
        Assert.Equal("Seattle Mariners", seattle.HomeTeamName);
        Assert.Equal("Houston Astros", seattle.AwayTeamName);
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 23, 10, 0, TimeSpan.Zero), seattle.StartsAt);
        Assert.Equal("SEA", seattle.Home!.DeskAbbrev);
        Assert.Equal("HOU", seattle.Away!.DeskAbbrev);

        // A schedule says nothing about status or score: a game already held keeps both.
        Assert.All(result.Games, g => Assert.Null(g.Status));
        Assert.All(result.Games, g => Assert.Null(g.HomeScore));
        // Nor about the season: the resolver stamps a new game from the desk's calendar.
        Assert.All(result.Games, g => Assert.Null(g.SeasonYear));
    }

    [Fact]
    public async Task A_club_another_provider_names_differently_carries_its_other_names()
    {
        var (adapter, _) = Build("the-odds-api.events.json", billed: 0);

        var games = (await adapter.FetchScheduleAsync("mlb", new DateOnly(2026, 9, 26), CancellationToken.None)).Games;

        var athletics = games.Single(g => g.SourceGameId == "ath40000000000000000000000000004").Home!;
        Assert.Equal("ATH", athletics.DeskAbbrev);
        Assert.Contains("Athletics", athletics.KnownAs!);
        Assert.Null(athletics.SourceTeamId);
    }

    [Fact]
    public async Task A_doubleheader_is_two_games_with_two_ids()
    {
        var (adapter, _) = Build("the-odds-api.events.json", billed: 0);

        var games = (await adapter.FetchScheduleAsync("mlb", new DateOnly(2026, 9, 26), CancellationToken.None)).Games;

        var pair = games.Where(g => g.HomeTeamName == "New York Yankees" && g.AwayTeamName == "Boston Red Sox").ToList();
        Assert.Equal(2, pair.Count);
        Assert.Equal(2, pair.Select(g => g.SourceGameId).Distinct().Count());
        Assert.Equal(TimeSpan.FromHours(6), pair[1].StartsAt - pair[0].StartsAt);
    }

    [Fact]
    public async Task Events_are_free_and_say_so()
    {
        var (adapter, _) = Build("the-odds-api.events.json", billed: 0);

        var cost = (await adapter.FetchScheduleAsync("mlb", new DateOnly(2026, 9, 26), CancellationToken.None)).Cost;

        Assert.Equal(new FetchCost(Requests: 1, Credits: 0, CreditsRemaining: 431), cost);
    }

    [Fact]
    public async Task A_league_before_its_season_start_is_not_scheduled()
    {
        var (adapter, _) = Build("the-odds-api.events.json", billed: 0,
            configure: o => o.Backfill.Seasons["mlb"] = new DateOnly(2026, 9, 27));

        var games = (await adapter.FetchScheduleAsync("mlb", new DateOnly(2026, 9, 26), CancellationToken.None)).Games;

        // Every fixture is on the league's 26th — the Athletics at 02:05 UTC on the 27th included, which
        // is 10:05pm Eastern on the 26th — so none is on or after a season that opens the 27th.
        Assert.Empty(games);
    }

    [Fact]
    public async Task A_fought_league_asks_nothing()
    {
        var (adapter, handler) = Build("the-odds-api.events.json", billed: 0);

        var schedule = await adapter.FetchScheduleAsync("ufc", new DateOnly(2026, 9, 26), CancellationToken.None);
        var scores = await adapter.FetchScoresAsync("ufc", CancellationToken.None);

        Assert.Empty(schedule.Games);
        Assert.Empty(scores.Games);
        Assert.Empty(handler.Asked);
        Assert.False(adapter.CoversScores("ufc"));
        Assert.True(adapter.CoversScores("nfl"));
    }

    [Fact]
    public async Task Box_scores_and_rosters_are_nothing_and_no_request()
    {
        var (adapter, handler) = Build("the-odds-api.events.json", billed: 0);

        Assert.Empty((await adapter.FetchBoxScoresAsync("mlb", new DateOnly(2026, 9, 26), CancellationToken.None)).Games);
        Assert.Empty((await adapter.FetchRosterAsync("mlb", CancellationToken.None)).Players);
        Assert.Empty(handler.Asked);
    }

    // ---- Scores: the metered results -----------------------------------------------------------

    [Fact]
    public async Task A_scores_answer_that_cannot_be_read_fails_with_what_it_was_billed()
    {
        // The provider bills when it answers. A body that is not JSON still cost two credits, and
        // the failure has to carry them or the failed run reads as a free call.
        var handler = new StubHttp("<html>not json</html>") { RequestsLast = 2, RequestsRemaining = 431 };

        var options = new IngestionOptions();
        options.TheOddsApi.Enabled = true;
        options.TheOddsApi.ApiKey = "test-key";

        var http = handler.Client("https://api.the-odds-api.com/v4/");
        var adapter = new TheOddsApiScheduleAdapter(http, Options.Create(options), NullLogger<TheOddsApiScheduleAdapter>.Instance);

        var failure = await Assert.ThrowsAsync<OddsFetchFailedException>(
            () => adapter.FetchScoresAsync("mlb", CancellationToken.None));

        Assert.Equal(new FetchCost(1, Credits: 2, CreditsRemaining: 431), failure.Cost);
        Assert.NotNull(failure.InnerException);
    }

    [Fact]
    public async Task Scores_are_asked_three_days_back_and_completed_games_are_finals()
    {
        var (adapter, handler) = Build("the-odds-api.scores.json", billed: 2);

        var result = await adapter.FetchScoresAsync("mlb", CancellationToken.None);

        var asked = Assert.Single(handler.Asked);
        Assert.Contains("/sports/baseball_mlb/scores", asked.AbsolutePath);
        Assert.Contains("daysFrom=3", asked.Query);
        // The whole request, exactly: `scores/` has always carried its trailing slash.
        Assert.Equal(
            "https://api.the-odds-api.com/v4/sports/baseball_mlb/scores/?apiKey=test-key&daysFrom=3&dateFormat=iso",
            asked.AbsoluteUri);

        var first = result.Games.Single(g => g.SourceGameId == "dh1a0000000000000000000000000001");
        Assert.Equal(("final", 5, 3), (first.Status, first.HomeScore, first.AwayScore));

        // Scores are read by name, not by position: the second game lists the away side first.
        var second = result.Games.Single(g => g.SourceGameId == "dh2b0000000000000000000000000002");
        Assert.Equal(("final", 2, 7), (second.Status, second.HomeScore, second.AwayScore));
    }

    [Fact]
    public async Task A_game_in_play_or_not_started_carries_no_status_or_score()
    {
        var (adapter, _) = Build("the-odds-api.scores.json", billed: 2);

        var games = (await adapter.FetchScoresAsync("mlb", CancellationToken.None)).Games;

        var inPlay = games.Single(g => g.SourceGameId == "sea30000000000000000000000000003");
        Assert.Equal((null, null, null), (inPlay.Status, inPlay.HomeScore, inPlay.AwayScore));

        var notStarted = games.Single(g => g.SourceGameId == "ath40000000000000000000000000004");
        Assert.Equal((null, null, null), (notStarted.Status, notStarted.HomeScore, notStarted.AwayScore));
    }

    [Fact]
    public async Task A_completed_game_with_no_score_entry_or_one_side_only_is_not_a_final()
    {
        var (adapter, _) = Build("the-odds-api.scores.json", billed: 2);

        var games = (await adapter.FetchScoresAsync("mlb", CancellationToken.None)).Games;

        var noScore = games.Single(g => g.SourceGameId == "noscore000000000000000000000006");
        Assert.Null(noScore.Status);
        Assert.Null(noScore.HomeScore);

        var oneSide = games.Single(g => g.SourceGameId == "oneside00000000000000000000000007");
        Assert.Null(oneSide.Status);
        Assert.Null(oneSide.HomeScore);
    }

    [Fact]
    public async Task The_scores_bill_is_the_providers_and_two_when_it_does_not_say()
    {
        var (billed, _) = Build("the-odds-api.scores.json", billed: 2);
        Assert.Equal(new FetchCost(1, 2, 431), (await billed.FetchScoresAsync("mlb", CancellationToken.None)).Cost);

        var (silent, _) = Build("the-odds-api.scores.json", billed: null);
        Assert.Equal(2, (await silent.FetchScoresAsync("mlb", CancellationToken.None)).Cost.Credits);

        Assert.Equal(2, billed.CreditsPerScoresCall);
        Assert.Equal(3, billed.ScoresReachDays);
    }

    // ---- The key --------------------------------------------------------------------------------

    [Fact]
    public async Task No_key_is_a_configuration_error_and_no_request()
    {
        var (adapter, handler) = Build("the-odds-api.scores.json", billed: 2, key: null);

        Assert.False(adapter.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.FetchScheduleAsync("mlb", new DateOnly(2026, 9, 26), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.FetchScoresAsync("mlb", CancellationToken.None));
        Assert.Empty(handler.Asked);
    }

    [Fact]
    public async Task A_key_typed_into_the_desk_is_the_one_sent()
    {
        var (adapter, handler) = Build("the-odds-api.events.json", billed: 0, key: null,
            runtime: new RuntimeOddsKey { TheOddsApi = "typed-in-the-desk" });

        Assert.True(adapter.IsConfigured);
        await adapter.FetchScheduleAsync("mlb", new DateOnly(2026, 9, 26), CancellationToken.None);

        Assert.Contains("apiKey=typed-in-the-desk", handler.Asked.Single().Query);
    }

    // ---- The desk's own table ----------------------------------------------------------------

    [Theory]
    [InlineData("mlb", 30)]
    [InlineData("nba", 30)]
    [InlineData("nfl", 32)]
    public void The_table_holds_every_club_once_with_an_abbreviation_unique_in_its_league(string league, int clubs)
    {
        var table = TheOddsApiTeams.League(league);

        Assert.Equal(clubs, table.Count);
        Assert.Equal(clubs, table.Select(c => c.Abbrev).Distinct().Count());
        Assert.Equal(table.Sum(c => c.Names.Count), table.SelectMany(c => c.Names).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("nba", "LA Clippers", "LAC")]
    [InlineData("nba", "Los Angeles Clippers", "LAC")]
    [InlineData("nfl", "San Francisco 49ers", "SF")]
    [InlineData("mlb", "St Louis Cardinals", "STL")]
    public void A_club_is_found_by_any_of_its_names(string league, string name, string abbrev)
        => Assert.Equal(abbrev, TheOddsApiTeams.Abbrev(league, name));
}
