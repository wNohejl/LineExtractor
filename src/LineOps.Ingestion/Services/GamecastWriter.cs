using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace LineOps.Ingestion.Services;

/// <summary>What one write did: the rows it added and the rows it changed, plays and drives apart.</summary>
public sealed record GamecastWriteResult(int PlaysInserted, int PlaysUpdated, int DrivesInserted, int DrivesUpdated)
{
    /// <summary>Whether the write saved anything — and so raised a <c>plays:{id}</c> notice.</summary>
    public bool Changed => PlaysInserted + PlaysUpdated + DrivesInserted + DrivesUpdated > 0;
}

/// <summary>
/// Upserts one game's play-by-play into the play store (spec §6.2), a poll at a time.
///
/// <para>
/// The feed hands over the whole gamecast every fifteen seconds while a game is on, and almost
/// all of it is what the last poll already wrote. So the writer compares before it assigns —
/// a property moves only when its value differs, <see cref="PlayerGameStat"/>'s pattern and
/// <see cref="EntityResolver"/>'s live state's — and a poll that saw nothing new leaves every row
/// unmodified and saves nothing. That matters beyond the write: a save is what raises
/// <c>plays:{GameId}</c>, and the Gamecast window reloads on it. One save per call, so one
/// notice per poll however many plays it carried.
/// </para>
///
/// <para>
/// ESPN rewrites plays after the fact — a review, a stat correction — and stamps them
/// <c>modified</c>; those update in place, by the provider's id. Nothing is ever deleted: a play
/// the payload stops listing still happened, and the store keeps the season (spec §6.2).
/// </para>
///
/// <para>
/// What is read back must compare equal to what is written, or every poll would call every row
/// changed: probabilities are rounded to the four places the column keeps, times are taken to UTC
/// and to the microsecond Postgres holds, and the two situation columns are compared as json —
/// Postgres hands jsonb back re-spaced and with its keys reordered.
/// </para>
///
/// <para>
/// The short strings — a play's type and clock, a drive's result and clocks, the provider's ids —
/// are cut to their columns before they are compared, at the lengths the context maps them with,
/// read from the model so the two cannot drift. One over-long value from ESPN would otherwise fail
/// every save for the game, and an uncut value would never compare equal to its stored cut.
/// </para>
/// </summary>
public class GamecastWriter(LineOpsDbContext db)
{
    /// <summary>The places <c>numeric(6,4)</c> keeps.</summary>
    private const int ProbabilityPlaces = 4;

    private static readonly JsonSerializerOptions SituationJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Writes <paramref name="gamecast"/> for the known game <paramref name="gameId"/> under the
    /// registered source <paramref name="sourceKey"/>, and says what moved.
    /// </summary>
    /// <exception cref="InvalidOperationException">The source is not registered, or the game does not exist.</exception>
    public async Task<GamecastWriteResult> WriteAsync(
        int gameId, string sourceKey, CanonicalGamecast gamecast, CancellationToken ct)
    {
        var source = await db.Sources.FirstOrDefaultAsync(s => s.Key == sourceKey, ct)
            ?? throw new InvalidOperationException($"Source '{sourceKey}' is not registered.");

        var game = await db.Games.AsNoTracking()
            .Where(g => g.Id == gameId)
            .Select(g => new { g.HomeTeamId, g.AwayTeamId })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Game {gameId} does not exist.");

        var sides = await SidesAsync(game.HomeTeamId, game.AwayTeamId, sourceKey, ct);
        int? Side(string? sourceTeamId) => sourceTeamId is not null && sides.TryGetValue(sourceTeamId, out var id) ? id : null;

        var now = DateTimeOffset.UtcNow;
        var columns = Columns.Of(db.Model);
        var (drivesInserted, drivesUpdated) = await WriteDrivesAsync(gameId, source.Id, gamecast.Drives, Side, columns, now, ct);

        // A play names its drive by the provider's id; the store links it by the drive's sequence.
        var driveSeq = gamecast.Drives
            .GroupBy(d => Fit(d.SourceDriveId, columns.DriveId)!)
            .ToDictionary(g => g.Key, g => g.Last().Sequence);

        var (playsInserted, playsUpdated) = await WritePlaysAsync(gameId, source.Id, gamecast.Plays, driveSeq, Side, columns, now, ct);

        var result = new GamecastWriteResult(playsInserted, playsUpdated, drivesInserted, drivesUpdated);

        if (result.Changed)
            await db.SaveChangesAsync(ct);

        return result;
    }

    /// <summary>
    /// Our team for each ESPN team id — this game's home and away, by the id the provider gave
    /// each when they were resolved. Never a lookup across the league and never a new team: a
    /// play naming neither side reads as no one (as <see cref="EntityResolver"/>'s possession does).
    /// </summary>
    private async Task<Dictionary<string, int>> SidesAsync(int homeTeamId, int awayTeamId, string sourceKey, CancellationToken ct)
    {
        var teams = await db.Teams.AsNoTracking()
            .Where(t => t.Id == homeTeamId || t.Id == awayTeamId)
            .ToListAsync(ct);

        var sides = new Dictionary<string, int>();

        foreach (var team in teams)
        {
            if (team.ExternalIds.TryGetValue(sourceKey, out var id))
                sides.TryAdd(id, team.Id);
        }

        return sides;
    }

    private async Task<(int Inserted, int Updated)> WriteDrivesAsync(
        int gameId, int sourceId, IReadOnlyList<CanonicalDrive> drives, Func<string?, int?> side,
        Columns columns, DateTimeOffset now, CancellationToken ct)
    {
        var stored = await db.GameDrives
            .Where(d => d.GameId == gameId && d.SourceId == sourceId)
            .ToDictionaryAsync(d => d.SourceDriveId, ct);

        var inserted = 0;
        var updated = 0;

        // The last word on a drive the payload lists twice is its later listing. The drive that
        // was current last poll is listed again as a previous one, and its flag moves with it.
        foreach (var listed in drives.GroupBy(d => Fit(d.SourceDriveId, columns.DriveId)!))
        {
            var (id, drive) = (listed.Key, listed.Last());

            if (!stored.TryGetValue(id, out var row))
            {
                row = new GameDrive { GameId = gameId, SourceId = sourceId, SourceDriveId = id };
                Apply(row, drive, side, columns);
                row.UpdatedAt = now;
                db.GameDrives.Add(row);
                inserted++;
            }
            else if (Apply(row, drive, side, columns))
            {
                row.UpdatedAt = now;
                updated++;
            }
        }

        return (inserted, updated);
    }

    private async Task<(int Inserted, int Updated)> WritePlaysAsync(
        int gameId, int sourceId, IReadOnlyList<CanonicalPlay> plays, IReadOnlyDictionary<string, int> driveSeq,
        Func<string?, int?> side, Columns columns, DateTimeOffset now, CancellationToken ct)
    {
        var stored = await db.GamePlays
            .Where(p => p.GameId == gameId && p.SourceId == sourceId)
            .ToDictionaryAsync(p => p.SourcePlayId, ct);

        var inserted = 0;
        var updated = 0;

        foreach (var listed in plays.GroupBy(p => Fit(p.SourcePlayId, columns.PlayId)!))
        {
            var (id, play) = (listed.Key, listed.Last());
            int? drive = Fit(play.SourceDriveId, columns.DriveId) is { } driveId && driveSeq.TryGetValue(driveId, out var seq)
                ? seq
                : null;

            if (!stored.TryGetValue(id, out var row))
            {
                row = new GamePlay { GameId = gameId, SourceId = sourceId, SourcePlayId = id };
                Apply(row, play, drive, side, columns);
                row.UpdatedAt = now;
                db.GamePlays.Add(row);
                inserted++;
            }
            else if (Apply(row, play, drive, side, columns))
            {
                row.UpdatedAt = now;
                updated++;
            }
        }

        return (inserted, updated);
    }

    /// <summary>Copies the drive onto the row, change-only, and says whether anything moved.</summary>
    private static bool Apply(GameDrive row, CanonicalDrive drive, Func<string?, int?> side, Columns columns)
    {
        var moved = new Changes();

        moved.Set(row.Sequence, drive.Sequence, v => row.Sequence = v);
        moved.Set(row.TeamId, side(drive.SourceTeamId), v => row.TeamId = v);
        moved.Set(row.Description, drive.Description, v => row.Description = v);
        moved.Set(row.Result, Fit(drive.Result, columns.DriveResult), v => row.Result = v);
        moved.Set(row.Yards, drive.Yards, v => row.Yards = v);
        moved.Set(row.OffensivePlays, drive.OffensivePlays, v => row.OffensivePlays = v);
        moved.Set(row.TimeElapsed, Fit(drive.TimeElapsed, columns.TimeElapsed), v => row.TimeElapsed = v);
        moved.Set(row.IsScore, drive.IsScore, v => row.IsScore = v);
        moved.Set(row.IsCurrent, drive.IsCurrent, v => row.IsCurrent = v);
        moved.Set(row.StartPeriod, drive.StartPeriod, v => row.StartPeriod = v);
        moved.Set(row.StartClock, Fit(drive.StartClock, columns.StartClock), v => row.StartClock = v);
        moved.Set(row.StartYardLine, drive.StartYardLine, v => row.StartYardLine = v);
        moved.Set(row.EndPeriod, drive.EndPeriod, v => row.EndPeriod = v);
        moved.Set(row.EndClock, Fit(drive.EndClock, columns.EndClock), v => row.EndClock = v);
        moved.Set(row.EndYardLine, drive.EndYardLine, v => row.EndYardLine = v);

        return moved.Any;
    }

    /// <summary>Copies the play onto the row, change-only, and says whether anything moved.</summary>
    private static bool Apply(GamePlay row, CanonicalPlay play, int? driveSeq, Func<string?, int?> side, Columns columns)
    {
        var moved = new Changes();

        moved.Set(row.Sequence, play.Sequence, v => row.Sequence = v);
        moved.Set(row.DriveSeq, driveSeq, v => row.DriveSeq = v);
        moved.Set(row.Period, play.Period, v => row.Period = v);
        moved.Set(row.Clock, Fit(play.Clock, columns.Clock), v => row.Clock = v);
        moved.Set(row.ClockSeconds, play.ClockSeconds, v => row.ClockSeconds = v);
        moved.Set(row.TypeId, Fit(play.TypeId, columns.TypeId), v => row.TypeId = v);
        moved.Set(row.Type, Fit(play.Type, columns.Type), v => row.Type = v);
        moved.Set(row.Text, play.Text, v => row.Text = v);
        moved.Set(row.HomeScore, play.HomeScore, v => row.HomeScore = v);
        moved.Set(row.AwayScore, play.AwayScore, v => row.AwayScore = v);
        moved.Set(row.IsScoring, play.IsScoring, v => row.IsScoring = v);
        moved.Set(row.IsTurnover, play.IsTurnover, v => row.IsTurnover = v);
        moved.Set(row.IsPenalty, play.IsPenalty, v => row.IsPenalty = v);
        moved.Set(row.Yardage, play.Yardage, v => row.Yardage = v);
        moved.Set(row.TeamId, side(play.SourceTeamId), v => row.TeamId = v);
        moved.SetJson(row.StartSituation, Situation(play.Start, side), v => row.StartSituation = v);
        moved.SetJson(row.EndSituation, Situation(play.End, side), v => row.EndSituation = v);
        moved.Set(row.HomeWinPct, Probability(play.HomeWinPct), v => row.HomeWinPct = v);
        moved.Set(row.SpreadCoverPctHome, Probability(play.SpreadCoverPctHome), v => row.SpreadCoverPctHome = v);
        moved.Set(row.TotalOverPct, Probability(play.TotalOverPct), v => row.TotalOverPct = v);
        moved.Set(row.Wallclock, Instant(play.Wallclock), v => row.Wallclock = v);
        moved.Set(row.Modified, Instant(play.Modified), v => row.Modified = v);

        return moved.Any;
    }

    /// <summary>
    /// The situation column's json, or null when the provider sent none. The side with the ball is
    /// written as our team id — the provider's means nothing to a reader of this table.
    /// </summary>
    private static string? Situation(CanonicalPlaySituation? situation, Func<string?, int?> side)
        => situation is null
            ? null
            : JsonSerializer.Serialize(new
            {
                situation.Down,
                situation.Distance,
                situation.YardLine,
                situation.YardsToEndzone,
                situation.DownDistanceText,
                situation.PossessionText,
                TeamId = side(situation.SourceTeamId)
            }, SituationJson);

    /// <summary>
    /// The lengths the context maps the short strings with, read from its model: the one place
    /// they are stated is the context's configuration, and the writer cuts to whatever it says.
    /// </summary>
    private sealed record Columns(
        int PlayId, int Clock, int TypeId, int Type,
        int DriveId, int DriveResult, int TimeElapsed, int StartClock, int EndClock)
    {
        private static readonly ConditionalWeakTable<IModel, Columns> Known = new();

        public static Columns Of(IModel model) => Known.GetValue(model, Read);

        private static Columns Read(IModel model)
        {
            var play = model.FindEntityType(typeof(GamePlay))!;
            var drive = model.FindEntityType(typeof(GameDrive))!;

            return new Columns(
                Length(play, nameof(GamePlay.SourcePlayId)),
                Length(play, nameof(GamePlay.Clock)),
                Length(play, nameof(GamePlay.TypeId)),
                Length(play, nameof(GamePlay.Type)),
                Length(drive, nameof(GameDrive.SourceDriveId)),
                Length(drive, nameof(GameDrive.Result)),
                Length(drive, nameof(GameDrive.TimeElapsed)),
                Length(drive, nameof(GameDrive.StartClock)),
                Length(drive, nameof(GameDrive.EndClock)));
        }

        private static int Length(IEntityType entity, string property)
            => entity.FindProperty(property)?.GetMaxLength() ?? int.MaxValue;
    }

    /// <summary>The value cut to <paramref name="length"/>, never splitting a surrogate pair.</summary>
    private static string? Fit(string? value, int length)
        => value is { Length: var n } && n > length
            ? value[..(char.IsHighSurrogate(value[length - 1]) ? length - 1 : length)]
            : value;

    /// <summary>A probability as the column will hold it, so the next poll's compares equal.</summary>
    private static decimal? Probability(decimal? value)
        => value is { } v ? Math.Round(v, ProbabilityPlaces, MidpointRounding.AwayFromZero) : null;

    /// <summary>An instant as Postgres will hand it back: UTC (Npgsql writes no other offset), to the microsecond.</summary>
    private static DateTimeOffset? Instant(DateTimeOffset? value)
    {
        if (value is not { } v)
            return null;

        var utc = v.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }

    /// <summary>Assigns a value only when it differs, and remembers whether any did.</summary>
    private sealed class Changes
    {
        public bool Any { get; private set; }

        public void Set<T>(T current, T value, Action<T> assign)
        {
            if (EqualityComparer<T>.Default.Equals(current, value))
                return;

            assign(value);
            Any = true;
        }

        public void SetJson(string? current, string? value, Action<string?> assign)
        {
            if (SameJson(current, value))
                return;

            assign(value);
            Any = true;
        }

        /// <summary>Whether two jsonb values say the same thing, whatever their spacing and key order.</summary>
        private static bool SameJson(string? a, string? b)
        {
            if (a is null || b is null)
                return a == b;

            if (a == b)
                return true;

            try
            {
                return JsonNode.DeepEquals(JsonNode.Parse(a), JsonNode.Parse(b));
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
