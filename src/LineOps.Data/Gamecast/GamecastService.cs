using System.Text.Json;
using System.Text.Json.Nodes;
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Data.Gamecast;

/// <summary>
/// What the Gamecast window reads for one game: the header (score, clock, situation and the
/// pre-game close), its plays, its drives, the probability line and the scoring summary
/// (spec 2026-10-24 §6.1, §6.4).
///
/// <para>
/// The window appends rather than reloads. Every open Gamecast on every circuit hears the same
/// <c>plays:{id}</c> notice and asks <see cref="PlaysSinceAsync"/> for what came after the last
/// play it holds, so that read is one query down <c>ix_game_play_game_sequence</c>, untracked,
/// projected straight to a record. The rest are asked when the window opens, or — the header —
/// on a <c>games:{id}</c> notice.
/// </para>
///
/// <para>
/// Nothing here hands the window an entity or a jsonb string. The linescore, the play
/// situations and baseball's diamond are parsed once, here, and a value that cannot be read
/// arrives as nothing rather than as an exception in a render: every ESPN field is optional
/// (spec §7), and a window that throws over a malformed situation shows no game at all.
/// </para>
///
/// <para>
/// A context per call, from the factory, as the Blazor panels take theirs: a window may ask for
/// its header and its plays at once, and one scoped context would refuse the second.
/// </para>
/// </summary>
public class GamecastService(IDbContextFactory<LineOpsDbContext> contexts)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The game with its teams, the state of play and the pre-game close; null when there is no
    /// such game.
    ///
    /// <para>
    /// The close is the board's own number (<see cref="BoardService.GetRowAsync"/>): the best home
    /// handicap and the best total, read market-first exactly as the board reads it, so the
    /// reference line on the chart is the number the operator saw on the row they opened it from.
    /// </para>
    /// </summary>
    public async Task<GamecastHeader?> HeaderAsync(int gameId, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        var row = await new BoardService(db).GetRowAsync(gameId, ct);

        if (row is null)
            return null;

        var game = row.Game;
        var (home, away) = Linescore(game.Linescore);

        var spread = row.Spread.First?.Line;
        var total = row.Total.First?.Line ?? row.Total.Second?.Line;

        return new GamecastHeader(
            GameId: game.Id,
            SportKey: game.Sport?.Key ?? string.Empty,
            SportName: game.Sport?.Name ?? string.Empty,
            Home: Team(game.HomeTeamId, game.HomeTeam),
            Away: Team(game.AwayTeamId, game.AwayTeam),
            StartsAt: game.StartsAt,
            Status: game.Status,
            HomeScore: game.HomeScore,
            AwayScore: game.AwayScore,
            Period: game.Period,
            DisplayClock: game.DisplayClock,
            ClockSeconds: game.ClockSeconds,
            StatusDetail: game.StatusDetail,
            PossessionTeamId: game.PossessionTeamId,
            Down: game.Down,
            Distance: game.Distance,
            YardLine: game.YardLine,
            DownDistanceText: game.DownDistanceText,
            PossessionText: game.PossessionText,
            IsRedZone: game.IsRedZone,
            HomeTimeouts: game.HomeTimeouts,
            AwayTimeouts: game.AwayTimeouts,
            LastPlay: game.LastPlay,
            HomeLinescore: home,
            AwayLinescore: away,
            Diamond: Read<GamecastDiamond>(game.Situation),
            LiveUpdatedAt: game.LiveUpdatedAt,
            Close: spread is null && total is null ? null : new GamecastClose(spread, total, row.PricesAreClosing));
    }

    /// <summary>
    /// Every play of the game, in the provider's order, ties in the order they were stored. Its
    /// own read rather than "after zero": the adapter stores a play ESPN sent no sequence for at
    /// zero, and "after zero" would leave it out.
    /// </summary>
    public async Task<IReadOnlyList<GamecastPlay>> PlaysAsync(int gameId, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        return await PlaysAsync(db.GamePlays.Where(p => p.GameId == gameId), ct);
    }

    /// <summary>
    /// The plays strictly after <paramref name="afterSequence"/>, in the provider's order, ties
    /// in the order they were stored. The hot path: see the class summary. Every play is
    /// <see cref="PlaysAsync(int, CancellationToken)"/>.
    ///
    /// <para>
    /// A play ESPN rewrites after a review may keep its sequence or take a new one
    /// (GamecastWriter updates it in place), so a window that wants corrections reads the drive
    /// it has open again and matches plays by id.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<GamecastPlay>> PlaysSinceAsync(
        int gameId, long afterSequence, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        return await PlaysAsync(db.GamePlays.Where(p => p.GameId == gameId && p.Sequence > afterSequence), ct);
    }

    /// <summary>The scoring plays, in order: the compact "how it got to 17–20" view.</summary>
    public async Task<IReadOnlyList<GamecastPlay>> ScoringPlaysAsync(int gameId, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        return await PlaysAsync(db.GamePlays.Where(p => p.GameId == gameId && p.IsScoring), ct);
    }

    /// <summary>
    /// The drives in order, each with how many of its plays we hold and the sequence range they
    /// span — enough for a collapsed drive's result line ("7 plays, 62 yds, 3:48 · TD") and for
    /// opening it onto its plays without a query per drive.
    ///
    /// <para>
    /// <see cref="GamecastDrive.OffensivePlays"/> is the provider's count, which leaves out
    /// penalties and kicks; <see cref="GamecastDrive.PlayCount"/> is the rows we hold. The result
    /// line wants the first, the drive's play list the second.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<GamecastDrive>> DrivesAsync(int gameId, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        var drives = await db.GameDrives
            .AsNoTracking()
            .Where(d => d.GameId == gameId)
            .OrderBy(d => d.Sequence)
            .Select(d => new
            {
                d.Sequence,
                d.TeamId,
                TeamAbbrev = d.Team!.Abbrev,
                d.Description,
                d.Result,
                d.Yards,
                d.OffensivePlays,
                d.TimeElapsed,
                d.IsScore,
                d.IsCurrent,
                d.StartPeriod,
                d.StartClock,
                d.StartYardLine,
                d.EndPeriod,
                d.EndClock,
                d.EndYardLine
            })
            .ToListAsync(ct);

        if (drives.Count == 0)
            return [];

        var spans = await db.GamePlays
            .AsNoTracking()
            .Where(p => p.GameId == gameId && p.DriveSeq != null)
            .GroupBy(p => p.DriveSeq!.Value)
            .Select(g => new { Drive = g.Key, Count = g.Count(), First = g.Min(p => p.Sequence), Last = g.Max(p => p.Sequence) })
            .ToDictionaryAsync(x => x.Drive, ct);

        return drives.Select(d =>
        {
            var span = spans.GetValueOrDefault(d.Sequence);

            return new GamecastDrive(
                d.Sequence, d.TeamId, d.TeamAbbrev, d.Description, d.Result, d.Yards, d.OffensivePlays,
                d.TimeElapsed, d.IsScore, d.IsCurrent, d.StartPeriod, d.StartClock, d.StartYardLine,
                d.EndPeriod, d.EndClock, d.EndYardLine,
                PlayCount: span?.Count ?? 0,
                FirstSequence: span?.First,
                LastSequence: span?.Last);
        }).ToList();
    }

    /// <summary>
    /// The probability line over play sequence, as fractions of one: home win, home covering the
    /// spread, and the total going over. Plays the provider gave no probability for are left out
    /// rather than drawn as zero.
    /// </summary>
    public async Task<IReadOnlyList<GamecastProbability>> ProbabilitySeriesAsync(
        int gameId, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        var rows = await db.GamePlays
            .AsNoTracking()
            .Where(p => p.GameId == gameId
                && (p.HomeWinPct != null || p.SpreadCoverPctHome != null || p.TotalOverPct != null))
            .OrderBy(p => p.Sequence)
            .Select(p => new { p.Sequence, p.Period, p.Clock, p.HomeWinPct, p.SpreadCoverPctHome, p.TotalOverPct })
            .ToListAsync(ct);

        return rows
            .Select(p => new GamecastProbability(
                p.Sequence, p.Period, p.Clock,
                (double?)p.HomeWinPct, (double?)p.SpreadCoverPctHome, (double?)p.TotalOverPct))
            .ToList();
    }

    private static async Task<IReadOnlyList<GamecastPlay>> PlaysAsync(IQueryable<GamePlay> plays, CancellationToken ct)
    {
        var rows = await plays
            .AsNoTracking()
            .OrderBy(p => p.Sequence)
            .ThenBy(p => p.Id)
            .Select(p => new
            {
                p.Id,
                p.Sequence,
                p.DriveSeq,
                p.Period,
                p.Clock,
                p.ClockSeconds,
                p.Type,
                p.Text,
                p.HomeScore,
                p.AwayScore,
                p.IsScoring,
                p.IsTurnover,
                p.IsPenalty,
                p.Yardage,
                p.TeamId,
                p.StartSituation,
                p.EndSituation,
                p.HomeWinPct,
                p.SpreadCoverPctHome,
                p.TotalOverPct,
                p.Wallclock,
                p.Modified
            })
            .ToListAsync(ct);

        return rows
            .Select(p => new GamecastPlay(
                p.Id, p.Sequence, p.DriveSeq, p.Period, p.Clock, p.ClockSeconds, p.Type, p.Text,
                p.HomeScore, p.AwayScore, p.IsScoring, p.IsTurnover, p.IsPenalty, p.Yardage, p.TeamId,
                Read<GamecastSituation>(p.StartSituation), Read<GamecastSituation>(p.EndSituation),
                (double?)p.HomeWinPct, (double?)p.SpreadCoverPctHome, (double?)p.TotalOverPct,
                p.Wallclock, p.Modified))
            .ToList();
    }

    private static GamecastTeam Team(int id, Team? team)
        => new(id, team?.Name ?? string.Empty, team?.Abbrev ?? string.Empty);

    /// <summary>A small jsonb object as its record, or null when there is none or it cannot be read.</summary>
    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Points by period for each side, first period first. A side whose list is missing or holds
    /// anything but whole numbers is empty: a linescore with a hole in it would put the third
    /// quarter's points under the second, which is the adapter's rule too.
    /// </summary>
    private static (IReadOnlyList<int> Home, IReadOnlyList<int> Away) Linescore(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ([], []);

        try
        {
            return JsonNode.Parse(json) is JsonObject o ? (Side(o["home"]), Side(o["away"])) : ([], []);
        }
        catch (JsonException)
        {
            return ([], []);
        }

        static IReadOnlyList<int> Side(JsonNode? node)
        {
            if (node is not JsonArray periods)
                return [];

            var points = new List<int>(periods.Count);
            foreach (var period in periods)
            {
                if (period is not JsonValue value || !value.TryGetValue<int>(out var n))
                    return [];
                points.Add(n);
            }

            return points;
        }
    }
}

/// <summary>One side of the game, as the window labels it.</summary>
public sealed record GamecastTeam(int Id, string Name, string Abbrev);

/// <summary>
/// The pre-game market: the home side's handicap and the total. <see cref="IsClosing"/> is false
/// only for a game not yet started, whose number is still the live scan.
/// </summary>
public sealed record GamecastClose(decimal? HomeSpread, decimal? Total, bool IsClosing);

/// <summary>Baseball's situation (<see cref="Game.Situation"/>): the count, the runners and the matchup.</summary>
public sealed record GamecastDiamond(
    int? Balls, int? Strikes, int? Outs, bool OnFirst, bool OnSecond, bool OnThird, string? Batter, string? Pitcher);

/// <summary>
/// The game and its state of play, for the title and the field strip. <see cref="YardLine"/> is
/// yards from the <b>home</b> goal line (see <see cref="Game.YardLine"/>). The linescores are
/// empty before a period has been played.
/// </summary>
public sealed record GamecastHeader(
    int GameId,
    string SportKey,
    string SportName,
    GamecastTeam Home,
    GamecastTeam Away,
    DateTimeOffset StartsAt,
    GameStatus Status,
    int? HomeScore,
    int? AwayScore,
    int? Period,
    string? DisplayClock,
    int? ClockSeconds,
    string? StatusDetail,
    int? PossessionTeamId,
    int? Down,
    int? Distance,
    int? YardLine,
    string? DownDistanceText,
    string? PossessionText,
    bool? IsRedZone,
    int? HomeTimeouts,
    int? AwayTimeouts,
    string? LastPlay,
    IReadOnlyList<int> HomeLinescore,
    IReadOnlyList<int> AwayLinescore,
    GamecastDiamond? Diamond,
    DateTimeOffset? LiveUpdatedAt,
    GamecastClose? Close);

/// <summary>
/// Down, distance and field position either side of a snap. <see cref="YardLine"/> is on the
/// home-goal frame; <see cref="TeamId"/> is our <see cref="Team.Id"/> for the side with the ball.
/// </summary>
public sealed record GamecastSituation(
    int? Down,
    int? Distance,
    int? YardLine,
    int? YardsToEndzone,
    string? DownDistanceText,
    string? PossessionText,
    int? TeamId);

/// <summary>One play, as a drive-log row draws it. Probabilities are fractions of one.</summary>
public sealed record GamecastPlay(
    long Id,
    long Sequence,
    int? DriveSeq,
    int? Period,
    string? Clock,
    int? ClockSeconds,
    string? Type,
    string? Text,
    int? HomeScore,
    int? AwayScore,
    bool IsScoring,
    bool IsTurnover,
    bool IsPenalty,
    int? Yardage,
    int? TeamId,
    GamecastSituation? Start,
    GamecastSituation? End,
    double? HomeWinPct,
    double? SpreadCoverPctHome,
    double? TotalOverPct,
    DateTimeOffset? Wallclock,
    DateTimeOffset? Modified);

/// <summary>
/// One drive. <see cref="PlayCount"/> and the sequence range are the plays we hold under it
/// (<see cref="GamePlay.DriveSeq"/> = <see cref="Sequence"/>); both are empty for a drive with none yet.
/// </summary>
public sealed record GamecastDrive(
    int Sequence,
    int? TeamId,
    string? TeamAbbrev,
    string? Description,
    string? Result,
    int? Yards,
    int? OffensivePlays,
    string? TimeElapsed,
    bool IsScore,
    bool IsCurrent,
    int? StartPeriod,
    string? StartClock,
    int? StartYardLine,
    int? EndPeriod,
    string? EndClock,
    int? EndYardLine,
    int PlayCount,
    long? FirstSequence,
    long? LastSequence);

/// <summary>One point on the probability line, at a play. Fractions of one; null where the provider gave none.</summary>
public sealed record GamecastProbability(
    long Sequence,
    int? Period,
    string? Clock,
    double? HomeWin,
    double? SpreadCoverHome,
    double? TotalOver);
