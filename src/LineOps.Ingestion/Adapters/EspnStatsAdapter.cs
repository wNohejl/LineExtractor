using System.Globalization;
using System.Text.Json;
using LineOps.Core.Analytics;
using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using Microsoft.Extensions.Logging;

namespace LineOps.Ingestion.Adapters;

/// <summary>
/// ESPN's undocumented public JSON endpoints: schedules, scores, rosters and box scores,
/// free and unauthenticated across every sport we track.
///
/// These are unofficial and can change without notice, which is a deliberate trade here:
/// nothing critical depends solely on ESPN, and when it does break it produces a genuine
/// incident to triage rather than a hypothetical one.
/// </summary>
public partial class EspnStatsAdapter(HttpClient http, ILogger<EspnStatsAdapter> logger) : IStatsSource, IClosingLineSource, IFighterSource
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
        var requests = scoreboard.Cost.Requests;

        foreach (var game in finals)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using var doc = await GetJsonAsync($"{path}/summary?event={game.SourceGameId}", ct);
                requests++;

                ParseBoxScore(doc.RootElement, sportKey, game.SourceGameId, players, stats);

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
            catch (Exception ex)
            {
                // One unavailable box score must not cost us the rest of the slate.
                logger.LogWarning(ex, "{Source}: box score unavailable for {GameId}",
                    SourceKey, game.SourceGameId);
            }
        }

        return new StatsFetchResult(scoreboard.Games, players, stats, new FetchCost(requests), lines);
    }

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

        // Moneyline: a price per side, no line.
        foreach (var (node, outcome) in Sides(entry, "moneyline", homeTeam, awayTeam))
        {
            if (ReadClose(node, out _, out var price))
                yield return new CanonicalOdds(sourceGameId, book, Markets.Moneyline, outcome, null, price, capturedAt);
        }

        // Spread: a handicap and a price per side.
        foreach (var (node, outcome) in Sides(entry, "pointSpread", homeTeam, awayTeam))
        {
            if (ReadClose(node, out var line, out var price) && line is not null)
                yield return new CanonicalOdds(sourceGameId, book, Markets.Spread, outcome, line, price, capturedAt);
        }

        // Total: one number for the game, quoted from both sides.
        foreach (var (node, outcome) in Sides(entry, "total", "over", "under"))
        {
            if (ReadClose(node, out var line, out var price) && line is not null)
                yield return new CanonicalOdds(sourceGameId, book, Markets.Total, outcome, line, price, capturedAt);
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

            if (TryAmerican(close, "moneyLine", out var ml))
                yield return new CanonicalOdds(sourceGameId, book, Markets.Moneyline, outcome, null, ml, capturedAt);

            if (TryLine(close, "pointSpread", out var spread) && TryAmerican(close, "spread", out var spreadPrice))
                yield return new CanonicalOdds(sourceGameId, book, Markets.Spread, outcome, spread, spreadPrice, capturedAt);
        }

        if (entry.TryGetProperty("close", out var game) && TryLine(game, "total", out var total))
        {
            if (TryAmerican(game, "over", out var over))
                yield return new CanonicalOdds(sourceGameId, book, Markets.Total, "over", total, over, capturedAt);

            if (TryAmerican(game, "under", out var under))
                yield return new CanonicalOdds(sourceGameId, book, Markets.Total, "under", total, under, capturedAt);
        }
    }

    /// <summary>A price block on the odds endpoint: <c>{ "american": "-110", … }</c>.</summary>
    private static bool TryAmerican(JsonElement parent, string name, out int price)
    {
        price = 0;
        return parent.TryGetProperty(name, out var block)
               && TryParseAmerican(Text(block, "american") ?? Text(block, "alternateDisplayValue"), out price);
    }

    /// <summary>A line block on the odds endpoint: <c>{ "american": "-7.5" }</c>, or a total's "47.5".</summary>
    private static bool TryLine(JsonElement parent, string name, out decimal line)
    {
        line = 0;

        if (!parent.TryGetProperty(name, out var block)
            || (Text(block, "american") ?? Text(block, "alternateDisplayValue")) is not { } raw)
            return false;

        // A pick'em is a spread of zero, written in words.
        if (raw.Trim().Equals("PK", StringComparison.OrdinalIgnoreCase)
            || raw.Trim().Equals("EVEN", StringComparison.OrdinalIgnoreCase))
            return true;

        return decimal.TryParse(raw.TrimStart('o', 'O', 'u', 'U'), NumberStyles.Number | NumberStyles.AllowLeadingSign,
                   CultureInfo.InvariantCulture, out line);
    }

    private static string? Text(JsonElement block, string name)
        => block.ValueKind == JsonValueKind.Object && block.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

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
    private static bool ReadClose(JsonElement side, out decimal? line, out int price)
    {
        line = null;
        price = 0;

        if (!side.TryGetProperty("close", out var close))
            return false;

        if (!close.TryGetProperty("odds", out var oddsEl)
            || !TryParseAmerican(oddsEl.GetString(), out price))
            return false;

        if (close.TryGetProperty("line", out var lineEl) && lineEl.GetString() is { } raw)
        {
            var trimmed = raw.TrimStart('o', 'O', 'u', 'U');

            if (decimal.TryParse(trimmed, NumberStyles.Number | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var parsed))
                line = parsed;
        }

        return true;
    }

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

        using var response = await http.GetAsync(relativeUrl, ct);
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
                }
                else
                {
                    away = reference;
                    awayScore = score;
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
                SeasonYear: ReadSeasonYear(e),
                SeasonType: ReadSeasonType(e));
        }
    }

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

            // The same group restating a value, or a different group agreeing with it: nothing
            // is lost by letting it stand, and qualifying here would split identical data.
            if (owner == group || (Values.TryGetValue(key, out var held) && held == value))
            {
                Values[key] = value;
                return;
            }

            // A real disagreement. Qualify the incumbent as well as the newcomer, so the line
            // never contains a bare key whose meaning depends on which group won.
            Values[$"{owner}.{key}"] = Values[key];
            Values.Remove(key);
            _origin.Remove(key);
            _qualified.Add(key);

            Values[$"{group}.{key}"] = value;
        }
    }
}
