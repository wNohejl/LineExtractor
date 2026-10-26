using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LineOps.Core.Analytics;
using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using Microsoft.Extensions.Logging;

namespace LineOps.Ingestion.Adapters;

/// <summary>
/// ESPN's UFC: cards, bouts and fighters (ADR 0021).
///
/// <para>
/// A team sport's day is a scoreboard and a summary per game. MMA has no summary — ESPN answers
/// 404 for it — and the scoreboard carries neither the method nor the order of a card. The fight
/// center does: one request returns the whole card with every bout's segment, place, result and
/// fight stats, and each fighter's bio as it stood that night. So a fight day is the scoreboard,
/// to learn which cards fall on it, and the fight center once per card.
/// </para>
///
/// <para>
/// Fighters are read separately (<see cref="IFighterSource"/>), per fighter rather than per day:
/// the athlete endpoints hold a whole career, and a career changes only when its fighter fights.
/// </para>
/// </summary>
public partial class EspnStatsAdapter
{
    /// <summary>The fight center and the athlete pages live on the web API, not the site API the client is based on.</summary>
    private const string WebApi = "https://site.web.api.espn.com/apis/common/v3/sports/";

    private const string CoreApi = "https://sports.core.api.espn.com/v2/sports/";

    /// <summary>How far past the asked-for day the slate pass looks for cards: the rest of the week.</summary>
    public const int SlateDaysAhead = 6;

    /// <summary>
    /// Every bout on the cards that fall on one day, read from the fight center.
    /// </summary>
    /// <param name="withResults">
    /// The results pass: fight stat lines for finished bouts and each one's closing line, as a
    /// team sport's box-score pass brings its box scores. The slate pass stops at the bouts.
    /// </param>
    private async Task<StatsFetchResult> FetchCardsAsync(
        string path, string sportKey, DateOnly date, bool withResults, CancellationToken ct)
    {
        // A card is billed weeks out and bet all week, and no book can put one on the desk (books
        // only attach to bouts ESPN holds), so the slate pass reads the week ahead in the same one
        // request. The results pass and the backfill walk read the day they are asked for.
        var dates = withResults ? $"{date:yyyyMMdd}" : $"{date:yyyyMMdd}-{date.AddDays(SlateDaysAhead):yyyyMMdd}";

        using var board = await GetJsonAsync($"{path}/scoreboard?dates={dates}", ct);
        var requests = 1;

        var games = new List<CanonicalGame>();
        var players = new List<CanonicalPlayer>();
        var stats = new List<CanonicalPlayerStat>();
        var lines = new List<CanonicalOdds>();
        var cards = 0;

        foreach (var card in ReadScoreboardCards(board.RootElement))
        {
            ct.ThrowIfCancellationRequested();
            cards++;

            List<CanonicalGame> bouts;

            try
            {
                using var center = await GetJsonAsync(
                    $"{WebApi}{path}/fightcenter/{card.EventId}?region=us&lang=en&contentorigin=espn", ct);
                requests++;

                bouts = ParseFightCenter(center.RootElement, sportKey, card.Rounds,
                    withResults ? players : null, withResults ? stats : null).ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The scoreboard has already read the card once. Without the fight center a bout
                // loses its method and its place on the card, not its fighters or its result.
                logger.LogWarning(ex, "{Source}: fight center unavailable for card {EventId}; reading the scoreboard's bouts",
                    SourceKey, card.EventId);

                bouts = ParseScoreboardBouts(board.RootElement, card.EventId, sportKey).ToList();
            }

            games.AddRange(bouts);

            if (!withResults)
                continue;

            foreach (var bout in bouts.Where(b => b.Status == "final"))
            {
                try
                {
                    lines.AddRange(await FetchClosingLinesAsync(sportKey,
                        new ClosingLineRequest(bout.SourceGameId, bout.HomeTeamName, bout.AwayTeamName, bout.StartsAt, card.EventId), ct));
                    requests++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A line is a reference; its absence must not cost the card.
                    logger.LogDebug(ex, "{Source}: no closing odds for bout {GameId}", SourceKey, bout.SourceGameId);
                }
            }
        }

        logger.LogInformation("{Source}: {Sport} {Date} -> {Cards} cards, {Bouts} bouts",
            SourceKey, sportKey, date, cards, games.Count);

        return new StatsFetchResult(games, players, stats, new FetchCost(requests), lines);
    }

    /// <summary>
    /// Whether a card on ESPN's UFC scoreboard is a UFC event. ESPN files the promotion's
    /// development shows under the same league — Dana White's Contender Series, a tryout whose
    /// winners are offered contracts, and Road to UFC — and their fighters are not yet UFC
    /// fighters. The desk holds the UFC only.
    /// </summary>
    internal static bool IsUfcCard(string? name)
        => name is not null
           && !name.Contains("Contender Series", StringComparison.OrdinalIgnoreCase)
           && !name.StartsWith("Road to UFC", StringComparison.OrdinalIgnoreCase);

    /// <summary>A card on the scoreboard, and the scheduled rounds of each of its bouts, which only the scoreboard states.</summary>
    internal sealed record ScoreboardCard(string EventId, IReadOnlyDictionary<string, int> Rounds);

    internal static IEnumerable<ScoreboardCard> ReadScoreboardCards(JsonElement root)
    {
        if (!root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var e in events.EnumerateArray())
        {
            if (Text(e, "id") is not { } id || !IsUfcCard(Text(e, "name")))
                continue;

            var rounds = new Dictionary<string, int>();

            if (e.TryGetProperty("competitions", out var comps) && comps.ValueKind == JsonValueKind.Array)
            {
                foreach (var comp in comps.EnumerateArray())
                {
                    if (Text(comp, "id") is { } compId && ScheduledRoundsOf(comp) is { } n)
                        rounds[compId] = n;
                }
            }

            yield return new ScoreboardCard(id, rounds);
        }
    }

    /// <summary>
    /// Every bout on one card, from the fight center: the card's segments in order, each bout with
    /// its place, weight, distance, result and two fighters.
    /// </summary>
    /// <param name="rounds">Scheduled rounds by competition id, from the scoreboard; the fight center does not say.</param>
    /// <param name="players">When given, each fighter of a finished bout is added, with…</param>
    /// <param name="stats">…their fight stat line for that bout.</param>
    internal static IEnumerable<CanonicalGame> ParseFightCenter(
        JsonElement root,
        string sportKey,
        IReadOnlyDictionary<string, int>? rounds = null,
        List<CanonicalPlayer>? players = null,
        List<CanonicalPlayerStat>? stats = null)
    {
        if (!root.TryGetProperty("event", out var ev) || Text(ev, "id") is not { } eventId)
            yield break;

        var card = new Card(
            eventId,
            Text(ev, "name") ?? Text(ev, "shortName") ?? $"Event {eventId}",
            Text(ev, "shortName"),
            root.TryGetProperty("season", out var season) ? ReadSeason(season) : default);

        if (!root.TryGetProperty("cards", out var segments) || segments.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var segment in segments.EnumerateObject())
        {
            if (!segment.Value.TryGetProperty("competitions", out var comps) || comps.ValueKind != JsonValueKind.Array)
                continue;

            var segmentName = Text(segment.Value, "displayName");

            foreach (var comp in comps.EnumerateArray())
            {
                if (ReadBout(comp, card, sportKey, segmentName, rounds, players, stats) is { } bout)
                    yield return bout;
            }
        }
    }

    /// <summary>
    /// The scoreboard's own reading of one card: fighters, weight, distance and result, without
    /// the method or the card order. The fallback when the fight center cannot be read.
    /// </summary>
    internal static IEnumerable<CanonicalGame> ParseScoreboardBouts(JsonElement root, string eventId, string sportKey)
    {
        if (!root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var e in events.EnumerateArray())
        {
            if (Text(e, "id") != eventId
                || !e.TryGetProperty("competitions", out var comps)
                || comps.ValueKind != JsonValueKind.Array)
                continue;

            var card = new Card(eventId, Text(e, "name") ?? $"Event {eventId}", Text(e, "shortName"), ReadSeason(e));

            foreach (var comp in comps.EnumerateArray())
            {
                if (ReadBout(comp, card, sportKey, null, null, null, null) is { } bout)
                    yield return bout;
            }
        }
    }

    private readonly record struct Card(string EventId, string Name, string? ShortName, (int? Year, SeasonType? Type) Season);

    private static (int? Year, SeasonType? Type) ReadSeason(JsonElement holder)
    {
        var season = holder.TryGetProperty("season", out var nested) ? nested : holder;

        int? year = season.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32() : null;

        // The fight center nests the type an object deeper than the scoreboard does.
        int? type = season.TryGetProperty("type", out var t)
            ? t.ValueKind == JsonValueKind.Number ? t.GetInt32()
              : t.ValueKind == JsonValueKind.Object && t.TryGetProperty("type", out var tt) && tt.ValueKind == JsonValueKind.Number ? tt.GetInt32()
              : null
            : null;

        return (year, type switch
        {
            1 => SeasonType.Preseason,
            2 => SeasonType.Regular,
            3 => SeasonType.Postseason,
            _ => null
        });
    }

    /// <summary>One fighter's corner of a bout, read from either the fight center or the scoreboard.</summary>
    private sealed record Corner(
        string Id, string Name, string? Surname, int Order, bool Winner, string? Record, JsonElement? Stats);

    private static CanonicalGame? ReadBout(
        JsonElement comp,
        Card card,
        string sportKey,
        string? segmentName,
        IReadOnlyDictionary<string, int>? rounds,
        List<CanonicalPlayer>? players,
        List<CanonicalPlayerStat>? stats)
    {
        if (Text(comp, "id") is not { } id
            || !comp.TryGetProperty("competitors", out var competitors)
            || competitors.ValueKind != JsonValueKind.Array)
            return null;

        var corners = competitors.EnumerateArray()
            .Select((c, index) => ReadCorner(c, index + 1))
            .OfType<Corner>()
            .OrderBy(c => c.Order)
            .ToList();

        if (corners.Count != 2)
            return null;

        // ESPN's order 1 is the red corner — the champion, or the favoured billing — and it is
        // also the side its odds call "home". Keeping the two the same is what pairs a close.
        var (red, blue) = (corners[0], corners[1]);

        var weightClass = comp.TryGetProperty("type", out var type)
            ? Text(type, "text") ?? Text(type, "abbreviation")
            : null;

        var note = Text(comp, "note");

        var title = (comp.TryGetProperty("types", out var types) && types.ValueKind == JsonValueKind.Array
                     && types.EnumerateArray().Any(t => Text(t, "text")?.Contains("Title", StringComparison.OrdinalIgnoreCase) == true))
                    || note?.Contains("Title Fight", StringComparison.OrdinalIgnoreCase) == true;

        int? matchNumber = comp.TryGetProperty("matchNumber", out var mn) && mn.ValueKind == JsonValueKind.Number
            ? mn.GetInt32()
            : null;

        var scheduled = rounds?.GetValueOrDefault(id) is > 0 and var known ? known
            : ScheduledRoundsOf(comp) ?? (title || matchNumber == 1 ? 5 : 3);

        var status = ReadStatus(comp);
        var result = ReadResult(comp, status, red, blue);

        var startsAt = Text(comp, "date") is { } raw
                       && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                           DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : (DateTimeOffset?)null;

        if (startsAt is null)
            return null;

        var bout = new CanonicalBout(
            EventId: card.EventId,
            EventName: card.Name,
            EventShortName: card.ShortName,
            CardSegment: comp.TryGetProperty("cardSegment", out var seg) ? Text(seg, "description") ?? segmentName : segmentName,
            MatchNumber: matchNumber,
            WeightClass: weightClass,
            ScheduledRounds: scheduled,
            TitleFight: title,
            HomeRecord: red.Record,
            AwayRecord: blue.Record,
            Outcome: result.Outcome,
            Method: result.Method,
            MethodDetail: result.Detail,
            EndRound: result.Round,
            EndSeconds: result.Seconds);

        if (players is not null && stats is not null && status == "final")
        {
            foreach (var corner in new[] { red, blue })
            {
                if (FightStatLine(corner) is not { } line)
                    continue;

                if (!players.Any(p => p.SourcePlayerId == corner.Id))
                {
                    players.Add(new CanonicalPlayer(
                        corner.Id, sportKey, corner.Name, Truncate(weightClass, 32), corner.Id, corner.Name));
                }

                stats.Add(new CanonicalPlayerStat(corner.Id, id, line, corner.Id, corner.Name));
            }
        }

        return new CanonicalGame(
            SourceGameId: id,
            SportKey: sportKey,
            HomeTeamName: red.Name,
            AwayTeamName: blue.Name,
            StartsAt: startsAt.Value,
            Status: status,
            HomeScore: result.HomeScore,
            AwayScore: result.AwayScore,
            Home: FighterRef(red, weightClass),
            Away: FighterRef(blue, weightClass),
            SeasonYear: card.Season.Year,
            SeasonType: card.Season.Type,
            Bout: bout);
    }

    /// <summary>
    /// A fighter as a side (ADR 0021): their ESPN athlete id, their name, the surname as the
    /// abbreviation, and the weight class to tell them from another fighter of the same name.
    /// </summary>
    private static CanonicalTeamRef FighterRef(Corner corner, string? weightClass)
        => new(corner.Name, corner.Id, Truncate(corner.Surname, 16)) { Qualifier = weightClass };

    private static Corner? ReadCorner(JsonElement competitor, int position)
    {
        var athlete = competitor.TryGetProperty("athlete", out var a) ? a : default;

        var id = (athlete.ValueKind == JsonValueKind.Object ? Text(athlete, "id") : null) ?? Text(competitor, "id");
        var name = athlete.ValueKind == JsonValueKind.Object
            ? Text(athlete, "displayName") ?? Text(athlete, "fullName")
            : null;

        if (id is null || string.IsNullOrWhiteSpace(name))
            return null;

        var surname = athlete.ValueKind == JsonValueKind.Object ? Text(athlete, "lastName") : null;
        surname ??= TeamNames.Short(new Team { Name = name }, name);

        int order = competitor.TryGetProperty("order", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : position;

        var winner = competitor.TryGetProperty("winner", out var w) && w.ValueKind == JsonValueKind.True;

        // The fight center states the record going in; the scoreboard lists it among records.
        var record = Text(competitor, "displayRecord");
        if (record is null && competitor.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array)
            record = records.EnumerateArray().Select(r => Text(r, "summary")).FirstOrDefault(s => s is not null);

        JsonElement? stats = competitor.TryGetProperty("stats", out var s) && s.ValueKind == JsonValueKind.Array && s.GetArrayLength() > 0
            ? s
            : null;

        return new Corner(id, name.Trim(), surname, order, winner, record, stats);
    }

    private readonly record struct BoutResult(
        BoutOutcome? Outcome, string? Method, string? Detail, int? Round, int? Seconds, int? HomeScore, int? AwayScore);

    /// <summary>
    /// How a bout ended. A finished bout with a winner is a win, and scores 1–0 to the winner so
    /// every reader of a record and a moneyline can read it as a score; with no winner it is a
    /// draw (0–0) or a no contest (no score, and its bets void). A bout not yet finished has only
    /// its round and clock, which a live one is worth showing.
    /// </summary>
    private static BoutResult ReadResult(JsonElement comp, string? status, Corner red, Corner blue)
    {
        if (!comp.TryGetProperty("status", out var st))
            return default;

        int? round = st.TryGetProperty("period", out var p) && p.ValueKind == JsonValueKind.Number && p.GetInt32() > 0
            ? p.GetInt32()
            : null;

        var seconds = ReadClock(st);

        var result = st.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.Object ? r : default;
        var method = result.ValueKind == JsonValueKind.Object ? NonEmpty(Text(result, "displayName")) : null;
        var detail = result.ValueKind == JsonValueKind.Object
            ? NonEmpty(Text(result, "displayDescription") ?? Text(result, "description"))
            : null;

        if (status != "final")
            return new BoutResult(null, method, detail, round, seconds, null, null);

        var said = $"{(result.ValueKind == JsonValueKind.Object ? Text(result, "name") : null)} {method}".ToLowerInvariant();

        if (red.Winner != blue.Winner)
            return new BoutResult(BoutOutcome.Win, method, detail, round, seconds, red.Winner ? 1 : 0, blue.Winner ? 1 : 0);

        if (said.Contains("no contest") || said.Contains("nocontest") || said.Contains("no-contest") || said.Contains("overturn"))
            return new BoutResult(BoutOutcome.NoContest, method, detail, round, seconds, null, null);

        // Final, nobody won, and a method on record: the judges could not separate them.
        if (method is not null || said.Contains("draw"))
            return new BoutResult(BoutOutcome.Draw, method ?? "Draw", detail, round, seconds, 0, 0);

        // Final with no winner and no result at all: ESPN has not said yet. Nothing is invented.
        return new BoutResult(null, null, null, round, seconds, null, null);
    }

    /// <summary>Seconds into the round: the fight center writes "194.0", the scoreboard 194.0, both write "3:14".</summary>
    private static int? ReadClock(JsonElement status)
    {
        if (status.TryGetProperty("clock", out var clock))
        {
            if (clock.ValueKind == JsonValueKind.Number)
                return (int)Math.Round(clock.GetDouble());

            if (clock.ValueKind == JsonValueKind.String
                && double.TryParse(clock.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && value > 0)
                return (int)Math.Round(value);
        }

        return Text(status, "displayClock") is { } shown && ParseClock(shown) is { } fromText ? fromText : null;
    }

    /// <summary>"3:14" as 194 seconds. Null for anything that is not a clock.</summary>
    internal static int? ParseClock(string? shown)
        => shown?.Split(':') is [var m, var s]
           && int.TryParse(m, out var minutes) && int.TryParse(s, out var secs)
            ? minutes * 60 + secs
            : null;

    private static int? ScheduledRoundsOf(JsonElement comp)
        => comp.TryGetProperty("format", out var format)
           && format.TryGetProperty("regulation", out var regulation)
           && regulation.TryGetProperty("periods", out var periods)
           && periods.ValueKind == JsonValueKind.Number
            ? periods.GetInt32()
            : null;

    /// <summary>
    /// The fight center's per-bout stats as the flat line a box score stores, under the short
    /// labels a fight sheet uses. "24/30" stays "24/30": landed of attempted is how the sport
    /// writes it, and splitting it would invent a precision the column does not need.
    /// </summary>
    private static string? FightStatLine(Corner corner)
    {
        if (corner.Stats is not { } stats)
            return null;

        var line = new Dictionary<string, string>();

        foreach (var stat in stats.EnumerateArray())
        {
            var name = Text(stat, "name");
            var value = Text(stat, "displayValue");

            if (name is null || value is null)
                continue;

            line[FightStatKeys.GetValueOrDefault(name) ?? Text(stat, "abbreviation") ?? name] = value;
        }

        return line.Count == 0 ? null : JsonSerializer.Serialize(line);
    }

    private static readonly Dictionary<string, string> FightStatKeys = new(StringComparer.Ordinal)
    {
        ["knockDowns"] = "KD",
        ["sigStrikes"] = "SIG",
        ["totalStrikes"] = "TOT",
        ["headStrikes"] = "HEAD",
        ["bodyStrikes"] = "BODY",
        ["legStrikes"] = "LEG",
        ["takedowns"] = "TD",
        ["submissions"] = "SUB",
        ["timeInControl"] = "CTRL"
    };

    // ---- Fighters -------------------------------------------------------------------------

    /// <summary>
    /// One fighter in full: the athlete page (bio, record splits and every recorded fight), the
    /// per-fight stat sheets, the career rates, and the record's breakdown. Four requests; only
    /// the first is essential, and a failure on any other costs that part rather than the fighter.
    /// </summary>
    public async Task<FighterFetchResult> FetchFighterAsync(string sportKey, string sourceFighterId, CancellationToken ct)
    {
        if (MapPath(sportKey) is not { } path || path.Split('/') is not [var sport, _] || !FightSports.IsFight(sportKey))
            return new FighterFetchResult(null, new FetchCost(0));

        var id = Uri.EscapeDataString(sourceFighterId);
        var requests = 1;

        using var athlete = await GetJsonAsync($"{WebApi}{sport}/athletes/{id}?region=us&lang=en", ct);

        Dictionary<string, List<FighterStat>> sheets = [];
        List<FighterStat> rates = [];
        JsonDocument? records = null;

        try
        {
            requests++;
            using var stats = await GetJsonAsync($"{WebApi}{sport}/athletes/{id}/stats?region=us&lang=en", ct);
            sheets = ParseFightSheets(stats.RootElement);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "{Source}: no fight sheets for fighter {Id}", SourceKey, sourceFighterId);
        }

        try
        {
            requests++;
            using var career = await GetJsonAsync($"{CoreApi}{sport}/athletes/{id}/statistics", ct);
            rates = ParseCareerRates(career.RootElement);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "{Source}: no career rates for fighter {Id}", SourceKey, sourceFighterId);
        }

        try
        {
            requests++;
            records = await GetJsonAsync($"{CoreApi}{sport}/athletes/{id}/records", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "{Source}: no record breakdown for fighter {Id}", SourceKey, sourceFighterId);
        }

        using (records)
        {
            var fighter = ParseFighter(athlete.RootElement, sheets, rates, records?.RootElement);
            return new FighterFetchResult(fighter, new FetchCost(requests));
        }
    }

    /// <summary>The fighter from the athlete page, with the parts read from the other three requests folded in.</summary>
    internal static CanonicalFighter? ParseFighter(
        JsonElement root,
        IReadOnlyDictionary<string, List<FighterStat>> sheets,
        IReadOnlyList<FighterStat> rates,
        JsonElement? records)
    {
        if (!root.TryGetProperty("athlete", out var a) || Text(a, "id") is not { } id)
            return null;

        var name = Text(a, "displayName") ?? Text(a, "fullName");
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var record = new List<FighterStat>();
        string? wld = null;

        if (a.TryGetProperty("statsSummary", out var summary)
            && summary.TryGetProperty("statistics", out var summaryStats)
            && summaryStats.ValueKind == JsonValueKind.Array)
        {
            foreach (var stat in summaryStats.EnumerateArray())
            {
                if (Text(stat, "name") is not { } key || Text(stat, "displayValue") is not { } value)
                    continue;

                record.Add(new FighterStat(key, Text(stat, "displayName") ?? key, value));

                if (key == "wins-losses-draws")
                    wld = value;
            }
        }

        if (records is { } r && r.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("stats", out var splits) || splits.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var stat in splits.EnumerateArray())
                {
                    if (Text(stat, "name") is { } key && Text(stat, "displayValue") is { } value
                        && record.All(x => x.Key != key))
                        record.Add(new FighterStat(key, Text(stat, "displayName") ?? key, value));
                }

                wld ??= Text(item, "summary");
            }
        }

        return new CanonicalFighter(
            SourceFighterId: id,
            FullName: name.Trim(),
            Nickname: NonEmpty(Text(a, "nickname")),
            Country: NonEmpty(Text(a, "citizenship")),
            Association: a.TryGetProperty("association", out var gym) ? NonEmpty(Text(gym, "name")) : null,
            Stance: a.TryGetProperty("stance", out var stance) ? NonEmpty(Text(stance, "text")) : null,
            WeightClass: a.TryGetProperty("weightClass", out var wc) ? NonEmpty(Text(wc, "text")) : null,
            HeightInches: ParseInches(Text(a, "displayHeight")),
            ReachInches: ParseInches(Text(a, "displayReach")),
            BornOn: DateOnly.TryParseExact(Text(a, "displayDOB"), "M/d/yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var born) ? born : null,
            Record: wld,
            Career: new FighterCareer(rates, record),
            History: ParseFightHistory(root, sheets));
    }

    /// <summary>
    /// Every recorded fight, newest first, from the athlete page's event map — UFC and before —
    /// each with its stat sheet where ESPN kept one.
    /// </summary>
    internal static IReadOnlyList<FighterFight> ParseFightHistory(
        JsonElement root, IReadOnlyDictionary<string, List<FighterStat>> sheets)
    {
        if (!root.TryGetProperty("eventsMap", out var map) || map.ValueKind != JsonValueKind.Object)
            return [];

        // The athlete page orders the fights in "events"; the map is keyed but unordered.
        var order = root.TryGetProperty("events", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(e => e.GetString()).OfType<string>().ToList()
            : map.EnumerateObject().Select(p => p.Name).ToList();

        var fights = new List<FighterFight>();

        foreach (var uid in order)
        {
            if (!map.TryGetProperty(uid, out var e))
                continue;

            var status = e.TryGetProperty("status", out var s) ? s : default;
            var result = status.ValueKind == JsonValueKind.Object && status.TryGetProperty("result", out var res) ? res : default;

            fights.Add(new FighterFight(
                Id: CompetitionOf(uid) ?? uid,
                EventId: Text(e, "id"),
                Event: Text(e, "name"),
                Ufc: uid.Contains("~l:3321~", StringComparison.Ordinal),
                Date: Text(e, "gameDate") is { } date
                      && DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                    ? at.ToUniversalTime()
                    : null,
                OpponentId: e.TryGetProperty("opponent", out var opp) ? Text(opp, "id") : null,
                Opponent: e.TryGetProperty("opponent", out var opp2) ? Text(opp2, "displayName") : null,
                Result: NonEmpty(Text(e, "gameResult")),
                Method: result.ValueKind == JsonValueKind.Object ? NonEmpty(Text(result, "displayName")) : null,
                MethodDetail: result.ValueKind == JsonValueKind.Object ? NonEmpty(Text(result, "description")) : null,
                Round: status.ValueKind == JsonValueKind.Object && status.TryGetProperty("period", out var p)
                       && p.ValueKind == JsonValueKind.Number && p.GetInt32() > 0
                    ? p.GetInt32()
                    : null,
                Clock: status.ValueKind == JsonValueKind.Object ? NonEmpty(Text(status, "displayClock")) : null,
                Title: e.TryGetProperty("titleFight", out var t) && t.ValueKind == JsonValueKind.True,
                Stats: sheets.GetValueOrDefault(uid) ?? []));
        }

        return fights;
    }

    /// <summary>
    /// ESPN's per-fight stat sheet for every recorded fight, keyed by the fight's uid: striking,
    /// clinch and ground, all 36 columns, under ESPN's own labels. A fight ESPN kept no sheet for
    /// writes "-" in every column, and is left with none rather than a row of dashes.
    /// </summary>
    internal static Dictionary<string, List<FighterStat>> ParseFightSheets(JsonElement root)
    {
        var sheets = new Dictionary<string, List<FighterStat>>();

        if (!root.TryGetProperty("categories", out var categories) || categories.ValueKind != JsonValueKind.Array)
            return sheets;

        foreach (var category in categories.EnumerateArray())
        {
            var labels = Strings(category, "labels");
            var names = Strings(category, "displayNames");

            if (labels.Count == 0 || !category.TryGetProperty("statistics", out var rows) || rows.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var row in rows.EnumerateArray())
            {
                if (Text(row, "uid") is not { } uid)
                    continue;

                var values = Strings(row, "stats");

                if (!sheets.TryGetValue(uid, out var sheet))
                    sheets[uid] = sheet = [];

                for (var i = 0; i < labels.Count && i < values.Count; i++)
                {
                    var value = values[i].Trim();
                    if (value is "" or "-")
                        continue;

                    var key = labels[i].Trim();
                    if (sheet.Any(x => x.Key == key))
                        continue;

                    sheet.Add(new FighterStat(key, i < names.Count ? names[i].Trim() : key, value));
                }
            }
        }

        foreach (var empty in sheets.Where(s => s.Value.Count == 0).Select(s => s.Key).ToList())
            sheets.Remove(empty);

        return sheets;
    }

    /// <summary>The career rates from the core statistics endpoint, under ESPN's names and as ESPN writes them.</summary>
    internal static List<FighterStat> ParseCareerRates(JsonElement root)
    {
        var rates = new List<FighterStat>();

        if (!root.TryGetProperty("splits", out var splits)
            || !splits.TryGetProperty("categories", out var categories)
            || categories.ValueKind != JsonValueKind.Array)
            return rates;

        foreach (var category in categories.EnumerateArray())
        {
            if (!category.TryGetProperty("stats", out var stats) || stats.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var stat in stats.EnumerateArray())
            {
                if (Text(stat, "name") is { } key && Text(stat, "displayValue") is { } value && rates.All(r => r.Key != key))
                    rates.Add(new FighterStat(key, Text(stat, "displayName") ?? key, value));
            }
        }

        return rates;
    }

    /// <summary>"6' 4\"" as 76, "79\"" as 79, "72.5\"" as 72.5.</summary>
    internal static decimal? ParseInches(string? shown)
    {
        if (string.IsNullOrWhiteSpace(shown))
            return null;

        var match = FeetInches().Match(shown);

        if (match.Success)
        {
            var feet = match.Groups["feet"].Success ? decimal.Parse(match.Groups["feet"].Value, CultureInfo.InvariantCulture) : 0m;
            var inches = match.Groups["inches"].Success ? decimal.Parse(match.Groups["inches"].Value, CultureInfo.InvariantCulture) : 0m;
            var total = feet * 12 + inches;
            return total > 0 ? total : null;
        }

        return null;
    }

    [GeneratedRegex("""^\s*(?:(?<feet>\d+)\s*'\s*)?(?:(?<inches>\d+(?:\.\d+)?)\s*")?\s*$""")]
    private static partial Regex FeetInches();

    /// <summary>The competition id from a uid, "s:3301~l:3321~e:600041053~c:401619309" → "401619309".</summary>
    private static string? CompetitionOf(string uid)
    {
        var at = uid.LastIndexOf("~c:", StringComparison.Ordinal);
        return at < 0 ? null : uid[(at + 3)..];
    }

    private static List<string> Strings(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.ToString()).ToList()
            : [];

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];
}
