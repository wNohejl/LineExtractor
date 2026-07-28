using System.Net;
using LineOps.Core.Entities;
using LineOps.Ingestion.Adapters;
using LineOps.Ingestion.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LineOps.Tests.Adapters;

/// <summary>
/// What the odds adapter puts on the wire.
///
/// The parsing tests cover what comes back; this covers what goes out, which is where the
/// free-tier cost model actually lives. Two properties matter and neither is visible in a
/// parsed result: a slate must cost two requests regardless of how many games or books are in
/// it, and every configured book must actually be asked for.
///
/// Uses a stub handler rather than the network, so it needs no API key and no provider.
/// </summary>
public class OddsApiIoRequestTests
{
    private sealed class RecordingHandler(Func<string, string> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(url))
            });
        }
    }

    private const string EventsJson = """
        [
          { "id": "evt-1", "home": "Detroit Tigers", "away": "Kansas City Royals", "date": "2026-07-26T17:10:00Z" },
          { "id": "evt-2", "home": "San Francisco Giants", "away": "Colorado Rockies", "date": "2026-07-26T20:05:00Z" }
        ]
        """;

    private const string OddsJson = """
        [
          {
            "eventId": "evt-1",
            "bookmakers": [
              { "key": "draftkings", "markets": [ { "key": "h2h", "outcomes": [ { "name": "Detroit Tigers", "price": -130 } ] } ] },
              { "key": "fanduel",    "markets": [ { "key": "h2h", "outcomes": [ { "name": "Detroit Tigers", "price": -125 } ] } ] },
              { "key": "betmgm",     "markets": [ { "key": "h2h", "outcomes": [ { "name": "Detroit Tigers", "price": -135 } ] } ] }
            ]
          }
        ]
        """;

    private static (OddsApiIoAdapter Adapter, RecordingHandler Handler) Create(params string[] books)
    {
        var handler = new RecordingHandler(url => url.Contains("events") ? EventsJson : OddsJson);

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.odds-api.io/v3/") };

        var options = Options.Create(new IngestionOptions
        {
            OddsApiIo = new SourceOptions
            {
                Enabled = true,
                ApiKey = "test-key",
                Bookmakers = books
            }
        });

        return (new OddsApiIoAdapter(http, options, NullLogger<OddsApiIoAdapter>.Instance), handler);
    }

    [Fact]
    public async Task AFullSlateCostsTwoRequestsRegardlessOfGameCount()
    {
        var (adapter, handler) = Create("draftkings", "fanduel");

        var result = await adapter.FetchSlateAsync("mlb", Markets.V1, CancellationToken.None);

        // /events then one batched /odds/multi. This is the whole reason the daily budget holds:
        // a request per game would put a 15-game slate over the hourly cap by mid-afternoon.
        Assert.Equal(2, handler.Urls.Count);
        Assert.Equal(2, result.Cost.Requests);
        Assert.Equal(0, result.Cost.Credits);
        Assert.Equal(2, result.Games.Count);
    }

    [Fact]
    public async Task EveryConfiguredBookIsRequested()
    {
        var (adapter, handler) = Create("draftkings", "fanduel", "betmgm", "caesars");

        await adapter.FetchSlateAsync("mlb", Markets.V1, CancellationToken.None);

        var oddsUrl = handler.Urls.Single(u => u.Contains("odds/multi"));

        // Variety is the point: one book is a price, several are a market. Adding books costs
        // no extra requests here, so the only ceiling is the provider's tier.
        Assert.Contains("draftkings", oddsUrl);
        Assert.Contains("fanduel", oddsUrl);
        Assert.Contains("betmgm", oddsUrl);
        Assert.Contains("caesars", oddsUrl);
    }

    [Fact]
    public async Task EveryEventIsBatchedIntoTheSingleOddsCall()
    {
        var (adapter, handler) = Create("draftkings");

        await adapter.FetchSlateAsync("mlb", Markets.V1, CancellationToken.None);

        var oddsUrl = handler.Urls.Single(u => u.Contains("odds/multi"));

        Assert.Contains("evt-1", oddsUrl);
        Assert.Contains("evt-2", oddsUrl);
    }

    [Fact]
    public async Task PricesFromEveryBookInTheResponseAreKept()
    {
        var (adapter, _) = Create("draftkings", "fanduel", "betmgm");

        var result = await adapter.FetchSlateAsync("mlb", Markets.V1, CancellationToken.None);

        var books = result.Odds.Select(o => o.Book).Distinct().OrderBy(b => b).ToList();

        Assert.Equal(["betmgm", "draftkings", "fanduel"], books);
    }

    [Fact]
    public async Task NoKeyIsARefusalRatherThanARequest()
    {
        var (_, handler) = Create("draftkings");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.odds-api.io/v3/") };
        var adapter = new OddsApiIoAdapter(
            http,
            Options.Create(new IngestionOptions { OddsApiIo = new SourceOptions { Enabled = true } }),
            NullLogger<OddsApiIoAdapter>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.FetchSlateAsync("mlb", Markets.V1, CancellationToken.None));

        // Nothing was sent — a keyless call would just spend a request to be rejected.
        Assert.Empty(handler.Urls);
    }
}
