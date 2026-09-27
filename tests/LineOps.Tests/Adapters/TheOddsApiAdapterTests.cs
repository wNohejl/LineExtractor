using System.Net;
using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Ingestion.Adapters;
using LineOps.Ingestion.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LineOps.Tests.Adapters;

/// <summary>
/// The one odds feed the desk runs on, pinned against a recorded payload through the adapter's
/// real entry point: the request it bills on, the credits it reports, and what it reads back.
/// The fixture carries the shapes a real response has — a market we do not model, a malformed
/// price, a book with no per-market timestamp, an event with no start and one with no id.
/// </summary>
public class TheOddsApiAdapterTests
{
    private sealed class Recorded(string body, int? creditsUsed) : HttpMessageHandler
    {
        public Uri? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Asked = request.RequestUri;

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            if (creditsUsed is { } used)
                response.Headers.Add("x-requests-last", used.ToString());
            response.Headers.Add("x-requests-remaining", "431");
            return Task.FromResult(response);
        }
    }

    private static (TheOddsApiAdapter Adapter, Recorded Handler) Build(
        string[]? books = null, string[]? markets = null, int? creditsUsed = 3)
    {
        var body = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "the-odds-api.odds.json"));
        var handler = new Recorded(body, creditsUsed);

        var options = new IngestionOptions();
        options.TheOddsApi.ApiKey = "test-key";
        options.TheOddsApi.Bookmakers = books ?? ["pinnacle", "draftkings", "fanduel", "betmgm"];
        options.TheOddsApi.Markets = markets ?? ["moneyline", "spread", "total"];

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.the-odds-api.com/v4/") };
        return (new TheOddsApiAdapter(http, Options.Create(options), NullLogger<TheOddsApiAdapter>.Instance), handler);
    }

    private static Task<OddsFetchResult> FetchAsync(TheOddsApiAdapter adapter, params string[] markets)
        => adapter.FetchSlateAsync("mlb", markets.Length > 0 ? markets : Markets.V1, CancellationToken.None);

    [Fact]
    public async Task Named_books_are_asked_for_by_name_so_they_bill_as_one_region()
    {
        var (adapter, handler) = Build();

        await FetchAsync(adapter);

        var query = handler.Asked!.Query;
        Assert.Contains("/sports/baseball_mlb/odds/", handler.Asked.AbsolutePath);
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

        Assert.Contains("regions=us", handler.Asked!.Query);
        Assert.DoesNotContain("bookmakers=", handler.Asked.Query);
    }

    [Fact]
    public async Task The_bill_is_what_the_provider_says_it_charged()
    {
        var (adapter, _) = Build(creditsUsed: 2);

        var result = await FetchAsync(adapter);

        Assert.Equal(new FetchCost(Requests: 1, Credits: 2), result.Cost);
    }

    [Fact]
    public async Task Without_the_header_the_bill_is_the_market_count()
    {
        var (adapter, _) = Build(creditsUsed: null);

        var result = await FetchAsync(adapter, Markets.Moneyline, Markets.Spread);

        Assert.Equal(2, result.Cost.Credits);
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

    [Fact]
    public async Task No_key_is_a_configuration_error_not_a_request()
    {
        var options = new IngestionOptions();
        var handler = new Recorded("[]", 0);
        var adapter = new TheOddsApiAdapter(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.the-odds-api.com/v4/") },
            Options.Create(options), NullLogger<TheOddsApiAdapter>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => FetchAsync(adapter));
        Assert.Null(handler.Asked);
    }
}
