using System.Globalization;
using System.Text.Json;
using LineOps.Core.Analytics;
using LineOps.Core.Contracts;

namespace LineOps.Ingestion.Adapters;

/// <summary>
/// A provider of play-by-play for one game in progress: the gamecast, and the per-play
/// probabilities that go with it.
///
/// <para>
/// Its own port rather than two more methods on <see cref="IStatsSource"/>: the stats source
/// is a slate-at-a-time job's port, and this is a one-game-at-a-time feed's (spec §6.3). The
/// feed depends on this, so a test can hand it a fake that states any game it likes.
/// </para>
/// </summary>
public interface IGamecastSource
{
    string Key { get; }

    /// <summary>
    /// The game's drives and plays as the provider states them now, with win probability on
    /// each play it has one for. Null when this source cannot address the league.
    /// </summary>
    Task<CanonicalGamecast?> FetchGamecastAsync(string sportKey, string sourceGameId, CancellationToken ct);

    /// <summary>
    /// Every per-play probability row the provider holds for the game, keyed by play id —
    /// what <see cref="EspnStatsAdapter.MergeProbabilities"/> lays onto a gamecast. Empty when
    /// this source cannot address the league.
    /// </summary>
    Task<IReadOnlyDictionary<string, CanonicalPlayProbability>> FetchProbabilitiesAsync(
        string sportKey, string sourceGameId, CancellationToken ct);

    /// <summary>
    /// One game's box score, exactly as <see cref="IStatsSource.FetchBoxScoresAsync"/> states a
    /// day's — the game's scoreboard row, its players' lines and the line it closed at — for the
    /// feed to write once when the game it was following goes final, without walking the slate.
    /// Empty when the game is not final on the provider, or this source cannot address the league.
    /// </summary>
    Task<StatsFetchResult> FetchBoxScoreAsync(
        string sportKey, DateOnly date, string sourceGameId, CancellationToken ct);
}

/// <summary>
/// One play's probabilities from ESPN's core API.
///
/// <para>
/// Every value is a fraction from 0 to 1, as ESPN states it (0.7331, not 73.31), and is kept
/// that way: a percentage is a rendering. <see cref="SpreadCoverPctHome"/> is the chance the
/// home side covers the spread ESPN's book has posted, and <see cref="TotalOverPct"/> the
/// chance the game goes over its total — closing-line context nothing else on the desk has
/// (spec §4.3).
/// </para>
/// </summary>
public record CanonicalPlayProbability(
    string SourcePlayId,
    decimal? HomeWinPct,
    decimal? SpreadCoverPctHome,
    decimal? TotalOverPct);

/// <summary>
/// The gamecast half of the ESPN port: the summary's drives, plays and win probability, and
/// the core API's per-play probabilities (spec §4.2, §4.3).
///
/// <para>
/// The summary is the whole game in one document, so one request draws everything the window
/// shows. It is also heavy — over half a megabyte for a finished NFL game — which is why it is
/// fetched for the game the operator has open and never for the slate.
/// </para>
///
/// <para>
/// Two quirks of the live shape the parse has to absorb. While a drive is the latest one ESPN
/// sends it twice, as <c>drives.current</c> and again as the last of <c>drives.previous</c>;
/// the drive and its plays are emitted once, marked current. And a play's win probability is
/// not on the play: it is in <c>winprobability[]</c>, paired by <c>playId</c>.
/// </para>
/// </summary>
public partial class EspnStatsAdapter : IGamecastSource
{
    public async Task<CanonicalGamecast?> FetchGamecastAsync(
        string sportKey, string sourceGameId, CancellationToken ct)
    {
        // MMA has no summary; a card has no drives.
        if (FightSports.IsFight(sportKey) || MapPath(sportKey) is not { } path)
            return null;

        using var doc = await GetJsonAsync($"{path}/summary?event={sourceGameId}", ct);

        return ParseGamecast(doc.RootElement, sourceGameId);
    }

    public async Task<IReadOnlyDictionary<string, CanonicalPlayProbability>> FetchProbabilitiesAsync(
        string sportKey, string sourceGameId, CancellationToken ct)
    {
        if (FightSports.IsFight(sportKey)
            || MapPath(sportKey) is not { } path
            || path.Split('/') is not [var sport, var league])
            return new Dictionary<string, CanonicalPlayProbability>();

        // One page, whole: a long NFL game is under two hundred plays, and the default page of
        // twenty-five would make a game a dozen requests.
        var url = $"https://sports.core.api.espn.com/v2/sports/{sport}/leagues/{league}"
                  + $"/events/{sourceGameId}/competitions/{sourceGameId}/probabilities?limit=1000";

        using var doc = await GetJsonAsync(url, ct);

        return ParseProbabilities(doc.RootElement);
    }

    /// <summary>
    /// The drives, in the order ESPN lists them, and every play in them once, in sequence
    /// order. A summary without drives — a game not started, or a sport ESPN does not narrate
    /// play by play — is an empty gamecast rather than an error.
    /// </summary>
    internal static CanonicalGamecast ParseGamecast(JsonElement root, string sourceGameId)
    {
        var state = root.TryGetProperty("header", out var header)
                    && header.TryGetProperty("competitions", out var comps)
                    && comps.ValueKind == JsonValueKind.Array
                    && comps.GetArrayLength() > 0
            ? ReadStatus(comps[0])
            : null;

        var winPct = new Dictionary<string, decimal>();

        if (root.TryGetProperty("winprobability", out var wp) && wp.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in wp.EnumerateArray())
            {
                if (GamecastJson.Str(row, "playId") is { } id && GamecastJson.Dec(row, "homeWinPercentage") is { } pct)
                    winPct[id] = pct;
            }
        }

        // ESPN's own scoring list, which a play's scoringPlay flag should agree with; either is
        // enough to mark it.
        var scoringIds = new HashSet<string>();

        if (root.TryGetProperty("scoringPlays", out var scoring) && scoring.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in scoring.EnumerateArray())
            {
                if (GamecastJson.Str(s, "id") is { } id)
                    scoringIds.Add(id);
            }
        }

        var driveNodes = new List<JsonElement>();
        string? currentId = null;

        if (root.TryGetProperty("drives", out var drives) && drives.ValueKind == JsonValueKind.Object)
        {
            if (drives.TryGetProperty("previous", out var previous) && previous.ValueKind == JsonValueKind.Array)
                driveNodes.AddRange(previous.EnumerateArray());

            if (drives.TryGetProperty("current", out var current) && current.ValueKind == JsonValueKind.Object)
            {
                currentId = GamecastJson.Str(current, "id");

                // The current drive is usually also the last previous one; read it from
                // current, which is the fresher copy, in the place previous gave it.
                var at = driveNodes.FindIndex(d => currentId is not null && GamecastJson.Str(d, "id") == currentId);

                if (at >= 0)
                    driveNodes[at] = current;
                else
                    driveNodes.Add(current);
            }
        }

        var parsedDrives = new List<CanonicalDrive>();
        var plays = new Dictionary<string, CanonicalPlay>();

        foreach (var node in driveNodes)
        {
            if (GamecastJson.Str(node, "id") is not { } driveId || parsedDrives.Any(d => d.SourceDriveId == driveId))
                continue;

            parsedDrives.Add(ReadDrive(node, driveId, parsedDrives.Count + 1, driveId == currentId));

            if (!node.TryGetProperty("plays", out var playNodes) || playNodes.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var p in playNodes.EnumerateArray())
            {
                if (ReadPlay(p, driveId, winPct, scoringIds) is { } play)
                    plays[play.SourcePlayId] = play;
            }
        }

        return new CanonicalGamecast(
            sourceGameId,
            state,
            parsedDrives,
            plays.Values.OrderBy(p => p.Sequence).ToList());
    }

    /// <summary>
    /// The core API's probability rows, keyed by play. A row names its play only by
    /// <c>play.$ref</c> (<c>…/plays/4018730321?lang=en</c>), so the id is that URL's last path
    /// segment. Values are kept as ESPN's 0–1 fractions.
    /// </summary>
    internal static IReadOnlyDictionary<string, CanonicalPlayProbability> ParseProbabilities(JsonElement root)
    {
        var rows = new Dictionary<string, CanonicalPlayProbability>();

        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return rows;

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("play", out var play) || GamecastJson.RefTail(GamecastJson.Str(play, "$ref")) is not { } playId)
                continue;

            rows[playId] = new CanonicalPlayProbability(
                playId,
                GamecastJson.Dec(item, "homeWinPercentage"),
                GamecastJson.Dec(item, "spreadCoverProbHome"),
                GamecastJson.Dec(item, "totalOverProb"));
        }

        return rows;
    }

    /// <summary>
    /// The gamecast with the core API's spread-cover and total-over probabilities laid onto the
    /// plays they belong to. The summary's win probability stands where it has one; the core
    /// row's fills it where it does not. A play with no row keeps what it had.
    /// </summary>
    public static CanonicalGamecast MergeProbabilities(
        CanonicalGamecast gamecast, IReadOnlyDictionary<string, CanonicalPlayProbability> probabilities)
    {
        if (probabilities.Count == 0)
            return gamecast;

        var plays = gamecast.Plays
            .Select(p => probabilities.TryGetValue(p.SourcePlayId, out var row)
                ? p with
                {
                    HomeWinPct = p.HomeWinPct ?? row.HomeWinPct,
                    SpreadCoverPctHome = row.SpreadCoverPctHome ?? p.SpreadCoverPctHome,
                    TotalOverPct = row.TotalOverPct ?? p.TotalOverPct
                }
                : p)
            .ToList();

        return gamecast with { Plays = plays };
    }

    private static CanonicalDrive ReadDrive(JsonElement node, string driveId, int sequence, bool isCurrent)
    {
        var start = node.TryGetProperty("start", out var s) ? s : default;
        var end = node.TryGetProperty("end", out var e) ? e : default;

        return new CanonicalDrive(
            driveId,
            sequence,
            node.TryGetProperty("team", out var team) ? GamecastJson.Str(team, "id") : null,
            GamecastJson.Str(node, "description"),
            // "Field Goal" reads where "FG" does not; the code is the fallback.
            GamecastJson.Str(node, "displayResult") ?? GamecastJson.Str(node, "result"),
            GamecastJson.Int(node, "yards"),
            GamecastJson.Int(node, "offensivePlays"),
            node.TryGetProperty("timeElapsed", out var elapsed) ? GamecastJson.Str(elapsed, "displayValue") : null,
            GamecastJson.Bool(node, "isScore"),
            isCurrent,
            GamecastJson.Period(start),
            GamecastJson.Clock(start),
            GamecastJson.Int(start, "yardLine"),
            GamecastJson.Period(end),
            GamecastJson.Clock(end),
            GamecastJson.Int(end, "yardLine"));
    }

    private static CanonicalPlay? ReadPlay(
        JsonElement p, string driveId, Dictionary<string, decimal> winPct, HashSet<string> scoringIds)
    {
        if (GamecastJson.Str(p, "id") is not { } id)
            return null;

        var clock = GamecastJson.Clock(p);
        var type = p.TryGetProperty("type", out var t) ? t : default;

        return new CanonicalPlay(
            id,
            GamecastJson.Long(p, "sequenceNumber") ?? 0,
            driveId,
            GamecastJson.Period(p),
            clock,
            GamecastJson.ClockSeconds(clock),
            GamecastJson.Str(type, "id"),
            GamecastJson.Str(type, "text"),
            GamecastJson.Str(p, "text"),
            GamecastJson.Int(p, "homeScore"),
            GamecastJson.Int(p, "awayScore"),
            GamecastJson.Bool(p, "scoringPlay") || scoringIds.Contains(id),
            GamecastJson.Bool(p, "isTurnover"),
            GamecastJson.Bool(p, "isPenalty"),
            GamecastJson.Int(p, "statYardage"),
            GamecastJson.Offence(p),
            GamecastJson.Situation(p, "start"),
            GamecastJson.Situation(p, "end"),
            winPct.TryGetValue(id, out var pct) ? pct : null,
            null,
            null,
            GamecastJson.Time(p, "wallclock"),
            GamecastJson.Time(p, "modified"));
    }

    /// <summary>
    /// Readers for the gamecast's JSON, kept apart from the adapter's own so a helper added to
    /// the box-score parse cannot collide with one here. Every one of them tolerates a missing
    /// or wrongly typed field (spec §7): ESPN states numbers as numbers in one place and as
    /// strings in the next.
    /// </summary>
    private static class GamecastJson
    {
        public static string? Str(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
                ? v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString(),
                    JsonValueKind.Number => v.GetRawText(),
                    _ => null
                }
                : null;

        public static int? Int(JsonElement e, string name)
            => int.TryParse(Str(e, name), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
                ? n
                : null;

        public static long? Long(JsonElement e, string name)
            => long.TryParse(Str(e, name), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
                ? n
                : null;

        public static decimal? Dec(JsonElement e, string name)
            => decimal.TryParse(Str(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                ? n
                : null;

        public static bool Bool(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object
               && e.TryGetProperty(name, out var v)
               && v.ValueKind == JsonValueKind.True;

        public static DateTimeOffset? Time(JsonElement e, string name)
            => DateTimeOffset.TryParse(Str(e, name), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
                ? at
                : null;

        /// <summary><c>{ "period": { "number": 2 } }</c>.</summary>
        public static int? Period(JsonElement e)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("period", out var period)
                ? Int(period, "number")
                : null;

        /// <summary><c>{ "clock": { "displayValue": "10:06" } }</c>.</summary>
        public static string? Clock(JsonElement e)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("clock", out var clock)
                ? Str(clock, "displayValue")
                : null;

        /// <summary>"10:06" is 606 seconds left in the period; anything else is unknown.</summary>
        public static int? ClockSeconds(string? clock)
            => clock?.Split(':') is [var m, var s]
               && int.TryParse(m, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
               && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                ? minutes * 60 + seconds
                : null;

        /// <summary>
        /// The side with the ball on the play, which ESPN names as the offence among the play's
        /// participants — the receiving side on a kickoff, though the kicking side starts it.
        /// Without participants, the side the play started with.
        /// </summary>
        public static string? Offence(JsonElement p)
        {
            if (p.TryGetProperty("teamParticipants", out var parts) && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (Str(part, "type") == "offense" && Str(part, "id") is { } id)
                        return id;
                }
            }

            return p.TryGetProperty("start", out var start) && start.ValueKind == JsonValueKind.Object
                   && start.TryGetProperty("team", out var team)
                ? Str(team, "id")
                : null;
        }

        public static CanonicalPlaySituation? Situation(JsonElement p, string name)
        {
            if (!p.TryGetProperty(name, out var s) || s.ValueKind != JsonValueKind.Object)
                return null;

            return new CanonicalPlaySituation(
                Int(s, "down"),
                Int(s, "distance"),
                Int(s, "yardLine"),
                Int(s, "yardsToEndzone"),
                Str(s, "downDistanceText"),
                Str(s, "possessionText"),
                s.TryGetProperty("team", out var team) ? Str(team, "id") : null);
        }

        /// <summary>The last path segment of a <c>$ref</c>, query dropped.</summary>
        public static string? RefTail(string? href)
        {
            if (string.IsNullOrEmpty(href))
                return null;

            var path = href.Split('?', 2)[0].TrimEnd('/');
            var slash = path.LastIndexOf('/');
            var tail = slash >= 0 ? path[(slash + 1)..] : path;

            return tail.Length > 0 ? tail : null;
        }
    }
}
