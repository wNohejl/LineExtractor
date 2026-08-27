using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Data.CrossReference;

/// <summary>
/// Reads individual games rather than summaries of them.
///
/// <para>
/// <see cref="MatchupCrossReference"/> aggregates a window into form — "12-8 over twenty
/// games", per-player totals. This is the opposite job: the rows those aggregates were
/// computed from, one per game, so an operator can see what a record is actually made of. A
/// 12-8 built from four blowouts and sixteen one-run games is not the same team as a 12-8
/// built the other way round, and only the log says which.
/// </para>
///
/// <para>
/// It is a lookup and not a copy, for the same reasons the aggregate side gives: nothing is
/// materialised into a summary table, because a stored aggregate is a second version of the
/// truth that goes stale without saying so.
/// </para>
///
/// <para>
/// Closing lines are joined in where they exist and left null where they do not. Only games
/// this system was running for have one — <c>OddsSnapshots</c> are deleted at first pitch
/// (ADR 0010) and <c>ClosingLines</c> is what survives — so a log reaching back far enough
/// will run out of market data before it runs out of games. Those rows are still returned:
/// dropping them would silently understate a record, which is the failure ADR 0003 is about.
/// </para>
/// </summary>
public class GameLogService(LineOpsDbContext db)
{
    /// <summary>
    /// One team's games inside a window, newest first, each graded against the closing line
    /// where one was captured.
    /// </summary>
    public async Task<IReadOnlyList<TeamGameLogRow>> TeamGameLogAsync(
        int teamId,
        TimeSpan window,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var since = now - window;

        var games = await db.Games
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .Where(g => (g.HomeTeamId == teamId || g.AwayTeamId == teamId)
                        && g.StartsAt >= since && g.StartsAt <= now)
            .OrderByDescending(g => g.StartsAt)
            .AsNoTracking()
            .ToListAsync(ct);

        if (games.Count == 0)
            return [];

        var lines = await ClosingLinesForAsync(games.Select(g => g.Id).ToList(), ct);

        return games.Select(g => BuildRow(g, teamId, lines)).ToList();
    }

    /// <summary>
    /// Previous meetings between the two teams in a game, newest first, excluding the game
    /// itself and anything not yet played.
    ///
    /// <para>
    /// Takes a game id rather than two team ids so any row on the desk can reach it without
    /// resolving the sides first — the matchup is what the operator clicked.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<HeadToHeadRow>> HeadToHeadAsync(
        int gameId,
        int take = 5,
        CancellationToken ct = default)
    {
        var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId, ct);

        if (game is null)
            return [];

        var home = game.HomeTeamId;
        var away = game.AwayTeamId;

        var meetings = await db.Games
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .Where(g => g.Id != gameId
                        && g.StartsAt < game.StartsAt
                        && ((g.HomeTeamId == home && g.AwayTeamId == away)
                            || (g.HomeTeamId == away && g.AwayTeamId == home)))
            .OrderByDescending(g => g.StartsAt)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(ct);

        if (meetings.Count == 0)
            return [];

        var lines = await ClosingLinesForAsync(meetings.Select(g => g.Id).ToList(), ct);

        // Read from the perspective of the upcoming game's home team throughout, so a column
        // of results answers "how has this side done in this fixture" rather than flipping
        // meaning with the venue.
        return meetings.Select(g =>
        {
            var row = BuildRow(g, home, lines);

            return new HeadToHeadRow(
                GameId: g.Id,
                StartsAt: g.StartsAt,
                HomeTeam: g.HomeTeam?.Name ?? "—",
                AwayTeam: g.AwayTeam?.Name ?? "—",
                HomeScore: g.HomeScore,
                AwayScore: g.AwayScore,
                Status: g.Status,
                SubjectWasHome: g.HomeTeamId == home,
                SubjectWon: row.Won,
                ClosingSpread: row.ClosingSpread,
                AtsResult: row.AtsResult,
                ClosingTotal: row.ClosingTotal,
                TotalResult: row.TotalResult);
        }).ToList();
    }

    /// <summary>
    /// One player's appearances, newest first — the stat lines the aggregate on a roster view
    /// was averaged from.
    /// </summary>
    public async Task<IReadOnlyList<PlayerGameRow>> PlayerGameLogAsync(
        int playerId,
        int take = 25,
        CancellationToken ct = default)
    {
        var rows = await db.PlayerGameStats
            .Where(s => s.PlayerId == playerId)
            .Join(db.Games.Include(g => g.HomeTeam).Include(g => g.AwayTeam),
                s => s.GameId, g => g.Id,
                (s, g) => new { s.StatLine, Game = g })
            .OrderByDescending(x => x.Game.StartsAt)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(ct);

        if (rows.Count == 0)
            return [];

        // Which side the player was on is not recorded on the stat line, so it is inferred
        // from their current team. A player traded mid-season will read wrong for games before
        // the move; that is a known limit of storing team on the player rather than on the
        // appearance, and it is better than omitting the column.
        var teamId = await db.Players
            .Where(p => p.Id == playerId)
            .Select(p => p.TeamId)
            .FirstOrDefaultAsync(ct);

        return rows.Select(x =>
        {
            var g = x.Game;
            var isHome = g.HomeTeamId == teamId;

            return new PlayerGameRow(
                GameId: g.Id,
                StartsAt: g.StartsAt,
                Opponent: (isHome ? g.AwayTeam?.Name : g.HomeTeam?.Name) ?? "—",
                Home: isHome,
                ScoreFor: isHome ? g.HomeScore : g.AwayScore,
                ScoreAgainst: isHome ? g.AwayScore : g.HomeScore,
                Status: g.Status,
                Stats: ReadStatLine(x.StatLine));
        }).ToList();
    }

    /// <summary>
    /// What each player actually did in one game.
    ///
    /// <para>
    /// The game view used to show a window aggregate here — every player's last thirty days —
    /// which answers a question nobody asked while looking at a specific game. Once a game has
    /// started, the interesting line is the one from <i>this</i> game, and it was already stored:
    /// the aggregate everywhere else on the desk is computed from exactly these rows.
    /// </para>
    ///
    /// <para>
    /// Returns null when the game has no stat lines yet, which is the ordinary state of a
    /// fixture that has not been played. That is distinct from a game with an empty box score,
    /// and the caller shows a preview rather than an empty table.
    /// </para>
    /// </summary>
    public async Task<GameBoxScore?> BoxScoreAsync(int gameId, CancellationToken ct = default)
    {
        var game = await db.Games
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == gameId, ct);

        if (game is null)
            return null;

        var lines = await db.PlayerGameStats
            .Where(s => s.GameId == gameId)
            .Join(db.Players, s => s.PlayerId, p => p.Id,
                (s, p) => new { s.StatLine, p.Id, p.FullName, p.Position, p.TeamId })
            .AsNoTracking()
            .ToListAsync(ct);

        if (lines.Count == 0)
            return null;

        var rows = lines
            .Select(l => new BoxScoreLine(
                PlayerId: l.Id,
                Name: l.FullName,
                Position: l.Position,
                IsHome: l.TeamId == game.HomeTeamId,
                Stats: ReadStatLine(l.StatLine)))
            .ToList();

        // Ordered by how much of the line is filled in, which puts the players who actually
        // featured above those with a single appearance stat. Alphabetical inside that, so a
        // reader can find a name.
        static List<BoxScoreLine> Side(IEnumerable<BoxScoreLine> side)
            => side.OrderByDescending(r => r.Stats.Count)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        return new GameBoxScore(
            game,
            Side(rows.Where(r => r.IsHome)),
            Side(rows.Where(r => !r.IsHome)));
    }

    /// <summary>
    /// Closing lines for a set of games, keyed by game.
    ///
    /// <para>
    /// A game can close at several books, and they rarely agree exactly. The consensus taken
    /// here is the median line per market, which is the number a game is normally described by
    /// — an outlier book should not be what a whole season's ATS column is graded against.
    /// </para>
    /// </summary>
    private async Task<Dictionary<int, GameClosingLines>> ClosingLinesForAsync(
        List<int> gameIds, CancellationToken ct)
    {
        var all = await db.ClosingLines
            .Where(c => gameIds.Contains(c.GameId)
                        && (c.Market == Markets.Spread || c.Market == Markets.Total))
            .Select(c => new { c.GameId, c.Market, c.Outcome, c.Line, Kind = c.Source!.Kind })
            .AsNoTracking()
            .ToListAsync(ct);

        // A book market outranks a stats provider's reference line, and does so per game rather
        // than globally: a fixture the odds feed covered is read from the market, while one it
        // never saw falls back to the reference instead of to nothing.
        //
        // They are never mixed. A reference is one book, and letting it into a consensus meant
        // to be drawn from several would move the number it is being averaged against — the
        // precise confusion ADR 0011 refuses.
        var lines = all
            .GroupBy(c => c.GameId)
            .SelectMany(g => g.Any(c => c.Kind == SourceKind.Odds)
                ? g.Where(c => c.Kind == SourceKind.Odds)
                : g)
            .ToList();

        return lines
            .GroupBy(c => c.GameId)
            .ToDictionary(g => g.Key, g => new GameClosingLines(
                // Spread is stored per side, so it is only meaningful with the outcome it
                // belongs to. Kept as a name→line map and resolved against the team later.
                Spreads: g.Where(c => c.Market == Markets.Spread && c.Line is not null)
                    .GroupBy(c => c.Outcome, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        s => s.Key,
                        s => Median(s.Select(x => x.Line!.Value)),
                        StringComparer.OrdinalIgnoreCase),

                // A total is one number for the game, whichever side of it a book quoted.
                Total: g.Where(c => c.Market == Markets.Total && c.Line is not null)
                    .Select(c => c.Line!.Value)
                    .ToList() is { Count: > 0 } totals
                    ? Median(totals)
                    : null));
    }

    private static TeamGameLogRow BuildRow(
        Game game, int teamId, Dictionary<int, GameClosingLines> lines)
    {
        var isHome = game.HomeTeamId == teamId;
        var teamName = (isHome ? game.HomeTeam?.Name : game.AwayTeam?.Name) ?? string.Empty;

        var scoreFor = isHome ? game.HomeScore : game.AwayScore;
        var scoreAgainst = isHome ? game.AwayScore : game.HomeScore;

        lines.TryGetValue(game.Id, out var closing);

        // A team's handicap is the one quoted against its own name. Falling back to the
        // opposite side negated would look tidy and be wrong whenever the two books disagree,
        // which is exactly when the number matters.
        var spread = closing?.SpreadFor(teamName);
        var total = closing?.Total;

        EntryResult? ats = null;
        EntryResult? overUnder = null;

        // Only a finished game with both scores can be graded. A live game has a score and no
        // result, which is not the same as a loss.
        if (game.Status == GameStatus.Final
            && game.HomeScore is { } h
            && game.AwayScore is { } a
            && game.HomeTeam?.Name is { } homeName
            && game.AwayTeam?.Name is { } awayName)
        {
            if (spread is not null)
            {
                ats = Grading.GradeOutcome(
                    Markets.Spread, teamName, spread, homeName, awayName, h, a);
            }

            if (total is not null)
            {
                overUnder = Grading.GradeOutcome(
                    Markets.Total, "over", total, homeName, awayName, h, a);
            }
        }

        return new TeamGameLogRow(
            GameId: game.Id,
            StartsAt: game.StartsAt,
            Opponent: (isHome ? game.AwayTeam?.Name : game.HomeTeam?.Name) ?? "—",
            Home: isHome,
            ScoreFor: scoreFor,
            ScoreAgainst: scoreAgainst,
            Status: game.Status,
            ClosingSpread: spread,
            AtsResult: ats,
            ClosingTotal: total,
            TotalResult: overUnder);
    }

    /// <summary>
    /// The middle value, averaging the two middles on an even count.
    ///
    /// Median rather than mean because book disagreement is not symmetrical noise — one book
    /// hanging a stale number should move the consensus by nothing, and a mean lets it.
    /// </summary>
    private static decimal Median(IEnumerable<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToList();

        if (sorted.Count == 0)
            return 0m;

        var mid = sorted.Count / 2;

        return sorted.Count % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2m;
    }

    /// <summary>
    /// The stat line as a display map, dropping the metadata key the box-score parser adds.
    ///
    /// Values are kept as strings rather than parsed: a game log shows what was recorded, and
    /// a pitching line of "6.0 IP" or a time on ice of "39:09" is meaningful as written and
    /// meaningless coerced. Numeric parsing belongs to the aggregate side, which needs it.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ReadStatLine(string statLineJson)
    {
        try
        {
            var parsed = System.Text.Json.JsonSerializer
                .Deserialize<Dictionary<string, string>>(statLineJson);

            if (parsed is null)
                return new Dictionary<string, string>();

            parsed.Remove("category");
            return parsed;
        }
        catch (System.Text.Json.JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>The consensus closing numbers for one game.</summary>
    private sealed record GameClosingLines(
        Dictionary<string, decimal> Spreads,
        decimal? Total)
    {
        public decimal? SpreadFor(string teamName)
            => Spreads.TryGetValue(teamName, out var line) ? line : null;
    }
}

/// <summary>
/// One game from a team's perspective, with what the market said about it.
///
/// <para>
/// The two result fields are null when there is no reading — no closing line, or a game not
/// yet final. Null is distinct from <see cref="EntryResult.Push"/>: a game with no stored
/// number did not land on it.
/// </para>
/// </summary>
public record TeamGameLogRow(
    int GameId,
    DateTimeOffset StartsAt,
    string Opponent,
    bool Home,
    int? ScoreFor,
    int? ScoreAgainst,
    GameStatus Status,
    decimal? ClosingSpread,
    EntryResult? AtsResult,
    decimal? ClosingTotal,
    EntryResult? TotalResult)
{
    /// <summary>Null until the game is final — a game in progress has no result yet.</summary>
    public bool? Won => Status == GameStatus.Final && ScoreFor is { } f && ScoreAgainst is { } a
        ? f > a
        : null;

    /// <summary>Whether this row contributed any market data, for coverage counting.</summary>
    public bool HasClosingLine => ClosingSpread is not null || ClosingTotal is not null;

    /// <summary>
    /// Whether the score means anything yet.
    ///
    /// A fixture that has not been played carries zeroes rather than nulls, so a null check
    /// alone renders an unplayed game as a nil-nil draw — the exact confusion ADR 0003 is
    /// about. The status is what distinguishes them, so it has to be part of the question.
    /// </summary>
    public bool HasScore
        => Status is GameStatus.Final or GameStatus.Live
           && ScoreFor is not null
           && ScoreAgainst is not null;
}

/// <summary>
/// A previous meeting between two teams. Result fields read from the perspective of the
/// upcoming game's home team, so the column means one thing all the way down.
/// </summary>
public record HeadToHeadRow(
    int GameId,
    DateTimeOffset StartsAt,
    string HomeTeam,
    string AwayTeam,
    int? HomeScore,
    int? AwayScore,
    GameStatus Status,
    bool SubjectWasHome,
    bool? SubjectWon,
    decimal? ClosingSpread,
    EntryResult? AtsResult,
    decimal? ClosingTotal,
    EntryResult? TotalResult)
{
    public bool HasClosingLine => ClosingSpread is not null || ClosingTotal is not null;

    /// <summary>See <see cref="TeamGameLogRow.HasScore"/> — an unplayed fixture holds zeroes.</summary>
    public bool HasScore
        => Status is GameStatus.Final or GameStatus.Live
           && HomeScore is not null
           && AwayScore is not null;
}

/// <summary>One appearance, with the stat line exactly as it was recorded.</summary>
public record PlayerGameRow(
    int GameId,
    DateTimeOffset StartsAt,
    string Opponent,
    bool Home,
    int? ScoreFor,
    int? ScoreAgainst,
    GameStatus Status,
    IReadOnlyDictionary<string, string> Stats)
{
    public bool? Won => Status == GameStatus.Final && ScoreFor is { } f && ScoreAgainst is { } a
        ? f > a
        : null;

    /// <summary>See <see cref="TeamGameLogRow.HasScore"/> — an unplayed fixture holds zeroes.</summary>
    public bool HasScore
        => Status is GameStatus.Final or GameStatus.Live
           && ScoreFor is not null
           && ScoreAgainst is not null;
}

/// <summary>
/// One game's box score, both sides.
///
/// The rosters here are who <i>played</i>, not who was on the books: a player with no line in
/// this game has no row, which is the same rule storage follows and the same one the aggregate
/// side follows. A blank and a zero mean different things and only one of them belongs here.
/// </summary>
public record GameBoxScore(
    Game Game,
    IReadOnlyList<BoxScoreLine> Home,
    IReadOnlyList<BoxScoreLine> Away)
{
    public bool IsEmpty => Home.Count == 0 && Away.Count == 0;

    /// <summary>
    /// Stat columns for one side, most widely reported first.
    ///
    /// Derived rather than fixed because a stat line is per-sport jsonb, and no column set fits
    /// baseball and hockey both.
    /// </summary>
    public static List<string> ColumnsFor(IReadOnlyList<BoxScoreLine> side, int max = 8)
        => side.SelectMany(r => r.Stats.Keys)
            .GroupBy(k => k)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key)
            .Take(max)
            .ToList();
}

/// <summary>One player's line from one game, exactly as it was recorded.</summary>
public record BoxScoreLine(
    int PlayerId,
    string Name,
    string? Position,
    bool IsHome,
    IReadOnlyDictionary<string, string> Stats);
