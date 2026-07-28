using System.Globalization;
using System.Text.Json;
using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Ingestion.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineOps.Ingestion.Adapters;

/// <summary>
/// Primary odds feed (odds-api.io). Free tier: 100 req/h, 500 req/day, moneyline/spread/total,
/// two recreational books. Costed in requests rather than credits.
///
/// Deliberately two calls per sport — /events then /odds/multi — so a full slate costs
/// 2 requests regardless of game count, which is what keeps the daily budget comfortable.
/// </summary>
public class OddsApiIoAdapter(
    HttpClient http,
    IOptions<IngestionOptions> options,
    ILogger<OddsApiIoAdapter> logger) : IOddsSource
{
    public const string SourceKey = "odds-api-io";

    private readonly SourceOptions _config = options.Value.OddsApiIo;

    public string Key => SourceKey;

    public IReadOnlyList<string> SupportedMarkets => Markets.V1;

    public async Task<OddsFetchResult> FetchSlateAsync(
        string sportKey,
        IReadOnlyList<string> markets,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
            throw new InvalidOperationException($"{SourceKey}: no API key configured.");

        var requests = 0;

        var eventsUrl = $"events?sport={Uri.EscapeDataString(MapSport(sportKey))}&apiKey={_config.ApiKey}";
        using var eventsDoc = await GetJsonAsync(eventsUrl, ct);
        requests++;

        var games = ParseEvents(eventsDoc.RootElement, sportKey).ToList();
        if (games.Count == 0)
        {
            logger.LogInformation("{Source}: no upcoming events for {Sport}", SourceKey, sportKey);
            return new OddsFetchResult(games, [], new FetchCost(requests));
        }

        // Batch every event into a single odds call — this is the whole point of /odds/multi.
        var ids = string.Join(',', games.Select(g => g.SourceGameId));
        var books = string.Join(',', _config.EffectiveBookmakers);
        var oddsUrl = $"odds/multi?eventIds={Uri.EscapeDataString(ids)}"
                    + $"&bookmakers={Uri.EscapeDataString(books)}&apiKey={_config.ApiKey}";

        using var oddsDoc = await GetJsonAsync(oddsUrl, ct);
        requests++;

        var capturedAt = DateTimeOffset.UtcNow;
        var odds = ParseOdds(oddsDoc.RootElement, markets, capturedAt).ToList();

        logger.LogInformation(
            "{Source}: {Sport} -> {Games} games, {Odds} prices in {Requests} requests",
            SourceKey, sportKey, games.Count, odds.Count, requests);

        return new OddsFetchResult(games, odds, new FetchCost(requests));
    }

    private async Task<JsonDocument> GetJsonAsync(string relativeUrl, CancellationToken ct)
    {
        using var response = await http.GetAsync(relativeUrl, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    /// <summary>Provider slugs differ from our sport keys; keep the mapping in one place.</summary>
    private static string MapSport(string sportKey) => sportKey switch
    {
        "nfl" => "american-football",
        "nba" => "basketball",
        "mlb" => "baseball",
        "nhl" => "ice-hockey",
        _ => sportKey
    };

    /// <summary>
    /// Providers rename fields between versions, so every read is tolerant: we try the
    /// documented name and fall back through known aliases rather than throwing.
    /// A schema drift shows up as a volume anomaly in the KPIs, not a crash.
    /// </summary>
    internal static IEnumerable<CanonicalGame> ParseEvents(JsonElement root, string sportKey)
    {
        var array = Unwrap(root);
        if (array.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var e in array.EnumerateArray())
        {
            var id = ReadString(e, "id", "eventId", "_id");
            var home = ReadString(e, "home", "homeTeam", "home_team");
            var away = ReadString(e, "away", "awayTeam", "away_team");
            var startsRaw = ReadString(e, "date", "startTime", "commence_time", "start");

            if (id is null || home is null || away is null)
                continue;

            var startsAt = ParseDate(startsRaw) ?? DateTimeOffset.UtcNow;

            yield return new CanonicalGame(
                SourceGameId: id,
                SportKey: sportKey,
                HomeTeamName: home,
                AwayTeamName: away,
                StartsAt: startsAt,
                Status: ReadString(e, "status"));
        }
    }

    internal static IEnumerable<CanonicalOdds> ParseOdds(
        JsonElement root,
        IReadOnlyList<string> markets,
        DateTimeOffset capturedAt)
    {
        var array = Unwrap(root);
        if (array.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var entry in array.EnumerateArray())
        {
            var eventId = ReadString(entry, "eventId", "id", "event_id");
            if (eventId is null)
                continue;

            if (!TryGet(entry, out var bookmakers, "bookmakers", "books"))
                continue;

            foreach (var book in bookmakers.EnumerateArray())
            {
                var bookName = ReadString(book, "name", "key", "bookmaker") ?? "unknown";

                if (!TryGet(book, out var marketList, "markets", "odds"))
                    continue;

                foreach (var market in marketList.EnumerateArray())
                {
                    var marketName = Normalise(ReadString(market, "name", "key", "market"));
                    if (marketName is null || !markets.Contains(marketName))
                        continue;

                    if (!TryGet(market, out var outcomes, "outcomes", "selections", "prices"))
                        continue;

                    foreach (var outcome in outcomes.EnumerateArray())
                    {
                        var name = ReadString(outcome, "name", "outcome", "selection");
                        var price = ReadInt(outcome, "price", "odds", "american", "priceAmerican");

                        if (name is null || price is null || price == 0)
                            continue;

                        yield return new CanonicalOdds(
                            SourceGameId: eventId,
                            Book: bookName.ToLowerInvariant(),
                            Market: marketName,
                            Outcome: name,
                            Line: ReadDecimal(outcome, "point", "line", "handicap", "total"),
                            PriceAmerican: price.Value,
                            CapturedAt: capturedAt);
                    }
                }
            }
        }
    }

    /// <summary>Maps provider market names onto our canonical keys.</summary>
    private static string? Normalise(string? market) => market?.ToLowerInvariant() switch
    {
        "h2h" or "moneyline" or "ml" or "1x2" or "match winner" => Markets.Moneyline,
        "spread" or "spreads" or "handicap" or "asian handicap" or "point spread" => Markets.Spread,
        "total" or "totals" or "over/under" or "over_under" or "ou" => Markets.Total,
        _ => null
    };

    /// <summary>Some responses wrap the payload in { data: [...] }; unwrap transparently.</summary>
    private static JsonElement Unwrap(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root;

        foreach (var name in (string[])["data", "events", "results", "odds"])
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(name, out var inner)
                && inner.ValueKind == JsonValueKind.Array)
                return inner;
        }

        return root;
    }

    private static bool TryGet(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out value)
                && value.ValueKind == JsonValueKind.Array)
                return true;
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var v))
                continue;

            switch (v.ValueKind)
            {
                case JsonValueKind.String:
                    return v.GetString();
                case JsonValueKind.Number:
                    return v.ToString();
                case JsonValueKind.Object:
                    // Nested { name: "..." } shapes for teams.
                    if (v.TryGetProperty("name", out var nested) && nested.ValueKind == JsonValueKind.String)
                        return nested.GetString();
                    break;
            }
        }

        return null;
    }

    private static int? ReadInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var v))
                continue;

            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i))
                return i;

            if (v.ValueKind == JsonValueKind.String
                && int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }

        return null;
    }

    private static decimal? ReadDecimal(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var v))
                continue;

            if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d))
                return d;

            if (v.ValueKind == JsonValueKind.String
                && decimal.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }

        return null;
    }

    private static DateTimeOffset? ParseDate(string? raw)
        => DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
}
