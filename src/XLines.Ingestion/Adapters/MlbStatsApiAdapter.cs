using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using XLines.Core.Contracts;
using XLines.Ingestion.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace XLines.Ingestion.Adapters;

/// <summary>
/// MLB's own schedule API — the entity spine for baseball.
///
/// <para>
/// Every other source names the same game differently. ESPN calls it event 401816288, The Odds
/// API calls it a 32-character hash, and a book calls it "SEA @ TEX". Resolution across them has
/// so far been team name plus start time, normalised for punctuation and compared with a
/// tolerance — which works, and is a heuristic. It fails on the cases that matter most: a
/// doubleheader is two games between the same teams on the same day, and a suspended game
/// resumed the next afternoon is one game with two start times.
/// </para>
///
/// <para>
/// <b>statsapi.mlb.com is the authority.</b> It is free, unauthenticated, and issues the ids the
/// sport itself runs on — <c>gamePk</c> for a game, MLBAM <c>teamId</c> and <c>playerId</c> for
/// the people in it. Resolving every source into those once turns a fuzzy N-way match into N
/// exact lookups against a spine, and gives the doubleheader an unambiguous answer because
/// <c>gameNumber</c> is part of the schedule rather than something to infer.
/// </para>
///
/// <para>
/// It also carries <b>probable pitchers</b>, hydrated with their MLBAM ids. That is not
/// decoration: a starting pitcher being scratched invalidates every price on the game and every
/// pitcher prop attached to them, and it is knowable here hours before a book takes the market
/// down.
/// </para>
///
/// <para>
/// The same call carries the game's circumstances: the venue with its roof and turf, the weather,
/// the umpiring crew and the posted batting orders, each hydrated into the slate rather than asked
/// for game by game. They are read into <see cref="MlbScheduledGame.Context"/> exactly as stated,
/// and only where stated.
/// </para>
///
/// <para>
/// This is a schedule source, not a box-score one. ESPN keeps that job (ADR 0011) — the point of
/// this adapter is identity, and the schedule is where identity is issued.
/// </para>
/// </summary>
public class MlbStatsApiAdapter(
    HttpClient http,
    IOptions<IngestionOptions> options,
    ILogger<MlbStatsApiAdapter> logger)
{
    public const string SourceKey = "mlb-statsapi";

    /// <summary>MLB's own id for the major leagues. Others exist; this is the one that pays.</summary>
    private const int MajorLeagueSportId = 1;

    private readonly SourceOptions _config = options.Value.MlbStatsApi;

    /// <summary>What the schedule call asks MLB to fill in, checked against the live API on 21 November 2026.</summary>
    internal const string Hydrate = "probablePitcher,team,lineups,weather,officials,venue(fieldInfo,location)";

    /// <summary>
    /// The day's games, with the identity the rest of the platform will key off.
    /// </summary>
    public async Task<IReadOnlyList<MlbScheduledGame>> FetchScheduleAsync(
        DateOnly date, CancellationToken ct = default)
    {
        // probablePitcher is hydrated rather than fetched per game: one request for the slate
        // instead of one per fixture, on an endpoint that is free either way but should still
        // not be asked fifteen times for what it will say once.
        //
        // The game's circumstances ride in the same request for the same reason: lineups, weather
        // and officials are hydrations of this call, not calls of their own. The venue has to be
        // asked for its fieldInfo and location by name — a bare 'venue' hydrates the id and name
        // alone, and the roof, the turf and the city are what it is hydrated for. It roughly
        // trebles the response (a fifteen-game day is about 48 KB without, 130 KB with), which is
        // still one request a day.
        var url = $"api/v1/schedule?sportId={MajorLeagueSportId}"
                + $"&date={date:yyyy-MM-dd}"
                + "&hydrate=" + Hydrate;

        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var games = Parse(doc.RootElement).ToList();

        logger.LogInformation("{Source}: {Date} -> {Count} games", SourceKey, date, games.Count);

        return games;
    }

    internal static IEnumerable<MlbScheduledGame> Parse(JsonElement root)
    {
        if (!root.TryGetProperty("dates", out var dates) || dates.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var day in dates.EnumerateArray())
        {
            if (!day.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var game in games.EnumerateArray())
            {
                if (!game.TryGetProperty("gamePk", out var pk) || !pk.TryGetInt64(out var gamePk))
                    continue;

                if (!game.TryGetProperty("teams", out var teams))
                    continue;

                var home = ReadSide(teams, "home");
                var away = ReadSide(teams, "away");

                if (home is null || away is null)
                    continue;

                var startsAt = game.TryGetProperty("gameDate", out var d)
                               && DateTimeOffset.TryParse(d.GetString(), CultureInfo.InvariantCulture,
                                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                   out var parsed)
                    ? parsed
                    : (DateTimeOffset?)null;

                if (startsAt is null)
                    continue;

                // Only games that count. MLB labels exhibitions in the same feed as everything
                // else, and gameType is the field that says which — 'R' regular, 'F'/'D'/'L'/'W'
                // the postseason rounds. 'S' is spring training and 'A' is the All-Star Game,
                // which are the two that have to be kept out of a team's record.
                var gameType = game.TryGetProperty("gameType", out var gt) ? gt.GetString() : null;

                yield return new MlbScheduledGame(
                    GamePk: gamePk,
                    StartsAt: startsAt.Value,
                    GameType: gameType ?? "R",
                    // A doubleheader is the case name-and-date matching cannot answer: two games,
                    // same teams, same day. MLB numbers them, so it is stated rather than guessed.
                    GameNumber: game.TryGetProperty("gameNumber", out var gn) && gn.TryGetInt32(out var n) ? n : 1,
                    DoubleHeader: game.TryGetProperty("doubleHeader", out var dh) ? dh.GetString() ?? "N" : "N",
                    DetailedState: game.TryGetProperty("status", out var st)
                                   && st.TryGetProperty("detailedState", out var ds)
                        ? ds.GetString() ?? "Scheduled"
                        : "Scheduled",
                    Home: home,
                    Away: away)
                {
                    Context = ReadContext(game, gamePk)
                };
            }
        }
    }

    /// <summary>
    /// What the hydrations state about one game, or null where they state nothing.
    ///
    /// <para>
    /// Each field is read only where MLB states it. A game a season ahead carries its venue and
    /// <c>dayNight</c> and an empty <c>weather</c> object; lineups appear a few hours before first
    /// pitch and are absent until then, not empty. Nothing here fills a gap: an unstated field is
    /// null, and the writer reads null as no news (ADR 0003). The schedule states no attendance and
    /// no chance of rain, so neither is read.
    /// </para>
    /// </summary>
    internal static CanonicalGameContext? ReadContext(JsonElement game, long gamePk)
    {
        // Each read is safe on a section MLB left out: a missing venue reads as the default element,
        // and every field read from it as null.
        var venue = JsonRead.Child(game, "venue");
        var location = JsonRead.Child(venue, "location");
        var field = JsonRead.Child(venue, "fieldInfo");
        var weather = JsonRead.Child(game, "weather");

        var condition = JsonRead.Words(weather, "condition");
        var (windSpeed, windDirection) = ParseWind(JsonRead.Words(weather, "wind"));

        var context = new CanonicalGameContext(gamePk.ToString(CultureInfo.InvariantCulture))
        {
            VenueName = JsonRead.Words(venue, "name"),
            VenueCity = JsonRead.Words(location, "city"),
            VenueState = JsonRead.Words(location, "stateAbbrev"),
            Indoor = ReadIndoor(JsonRead.Words(field, "roofType"), condition),
            Surface = ReadSurface(JsonRead.Words(field, "turfType")),
            WeatherCondition = condition,
            TemperatureF = Integer(weather, "temp"),
            WindSpeedMph = windSpeed,
            WindDirection = windDirection,
            DayNight = JsonRead.Words(game, "dayNight")?.ToLowerInvariant() switch
            {
                "day" => GameDayNight.Day,
                "night" => GameDayNight.Night,
                _ => null
            },
            Officials = ReadOfficials(game),
            HomeLineup = ReadLineup(game, "homePlayers"),
            AwayLineup = ReadLineup(game, "awayPlayers")
        };

        return context.StatesAnything ? context : null;
    }

    /// <summary>
    /// Whether the game was played under a roof, as far as MLB says.
    ///
    /// <para>
    /// A dome is always closed and an open park never is. A retractable roof is closed when MLB's
    /// weather says "Roof Closed" — it does, in place of the sky, on every closed-roof game read in
    /// July 2026, with the wind given as "0 mph, None". Any other condition at a retractable park
    /// is weather, which suggests the roof was open but does not say so, and a suggestion is not
    /// stated: it stays null.
    /// </para>
    /// </summary>
    internal static bool? ReadIndoor(string? roofType, string? condition)
        => roofType?.ToLowerInvariant() switch
        {
            "dome" => true,
            "open" => false,
            "retractable" when string.Equals(condition, "Roof Closed", StringComparison.OrdinalIgnoreCase) => true,
            _ => null
        };

    /// <summary>"Grass" is grass; "Artificial Turf", the only other value MLB uses, is turf.</summary>
    internal static string? ReadSurface(string? turfType)
    {
        if (turfType is null)
            return null;

        if (turfType.Contains("grass", StringComparison.OrdinalIgnoreCase))
            return GameSurfaces.Grass;

        return turfType.Contains("turf", StringComparison.OrdinalIgnoreCase)
               || turfType.Contains("artificial", StringComparison.OrdinalIgnoreCase)
            ? GameSurfaces.Turf
            : null;
    }

    /// <summary>
    /// MLB's wind, "8 mph, Out To LF", as a speed and a direction.
    ///
    /// <para>
    /// The direction is kept in MLB's own words, spacing tidied. "None" is MLB saying there is no
    /// direction — it comes with "0 mph" under a closed roof — so it is no direction rather than a
    /// direction called None; "Calm" and "Varies" are directions MLB states and are kept. A string
    /// with no leading speed is a direction alone, and a speed with nothing after the comma is a
    /// speed alone.
    /// </para>
    /// </summary>
    internal static (int? SpeedMph, string? Direction) ParseWind(string? wind)
    {
        if (string.IsNullOrWhiteSpace(wind))
            return (null, null);

        var comma = wind.IndexOf(',');
        var head = Tidy(comma >= 0 ? wind[..comma] : wind);
        var tail = comma >= 0 ? Tidy(wind[(comma + 1)..]) : null;

        int? speed = null;
        var match = Regex.Match(
            head ?? "", @"^(\d+)\s*mph$", RegexOptions.IgnoreCase);

        if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var mph))
            speed = mph;
        else if (comma < 0)
            tail = head;

        var direction = string.Equals(tail, "None", StringComparison.OrdinalIgnoreCase) ? null : tail;

        return (speed, direction);

        static string? Tidy(string s)
        {
            var collapsed = string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return collapsed.Length == 0 ? null : collapsed;
        }
    }

    /// <summary>
    /// The umpires, as MLB lists them, with the base each worked as the role. MLB's list is not in
    /// crew order — first base usually comes before home plate — and the writer keeps it as given;
    /// the role, not the position in the list, is what says who called balls and strikes.
    /// </summary>
    private static IReadOnlyList<CanonicalOfficial>? ReadOfficials(JsonElement game)
    {
        if (!game.TryGetProperty("officials", out var officials) || officials.ValueKind != JsonValueKind.Array)
            return null;

        var crew = new List<CanonicalOfficial>();

        foreach (var entry in officials.EnumerateArray())
        {
            var name = JsonRead.Words(JsonRead.Child(entry, "official"), "fullName");

            if (name is not null)
                crew.Add(new CanonicalOfficial(name, JsonRead.Words(entry, "officialType"), null));
        }

        return crew.Count > 0 ? crew : null;
    }

    /// <summary>
    /// One side's posted batting order, leadoff first, in the order MLB lists it. The position is
    /// the one the player is batting at in this game — a regular catcher who is the day's DH is
    /// listed as DH — which is what the lineup means by it. Absent until the lineup is posted.
    /// </summary>
    private static IReadOnlyList<CanonicalLineupSlot>? ReadLineup(JsonElement game, string side)
    {
        if (JsonRead.Child(game, "lineups") is not { ValueKind: JsonValueKind.Object } lineups
            || !lineups.TryGetProperty(side, out var players)
            || players.ValueKind != JsonValueKind.Array)
            return null;

        var slots = new List<CanonicalLineupSlot>();

        foreach (var player in players.EnumerateArray())
        {
            if (!player.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id))
                continue;

            if (JsonRead.Words(player, "fullName") is not { } name)
                continue;

            var position = JsonRead.Words(JsonRead.Child(player, "primaryPosition"), "abbreviation");

            slots.Add(new CanonicalLineupSlot(slots.Count + 1, id.ToString(CultureInfo.InvariantCulture), name, position));
        }

        return slots.Count > 0 ? slots : null;
    }

    /// <summary>
    /// A whole number MLB may send as a string — the temperature is "82", not 82 — or as a number.
    ///
    /// <para>
    /// Not <see cref="JsonRead.Int"/>, which reads a number alone, and not ESPN's <c>WholeNumber</c>,
    /// which rounds "82.5" to 83: MLB states a whole temperature, so a fraction is not one it stated
    /// and is refused rather than rounded into a reading MLB never gave.
    /// </para>
    /// </summary>
    private static int? Integer(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var el))
            return null;

        if (el.ValueKind == JsonValueKind.Number)
            return el.TryGetInt32(out var n) ? n : null;

        return el.ValueKind == JsonValueKind.String
               && int.TryParse(el.GetString()?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static MlbSide? ReadSide(JsonElement teams, string side)
    {
        if (!teams.TryGetProperty(side, out var entry) || !entry.TryGetProperty("team", out var team))
            return null;

        if (!team.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var teamId))
            return null;

        var name = team.TryGetProperty("name", out var n) ? n.GetString() : null;

        if (string.IsNullOrWhiteSpace(name))
            return null;

        MlbProbablePitcher? probable = null;

        if (entry.TryGetProperty("probablePitcher", out var pp)
            && pp.TryGetProperty("id", out var ppId)
            && ppId.TryGetInt32(out var pitcherId))
        {
            probable = new MlbProbablePitcher(
                pitcherId,
                pp.TryGetProperty("fullName", out var fn) ? fn.GetString() ?? "" : "");
        }

        return new MlbSide(
            TeamId: teamId,
            Name: name,
            Abbrev: team.TryGetProperty("abbreviation", out var ab) ? ab.GetString() : null,
            Score: entry.TryGetProperty("score", out var sc) && sc.TryGetInt32(out var score) ? score : null,
            ProbablePitcher: probable);
    }

    /// <summary>The delay between calls, honoured even though MLB publishes no limit.</summary>
    public TimeSpan RequestDelay => _config.RequestDelay;
}

/// <summary>One game as MLB itself identifies it.</summary>
public record MlbScheduledGame(
    long GamePk,
    DateTimeOffset StartsAt,
    string GameType,
    int GameNumber,
    string DoubleHeader,
    string DetailedState,
    MlbSide Home,
    MlbSide Away)
{
    /// <summary>
    /// Whether this game counts towards a record.
    ///
    /// 'S' is spring training and 'A' the All-Star Game — the two that would otherwise put
    /// results in a team's record and at-bats in a player's form from games nobody counts.
    /// Everything else, regular season through the World Series, does count. Named rather than
    /// whitelisted so an unfamiliar postseason code is kept rather than silently dropped.
    /// </summary>
    public bool Counts => GameType is not ("S" or "A" or "E");

    /// <summary>True when this is one of two games between the same teams on the same day.</summary>
    public bool IsDoubleHeader => DoubleHeader is not "N";

    /// <summary>
    /// What the schedule's hydrations state about the game — venue, weather, officials, the
    /// confirmed lineups — keyed by <see cref="GamePk"/>. Null where the parser read none; the spine
    /// writes it onto the game it matched, under the MLB Stats API's own source.
    /// </summary>
    public CanonicalGameContext? Context { get; init; }
}

/// <summary>One side of a scheduled game, keyed by MLB's own team id.</summary>
public record MlbSide(
    int TeamId,
    string Name,
    string? Abbrev,
    int? Score,
    MlbProbablePitcher? ProbablePitcher);

/// <summary>
/// The announced starter, with the id the rest of baseball uses for them.
///
/// Worth carrying because a scratch invalidates a market: the whole pitcher prop board for that
/// game, and a good deal of the moneyline's reasoning, rests on who is actually starting.
/// </summary>
public record MlbProbablePitcher(int PlayerId, string FullName);
