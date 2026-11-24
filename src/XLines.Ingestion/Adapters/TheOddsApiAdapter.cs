using System.Text.Json;
using XLines.Core.Contracts;
using XLines.Core.Entities;
using XLines.Ingestion.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace XLines.Ingestion.Adapters;

/// <summary>
/// The odds feed (The Odds API) — the book market ADR 0011 prices games from, and since ADR
/// 0014 the one the desk actually runs on.
///
/// It bills in credits, not requests: one /odds call costs markets x regions credits, so
/// the 500-credit free month is consumed by roughly 55 three-market single-region calls.
/// The adapter therefore reports its true credit cost so the budget guard can stop it
/// long before the allowance runs out, and requests a single region deliberately.
///
/// It asks only for games that have not started (<c>commenceTimeFrom</c> = now), and the
/// provider does not bill a call that lists no events — so a league whose games are all in play
/// costs nothing, and an empty answer is reported as zero credits, not the market count.
/// </summary>
public class TheOddsApiAdapter(
    HttpClient http,
    IOptions<IngestionOptions> options,
    ILogger<TheOddsApiAdapter> logger,
    Services.RuntimeOddsKey? runtimeKey = null) : IOddsSource
{
    public const string SourceKey = "the-odds-api";

    private readonly SourceOptions _config = options.Value.TheOddsApi;

    private readonly TheOddsApiKey _key = new(options.Value.TheOddsApi, runtimeKey);

    public string Key => SourceKey;

    /// <summary>
    /// A supplied key needs no <c>Enabled</c> switch beside it: supplying it is the switch.
    /// Configuration's key still needs its switch, as it always has.
    /// </summary>
    public bool IsConfigured => _key.IsConfigured;

    /// <summary>
    /// The markets configuration asks for, not every market the platform models.
    ///
    /// Each one is billed separately on every call, so this list is the difference between a
    /// 500-credit month lasting a month and lasting two thirds of one.
    /// </summary>
    public IReadOnlyList<string> SupportedMarkets => _config.EffectiveMarkets;

    public IReadOnlyList<string> MarketsFor(string sportKey) => _config.MarketsFor(sportKey);

    /// <summary>
    /// Markets times one region unit. The selector in <see cref="FetchSlateAsync"/> names up to
    /// ten books — one unit — or falls back to <c>regions=us</c>, also one, so a scan of a sport
    /// costs exactly its market count. The header on the response remains the record of what was
    /// actually billed; this is the forecast the planner paces on, and the ceiling: a scan that
    /// lists no upcoming games is not billed at all.
    /// </summary>
    public int CreditsPerScan(string sportKey) => MarketsFor(sportKey).Count;

    public async Task<OddsFetchResult> FetchSlateAsync(
        string sportKey,
        IReadOnlyList<string> markets,
        CancellationToken ct)
    {
        var apiKey = _key.Require(SourceKey);

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
        var named = books.Length is > 0 and <= BooksPerRegionUnit ? books : null;

        // Only games that have not started. Without a lower bound the provider also lists games
        // already in play, and this product never stores their prices: a game with a closing line
        // is dropped on ingest, and the close is read only from scans at or before the start.
        // They cost credits to carry and are thrown away on arrival.
        //
        // There is deliberately no upper bound: an operator's manual pull prices games days
        // ahead, and a window would take that away. And when nothing is left to list — a league
        // whose every game is under way — the provider does not bill the call ("If no events are
        // returned, the request will not count against the usage quota"), so a quiet league is free.
        var capturedAt = DateTimeOffset.UtcNow;

        var url = TheOddsApiWire.OddsUrl(sportKey, apiKey, named, providerMarkets, capturedAt);

        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        // From here the call has been answered and billed. Whatever goes wrong reading the answer,
        // the run must still record what it cost: an exception that is not OddsFetchFailedException
        // reaches ingestion as a call that spent nothing, and the month reads low until the next
        // good call. The readers below skip a field of the wrong type rather than throw, so this is
        // the guard for what they did not foresee — a body that is not JSON, a stream cut short.
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var games = new List<CanonicalGame>();
            var odds = new List<CanonicalOdds>();

            var listed = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0;

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in doc.RootElement.EnumerateArray())
                    ParseEvent(e, sportKey, markets, capturedAt, games, odds);
            }

            // The provider reports actual spend in response headers — always prefer those
            // over our own estimate, because the billing rule is theirs, not ours. Without the
            // header the estimate is the market count, unless the answer was an empty list, which
            // the provider does not bill: then it is nothing, not a charge we invented.
            var cost = TheOddsApiWire.CostOf(response, fallbackCredits: listed ? providerMarkets.Length : 0);

            // An empty list is a normal answer; a list whose every event we could not read is not.
            // Ingestion reads "no games" as an empty slate and a success, so returning nothing here
            // would file a changed payload as a quiet day. It fails the run instead. The call was
            // billed all the same, so the failure carries its cost: the failed run records the
            // credits and the reported remainder, and the month is right at once rather than when
            // the next call succeeds.
            if (listed && games.Count == 0)
                throw new OddsFetchFailedException(
                    $"{SourceKey}: {sportKey} listed {doc.RootElement.GetArrayLength()} events and none could be read — possible upstream schema drift.",
                    cost);

            logger.LogInformation(
                "{Source}: {Sport} -> {Games} games, {Odds} prices, {Credits} credits ({Remaining} remaining)",
                SourceKey, sportKey, games.Count, odds.Count, cost.Credits, cost.CreditsRemaining?.ToString() ?? "?");

            return new OddsFetchResult(games, odds, cost);
        }
        catch (Exception ex) when (ex is not OddsFetchFailedException and not OperationCanceledException)
        {
            // What was billed is unknown only in the estimate: the header, where sent, still says.
            // Without it the answer was not an empty list we could see, so the market count is the
            // honest estimate rather than nothing.
            //
            // The cause rides along as the inner exception, and is named in the message as well,
            // because the message is what the run row keeps.
            throw new OddsFetchFailedException(
                $"{SourceKey}: {sportKey} answered and could not be read ({ex.GetType().Name}: {ex.Message}).",
                TheOddsApiWire.CostOf(response, fallbackCredits: providerMarkets.Length),
                ex);
        }
    }

    private static void ParseEvent(
        JsonElement e,
        string sportKey,
        IReadOnlyList<string> markets,
        DateTimeOffset capturedAt,
        List<CanonicalGame> games,
        List<CanonicalOdds> odds)
    {
        // Every read below checks the JSON type before it takes a value. GetString and the Try*
        // number readers throw InvalidOperationException on the wrong type, and that is not
        // OddsFetchFailedException: one odd field would fail a billed call as if it were free.
        // A field of the wrong type skips its event, book, market or outcome instead, and a slate
        // with nothing readable left is caught by the drift guard in FetchSlateAsync.
        //
        // No team refs: a book's game is matched to games the stats source already holds, by name
        // (see TheOddsApiWire.ReadEvent).
        if (TheOddsApiWire.ReadEvent(e, sportKey, withRefs: false) is not { } game)
            return;

        var id = game.SourceGameId;
        games.Add(game);

        if (!e.TryGetProperty("bookmakers", out var bookmakers)
            || bookmakers.ValueKind != JsonValueKind.Array)
            return;

        foreach (var book in bookmakers.EnumerateArray())
        {
            if (TheOddsApiWire.ReadString(book, "key") is not { } bookKey
                || !book.TryGetProperty("markets", out var marketList)
                || marketList.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var market in marketList.EnumerateArray())
            {
                if (market.ValueKind != JsonValueKind.Object)
                    continue;

                // When the book moved, not when we noticed.
                //
                // Stamping our fetch time makes every price in a scan look simultaneous and
                // makes the interval between scans look like the interval between moves — so a
                // line that moved once at 09:04 reads as having moved at whatever o'clock we
                // happened to poll. The movement chart is only an honest record of the market
                // if the timestamps are the market's.
                var movedAt = TheOddsApiWire.ReadTimestamp(market, "last_update")
                              ?? TheOddsApiWire.ReadTimestamp(book, "last_update")
                              ?? capturedAt;

                var canonical = FromProviderMarket(TheOddsApiWire.ReadString(market, "key"));

                if (canonical is null || !markets.Contains(canonical))
                    continue;

                if (!market.TryGetProperty("outcomes", out var outcomes)
                    || outcomes.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var outcome in outcomes.EnumerateArray())
                {
                    if (TheOddsApiWire.ReadString(outcome, "name") is not { } name
                        || !outcome.TryGetProperty("price", out var p)
                        || p.ValueKind != JsonValueKind.Number
                        || !p.TryGetInt32(out var price)
                        || price == 0)
                        continue;

                    // The provider writes a total's sides "Over" and "Under"; every other reader
                    // on the platform — ESPN, odds-api.io, the board, settlement, fair value —
                    // says "over" and "under". Left as sent, every total from this feed would
                    // pair with nothing and never reach the board.
                    if (canonical == Markets.Total)
                        name = name.ToLowerInvariant();

                    decimal? point = outcome.TryGetProperty("point", out var pt)
                                     && pt.ValueKind == JsonValueKind.Number
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

    /// <summary>
    /// Ten bookmakers bill as one region, so a list at or under this costs exactly what a single
    /// region costs — and is not confined to one.
    /// </summary>
    private const int BooksPerRegionUnit = 10;
}
