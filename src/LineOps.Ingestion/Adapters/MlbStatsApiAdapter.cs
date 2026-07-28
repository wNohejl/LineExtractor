using System.Globalization;
using System.Text.Json;
using LineOps.Core.Contracts;
using LineOps.Ingestion.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineOps.Ingestion.Adapters;

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

    /// <summary>
    /// The day's games, with the identity the rest of the platform will key off.
    /// </summary>
    public async Task<IReadOnlyList<MlbScheduledGame>> FetchScheduleAsync(
        DateOnly date, CancellationToken ct = default)
    {
        // probablePitcher is hydrated rather than fetched per game: one request for the slate
        // instead of one per fixture, on an endpoint that is free either way but should still
        // not be asked fifteen times for what it will say once.
        var url = $"api/v1/schedule?sportId={MajorLeagueSportId}"
                + $"&date={date:yyyy-MM-dd}"
                + "&hydrate=probablePitcher,team";

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
                    Away: away);
            }
        }
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
