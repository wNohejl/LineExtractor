using System.Net;
using XLines.Core.Contracts;
using XLines.Ingestion;
using XLines.Core.Entities;
using XLines.Ingestion.Adapters;
using XLines.Ingestion.Configuration;
using XLines.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace XLines.Tests.Adapters;

/// <summary>
/// The one odds feed the desk runs on, pinned against a recorded payload through the adapter's
/// real entry point: the request it bills on, the credits it reports, and what it reads back.
/// The fixture carries the shapes a real response has — a market we do not model, a malformed
/// price, a book with no per-market timestamp, an event with no start and one with no id.
/// </summary>
public class TheOddsApiAdapterTests
{
    private static (TheOddsApiAdapter Adapter, StubHttp Handler) Build(
        string[]? books = null, string[]? markets = null, int? creditsUsed = 3, string? body = null)
    {
        body ??= Fixtures.Text("the-odds-api.odds.json");
        var handler = new StubHttp(body) { RequestsLast = creditsUsed, RequestsRemaining = 431 };

        var options = new IngestionOptions();
        options.TheOddsApi.ApiKey = "test-key";
        options.TheOddsApi.Bookmakers = books ?? ["pinnacle", "draftkings", "fanduel", "betmgm"];
        options.TheOddsApi.Markets = markets ?? ["moneyline", "spread", "total"];

        var http = handler.Client("https://api.the-odds-api.com/v4/");
        return (new TheOddsApiAdapter(http, Options.Create(options), NullLogger<TheOddsApiAdapter>.Instance), handler);
    }

    private static Task<OddsFetchResult> FetchAsync(TheOddsApiAdapter adapter, params string[] markets)
        => adapter.FetchSlateAsync("mlb", markets.Length > 0 ? markets : Markets.V1, CancellationToken.None);

    [Fact]
    public async Task Named_books_are_asked_for_by_name_so_they_bill_as_one_region()
    {
        var (adapter, handler) = Build();

        await FetchAsync(adapter);

        var query = handler.Last!.Query;
        Assert.Contains("/sports/baseball_mlb/odds/", handler.Last.AbsolutePath);
        Assert.Contains("bookmakers=pinnacle,draftkings,fanduel,betmgm", query);
        Assert.DoesNotContain("regions=", query);
        Assert.Contains("markets=h2h,spreads,totals", query);
        Assert.Contains("oddsFormat=american", query);
    }

    [Fact]
    public async Task More_than_ten_books_fall_back_to_one_region_rather_than_billing_two()
    {
        var eleven = Enumerable.Range(1, 11).Select(i => $"book{i}").ToArray();
        var (adapter, handler) = Build(books: eleven);

        await FetchAsync(adapter);

        Assert.Contains("regions=us", handler.Last!.Query);
        Assert.DoesNotContain("bookmakers=", handler.Last.Query);
    }

    [Fact]
    public async Task The_bill_is_what_the_provider_says_it_charged()
    {
        var (adapter, _) = Build(creditsUsed: 2);

        var result = await FetchAsync(adapter);

        // And what is left on the key, which is the only count that sees another machine's spend.
        Assert.Equal(new FetchCost(Requests: 1, Credits: 2, CreditsRemaining: 431), result.Cost);
    }

    [Fact]
    public async Task Without_the_header_the_bill_is_the_market_count()
    {
        var (adapter, _) = Build(creditsUsed: null);

        var result = await FetchAsync(adapter, Markets.Moneyline, Markets.Spread);

        Assert.Equal(2, result.Cost.Credits);
    }

    [Fact]
    public async Task Only_games_yet_to_start_are_asked_for_and_the_window_has_no_far_end()
    {
        var (adapter, handler) = Build();
        var before = DateTimeOffset.UtcNow;

        await FetchAsync(adapter);

        var query = System.Web.HttpUtility.ParseQueryString(handler.Last!.Query);
        var from = DateTimeOffset.Parse(query["commenceTimeFrom"]!, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal);

        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", query["commenceTimeFrom"]!);
        Assert.InRange(from, before.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        // A manual pull prices games days ahead; a far end would take that away.
        Assert.DoesNotContain("commenceTimeTo", handler.Last.Query);
    }

    [Fact]
    public async Task An_empty_slate_without_the_header_is_free_not_the_market_count()
    {
        var (adapter, _) = Build(creditsUsed: null, body: "[]");

        var result = await FetchAsync(adapter);

        Assert.Empty(result.Games);
        Assert.Equal(0, result.Cost.Credits);
    }

    [Fact]
    public async Task An_empty_slate_still_reports_what_the_provider_says_it_charged()
    {
        var (adapter, _) = Build(creditsUsed: 0, body: "[]");

        Assert.Equal(0, (await FetchAsync(adapter)).Cost.Credits);
    }

    [Fact]
    public async Task Events_listed_and_none_readable_fail_the_run_rather_than_read_as_a_quiet_day()
    {
        // No games is a success now (ADR 0003), so a changed payload must not arrive as no games.
        var (adapter, _) = Build(creditsUsed: 3, body: """[{ "event_id": "a" }, { "event_id": "b" }]""");

        var thrown = await Assert.ThrowsAsync<OddsFetchFailedException>(() => FetchAsync(adapter));

        Assert.Contains("listed 2 events", thrown.Message);

        // The call was billed, so the failure says what it cost, in the provider's own numbers.
        Assert.Equal(new FetchCost(Requests: 1, Credits: 3, CreditsRemaining: 431), thrown.Cost);
    }

    [Fact]
    public async Task Every_provider_client_asks_for_and_unpacks_gzip()
    {
        // ESPN's scoreboard is 236 KB plain and 20 KB gzipped; .NET asks for neither unless told.
        using var handler = IngestionServiceCollectionExtensions.ProviderHandler();

        Assert.Equal(DecompressionMethods.All, handler.AutomaticDecompression);
        Assert.False(handler.UseCookies);
        Assert.Equal(TimeSpan.FromMinutes(2), handler.PooledConnectionLifetime);

        // And on the wire: a loopback server that answers only in gzip, asserting the request asked.
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serving = Task.Run(async () =>
        {
            using var tcp = await listener.AcceptTcpClientAsync();
            var stream = tcp.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = System.Text.Encoding.ASCII.GetString(buffer, 0, read);

            using var packed = new MemoryStream();
            await using (var gzip = new System.IO.Compression.GZipStream(packed, System.IO.Compression.CompressionLevel.Fastest, true))
                await gzip.WriteAsync("[\"ok\"]"u8.ToArray());

            var head = $"HTTP/1.1 200 OK\r\nContent-Encoding: gzip\r\nContent-Length: {packed.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(head));
            await stream.WriteAsync(packed.ToArray());
            return request;
        });

        try
        {
            using var http = new HttpClient(handler);
            var body = await http.GetStringAsync($"http://127.0.0.1:{port}/");

            Assert.Equal("[\"ok\"]", body);
            Assert.Contains("Accept-Encoding: gzip", await serving, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task An_event_without_a_start_or_an_id_is_not_a_fixture()
    {
        var (adapter, _) = Build();

        var games = (await FetchAsync(adapter)).Games;

        var game = Assert.Single(games);
        Assert.Equal("e7a1c0f2", game.SourceGameId);
        Assert.Equal("Seattle Mariners", game.HomeTeamName);
        Assert.Equal("Houston Astros", game.AwayTeamName);
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 23, 5, 0, TimeSpan.Zero), game.StartsAt);
    }

    [Fact]
    public async Task Markets_are_read_in_the_platforms_names_and_unmodelled_ones_are_dropped()
    {
        var (adapter, _) = Build();

        var odds = (await FetchAsync(adapter)).Odds;

        Assert.All(odds, o => Assert.Contains(o.Market, Markets.V1));
        // h2h_lay is a real Odds API market; its -150 must not pass for DraftKings' moneyline.
        Assert.DoesNotContain(odds, o => o.Book == "draftkings" && o.PriceAmerican == -150);
    }

    [Fact]
    public async Task A_market_not_asked_for_is_not_kept_even_when_the_response_carries_it()
    {
        var (adapter, _) = Build();

        var odds = (await FetchAsync(adapter, Markets.Moneyline)).Odds;

        Assert.All(odds, o => Assert.Equal(Markets.Moneyline, o.Market));
    }

    [Fact]
    public async Task A_zero_or_missing_price_is_not_a_price()
    {
        var (adapter, _) = Build();

        var odds = (await FetchAsync(adapter)).Odds;

        Assert.DoesNotContain(odds, o => o.Book == "fanduel");
    }

    [Fact]
    public async Task Handicaps_keep_their_sign_and_side()
    {
        var (adapter, _) = Build();

        var spreads = (await FetchAsync(adapter)).Odds.Where(o => o.Market == Markets.Spread).ToList();

        Assert.Equal(-1.5m, spreads.Single(o => o.Outcome == "Seattle Mariners").Line);
        Assert.Equal(1.5m, spreads.Single(o => o.Outcome == "Houston Astros").Line);
        Assert.Equal(150, spreads.Single(o => o.Outcome == "Seattle Mariners").PriceAmerican);
    }

    [Fact]
    public async Task A_total_is_over_and_under_in_the_platforms_spelling()
    {
        // The Odds API writes "Over"; ESPN, odds-api.io, the board and settlement all say "over".
        // A capital here would make every total from this feed invisible on the board.
        var (adapter, _) = Build();

        var totals = (await FetchAsync(adapter)).Odds.Where(o => o.Market == Markets.Total).ToList();

        Assert.Equal(["over", "under"], totals.Select(o => o.Outcome).Order());
        Assert.All(totals, o => Assert.Equal(7.5m, o.Line));
    }

    [Fact]
    public async Task A_price_is_stamped_when_the_book_moved_it_not_when_it_was_fetched()
    {
        var (adapter, _) = Build();

        var odds = (await FetchAsync(adapter)).Odds;

        // The market's own timestamp where it has one, the book's where it does not.
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 14, 58, 0, TimeSpan.Zero),
            odds.First(o => o.Book == "pinnacle" && o.Market == Markets.Moneyline).CapturedAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 15, 1, 0, TimeSpan.Zero),
            odds.First(o => o.Book == "draftkings" && o.Market == Markets.Moneyline).CapturedAt);
    }

    // ---- A field of the wrong type --------------------------------------------------------------
    //
    // GetString and the Try* number readers throw on the wrong JSON type, and that exception is not
    // OddsFetchFailedException: before the readers checked types, one odd field failed a billed
    // call as though it had cost nothing. These pin that a wrong type skips what it belongs to.

    private const string Readable = """
        { "id": "good", "home_team": "Seattle Mariners", "away_team": "Houston Astros",
          "commence_time": "2026-09-26T23:05:00Z", "bookmakers": [] }
        """;

    [Theory]
    [InlineData("""{ "id": 42, "home_team": "A", "away_team": "B", "commence_time": "2026-09-26T23:05:00Z" }""")]
    [InlineData("""{ "id": "x", "home_team": 7, "away_team": "B", "commence_time": "2026-09-26T23:05:00Z" }""")]
    [InlineData("""{ "id": "x", "home_team": "A", "away_team": { "name": "B" }, "commence_time": "2026-09-26T23:05:00Z" }""")]
    [InlineData("""{ "id": "x", "home_team": "A", "away_team": "B", "commence_time": 1790550300 }""")]
    [InlineData("""{ "id": "x", "home_team": "A", "away_team": "B", "commence_time": "not a time" }""")]
    [InlineData("""{ "id": "x", "home_team": " ", "away_team": "B", "commence_time": "2026-09-26T23:05:00Z" }""")]
    [InlineData("""{ "id": "x", "home_team": "A", "away_team": "", "commence_time": "2026-09-26T23:05:00Z" }""")]
    [InlineData(""" "not an event" """)]
    public async Task An_event_with_a_wrong_typed_or_blank_field_is_skipped_not_thrown(string odd)
    {
        var (adapter, _) = Build(body: $"[{odd}, {Readable}]");

        var game = Assert.Single((await FetchAsync(adapter)).Games);

        Assert.Equal("good", game.SourceGameId);
        // A book's game carries no team refs: it is matched by name to games the stats source holds.
        Assert.Null(game.Home);
        Assert.Null(game.Away);
    }

    [Fact]
    public async Task A_wrong_typed_book_market_or_outcome_is_skipped_and_the_rest_is_read()
    {
        const string body = """
            [{ "id": "good", "home_team": "Seattle Mariners", "away_team": "Houston Astros",
               "commence_time": "2026-09-26T23:05:00Z",
               "bookmakers": [
                 "not a book",
                 { "key": 7, "markets": [{ "key": "h2h", "outcomes": [{ "name": "Seattle Mariners", "price": -120 }] }] },
                 { "key": "pinnacle", "last_update": 1790550300, "markets": [
                     "not a market",
                     { "key": ["h2h"], "outcomes": [{ "name": "Seattle Mariners", "price": -130 }] },
                     { "key": "h2h", "last_update": "2026-09-26T14:58:00Z", "outcomes": [
                         "not an outcome",
                         { "name": 12, "price": -140 },
                         { "name": "Seattle Mariners", "price": "-150" },
                         { "name": "Houston Astros", "price": 125, "point": "none" }
                     ] }
                 ] }
               ] }]
            """;
        var (adapter, _) = Build(body: body);

        var result = await FetchAsync(adapter);

        Assert.Single(result.Games);
        var price = Assert.Single(result.Odds);
        Assert.Equal(("pinnacle", Markets.Moneyline, "Houston Astros", 125, (decimal?)null),
            (price.Book, price.Market, price.Outcome, price.PriceAmerican, price.Line));
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 14, 58, 0, TimeSpan.Zero), price.CapturedAt);
    }

    [Fact]
    public async Task A_slate_whose_every_event_is_wrong_typed_fails_with_what_it_cost()
    {
        var (adapter, _) = Build(creditsUsed: 3, body: """
            [{ "id": 1, "home_team": "A", "away_team": "B", "commence_time": "2026-09-26T23:05:00Z" },
             { "id": "x", "home_team": 2, "away_team": "B", "commence_time": "2026-09-26T23:05:00Z" },
             { "id": "y", "home_team": "A", "away_team": "B", "commence_time": false }]
            """);

        var thrown = await Assert.ThrowsAsync<OddsFetchFailedException>(() => FetchAsync(adapter));

        Assert.Contains("listed 3 events", thrown.Message);
        Assert.Equal(new FetchCost(Requests: 1, Credits: 3, CreditsRemaining: 431), thrown.Cost);
    }

    [Fact]
    public async Task A_slate_whose_every_event_has_blank_sides_fails_rather_than_reading_as_a_quiet_day()
    {
        var (adapter, _) = Build(creditsUsed: 3, body: """
            [{ "id": "x", "home_team": "", "away_team": "  ", "commence_time": "2026-09-26T23:05:00Z" }]
            """);

        var thrown = await Assert.ThrowsAsync<OddsFetchFailedException>(() => FetchAsync(adapter));

        Assert.Equal(3, thrown.Cost.Credits);
    }

    [Fact]
    public async Task An_answer_that_is_not_json_fails_with_what_it_cost_not_as_a_free_call()
    {
        var (adapter, _) = Build(creditsUsed: 3, body: "[{ \"id\": \"cut short");

        var thrown = await Assert.ThrowsAsync<OddsFetchFailedException>(() => FetchAsync(adapter));

        Assert.Contains("could not be read", thrown.Message);
        Assert.Equal(new FetchCost(Requests: 1, Credits: 3, CreditsRemaining: 431), thrown.Cost);
    }

    [Fact]
    public async Task An_unreadable_answer_without_the_header_is_billed_at_the_market_count()
    {
        var (adapter, _) = Build(creditsUsed: null, body: "not json");

        var thrown = await Assert.ThrowsAsync<OddsFetchFailedException>(
            () => FetchAsync(adapter, Markets.Moneyline, Markets.Spread));

        Assert.Equal(new FetchCost(Requests: 1, Credits: 2, CreditsRemaining: 431), thrown.Cost);
    }

    // ---- The request, exactly -------------------------------------------------------------------

    [Fact]
    public async Task The_odds_request_is_the_one_it_has_always_been()
    {
        var (adapter, handler) = Build();

        await FetchAsync(adapter);

        // Everything but the start bound, which is the moment of the call.
        Assert.Matches(
            @"^https://api\.the-odds-api\.com/v4/sports/baseball_mlb/odds/\?apiKey=test-key"
            + @"&bookmakers=pinnacle,draftkings,fanduel,betmgm&markets=h2h,spreads,totals&oddsFormat=american"
            + @"&commenceTimeFrom=\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$",
            handler.Last!.AbsoluteUri);
    }

    [Fact]
    public void The_wire_builds_each_address_with_its_own_trailing_slash()
    {
        var at = new DateTimeOffset(2026, 9, 26, 4, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            "sports/baseball_mlb/odds/?apiKey=k%2By&bookmakers=pinnacle,draftkings&markets=h2h,totals&oddsFormat=american&commenceTimeFrom=2026-09-26T04:00:00Z",
            TheOddsApiWire.OddsUrl("mlb", "k+y", ["pinnacle", "draftkings"], ["h2h", "totals"], at));
        Assert.Equal(
            "sports/americanfootball_nfl/odds/?apiKey=k&regions=us&markets=h2h&oddsFormat=american&commenceTimeFrom=2026-09-26T04:00:00Z",
            TheOddsApiWire.OddsUrl("nfl", "k", null, ["h2h"], at));
        Assert.Equal(
            "sports/baseball_mlb/events?apiKey=k&dateFormat=iso&commenceTimeFrom=2026-09-26T04:00:00Z&commenceTimeTo=2026-10-04T04:00:00Z",
            TheOddsApiWire.EventsUrl("mlb", "k", at, at.AddDays(8)));
        Assert.Equal(
            "sports/basketball_nba/scores/?apiKey=k&daysFrom=3&dateFormat=iso",
            TheOddsApiWire.ScoresUrl("nba", "k", 3));
    }

    [Fact]
    public async Task No_key_is_a_configuration_error_not_a_request()
    {
        var options = new IngestionOptions();
        var handler = new StubHttp("[]") { RequestsLast = 0, RequestsRemaining = 431 };
        var adapter = new TheOddsApiAdapter(
            handler.Client("https://api.the-odds-api.com/v4/"),
            Options.Create(options), NullLogger<TheOddsApiAdapter>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => FetchAsync(adapter));
        Assert.Null(handler.Last);
    }
}
