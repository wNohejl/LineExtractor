using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Ingestion.Adapters;
using LineOps.Ingestion.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LineOps.Tests.Adapters;

/// <summary>
/// The demo source prices the real schedule.
///
/// It used to invent fixtures from a fixed eight-team roster, which left the desk holding two
/// disjoint sets of games: ESPN's real slate with no prices, and invented matchups with prices
/// for teams that were not playing. A genuine fixture — Yankees at Phillies — showed a row of
/// dashes because the Phillies were not in the roster, and a board of mostly fabricated rows
/// looked populated, which is worse than looking empty.
/// </summary>
public class DemoOddsSourceTests
{
    private sealed class StubSchedule(params ScheduledGame[] games) : IScheduleReader
    {
        public List<(string Sport, TimeSpan Window)> Asked { get; } = [];

        public Task<IReadOnlyList<ScheduledGame>> GetUpcomingAsync(
            string sportKey, TimeSpan window, CancellationToken ct = default)
        {
            Asked.Add((sportKey, window));

            return Task.FromResult<IReadOnlyList<ScheduledGame>>(
                games.Where(g => g.SportKey == sportKey).ToList());
        }
    }

    private static DemoOddsSource Create(IScheduleReader schedule, params string[] books)
        => new(
            schedule,
            Options.Create(new IngestionOptions
            {
                OddsApiIo = new SourceOptions { Bookmakers = books }
            }),
            NullLogger<DemoOddsSource>.Instance);

    private static ScheduledGame Fixture(string home, string away, int hoursAhead = 6)
        => new("mlb", home, away, DateTimeOffset.UtcNow.AddHours(hoursAhead));

    [Fact]
    public async Task ItQuotesTheFixturesOnTheSchedule()
    {
        var schedule = new StubSchedule(
            Fixture("Philadelphia Phillies", "New York Yankees"),
            Fixture("Seattle Mariners", "Houston Astros", 9));

        var result = await Create(schedule, "draftkings", "fanduel")
            .FetchSlateAsync("mlb", Markets.V1, CancellationToken.None);

        var matchups = result.Games
            .Select(g => $"{g.AwayTeamName} at {g.HomeTeamName}")
            .OrderBy(m => m)
            .ToList();

        // The exact game that exposed the bug.
        Assert.Contains("New York Yankees at Philadelphia Phillies", matchups);
        Assert.Equal(2, result.Games.Count);
    }

    [Fact]
    public async Task ItInventsNoFixtureOfItsOwn()
    {
        var schedule = new StubSchedule();

        var result = await Create(schedule, "draftkings")
            .FetchSlateAsync("mlb", Markets.V1, CancellationToken.None);

        // An empty schedule must produce an empty slate. Anything else is a fabricated game
        // that will sit on the board looking like a real one.
        Assert.Empty(result.Games);
        Assert.Empty(result.Odds);
    }

    [Fact]
    public async Task EveryFixtureIsPricedByEveryBookOnEveryMarket()
    {
        var schedule = new StubSchedule(Fixture("Philadelphia Phillies", "New York Yankees"));

        var result = await Create(schedule, "draftkings", "fanduel", "bet365", "betmgm")
            .FetchSlateAsync("mlb", Markets.V1, CancellationToken.None);

        var books = result.Odds.Select(o => o.Book).Distinct().ToList();
        Assert.Equal(4, books.Count);

        // Two sides on each of three markets, for each book.
        Assert.Equal(4 * 3 * 2, result.Odds.Count);
    }

    [Fact]
    public async Task BooksDisagree()
    {
        var schedule = new StubSchedule(Fixture("Philadelphia Phillies", "New York Yankees"));

        var result = await Create(schedule, "draftkings", "fanduel", "bet365", "betmgm")
            .FetchSlateAsync("mlb", Markets.V1, CancellationToken.None);

        var moneyline = result.Odds
            .Where(o => o.Market == Markets.Moneyline && o.Outcome == "Philadelphia Phillies")
            .Select(o => o.PriceAmerican)
            .Distinct()
            .ToList();

        // A board whose whole point is "which book is best" is untestable against a fixture
        // where every book quotes the same number.
        Assert.True(moneyline.Count > 1, "Books must not all quote the same price.");
    }

    [Fact]
    public async Task TheSameFixtureKeepsTheSameIdentityAcrossScans()
    {
        var start = DateTimeOffset.UtcNow.AddHours(6);
        var schedule = new StubSchedule(new ScheduledGame("mlb", "Philadelphia Phillies", "New York Yankees", start));

        var source = Create(schedule, "draftkings");

        var first = await source.FetchSlateAsync("mlb", Markets.V1, CancellationToken.None);
        var second = await source.FetchSlateAsync("mlb", Markets.V1, CancellationToken.None);

        // A re-scan must re-quote the same game rather than resolve to a new one, or every
        // poll would fork the fixture and the movement chart would restart each time.
        Assert.Equal(first.Games[0].SourceGameId, second.Games[0].SourceGameId);
    }

    [Fact]
    public async Task AnInjectedFaultStillYieldsNothing()
    {
        var schedule = new StubSchedule(Fixture("Philadelphia Phillies", "New York Yankees"));
        var source = Create(schedule, "draftkings");

        source.FailureMode = "empty";

        var result = await source.FetchSlateAsync("mlb", Markets.V1, CancellationToken.None);

        // The on-call drill depends on this: injecting a fault must still produce the empty
        // payload the alert rules are written against.
        Assert.Empty(result.Games);
    }
}
