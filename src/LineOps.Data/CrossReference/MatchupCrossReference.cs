using System.Text.Json;
using LineOps.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Data.CrossReference;

/// <summary>
/// Looks from a priced game through to the recent form of the players in it.
///
/// <para>
/// This is the join the product exists to make. Odds say what the market thinks; the stats
/// backfill says what actually happened. Neither is useful alone at the moment you are looking
/// at a line, and the answer to "is this number reasonable" lives in the gap between them.
/// </para>
///
/// <para><b>It is a lookup, not a copy.</b> Nothing here is denormalised onto the odds tier and
/// nothing is precomputed into a summary table. Form changes every day, a stat line can be
/// corrected after the fact, and a player can move team mid-season — any materialised copy
/// would be a second version of the truth that goes stale silently. The cost of reading it
/// live is two indexed queries, which is cheaper than the invalidation logic a cache would
/// need.</para>
///
/// <para>
/// The two hops are indexed deliberately: <c>ix_player_team</c> gets from a game's teams to
/// their players without scanning the league, and <c>ux_player_game_stat</c> is led by
/// <c>PlayerId</c>, so pulling those players' lines is a set of index seeks. The window is
/// bounded by date so the work is proportional to recent form rather than to career length.
/// </para>
///
/// <para>
/// Players with no appearances in the window are dropped rather than returned as rows of
/// zeroes. That mirrors what storage does — a player who did not play has no stat row at all —
/// and it keeps the reading honest: a blank line and a zero line mean different things, and
/// only one of them belongs on screen.
/// </para>
/// </summary>
public class MatchupCrossReference(LineOpsDbContext db)
{
    /// <summary>
    /// Recent form for both sides of a game.
    /// </summary>
    /// <param name="gameId">The game being priced.</param>
    /// <param name="window">How far back to look. Form, not career.</param>
    /// <param name="maxPlayersPerTeam">Cap per side, ordered by appearances.</param>
    public async Task<MatchupForm?> GetAsync(
        int gameId,
        TimeSpan window,
        int maxPlayersPerTeam = 12,
        int? seasonYear = null,
        CancellationToken ct = default)
    {
        var game = await db.Games
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == gameId, ct);

        if (game is null)
            return null;

        var since = game.StartsAt - window;

        // The games form is drawn from: strictly before this one, and either inside the
        // named season or inside the rolling window when no season was named.
        var prior = seasonYear is { } year
            ? db.Games.Where(g => g.StartsAt < game.StartsAt && g.SeasonYear == year)
            : db.Games.Where(g => g.StartsAt < game.StartsAt && g.StartsAt >= since);

        // Hop one: the teams' current rosters. Indexed by ix_player_team.
        var players = await db.Players
            .Where(p => p.TeamId == game.HomeTeamId || p.TeamId == game.AwayTeamId)
            .Select(p => new { p.Id, p.FullName, p.Position, p.TeamId })
            .AsNoTracking()
            .ToListAsync(ct);

        if (players.Count == 0)
            return new MatchupForm(game, [], [], 0);

        var playerIds = players.Select(p => p.Id).ToList();

        // Hop two: their lines from games already played, inside the window and strictly
        // before this one. Joined to Games for the date rather than trusting CapturedAt,
        // which records when we ingested rather than when the game was.
        var lines = await db.PlayerGameStats
            .Where(s => playerIds.Contains(s.PlayerId))
            .Join(prior,
                s => s.GameId, g => g.Id,
                (s, g) => new { s.PlayerId, s.StatLine, g.StartsAt })
            .AsNoTracking()
            .ToListAsync(ct);

        var byPlayer = lines
            .GroupBy(l => l.PlayerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<PlayerForm>();

        foreach (var player in players)
        {
            if (!byPlayer.TryGetValue(player.Id, out var appearances))
                continue; // Did not play in the window. No row, same as storage.

            var totals = new Dictionary<string, double>();
            var counts = new Dictionary<string, int>();
            var counting = new Dictionary<string, bool>();

            foreach (var appearance in appearances)
            {
                foreach (var (key, value) in ParseNumeric(appearance.StatLine))
                {
                    totals[key] = totals.GetValueOrDefault(key) + value;
                    counts[key] = counts.GetValueOrDefault(key) + 1;

                    // Whole numbers every time means this counts things — hits, walks, pitches
                    // — and adding them up is the right summary. A fractional value anywhere
                    // means it is a rate, and summing a rate is nonsense: twenty games of a
                    // .263 average must not read as 5.26.
                    var isWhole = Math.Abs(value % 1) < 1e-9;
                    counting[key] = counting.GetValueOrDefault(key, true) && isWhole;
                }
            }

            rows.Add(new PlayerForm(
                PlayerId: player.Id,
                Name: player.FullName,
                Position: player.Position,
                IsHome: player.TeamId == game.HomeTeamId,
                Appearances: appearances.Count,
                LastPlayed: appearances.Max(a => a.StartsAt),
                Totals: totals,
                Counts: counts,
                Counting: counting));
        }

        var home = rows.Where(r => r.IsHome)
            .OrderByDescending(r => r.Appearances).ThenBy(r => r.Name)
            .Take(maxPlayersPerTeam).ToList();

        var away = rows.Where(r => !r.IsHome)
            .OrderByDescending(r => r.Appearances).ThenBy(r => r.Name)
            .Take(maxPlayersPerTeam).ToList();

        return new MatchupForm(game, home, away, lines.Count);
    }

    /// <summary>
    /// One team's recent record and roster form, independent of any particular upcoming game.
    ///
    /// The per-matchup lookup above answers "how have these two sides been playing coming into
    /// this game"; this answers "how has this team been playing", which is what following a
    /// team name off any screen should open onto — the game that led there is context, not a
    /// requirement.
    /// </summary>
    public async Task<TeamForm?> GetTeamAsync(
        int teamId,
        TimeSpan window,
        int maxPlayers = 10,
        int? seasonYear = null,
        CancellationToken ct = default)
    {
        var team = await db.Teams
            .Include(t => t.Sport)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == teamId, ct);

        if (team is null)
            return null;

        var now = DateTimeOffset.UtcNow;
        var since = now - window;

        var played = db.Games
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .Where(g => (g.HomeTeamId == teamId || g.AwayTeamId == teamId) && g.StartsAt <= now);

        played = seasonYear is { } year
            ? played.Where(g => g.SeasonYear == year)
            : played.Where(g => g.StartsAt >= since);

        var games = await played
            .OrderByDescending(g => g.StartsAt)
            .AsNoTracking()
            .ToListAsync(ct);

        var recent = games.Select(g =>
        {
            var isHome = g.HomeTeamId == teamId;

            return new TeamRecentGame(
                GameId: g.Id,
                StartsAt: g.StartsAt,
                Opponent: (isHome ? g.AwayTeam?.Name : g.HomeTeam?.Name) ?? "—",
                Home: isHome,
                ScoreFor: isHome ? g.HomeScore : g.AwayScore,
                ScoreAgainst: isHome ? g.AwayScore : g.HomeScore,
                Status: g.Status);
        }).ToList();

        var roster = await RosterFormAsync(teamId, since, now, maxPlayers, seasonYear, ct);

        return new TeamForm(team, recent, roster);
    }

    /// <summary>
    /// One team's roster, with their appearances inside a window — or inside a season, when
    /// one is named. Shared by the team lookup above; the per-matchup lookup keeps its own
    /// two-team query because it can resolve both rosters in one round trip, which this
    /// single-team case has no need of.
    /// </summary>
    private async Task<List<PlayerForm>> RosterFormAsync(
        int teamId, DateTimeOffset since, DateTimeOffset before, int max, int? seasonYear, CancellationToken ct)
    {
        var players = await db.Players
            .Where(p => p.TeamId == teamId)
            .Select(p => new { p.Id, p.FullName, p.Position })
            .AsNoTracking()
            .ToListAsync(ct);

        if (players.Count == 0)
            return [];

        var playerIds = players.Select(p => p.Id).ToList();

        var prior = seasonYear is { } year
            ? db.Games.Where(g => g.StartsAt < before && g.SeasonYear == year)
            : db.Games.Where(g => g.StartsAt < before && g.StartsAt >= since);

        var lines = await db.PlayerGameStats
            .Where(s => playerIds.Contains(s.PlayerId))
            .Join(prior,
                s => s.GameId, g => g.Id,
                (s, g) => new { s.PlayerId, s.StatLine, g.StartsAt })
            .AsNoTracking()
            .ToListAsync(ct);

        var byPlayer = lines.GroupBy(l => l.PlayerId).ToDictionary(g => g.Key, g => g.ToList());
        var rows = new List<PlayerForm>();

        foreach (var player in players)
        {
            if (!byPlayer.TryGetValue(player.Id, out var appearances))
                continue;

            var totals = new Dictionary<string, double>();
            var counts = new Dictionary<string, int>();
            var counting = new Dictionary<string, bool>();

            foreach (var appearance in appearances)
            {
                foreach (var (key, value) in ParseNumeric(appearance.StatLine))
                {
                    totals[key] = totals.GetValueOrDefault(key) + value;
                    counts[key] = counts.GetValueOrDefault(key) + 1;

                    var isWhole = Math.Abs(value % 1) < 1e-9;
                    counting[key] = counting.GetValueOrDefault(key, true) && isWhole;
                }
            }

            rows.Add(new PlayerForm(
                PlayerId: player.Id,
                Name: player.FullName,
                Position: player.Position,
                IsHome: true,
                Appearances: appearances.Count,
                LastPlayed: appearances.Max(a => a.StartsAt),
                Totals: totals,
                Counts: counts,
                Counting: counting));
        }

        return rows.OrderByDescending(r => r.Appearances).ThenBy(r => r.Name).Take(max).ToList();
    }

    /// <summary>
    /// Pulls the numeric fields out of a stat line.
    ///
    /// Stat lines are jsonb with per-sport keys, so there is no fixed set to read. Values that
    /// do not parse as numbers — a pitching line like "6.0 IP", a time on ice of "39:09" — are
    /// skipped rather than coerced, because a wrong number is worse than a missing one.
    /// </summary>
    internal static IEnumerable<KeyValuePair<string, double>> ParseNumeric(string statLineJson)
    {
        Dictionary<string, string>? line;

        try
        {
            line = JsonSerializer.Deserialize<Dictionary<string, string>>(statLineJson);
        }
        catch (JsonException)
        {
            yield break;
        }

        if (line is null)
            yield break;

        foreach (var (key, raw) in line)
        {
            if (key == "category")
                continue;

            if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var value))
                yield return new KeyValuePair<string, double>(key, value);
        }
    }
}

/// <summary>Both sides of a matchup, with the game they belong to.</summary>
public record MatchupForm(
    Game Game,
    IReadOnlyList<PlayerForm> Home,
    IReadOnlyList<PlayerForm> Away,
    int LinesRead)
{
    public bool IsEmpty => Home.Count == 0 && Away.Count == 0;
}

/// <summary>One player's recent form. Only ever created for a player who actually played.</summary>
public record PlayerForm(
    int PlayerId,
    string Name,
    string? Position,
    bool IsHome,
    int Appearances,
    DateTimeOffset LastPlayed,
    IReadOnlyDictionary<string, double> Totals,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyDictionary<string, bool> Counting)
{
    /// <summary>Per-game average for a stat, or null when the player has no reading for it.</summary>
    public double? PerGame(string key)
        => Counts.TryGetValue(key, out var n) && n > 0 ? Totals[key] / n : null;

    public double? Total(string key)
        => Totals.TryGetValue(key, out var v) ? v : null;

    /// <summary>True when every observed value was a whole number, so summing it means something.</summary>
    public bool IsCounting(string key) => Counting.GetValueOrDefault(key, true);

    /// <summary>
    /// The right summary for this stat: a total for things that are counted, a mean for
    /// things that are rates.
    ///
    /// The mean of per-game rates is not always the true rate — a batting average over a
    /// stretch is total hits over total at-bats, not the average of each night's average —
    /// but it is the honest generic answer when the schema does not say which fields combine
    /// to make the rate, and it is the right order of magnitude, which a sum is not.
    /// </summary>
    public double? Summary(string key)
        => !Totals.ContainsKey(key) ? null : IsCounting(key) ? Total(key) : PerGame(key);
}

/// <summary>A team's recent record and roster form, read independent of any one upcoming game.</summary>
public record TeamForm(Team Team, IReadOnlyList<TeamRecentGame> Recent, IReadOnlyList<PlayerForm> Roster)
{
    public int Wins => Recent.Count(g => g.Won == true);
    public int Losses => Recent.Count(g => g.Won == false);
}

/// <summary>One game a team played (or is playing) inside the window, from that team's side.</summary>
public record TeamRecentGame(
    int GameId,
    DateTimeOffset StartsAt,
    string Opponent,
    bool Home,
    int? ScoreFor,
    int? ScoreAgainst,
    GameStatus Status)
{
    /// <summary>Null until the game is final — a game in progress has no result yet, not a loss.</summary>
    public bool? Won => Status == GameStatus.Final && ScoreFor is { } f && ScoreAgainst is { } a
        ? f > a
        : null;
}
