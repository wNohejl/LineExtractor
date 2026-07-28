using System.Globalization;
using System.Text.Json;
using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Ingestion.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineOps.Ingestion.Adapters;

/// <summary>
/// Secondary odds feed (The Odds API), used for cross-source reconciliation rather than
/// as a primary feed.
///
/// It bills in credits, not requests: one /odds call costs markets x regions credits, so
/// the 500-credit free month is consumed by roughly 55 three-market single-region calls.
/// The adapter therefore reports its true credit cost so the budget guard can stop it
/// long before the allowance runs out, and requests a single region deliberately.
/// </summary>
public class TheOddsApiAdapter(
    HttpClient http,
    IOptions<IngestionOptions> options,
    ILogger<TheOddsApiAdapter> logger) : IOddsSource
{
    public const string SourceKey = "the-odds-api";

    private readonly SourceOptions _config = options.Value.TheOddsApi;

    public string Key => SourceKey;

    /// <summary>
    /// The markets configuration asks for, not every market the platform models.
    ///
    /// Each one is billed separately on every call, so this list is the difference between a
    /// 500-credit month lasting a month and lasting two thirds of one.
    /// </summary>
    public IReadOnlyList<string> SupportedMarkets => _config.EffectiveMarkets;

    public async Task<OddsFetchResult> FetchSlateAsync(
        string sportKey,
        IReadOnlyList<string> markets,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
            throw new InvalidOperationException($"{SourceKey}: no API key configured.");

        var providerMarkets = markets.Select(ToProviderMarket).Distinct().ToArray();
        var books = _config.EffectiveBookmakers;

        // Books are named rather than a region requested.
        //
        // Billing counts each ten bookmakers as one region, so naming four costs exactly what
        // `regions=us` costs — and reaches books that no single region contains. That is the
        // whole argument: Pinnacle is carried in `eu`, and asking for both regions to get it
        // would double the bill on every call for the rest of the month. Named books cross
        // regions for free.
        //
        // Pinnacle is the reason to care. It is the sharpest price on the board and the lowest
        // vig, which makes it the reference the others are judged against rather than one more
        // number to shop — a DraftKings price is "good" relative to Pinnacle or it is not good.
        var selector = books.Length is > 0 and <= BooksPerRegionUnit
            ? $"&bookmakers={string.Join(',', books)}"
            : "&regions=us";

        var url = $"sports/{MapSport(sportKey)}/odds/"
                + $"?apiKey={_config.ApiKey}"
                + selector
                + $"&markets={string.Join(',', providerMarkets)}"
                + "&oddsFormat=american";

        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        // The provider reports actual spend in response headers — always prefer those
        // over our own estimate, because the billing rule is theirs, not ours.
        var credits = ReadHeaderInt(response, "x-requests-last")
                      ?? providerMarkets.Length;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var capturedAt = DateTimeOffset.UtcNow;
        var games = new List<CanonicalGame>();
        var odds = new List<CanonicalOdds>();

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in doc.RootElement.EnumerateArray())
                ParseEvent(e, sportKey, markets, capturedAt, games, odds);
        }

        var remaining = ReadHeaderInt(response, "x-requests-remaining");
        logger.LogInformation(
            "{Source}: {Sport} -> {Games} games, {Odds} prices, {Credits} credits ({Remaining} remaining)",
            SourceKey, sportKey, games.Count, odds.Count, credits, remaining?.ToString() ?? "?");

        return new OddsFetchResult(games, odds, new FetchCost(Requests: 1, Credits: credits));
    }

    private static void ParseEvent(
        JsonElement e,
        string sportKey,
        IReadOnlyList<string> markets,
        DateTimeOffset capturedAt,
        List<CanonicalGame> games,
        List<CanonicalOdds> odds)
    {
        if (!e.TryGetProperty("id", out var idEl) || idEl.GetString() is not { } id)
            return;

        var home = e.TryGetProperty("home_team", out var h) ? h.GetString() : null;
        var away = e.TryGetProperty("away_team", out var a) ? a.GetString() : null;
        if (home is null || away is null)
            return;

        var startsAt = e.TryGetProperty("commence_time", out var c)
                       && c.TryGetDateTimeOffset(out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

        games.Add(new CanonicalGame(id, sportKey, home, away, startsAt));

        if (!e.TryGetProperty("bookmakers", out var bookmakers)
            || bookmakers.ValueKind != JsonValueKind.Array)
            return;

        foreach (var book in bookmakers.EnumerateArray())
        {
            var bookKey = book.TryGetProperty("key", out var bk) ? bk.GetString() : null;
            if (bookKey is null
                || !book.TryGetProperty("markets", out var marketList)
                || marketList.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var market in marketList.EnumerateArray())
            {
                // When the book moved, not when we noticed.
                //
                // Stamping our fetch time makes every price in a scan look simultaneous and
                // makes the interval between scans look like the interval between moves — so a
                // line that moved once at 09:04 reads as having moved at whatever o'clock we
                // happened to poll. The movement chart is only an honest record of the market
                // if the timestamps are the market's.
                var movedAt = ReadTimestamp(market, "last_update")
                              ?? ReadTimestamp(book, "last_update")
                              ?? capturedAt;

                var providerMarket = market.TryGetProperty("key", out var mk) ? mk.GetString() : null;
                var canonical = FromProviderMarket(providerMarket);

                if (canonical is null || !markets.Contains(canonical))
                    continue;

                if (!market.TryGetProperty("outcomes", out var outcomes)
                    || outcomes.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var outcome in outcomes.EnumerateArray())
                {
                    var name = outcome.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (name is null
                        || !outcome.TryGetProperty("price", out var p)
                        || !p.TryGetInt32(out var price)
                        || price == 0)
                        continue;

                    decimal? point = outcome.TryGetProperty("point", out var pt)
                                     && pt.TryGetDecimal(out var d)
                        ? d
                        : null;

                    odds.Add(new CanonicalOdds(
                        SourceGameId: id,
                        Book: bookKey,
                        Market: canonical,
                        Outcome: name,
                        Line: point,
                        PriceAmerican: price,
                        CapturedAt: movedAt));
                }
            }
        }
    }

    private static string ToProviderMarket(string market) => market switch
    {
        Markets.Moneyline => "h2h",
        Markets.Spread => "spreads",
        Markets.Total => "totals",
        _ => market
    };

    private static string? FromProviderMarket(string? providerMarket) => providerMarket switch
    {
        "h2h" => Markets.Moneyline,
        "spreads" => Markets.Spread,
        "totals" => Markets.Total,
        _ => null
    };

    private static string MapSport(string sportKey) => sportKey switch
    {
        "nfl" => "americanfootball_nfl",
        "nba" => "basketball_nba",
        "mlb" => "baseball_mlb",
        "nhl" => "icehockey_nhl",
        _ => sportKey
    };

    /// <summary>
    /// Ten bookmakers bill as one region, so a list at or under this costs exactly what a single
    /// region costs — and is not confined to one.
    /// </summary>
    private const int BooksPerRegionUnit = 10;

    private static DateTimeOffset? ReadTimestamp(JsonElement element, string property)
        => element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
               DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    private static int? ReadHeaderInt(HttpResponseMessage response, string header)
        => response.Headers.TryGetValues(header, out var values)
           && int.TryParse(values.FirstOrDefault(), out var parsed)
            ? parsed
            : null;
}
