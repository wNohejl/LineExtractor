using System.Globalization;
using System.Text.Json;
using XLines.Core.Analytics;
using XLines.Core.Contracts;
using XLines.Core.Entities;
using Microsoft.Extensions.Logging;

namespace XLines.Ingestion.Adapters;

/// <summary>
/// ESPN's undocumented public JSON endpoints: schedules, scores, rosters and box scores,
/// free and unauthenticated across every sport we track.
///
/// These are unofficial and can change without notice, which is a deliberate trade here:
/// nothing critical depends solely on ESPN, and when it does break it produces a genuine
/// incident to triage rather than a hypothetical one.
/// </summary>
public partial class EspnStatsAdapter(HttpClient http, ILogger<EspnStatsAdapter> logger) : IStatsSource, IClosingLineSource, IFighterSource, IGameDaySource
{
    public const string SourceKey = "espn";

    public string Key => SourceKey;

    /// <summary>
    /// Pause between calls, applied to every request this adapter makes rather than only to
    /// the box-score loop.
    ///
    /// ESPN publishes no rate limit, which is a reason for more care rather than less: there
    /// is no documented ceiling to stay under, so the only safe assumption is to stay quiet.
    /// It matters most under history backfill, where a slate day is one call per finished
    /// game and the walk covers hundreds of days — see <c>HistoryBackfillService</c>.
    /// </summary>
    public TimeSpan RequestDelay { get; set; } = TimeSpan.Zero;

    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    /// <summary>Nothing fetched, nothing spent — for a league this port does not serve.</summary>
    private static StatsFetchResult Empty => new([], [], [], new FetchCost(0));

    /// <summary>
    /// ESPN paths are {sport}/{league}, which does not match our flat sport keys. The mapping
    /// lives in <see cref="EspnLeagues"/> so adding a league does not mean editing parsing code.
    /// </summary>
    private string? MapPath(string sportKey)
    {
        var path = EspnLeagues.PathFor(sportKey);

        if (path is null)
            logger.LogDebug("{Source}: no league mapping for '{Sport}'", SourceKey, sportKey);

        return path;
    }

    public async Task<StatsFetchResult> FetchScheduleAsync(
        string sportKey, DateOnly date, CancellationToken ct)
    {
        if (MapPath(sportKey) is not { } path)
            return Empty;

        // A card is read whole from the fight center; the scoreboard alone has no method or order.
        if (FightSports.IsFight(sportKey))
            return await FetchCardsAsync(path, sportKey, date, withResults: false, ct);

        var url = $"{path}/scoreboard?dates={date:yyyyMMdd}";

        using var doc = await GetJsonAsync(url, ct);
        var games = ParseScoreboard(doc.RootElement, sportKey).ToList();

        logger.LogInformation("{Source}: {Sport} {Date} -> {Count} games",
            SourceKey, sportKey, date, games.Count);

        return new StatsFetchResult(games, [], [], new FetchCost(1));
    }

    /// <summary>
    /// Deliberately does nothing, and returns no players.
    ///
    /// <para>
    /// It used to call <c>/teams</c> and throw the response away, which read as an implemented
    /// roster sync and was not one. Implementing it properly would be worse: a roster is every
    /// player on the books, and storing a player who has not appeared in a box score is exactly
    /// the allocation <c>StatsIngestionService</c> now refuses — those rows would be created
    /// and immediately discarded.
    /// </para>
    ///
    /// <para>
    /// Players enter through box scores, because appearing in one is what makes a player worth
    /// a row. Team identity, the other thing <c>/teams</c> would have given us, already arrives
    /// on the scoreboard at no extra request.
    /// </para>
    /// </summary>
    public Task<StatsFetchResult> FetchRosterAsync(string sportKey, CancellationToken ct)
        => Task.FromResult(new StatsFetchResult([], [], [], new FetchCost(0)));

    public async Task<StatsFetchResult> FetchBoxScoresAsync(
        string sportKey, DateOnly date, CancellationToken ct)
    {
        if (MapPath(sportKey) is not { } path)
            return Empty;

        // MMA has no summary endpoint: the fight center is the box score, one request a card.
        if (FightSports.IsFight(sportKey))
            return await FetchCardsAsync(path, sportKey, date, withResults: true, ct);

        var scoreboard = await FetchScheduleAsync(sportKey, date, ct);
        var finals = scoreboard.Games.Where(g => g.Status == "final").ToList();

        var players = new List<CanonicalPlayer>();
        var stats = new List<CanonicalPlayerStat>();
        var lines = new List<CanonicalOdds>();
        var contexts = new List<CanonicalGameContext>();
        var requests = scoreboard.Cost.Requests;

        foreach (var game in finals)
        {
            ct.ThrowIfCancellationRequested();
            requests += await FetchSummaryBoxAsync(path, sportKey, game, players, stats, lines, contexts, ct);
        }

        return new StatsFetchResult(scoreboard.Games, players, stats, new FetchCost(requests), lines)
        {
            GameContexts = contexts
        };
    }

    /// <summary>
    /// One game's box score: its row from the day's scoreboard, then its summary, read exactly as
    /// <see cref="FetchBoxScoresAsync"/> reads every final on the day. Two requests where the
    /// day's walk is one per final — the price of the gamecast feed landing the box the moment the
    /// game it follows ends (spec 2026-10-24 §6.3.4) rather than on the next results sweep.
    /// </summary>
    public async Task<StatsFetchResult> FetchBoxScoreAsync(
        string sportKey, DateOnly date, string sourceGameId, CancellationToken ct)
    {
        if (FightSports.IsFight(sportKey) || MapPath(sportKey) is not { } path)
            return Empty;

        var scoreboard = await FetchScheduleAsync(sportKey, date, ct);
        var game = scoreboard.Games.FirstOrDefault(g => g.SourceGameId == sourceGameId && g.Status == "final");

        if (game is null)
            return Empty with { Cost = scoreboard.Cost };

        var players = new List<CanonicalPlayer>();
        var stats = new List<CanonicalPlayerStat>();
        var lines = new List<CanonicalOdds>();
        var contexts = new List<CanonicalGameContext>();
        var requests = scoreboard.Cost.Requests
                       + await FetchSummaryBoxAsync(path, sportKey, game, players, stats, lines, contexts, ct);

        return new StatsFetchResult([game], players, stats, new FetchCost(requests), lines)
        {
            GameContexts = contexts
        };
    }

    /// <summary>
    /// One final's summary into the players, stat lines, closing lines and game context being
    /// gathered. A box score that will not come is logged and skipped, so one game cannot cost a
    /// slate.
    /// </summary>
    /// <returns>The requests it made.</returns>
    private async Task<int> FetchSummaryBoxAsync(
        string path, string sportKey, CanonicalGame game,
        List<CanonicalPlayer> players, List<CanonicalPlayerStat> stats, List<CanonicalOdds> lines,
        List<CanonicalGameContext> contexts, CancellationToken ct)
    {
        var requests = 0;

        try
        {
            using var doc = await GetJsonAsync($"{path}/summary?event={game.SourceGameId}", ct);
            requests++;

            ParseBoxScore(doc.RootElement, sportKey, game.SourceGameId, players, stats);

            // The venue, the crowd and the officials are in the same document, so they are read
            // from it rather than thrown away with the rest of what the box score does not need.
            if (ParseGameContext(doc.RootElement, sportKey, game.SourceGameId) is { } context)
                contexts.Add(context);

            // The same response carries what the line closed at, so the historical
            // reference is free — no second request, no second walk.
            var closed = ParseClosingLines(
                doc.RootElement, game.SourceGameId,
                game.HomeTeamName, game.AwayTeamName, game.StartsAt).ToList();

            // Except where the summary has dropped its odds block, which it does for older
            // games. The odds endpoint still has the close; one request buys it back.
            if (closed.Count == 0)
            {
                try
                {
                    closed.AddRange(await FetchClosingLinesAsync(sportKey,
                        new ClosingLineRequest(game.SourceGameId, game.HomeTeamName, game.AwayTeamName, game.StartsAt), ct));
                    requests++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A line is a reference; its absence must not cost the box score.
                    logger.LogDebug(ex, "{Source}: no closing odds for {GameId}", SourceKey, game.SourceGameId);
                }
            }

            lines.AddRange(closed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // One unavailable box score must not cost us the rest of the slate.
            logger.LogWarning(ex, "{Source}: box score unavailable for {GameId}",
                SourceKey, game.SourceGameId);
        }

        return requests;
    }

    /// <summary>
    /// The circumstances of one game as its summary states them: the venue and its surface, the
    /// weather, the crowd and the officials from <c>gameInfo</c>, and ESPN's own pre-game chances
    /// from <c>predictor</c>.
    ///
    /// <para>
    /// What is in the document depends on when in the game's life it is read, and nothing here
    /// fills the gaps (ADR 0003). Measured: a final carries the venue, the attendance and the
    /// officials, and has dropped both the weather and the predictor; a scheduled game carries the
    /// venue, the predictor and — outdoors in the NFL — the weather, but no crowd and no officials.
    /// The weather states a temperature, a <c>conditionId</c> code and a precipitation figure; the
    /// condition in words is read only where ESPN writes it, as the scoreboard does, because a code
    /// table of ours would be words ESPN never said. No wind was seen on any summary, so none is read.
    /// </para>
    ///
    /// <para>
    /// The surface is read for football and baseball alone. ESPN sends <c>grass: false</c> for a
    /// basketball arena as well, and a hardwood court recorded as turf would be a fabrication with
    /// the provider's name on it. Whether the venue is indoor is not in the summary at all — it is
    /// on the scoreboard's venue — so it is read here only should ESPN ever add it.
    /// </para>
    ///
    /// <para>
    /// The predictor's <c>gameProjection</c> is a string ("94.8") and is kept as the percent it is,
    /// not divided down to a fraction (see <see cref="CanonicalGameContext.HomeWinPercent"/>).
    /// </para>
    /// </summary>
    /// <returns>The context, or null where the summary states none of it.</returns>
    internal static CanonicalGameContext? ParseGameContext(JsonElement root, string sportKey, string sourceGameId)
    {
        var info = JsonRead.Child(root, "gameInfo");
        var venue = JsonRead.Child(info, "venue");
        var address = JsonRead.Child(venue, "address");
        var weather = JsonRead.Child(info, "weather");

        var predictor = JsonRead.Child(root, "predictor");

        // Grass or not is a field's question; see the remarks for the arena that answers it anyway.
        var path = EspnLeagues.PathFor(sportKey);
        var playedOnAField = path is not null
                             && (path.StartsWith("football/", StringComparison.Ordinal)
                                 || path.StartsWith("baseball/", StringComparison.Ordinal));
        var grass = playedOnAField ? JsonRead.Bool(venue, "grass") : null;

        var context = new CanonicalGameContext(sourceGameId)
        {
            VenueName = JsonRead.Words(venue, "fullName"),
            VenueCity = JsonRead.Words(address, "city"),
            VenueState = JsonRead.Words(address, "state"),
            Indoor = JsonRead.Bool(venue, "indoor"),
            Surface = grass switch
            {
                true => GameSurfaces.Grass,
                false => GameSurfaces.Turf,
                null => null
            },
            WeatherCondition = JsonRead.Words(weather, "displayValue") ?? JsonRead.Words(weather, "condition"),
            TemperatureF = WholeNumber(weather, "temperature"),
            PrecipitationPercent = WholeNumber(weather, "precipitation"),

            // ESPN writes nought where it has no count, before a game and for some it never had;
            // a crowd of nobody is not a figure it measured.
            Attendance = WholeNumber(info, "attendance") is > 0 and var crowd ? crowd : null,
            Officials = ReadOfficials(info),
            HomeWinPercent = Number(JsonRead.Child(predictor, "homeTeam"), "gameProjection"),
            AwayWinPercent = Number(JsonRead.Child(predictor, "awayTeam"), "gameProjection"),
            MatchupQuality = Number(predictor, "matchupQuality")
        };

        // A context that states nothing is not news to anyone. The officials count: a summary that
        // names the crew and nothing else is still worth the write.
        return context.StatesAnything ? context : null;
    }

    /// <summary>
    /// The officials as <c>gameInfo</c> lists them. The NFL and NBA give a <c>fullName</c>, MLB only a
    /// <c>displayName</c> — some with a double space in it ("Nate  Tomlinson"), which is collapsed so
    /// one umpire is not two people by spacing. The role is the position's display name ("Referee",
    /// "Home Plate Umpire"); the order is ESPN's own. Null where nobody is named.
    /// </summary>
    private static IReadOnlyList<CanonicalOfficial>? ReadOfficials(JsonElement info)
    {
        if (info.ValueKind != JsonValueKind.Object
            || !info.TryGetProperty("officials", out var officials)
            || officials.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<CanonicalOfficial>();

        foreach (var official in officials.EnumerateArray())
        {
            if (official.ValueKind != JsonValueKind.Object
                || (JsonRead.Words(official, "fullName") ?? JsonRead.Words(official, "displayName")) is not { } name)
                continue;

            var position = JsonRead.Child(official, "position");

            list.Add(new CanonicalOfficial(
                string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
                JsonRead.Words(position, "displayName") ?? JsonRead.Words(position, "name"),
                JsonRead.Int(official, "order")));
        }

        return list.Count == 0 ? null : list;
    }

    /// <summary>
    /// A figure ESPN may write as a number (<c>65</c>) or as a string (<c>"94.8"</c>) — the predictor
    /// does the second. Null where it is neither.
    /// </summary>
    private static decimal? Number(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var v))
            return null;

        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetDecimal(out var d) => d,
            JsonValueKind.String when decimal.TryParse(v.GetString(), NumberStyles.Number,
                CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        };
    }

    /// <summary>
    /// <see cref="Number"/> rounded to a whole one, for degrees, percents and heads. Kept here rather
    /// than in <see cref="JsonRead"/> because it is not MLB's whole number: ESPN writes figures meant
    /// whole with a decimal in them and this rounds "82.5" up, where MLB's reader refuses it.
    /// </summary>
    private static int? WholeNumber(JsonElement parent, string name)
        => Number(parent, name) is { } n && n is >= int.MinValue and <= int.MaxValue
            ? (int)Math.Round(n, MidpointRounding.AwayFromZero)
            : null;

    /// <summary>
    /// The line as it closed, from the odds block ESPN attaches to a finished game's summary.
    ///
    /// <para>
    /// This is deliberately narrow. ESPN carries one book, and one book is a price rather than a
    /// market — which is exactly why <c>ADR 0011</c> refuses it as the odds port. Nothing here
    /// changes that: these rows are only ever read for games that have already finished, where
    /// the question is "what did the market think of this matchup" rather than "what should I
    /// bet now". A real book market always takes precedence; this fills the gap for the
    /// thousands of past fixtures nobody was watching live.
    /// </para>
    ///
    /// <para>
    /// ESPN states <c>close</c> explicitly alongside <c>open</c>, so this is the closing number
    /// as the provider reports it rather than the last value we happened to observe — a better
    /// record than our own scan tier could produce for a game we never watched.
    /// </para>
    ///
    /// <para>
    /// Prices are read from the per-side <c>close</c> blocks rather than the flatter
    /// <c>homeTeamOdds</c>/<c>overOdds</c> fields, because those carry the moneyline and total
    /// price but not the spread's, and a spread stored at an assumed -110 would be a fabricated
    /// number sitting in the same column as measured ones.
    /// </para>
    /// </summary>
    internal static IEnumerable<CanonicalOdds> ParseClosingLines(
        JsonElement root, string sourceGameId, string homeTeam, string awayTeam, DateTimeOffset capturedAt)
    {
        if (!root.TryGetProperty("pickcenter", out var pickcenter)
            || pickcenter.ValueKind != JsonValueKind.Array
            || pickcenter.GetArrayLength() == 0)
            yield break;

        var entry = pickcenter[0];

        var book = entry.TryGetProperty("provider", out var provider)
                   && provider.TryGetProperty("name", out var providerName)
            ? providerName.GetString() ?? "ESPN"
            : "ESPN";

        // Each side's open sits beside its close in the same node and is decoded the same way. It
        // rides on the close it belongs to, so a side ESPN did not close is not opened either.

        // Moneyline: a price per side, no line — not even an opening one.
        foreach (var (node, outcome) in Sides(entry, "moneyline", homeTeam, awayTeam))
        {
            if (ReadClose(node, Markets.Moneyline, out _, out var price))
                yield return new CanonicalOdds(sourceGameId, book, Markets.Moneyline, outcome, null, price, capturedAt)
                {
                    Opening = ReadOpen(node, Markets.Moneyline)
                };
        }

        // Spread: a handicap and a price per side.
        foreach (var (node, outcome) in Sides(entry, "pointSpread", homeTeam, awayTeam))
        {
            if (ReadClose(node, Markets.Spread, out var line, out var price) && line is not null)
                yield return new CanonicalOdds(sourceGameId, book, Markets.Spread, outcome, line, price, capturedAt)
                {
                    Opening = ReadOpen(node, Markets.Spread)
                };
        }

        // Total: one number for the game, quoted from both sides.
        foreach (var (node, outcome) in Sides(entry, "total", "over", "under"))
        {
            if (ReadClose(node, Markets.Total, out var line, out var price) && line is not null)
                yield return new CanonicalOdds(sourceGameId, book, Markets.Total, outcome, line, price, capturedAt)
                {
                    Opening = ReadOpen(node, Markets.Total)
                };
        }
    }

    /// <summary>
    /// What one finished game closed at, from ESPN's odds endpoint rather than the summary.
    ///
    /// <para>
    /// The summary's <c>pickcenter</c> is empty for most of a season gone by — the 2025 NFL
    /// season had none before late November — while
    /// <c>sports.core.api.espn.com/…/competitions/{id}/odds</c> still states the open and the
    /// close for every game. Same provider, same book, same rule: a reference for a game that
    /// has finished, never the market (ADR 0011).
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<CanonicalOdds>> FetchClosingLinesAsync(
        string sportKey, ClosingLineRequest game, CancellationToken ct)
    {
        if (MapPath(sportKey) is not { } path || path.Split('/') is not [var sport, var league])
            return [];

        var url = $"https://sports.core.api.espn.com/v2/sports/{sport}/leagues/{league}"
                  + $"/events/{game.SourceEventId ?? game.SourceGameId}/competitions/{game.SourceGameId}/odds";

        using var doc = await GetJsonAsync(url, ct);

        return ParseCoreOdds(doc.RootElement, game.SourceGameId, game.HomeTeamName, game.AwayTeamName, game.StartsAt)
            .ToList();
    }

    /// <summary>
    /// The close from the odds endpoint's shape: one item per provider, each with
    /// <c>homeTeamOdds</c>/<c>awayTeamOdds</c> carrying the moneyline and spread closes and a
    /// top-level <c>close</c> carrying the total and its two prices.
    ///
    /// <para>
    /// The pre-game book is read and the live one skipped: "ESPN Bet - Live Odds" is the number
    /// in play, which is the one thing a closing line must not be. Prices are the per-side
    /// closes, never the flatter <c>spreadOdds</c>/<c>overOdds</c>, which on this endpoint are
    /// the last live quote.
    /// </para>
    /// </summary>
    internal static IEnumerable<CanonicalOdds> ParseCoreOdds(
        JsonElement root, string sourceGameId, string homeTeam, string awayTeam, DateTimeOffset capturedAt)
    {
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            yield break;

        JsonElement? chosen = null;
        var book = "ESPN";

        foreach (var item in items.EnumerateArray())
        {
            var name = item.TryGetProperty("provider", out var provider)
                       && provider.TryGetProperty("name", out var providerName)
                ? providerName.GetString()
                : null;

            if (name is not null && name.Contains("live", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!item.TryGetProperty("close", out _)
                && !(item.TryGetProperty("homeTeamOdds", out var h) && h.TryGetProperty("close", out _)))
                continue;

            chosen = item;
            book = name ?? book;
            break;
        }

        if (chosen is not { } entry)
            yield break;

        // A fight's sides are athletes, not teams (ADR 0021); the block is the same shape either way.
        foreach (var (key, athleteKey, outcome) in new[]
                 {
                     ("homeTeamOdds", "homeAthleteOdds", homeTeam), ("awayTeamOdds", "awayAthleteOdds", awayTeam)
                 })
        {
            if (!(entry.TryGetProperty(key, out var side) || entry.TryGetProperty(athleteKey, out side))
                || !side.TryGetProperty("close", out var close))
                continue;

            // The side's open is a sibling of its close, in the same blocks; a side may state one
            // and not the other, which leaves the opening null rather than guessed.
            var open = side.TryGetProperty("open", out var o) && o.ValueKind == JsonValueKind.Object ? o : default;

            if (TryAmerican(close, "moneyLine", out var ml))
                yield return new CanonicalOdds(sourceGameId, book, Markets.Moneyline, outcome, null, ml, capturedAt)
                {
                    Opening = OpeningOf(null, OpenPrice(open, "moneyLine"))
                };

            if (TryLine(close, "pointSpread", spread: true, out var spread) && TryAmerican(close, "spread", out var spreadPrice))
                yield return new CanonicalOdds(sourceGameId, book, Markets.Spread, outcome, spread, spreadPrice, capturedAt)
                {
                    Opening = OpeningOf(OpenLine(open, "pointSpread", spread: true), OpenPrice(open, "spread"))
                };
        }

        if (entry.TryGetProperty("close", out var game) && TryLine(game, "total", spread: false, out var total))
        {
            // The total's open is one number for the game, as its close is, with a price per side.
            var open = entry.TryGetProperty("open", out var o) && o.ValueKind == JsonValueKind.Object ? o : default;
            var openTotal = OpenLine(open, "total", spread: false);

            if (TryAmerican(game, "over", out var over))
                yield return new CanonicalOdds(sourceGameId, book, Markets.Total, "over", total, over, capturedAt)
                {
                    Opening = OpeningOf(openTotal, OpenPrice(open, "over"))
                };

            if (TryAmerican(game, "under", out var under))
                yield return new CanonicalOdds(sourceGameId, book, Markets.Total, "under", total, under, capturedAt)
                {
                    Opening = OpeningOf(openTotal, OpenPrice(open, "under"))
                };
        }
    }

    /// <summary>An opening price block on the odds endpoint, or null where the open states none.</summary>
    private static int? OpenPrice(JsonElement open, string name)
        => open.ValueKind == JsonValueKind.Object && TryAmerican(open, name, out var price) ? price : null;

    /// <summary>An opening line block on the odds endpoint, or null where the open states none.</summary>
    private static decimal? OpenLine(JsonElement open, string name, bool spread)
        => open.ValueKind == JsonValueKind.Object && TryLine(open, name, spread, out var line) ? line : null;

    /// <summary>The two halves as an opening, or null where neither was stated.</summary>
    private static CanonicalOpening? OpeningOf(decimal? line, int? price)
        => line is null && price is null ? null : new CanonicalOpening(line, price);

    /// <summary>A price block on the odds endpoint: <c>{ "american": "-110", … }</c>.</summary>
    private static bool TryAmerican(JsonElement parent, string name, out int price)
    {
        price = 0;
        return parent.TryGetProperty(name, out var block)
               && TryParseAmerican(JsonRead.String(block, "american") ?? JsonRead.String(block, "alternateDisplayValue"), out price);
    }

    /// <summary>
    /// A line block on the odds endpoint: <c>{ "american": "-7.5" }</c>, or a total's "47.5". A
    /// pick'em reads as a spread of zero, as it does in the summary (<see cref="JsonRead.TryParseLine"/>).
    /// </summary>
    private static bool TryLine(JsonElement parent, string name, bool spread, out decimal line)
    {
        line = 0;

        return parent.TryGetProperty(name, out var block)
               && JsonRead.TryParseLine(JsonRead.String(block, "american") ?? JsonRead.String(block, "alternateDisplayValue"), spread, out line);
    }

    /// <summary>
    /// The two sides of one market, named as the platform names them. ESPN keys them
    /// home/away even for a total, where the sides are over/under.
    /// </summary>
    private static IEnumerable<(JsonElement Node, string Outcome)> Sides(
        JsonElement entry, string market, string first, string second)
    {
        if (!entry.TryGetProperty(market, out var block))
            yield break;

        var firstKey = market == "total" ? "over" : "home";
        var secondKey = market == "total" ? "under" : "away";

        if (block.TryGetProperty(firstKey, out var a))
            yield return (a, first);

        if (block.TryGetProperty(secondKey, out var b))
            yield return (b, second);
    }

    /// <summary>
    /// The closing line and price from one side of one market.
    ///
    /// Lines arrive decorated — "o8.5", "u8.5", "+1.5" — so the leading marker is stripped
    /// before parsing. A side missing its close is skipped rather than defaulted: a market
    /// ESPN did not price is not a market priced at zero.
    /// </summary>
    private static bool ReadClose(JsonElement side, string market, out decimal? line, out int price)
    {
        line = null;
        price = 0;

        if (!side.TryGetProperty("close", out var close))
            return false;

        if (!close.TryGetProperty("odds", out var oddsEl)
            || !TryParseAmerican(oddsEl.GetString(), out price))
            return false;

        line = ReadPickLine(close, market);

        return true;
    }

    /// <summary>
    /// What one side of one market opened at, from the <c>open</c> block beside its close, decoded
    /// as <see cref="ReadClose"/> decodes the close. Unlike the close, either half may stand alone:
    /// ESPN writes "OFF" for a price it took down, and the line opened at is still a fact. Null
    /// where the side states neither. A moneyline's open has no line, whatever the block says.
    /// </summary>
    private static CanonicalOpening? ReadOpen(JsonElement side, string market)
    {
        if (!side.TryGetProperty("open", out var open) || open.ValueKind != JsonValueKind.Object)
            return null;

        int? price = JsonRead.String(open, "odds") is { } raw && TryParseAmerican(raw, out var parsed) ? parsed : null;

        return OpeningOf(ReadPickLine(open, market), price);
    }

    /// <summary>
    /// The line in a pickcenter <c>open</c> or <c>close</c> block, parsed as the odds endpoint's is
    /// (<see cref="JsonRead.TryParseLine"/>): decorated lines stripped, and a pick'em a spread of
    /// zero. It used to refuse "PK", which dropped a pick'em's spread from the summary — and, with
    /// the moneyline read, the odds endpoint was never asked, so the spread stayed missing. A
    /// moneyline has no line, whatever the block says.
    /// </summary>
    private static decimal? ReadPickLine(JsonElement block, string market)
        => market != Markets.Moneyline
           && JsonRead.TryParseLine(JsonRead.String(block, "line"), market == Markets.Spread, out var line)
            ? line
            : null;

    /// <summary>
    /// An American price as ESPN writes it: "+130", "-157", and occasionally "EVEN".
    /// </summary>
    internal static bool TryParseAmerican(string? raw, out int price)
    {
        price = 0;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var text = raw.Trim();

        // "EVEN" is a real quote meaning +100, and dropping it would silently lose a market.
        if (text.Equals("EVEN", StringComparison.OrdinalIgnoreCase))
        {
            price = 100;
            return true;
        }

        return int.TryParse(text.TrimStart('+'), NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out price);
    }

    private async Task<JsonDocument> GetJsonAsync(string relativeUrl, CancellationToken ct)
    {
        // Spacing is measured from the last call rather than slept unconditionally, so a slow
        // response has already paid the gap and does not pay it twice.
        if (RequestDelay > TimeSpan.Zero)
        {
            var since = DateTimeOffset.UtcNow - _lastRequestAt;

            if (since < RequestDelay)
                await Task.Delay(RequestDelay - since, ct);
        }

        _lastRequestAt = DateTimeOffset.UtcNow;

        // Headers first, then the body straight into the parser. A game summary is close to half a
        // megabyte of JSON and the gamecast feed reads one per followed game every poll; buffering
        // each whole before parsing it held the document in memory twice.
        using var response = await http.GetAsync(relativeUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    internal static IEnumerable<CanonicalGame> ParseScoreboard(JsonElement root, string sportKey)
    {
        if (!root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var e in events.EnumerateArray())
        {
            var id = e.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (id is null)
                continue;

            if (IsPreseason(e))
                continue;

            if (!e.TryGetProperty("competitions", out var comps)
                || comps.ValueKind != JsonValueKind.Array
                || comps.GetArrayLength() == 0)
                continue;

            var comp = comps[0];

            if (IsExhibition(comp))
                continue;

            if (!comp.TryGetProperty("competitors", out var competitors)
                || competitors.ValueKind != JsonValueKind.Array)
                continue;

            CanonicalTeamRef? home = null, away = null;
            int? homeScore = null, awayScore = null;
            IReadOnlyList<int>? homeLines = null, awayLines = null;

            foreach (var c in competitors.EnumerateArray())
            {
                var isHome = c.TryGetProperty("homeAway", out var ha) && ha.GetString() == "home";

                // ESPN gives a stable team id and the official abbreviation right here, on a
                // response we were already fetching. Reading only the display name meant the
                // resolver matched teams by wording and invented abbreviations from it.
                var reference = ReadTeam(c);

                int? score = c.TryGetProperty("score", out var sc)
                             && int.TryParse(sc.GetString(), out var parsed)
                    ? parsed
                    : null;

                if (isHome)
                {
                    home = reference;
                    homeScore = score;
                    homeLines = ReadLinescore(c);
                }
                else
                {
                    away = reference;
                    awayScore = score;
                    awayLines = ReadLinescore(c);
                }
            }

            if (home is null || away is null)
                continue;

            var startsAt = e.TryGetProperty("date", out var d)
                           && DateTimeOffset.TryParse(d.GetString(), CultureInfo.InvariantCulture,
                               DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                               out var parsedDate)
                ? parsedDate
                : DateTimeOffset.UtcNow;

            yield return new CanonicalGame(
                SourceGameId: id,
                SportKey: sportKey,
                HomeTeamName: home.Name,
                AwayTeamName: away.Name,
                StartsAt: startsAt,
                Status: ReadStatus(comp),
                HomeScore: homeScore,
                AwayScore: awayScore,
                Home: home,
                Away: away,
                SeasonYear: ReadSeasonYear(e) is { } espnYear ? Core.Analytics.SeasonCalendar.FromEspnYear(sportKey, espnYear) : null,
                SeasonType: ReadSeasonType(e),
                Live: ReadLive(comp, homeLines, awayLines));
        }
    }

    /// <summary>
    /// The state of play the scoreboard carries beside the score (spec 2026-10-24 §4.1): clock,
    /// period, ESPN's short status line and — while the game is in play — the situation.
    ///
    /// <para>
    /// This used to be fetched every poll and thrown away, which is why the desk showed a score and
    /// the word "Live" while ESPN's own page showed the down and the last play. Every field is read
    /// defensively: ESPN documents none of them, a situation is present only while a game is in
    /// play, and a field in a shape we did not expect reads as null rather than costing the slate.
    /// </para>
    ///
    /// <para>
    /// Before the start ESPN still sends period 0 and a "0:00" clock. Those are placeholders, not a
    /// state of play, so a game that has not begun carries only its status line ("11/1 - 8:20 PM").
    /// </para>
    /// </summary>
    internal static CanonicalLive? ReadLive(
        JsonElement competition, IReadOnlyList<int>? homeLines, IReadOnlyList<int>? awayLines)
    {
        if (!competition.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Object)
            return null;

        var type = JsonRead.Child(status, "type");
        var state = JsonRead.String(type, "state");
        var detail = Fit(JsonRead.String(type, "shortDetail"), Game.StatusDetailLength);

        if (state == "pre")
            return new CanonicalLive(null, null, null, detail, null, null);

        var clock = status.TryGetProperty("clock", out var cl) && cl.ValueKind == JsonValueKind.Number
                    && cl.TryGetDouble(out var seconds)
            ? (int?)Math.Round(seconds)
            : null;

        var live = new CanonicalLive(
            Period: JsonRead.Int(status, "period"),
            DisplayClock: Fit(JsonRead.String(status, "displayClock"), Game.DisplayClockLength),
            ClockSeconds: clock,
            StatusDetail: detail,
            HomeLinescore: homeLines,
            AwayLinescore: awayLines);

        // ESPN sends a situation only while the game is in play — measured null on the final.
        if (!competition.TryGetProperty("situation", out var s) || s.ValueKind != JsonValueKind.Object)
            return live;

        return live with
        {
            PossessionSourceTeamId = s.TryGetProperty("possession", out var p)
                ? p.ValueKind switch
                {
                    JsonValueKind.String => p.GetString(),
                    JsonValueKind.Number => p.GetRawText(),
                    _ => null
                }
                : null,
            Down = JsonRead.Int(s, "down"),
            Distance = JsonRead.Int(s, "distance"),
            YardLine = JsonRead.Int(s, "yardLine"),
            DownDistanceText = Fit(JsonRead.String(s, "downDistanceText"), Game.DownDistanceTextLength),
            PossessionText = Fit(JsonRead.String(s, "possessionText"), Game.PossessionTextLength),
            IsRedZone = JsonRead.Bool(s, "isRedZone"),
            HomeTimeouts = JsonRead.Int(s, "homeTimeouts"),
            AwayTimeouts = JsonRead.Int(s, "awayTimeouts"),
            LastPlay = s.TryGetProperty("lastPlay", out var lp) && lp.ValueKind == JsonValueKind.Object
                ? JsonRead.String(lp, "text")
                : null,
            SituationJson = ReadDiamond(s)
        };
    }

    /// <summary>
    /// Baseball's situation — the count, the outs, the runners and the matchup — as a small json
    /// bag, since none of it has a column. Only the keys ESPN sent in the shape expected are kept;
    /// null when none were, which is every football game.
    /// </summary>
    private static string? ReadDiamond(JsonElement situation)
    {
        var bag = new System.Text.Json.Nodes.JsonObject();

        foreach (var key in (string[])["balls", "strikes", "outs"])
            if (JsonRead.Int(situation, key) is { } n)
                bag[key] = n;

        foreach (var key in (string[])["onFirst", "onSecond", "onThird"])
            if (JsonRead.Bool(situation, key) is { } b)
                bag[key] = b;

        foreach (var key in (string[])["batter", "pitcher"])
            if (situation.TryGetProperty(key, out var who) && who.ValueKind == JsonValueKind.Object
                && who.TryGetProperty("athlete", out var athlete) && athlete.ValueKind == JsonValueKind.Object
                && JsonRead.String(athlete, "displayName") is { } name)
                bag[key] = name;

        return bag.Count == 0 ? null : bag.ToJsonString();
    }

    /// <summary>
    /// A competitor's points by period, first period first. Null when ESPN sent none — before the
    /// start — or any entry is unreadable: a linescore with a hole in it would put the third
    /// quarter's points under the second.
    /// </summary>
    private static IReadOnlyList<int>? ReadLinescore(JsonElement competitor)
    {
        if (!competitor.TryGetProperty("linescores", out var lines)
            || lines.ValueKind != JsonValueKind.Array
            || lines.GetArrayLength() == 0)
            return null;

        var points = new List<int>(lines.GetArrayLength());

        foreach (var line in lines.EnumerateArray())
        {
            if (line.ValueKind != JsonValueKind.Object
                || !line.TryGetProperty("value", out var v)
                || v.ValueKind != JsonValueKind.Number
                || !v.TryGetDouble(out var value))
                return null;

            points.Add((int)Math.Round(value));
        }

        return points;
    }

    /// <summary>
    /// A string cut to its column's length. ESPN documents none of the state-of-play strings, and
    /// one longer than its column would fail the save that carries the whole slate. A cut never
    /// splits a surrogate pair.
    /// </summary>
    private static string? Fit(string? value, int length)
        => ColumnText.Fit(value, length);

    /// <summary>
    /// The season ESPN stamps on the event itself — year and type — read from the event rather
    /// than the payload root for the same reason <see cref="IsPreseason"/> is: the root describes
    /// the season the query fell in, the event describes the game. A February playoff carries
    /// the prior year, which is exactly what a season filter needs and a date alone cannot say.
    /// </summary>
    internal static int? ReadSeasonYear(JsonElement e)
        => e.TryGetProperty("season", out var season)
           && season.TryGetProperty("year", out var year)
           && year.ValueKind == JsonValueKind.Number
            ? year.GetInt32()
            : null;

    /// <summary>ESPN's season types: 1 preseason, 2 regular, 3 postseason. Anything else is unknown.</summary>
    internal static SeasonType? ReadSeasonType(JsonElement e)
        => e.TryGetProperty("season", out var season)
           && season.TryGetProperty("type", out var type)
           && type.ValueKind == JsonValueKind.Number
            ? type.GetInt32() switch
            {
                1 => SeasonType.Preseason,
                2 => SeasonType.Regular,
                3 => SeasonType.Postseason,
                _ => null
            }
            : null;

    /// <summary>
    /// A competitor's team identity: id and official abbreviation alongside the name.
    /// Returns null when there is no usable name, which is the one field resolution cannot
    /// do without.
    /// </summary>
    private static CanonicalTeamRef? ReadTeam(JsonElement competitor)
    {
        if (!competitor.TryGetProperty("team", out var team))
            return null;

        var name = team.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;

        if (string.IsNullOrWhiteSpace(name))
            return null;

        var id = team.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        var abbrev = team.TryGetProperty("abbreviation", out var ab) ? ab.GetString() : null;

        return new CanonicalTeamRef(name, id, abbrev);
    }

    /// <summary>
    /// Whether this event is an exhibition rather than a game that counted.
    ///
    /// <para>
    /// The scoreboard returns spring training beside the regular season without being asked:
    /// 24 March 2026 is preseason, 25 March is opening day, and both answer the same query. Those
    /// games have provisional rosters, minor-leaguers batting in the ninth and results nobody
    /// records — so ingesting them puts losses on a team's record that never happened and stat
    /// lines in a player's form from games that did not count.
    /// </para>
    ///
    /// <para>
    /// Read from the <i>event</i> rather than the payload root, which reports the season the
    /// query falls in rather than the one the game belongs to — on a spring training date the
    /// root already says "Regular Season" while every event on it says preseason. Only type 1 is
    /// excluded: the postseason is type 3 and unambiguously counts.
    /// </para>
    /// </summary>
    private static bool IsPreseason(JsonElement e)
        => e.TryGetProperty("season", out var season)
           && season.TryGetProperty("type", out var type)
           && type.ValueKind == JsonValueKind.Number
           && type.GetInt32() == 1;

    /// <summary>
    /// Whether the competition is an exhibition wearing a regular-season label.
    ///
    /// <para>
    /// The All-Star Game is reported inside the regular season — season type 2, on an ordinary
    /// July date — so <see cref="IsPreseason"/> passes it through. It then resolves normally and
    /// invents two franchises to hold it: "American All-Stars" and "National All-Stars" appeared
    /// in the team list beside the thirty real ones, each with a record of one game.
    /// </para>
    ///
    /// <para>
    /// The competition carries the distinction that the season does not: <c>ALLSTAR</c> against
    /// <c>STD</c> for a game that counts. Named types are rejected rather than unnamed ones
    /// accepted, because the costs are not symmetric — letting an exhibition through adds a row
    /// that can be found and removed, while requiring a known type would silently drop every
    /// postseason game the first time ESPN labels one differently.
    /// </para>
    /// </summary>
    private static bool IsExhibition(JsonElement competition)
        => competition.TryGetProperty("type", out var type)
           && type.TryGetProperty("abbreviation", out var abbrev)
           && abbrev.GetString() is "ALLSTAR";

    private static string? ReadStatus(JsonElement competition)
    {
        if (!competition.TryGetProperty("status", out var status)
            || !status.TryGetProperty("type", out var type)
            || !type.TryGetProperty("name", out var name))
            return null;

        // ESPN's coarse state beside the name: pre, in or post. It is what catches the in-game
        // statuses nobody listed — a rain delay, the end of an inning or a quarter, overtime —
        // which used to fall through to "scheduled" and walk a game in progress back to before
        // its first pitch.
        var state = type.TryGetProperty("state", out var s) ? s.GetString() : null;

        return name.GetString() switch
        {
            // A forfeit is a final with a score. A suspended or abandoned game is not
            // coming back on this date: folding it into "scheduled" kept its date owed
            // and the results sweep re-walking every box score on it for weeks.
            "STATUS_FINAL" or "STATUS_FORFEIT" => "final",
            "STATUS_IN_PROGRESS" or "STATUS_HALFTIME" => "live",
            "STATUS_POSTPONED" or "STATUS_CANCELED" or "STATUS_CANCELLED"
                or "STATUS_SUSPENDED" or "STATUS_ABANDONED" => "postponed",
            _ when state == "in" => "live",
            _ => "scheduled"
        };
    }

    /// <summary>
    /// Box-score shapes differ per sport, so the per-player stat line is kept as jsonb
    /// rather than forced into a fixed column set that would only fit one sport.
    ///
    /// <para>
    /// ESPN reports a player once per <i>category</i> — a batter appears under batting and
    /// again under fielding, a quarterback under passing and rushing. Storage allows one row
    /// per player per game per source (<c>ux_player_game_stat</c>), so categories are merged
    /// into a single line here rather than emitted as several. Emitting several put two
    /// inserts with the same key into one transaction, and the day failed on a duplicate-key
    /// violation the moment a player did two things in a game.
    /// </para>
    ///
    /// <para>
    /// Merging is only safe if collisions are handled, because categories reuse short labels —
    /// <c>H</c> is hits under batting and hits allowed under pitching. Keys are therefore kept
    /// plain until two groups disagree about one, at which point <i>both</i> are qualified with
    /// their group (<c>batting.H</c>, <c>pitching.H</c>). Qualifying everything unconditionally
    /// was the first attempt and was worse: ESPN leaves the group unnamed for some sports —
    /// MLB among them — so it produced no prefix exactly where the collisions are, and ugly
    /// ones everywhere else.
    /// </para>
    ///
    /// <para>
    /// The line stays a flat string map, which is what the Players panel deserialises and
    /// derives its columns from.
    /// </para>
    /// </summary>
    internal static void ParseBoxScore(
        JsonElement root,
        string sportKey,
        string sourceGameId,
        List<CanonicalPlayer> players,
        List<CanonicalPlayerStat> stats)
    {
        if (!root.TryGetProperty("boxscore", out var boxscore)
            || !boxscore.TryGetProperty("players", out var teamGroups)
            || teamGroups.ValueKind != JsonValueKind.Array)
            return;

        // One accumulating line per player, for the duration of this game.
        var lines = new Dictionary<string, PlayerLine>();
        var groupOrdinal = 0;

        // The side each player's line was listed under. The box score is grouped by team, so
        // this is what the player played for in this game — which the player list above cannot
        // say, because it is deduplicated across a whole day's payload.
        var sides = new Dictionary<string, (string? Id, string? Name)>();

        foreach (var teamGroup in teamGroups.EnumerateArray())
        {
            var teamName = teamGroup.TryGetProperty("team", out var team)
                           && team.TryGetProperty("displayName", out var dn)
                ? dn.GetString()
                : null;

            var teamId = teamGroup.TryGetProperty("team", out var t)
                         && t.TryGetProperty("id", out var tid)
                ? tid.GetString()
                : null;

            if (!teamGroup.TryGetProperty("statistics", out var statGroups)
                || statGroups.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var statGroup in statGroups.EnumerateArray())
            {
                var labels = statGroup.TryGetProperty("labels", out var l)
                             && l.ValueKind == JsonValueKind.Array
                    ? l.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray()
                    : [];

                var category = statGroup.TryGetProperty("name", out var cat) ? cat.GetString() : null;

                // ESPN names the group for some sports and not others — NHL gives "forwards"
                // and "goalies", MLB gives nothing at all. An unnamed group still needs an
                // identity, because it is what a collision gets qualified by; it just should
                // not be reported as a category the operator can read anything into.
                var named = !string.IsNullOrWhiteSpace(category);
                var groupLabel = named ? category! : $"g{groupOrdinal}";
                groupOrdinal++;

                if (!statGroup.TryGetProperty("athletes", out var athletes)
                    || athletes.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var athlete in athletes.EnumerateArray())
                {
                    if (!athlete.TryGetProperty("athlete", out var a))
                        continue;

                    var playerId = a.TryGetProperty("id", out var pid) ? pid.GetString() : null;
                    var fullName = a.TryGetProperty("displayName", out var pn) ? pn.GetString() : null;

                    if (playerId is null || fullName is null)
                        continue;

                    // ESPN lists the club itself among some groups' athletes (team fumbles and
                    // the like) under a negative id and the name " Team". That is not a person,
                    // and taking it as one put a "Team" on a roster with a stat line.
                    if (playerId.StartsWith('-'))
                        continue;

                    var position = a.TryGetProperty("position", out var pos)
                                   && pos.TryGetProperty("abbreviation", out var pa)
                        ? pa.GetString()
                        : null;

                    if (!players.Any(p => p.SourcePlayerId == playerId))
                    {
                        players.Add(new CanonicalPlayer(
                            playerId, sportKey, fullName, position, teamId, teamName));
                    }

                    var values = athlete.TryGetProperty("stats", out var s)
                                 && s.ValueKind == JsonValueKind.Array
                        ? s.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray()
                        : [];

                    if (values.Length == 0)
                        continue;

                    if (!lines.TryGetValue(playerId, out var line))
                        lines[playerId] = line = new PlayerLine();

                    sides.TryAdd(playerId, (teamId, teamName));

                    for (var i = 0; i < values.Length && i < labels.Length; i++)
                        line.Merge(groupLabel, labels[i], values[i], named);
                }
            }
        }

        foreach (var (playerId, line) in lines)
        {
            // "category" rather than "categories": the Players panel already treats that key
            // as metadata and keeps it out of the derived column set. Omitted entirely when
            // the provider named no groups, rather than recorded as a row of placeholders.
            if (line.Categories.Count > 0)
                line.Values["category"] = string.Join(',', line.Categories);

            var side = sides.GetValueOrDefault(playerId);

            stats.Add(new CanonicalPlayerStat(
                playerId, sourceGameId, JsonSerializer.Serialize(line.Values), side.Id, side.Name));
        }
    }

    /// <summary>
    /// A player's stat line, assembled across however many groups the provider reports them in.
    ///
    /// Keys stay plain until two groups disagree about one; then both sides are qualified with
    /// their group name so neither is lost. See <see cref="ParseBoxScore"/> for why that is
    /// on-demand rather than always.
    /// </summary>
    private sealed class PlayerLine
    {
        /// <summary>Which group each unqualified key came from.</summary>
        private readonly Dictionary<string, string> _origin = [];

        /// <summary>Keys that have already collided; every later group qualifies them too.</summary>
        private readonly HashSet<string> _qualified = [];

        public Dictionary<string, string> Values { get; } = [];

        /// <summary>Named groups only, ordered and de-duplicated.</summary>
        public SortedSet<string> Categories { get; } = [];

        public void Merge(string group, string key, string value, bool named)
        {
            if (named)
                Categories.Add(group);

            if (_qualified.Contains(key))
            {
                Values[$"{group}.{key}"] = value;
                return;
            }

            if (!_origin.TryGetValue(key, out var owner))
            {
                Values[key] = value;
                _origin[key] = group;
                return;
            }

            // The same group restating a value: nothing is lost by letting it stand.
            if (owner == group)
            {
                Values[key] = value;
                return;
            }

            // A second group reporting the key. Qualify the incumbent as well as the newcomer,
            // so the line never contains a bare key whose meaning depends on which group won —
            // even when the two agree: a back with 15 rushing and 15 receiving yards was stored
            // as one bare YDS, and the back's line had no rushing yards to show.
            Values[$"{owner}.{key}"] = Values[key];
            Values.Remove(key);
            _origin.Remove(key);
            _qualified.Add(key);

            Values[$"{group}.{key}"] = value;
        }
    }
}
