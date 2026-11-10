using System.Text.Json;
using LineOps.Core.Analytics;
using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Adapters;
using LineOps.Ingestion.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineOps.Ingestion.Services;

/// <summary>A finished game owed its plays, and what the source calls it.</summary>
public record GamecastBackportCandidate(int GameId, string Sport, string EventId, DateTimeOffset StartsAt);

/// <summary>
/// What one backport run did. <see cref="RunId"/> is nought when the run asked for nothing — no
/// game owed, a game in play, or no source — and so wrote no run row.
/// </summary>
public record GamecastBackportOutcome(
    long RunId, RunStatus Status, int Games, int Misses, int Rows, int Requests, int StillOwed, string? Detail);

/// <summary>
/// Fills the play store for games that finished before the gamecast feed existed
/// (<see cref="IngestionJobs.EspnGamecastBackport"/>).
///
/// <para>
/// The feed writes a game's plays while it is being played, so every final from before it — a
/// season and more of football — has no plays, no drives, no probabilities, and most have no
/// linescore either: the scoreboard pass that writes one was not reading it yet. This asks ESPN
/// for those games one at a time, the summary and the core API's probabilities, and writes them
/// through <see cref="GamecastWriter"/> exactly as the feed's last write would have.
/// </para>
///
/// <para>
/// <b>Which games.</b> Finals in the gamecast leagues (<see cref="GamecastOptions.EffectiveSports"/>)
/// from each league's backfill season start (<c>Backfill:Seasons</c>) — the history this desk
/// holds on purpose; a league with no season start is not backported — with an event id from the
/// source and no plays from it. Newest first, as the history backfill walks, so a run stopped
/// early leaves the games an operator is most likely to open. A batch a run
/// (<see cref="GamecastOptions.BackportBatch"/>).
/// </para>
///
/// <para>
/// <b>What it writes.</b> The plays and drives, with no drive left current — a final has no drive
/// in progress, and a summary can still say otherwise. The game's linescore and status line from
/// the summary's header, where the game has none (a status line that is not a final's counts as
/// none): the scoreboard's word stands where it has spoken. Never the score or the status, which
/// are the scoreboard pass's.
/// </para>
///
/// <para>
/// <b>What it cannot fill.</b> A game whose summary fails, or holds no plays — early history often
/// has none — is recorded (<see cref="GamecastBackportMiss"/>) and skipped until its retry: the
/// configured <see cref="GamecastOptions.BackportRetryAfter"/> for no plays, a day doubling to the
/// same ceiling for a failed request, or until <see cref="ForgetMissesAsync"/> clears the record.
/// Without that the same empty documents would be asked for on every run, and the job would never
/// stop being due. <see cref="FailuresBeforeStopping"/> failures in a row end the run, so an outage
/// sets three games aside rather than a batch.
/// </para>
///
/// <para>
/// <b>When.</b> Never while a game is in play in any configured league (<see cref="IngestionJobs.InPlay"/>):
/// ESPN's patience is the live passes' first. One port for the whole run, so the adapter's
/// <c>RequestDelay</c>, measured from its own last request, holds between games — the feed's
/// lesson — and a scope per game for the writes, so one game's change tracker never carries into
/// the next. Singleton for the reason <see cref="HistoryBackfillService"/> is.
/// </para>
/// </summary>
public class GamecastBackport(
    IServiceScopeFactory scopeFactory,
    IOptions<IngestionOptions> options,
    ILogger<GamecastBackport> logger)
{
    /// <summary>
    /// Failed requests in a row before a run stops. ESPN refusing three games running is ESPN
    /// refusing, and asking the rest of the batch would only set every one of them aside.
    /// </summary>
    public const int FailuresBeforeStopping = 3;

    /// <summary>The first wait after a failed request, doubling with each failure up to the configured retry.</summary>
    private static readonly TimeSpan FailureRetry = TimeSpan.FromDays(1);

    private readonly IngestionOptions _options = options.Value;

    private GamecastOptions Gamecast => _options.Gamecast;

    /// <summary>The leagues whose games in play hold the backport off: every configured one, and the gamecast's.</summary>
    private string[] LiveSports => _options.EffectiveSports
        .Concat(Gamecast.EffectiveSports)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>
    /// The games owed their plays, newest first, at most <paramref name="limit"/>. Read through the
    /// registered gamecast source; none registered, none owed.
    /// </summary>
    public async Task<IReadOnlyList<GamecastBackportCandidate>> EligibleAsync(
        DateTimeOffset now, int limit, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        if (scope.ServiceProvider.GetService<IGamecastSource>() is not { } source)
            return [];

        return await EligibleAsync(scope.ServiceProvider.GetRequiredService<LineOpsDbContext>(), source.Key, now, limit, ct);
    }

    /// <summary>How many games are owed their plays, for the Pull menu's estimate and a run's summary.</summary>
    public async Task<int> OwedCountAsync(DateTimeOffset now, CancellationToken ct)
        => (await EligibleAsync(now, int.MaxValue, ct)).Count;

    private async Task<IReadOnlyList<GamecastBackportCandidate>> EligibleAsync(
        LineOpsDbContext db, string sourceKey, DateTimeOffset now, int limit, CancellationToken ct)
    {
        if (limit <= 0)
            return [];

        var sourceId = await db.Sources.Where(s => s.Key == sourceKey).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);

        if (sourceId is not { } id)
            return [];

        var candidates = new List<GamecastBackportCandidate>();

        foreach (var sport in Gamecast.EffectiveSports)
        {
            // A league with no season start has no history this desk keeps on purpose (CLAUDE.md, Scope).
            if (!_options.Backfill.Seasons.TryGetValue(sport, out var seasonStart))
                continue;

            var from = LeagueClock.StartOf(seasonStart);

            var games = await db.Games.AsNoTracking()
                .Where(g => g.Sport!.Key == sport
                            && g.Status == GameStatus.Final
                            && g.StartsAt >= from
                            && !db.GamePlays.Any(p => p.GameId == g.Id && p.SourceId == id)
                            && !db.GamecastBackportMisses.Any(m => m.GameId == g.Id && m.RetryAt > now))
                .Select(g => new { g.Id, g.StartsAt, g.ExternalIds })
                .ToListAsync(ct);

            // The event id is in a jsonb map; the season's finals are a few hundred rows, read whole.
            candidates.AddRange(games
                .Where(g => g.ExternalIds.ContainsKey(sourceKey))
                .Select(g => new GamecastBackportCandidate(g.Id, sport, g.ExternalIds[sourceKey], g.StartsAt)));
        }

        return candidates
            .OrderByDescending(c => c.StartsAt)
            .ThenByDescending(c => c.GameId)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Due while a game is owed its plays and no game is in play. The scheduler asks this on its own
    /// slow cadence (<see cref="GamecastOptions.BackportInterval"/>).
    /// </summary>
    public async Task<bool> IsDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (Gamecast.BackportBatch <= 0)
            return false;

        await using var scope = scopeFactory.CreateAsyncScope();

        if (scope.ServiceProvider.GetService<IGamecastSource>() is not { } source)
            return false;

        var db = scope.ServiceProvider.GetRequiredService<LineOpsDbContext>();

        if (await IngestionJobs.InPlay(db, now, LiveSports).AnyAsync(ct))
            return false;

        return (await EligibleAsync(db, source.Key, now, 1, ct)).Count > 0;
    }

    /// <summary>
    /// One batch: up to <see cref="GamecastOptions.BackportBatch"/> games read and written, recorded
    /// as one run under <see cref="IngestionJobs.EspnGamecastBackport"/>. Nothing is asked, and no
    /// run written, while a game is in play or when nothing is owed.
    /// </summary>
    public async Task<GamecastBackportOutcome> RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        static GamecastBackportOutcome Nothing(int owed, string detail)
            => new(0, RunStatus.Success, 0, 0, 0, 0, owed, detail);

        if (Gamecast.BackportBatch <= 0)
            return Nothing(0, "The backport is switched off (Ingestion:Gamecast:BackportBatch).");

        // The run's scope holds the one port every request goes through, so its pacing spans the games.
        await using var scope = scopeFactory.CreateAsyncScope();

        if (scope.ServiceProvider.GetService<IGamecastSource>() is not { } source)
            return Nothing(0, "No gamecast source is registered (Ingestion:Espn:Enabled).");

        var db = scope.ServiceProvider.GetRequiredService<LineOpsDbContext>();

        if (await IngestionJobs.InPlay(db, now, LiveSports).AnyAsync(ct))
            return Nothing(0, "A game is in play; the backport waits for it to finish.");

        var picked = await EligibleAsync(db, source.Key, now, Gamecast.BackportBatch, ct);

        if (picked.Count == 0)
            return Nothing(0, "No finished game is owed its plays.");

        var sourceRow = await db.Sources.FirstAsync(s => s.Key == source.Key, ct);

        var run = new IngestionRun
        {
            SourceId = sourceRow.Id,
            JobKey = IngestionJobs.EspnGamecastBackport,
            StartedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Running
        };

        db.IngestionRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var games = 0;
        var misses = 0;
        var failures = 0;
        var rows = 0;
        var requests = 0;
        string? lastError = null;
        var inARow = 0;

        foreach (var game in picked)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                requests++;
                var gamecast = await source.FetchGamecastAsync(game.Sport, game.EventId, ct);
                inARow = 0;

                if (gamecast is null || gamecast.Plays.Count == 0)
                {
                    misses++;
                    await MissAsync(game, sourceRow.Id, now, failed: false,
                        gamecast is null ? "The source cannot address this league." : "No plays in the summary.", ct);
                    continue;
                }

                requests++;
                var probabilities = await ProbabilitiesAsync(source, game, ct);

                rows += await WriteAsync(game, source.Key, gamecast, probabilities, ct);
                games++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                misses++;
                failures++;
                lastError = $"{ex.GetType().Name}: {ex.Message}";
                logger.LogWarning(ex, "Gamecast backport {GameId} ({EventId}): summary unavailable; asked again later",
                    game.GameId, game.EventId);

                await MissAsync(game, sourceRow.Id, now, failed: true, lastError, ct);

                if (++inARow >= FailuresBeforeStopping)
                {
                    logger.LogWarning("Gamecast backport: {Failures} requests failed in a row; stopping this run", inARow);
                    break;
                }
            }
        }

        var stillOwed = (await EligibleAsync(db, source.Key, now, int.MaxValue, ct)).Count;

        var detail = $"{games} of {picked.Count} games backported, {rows:N0} rows in {requests} requests"
                     + (misses > 0 ? $", {misses} without plays" : "")
                     + (stillOwed > 0 ? $"; {stillOwed:N0} still owed." : "; none left owed.");

        // A game with no play-by-play is history ESPN never kept, not a failure; a request that threw is.
        run.RequestsMade = requests;
        run.RowsIngested = rows;
        run.FinishedAt = DateTimeOffset.UtcNow;
        run.Status = failures == 0 ? (misses == 0 ? RunStatus.Success : RunStatus.Partial)
            : games > 0 ? RunStatus.Partial : RunStatus.Failed;
        run.Error = misses == 0 ? null
            : $"{misses} of {picked.Count} games without plays ({failures} failed)" + (lastError is null ? "." : $"; last: {lastError}");
        run.Detail = detail;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Gamecast backport: {Detail}", detail);

        return new GamecastBackportOutcome(run.Id, run.Status, games, misses, rows, requests, stillOwed, detail);
    }

    /// <summary>
    /// The core API's rows for the game. A failure costs the game its spread and total
    /// probabilities, not its plays: the summary's win probability still stands.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, CanonicalPlayProbability>> ProbabilitiesAsync(
        IGamecastSource source, GamecastBackportCandidate game, CancellationToken ct)
    {
        try
        {
            return await source.FetchProbabilitiesAsync(game.Sport, game.EventId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Gamecast backport {EventId}: probabilities unavailable; writing the plays without them",
                game.EventId);
            return new Dictionary<string, CanonicalPlayProbability>();
        }
    }

    /// <summary>
    /// One game written, in its own scope: the plays and drives with none current, then the game's
    /// linescore and status line where it has none. Returns the play and drive rows written.
    /// </summary>
    private async Task<int> WriteAsync(
        GamecastBackportCandidate game, string sourceKey, CanonicalGamecast gamecast,
        IReadOnlyDictionary<string, CanonicalPlayProbability> probabilities, CancellationToken ct)
    {
        var merged = EspnStatsAdapter.MergeProbabilities(gamecast, probabilities);
        merged = merged with { Drives = merged.Drives.Select(d => d.IsCurrent ? d with { IsCurrent = false } : d).ToList() };

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LineOpsDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<GamecastWriter>();

        var written = await writer.WriteAsync(game.GameId, sourceKey, merged, ct);

        // A drive another writer left current, beyond the ones this payload lists.
        foreach (var drive in await db.GameDrives.Where(d => d.GameId == game.GameId && d.IsCurrent).ToListAsync(ct))
        {
            drive.IsCurrent = false;
            drive.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var row = await db.Games.FirstAsync(g => g.Id == game.GameId, ct);

        if (row.Linescore is null && Linescore(gamecast) is { } linescore)
            row.Linescore = linescore;

        if (gamecast.State == "final"
            && gamecast.StatusDetail is { } detail
            && (string.IsNullOrWhiteSpace(row.StatusDetail) || !row.StatusDetail.StartsWith("Final", StringComparison.OrdinalIgnoreCase)))
            row.StatusDetail = detail;

        // Recovered: a miss from an earlier run is history now.
        if (await db.GamecastBackportMisses.FirstOrDefaultAsync(m => m.GameId == game.GameId, ct) is { } miss)
            db.GamecastBackportMisses.Remove(miss);

        // Through SaveChanges only when something moved, so an unchanged game raises no notice.
        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);

        return written.PlaysInserted + written.PlaysUpdated + written.DrivesInserted + written.DrivesUpdated;
    }

    /// <summary>The linescore column's json, <see cref="EntityResolver"/>'s shape, or null when the header had none.</summary>
    private static string? Linescore(CanonicalGamecast gamecast)
        => gamecast.HomeLinescore is null && gamecast.AwayLinescore is null
            ? null
            : JsonSerializer.Serialize(new { home = gamecast.HomeLinescore ?? [], away = gamecast.AwayLinescore ?? [] });

    /// <summary>
    /// Records a game the run could not fill, so later runs pass it by until its retry: the
    /// configured wait for a summary with no plays, and for a request that failed a day, doubling
    /// with each attempt up to the same ceiling.
    /// </summary>
    private async Task MissAsync(
        GamecastBackportCandidate game, int sourceId, DateTimeOffset now, bool failed, string reason, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LineOpsDbContext>();

        var miss = await db.GamecastBackportMisses.FirstOrDefaultAsync(m => m.GameId == game.GameId, ct);

        if (miss is null)
        {
            miss = new GamecastBackportMiss { GameId = game.GameId };
            db.GamecastBackportMisses.Add(miss);
        }

        miss.SourceId = sourceId;
        miss.Attempts++;
        miss.LastTriedAt = now;
        miss.Reason = reason.Length > 512 ? reason[..512] : reason;

        var ceiling = Gamecast.BackportRetryAfter > TimeSpan.Zero ? Gamecast.BackportRetryAfter : new GamecastOptions().BackportRetryAfter;
        miss.RetryAt = now + (failed
            ? TimeSpan.FromTicks(Math.Min(FailureRetry.Ticks * (1L << Math.Min(miss.Attempts - 1, 10)), ceiling.Ticks))
            : ceiling);

        await db.SaveChangesAsync(ct);

        logger.LogDebug("Gamecast backport {GameId} ({EventId}): {Reason} (attempt {Attempts})",
            game.GameId, game.EventId, reason, miss.Attempts);
    }

    /// <summary>
    /// Forgets every game the backport could not fill, so the next run asks for them again —
    /// for after ESPN has filled in old play-by-play, or a fix to the parse. Bookkeeping, not data.
    /// </summary>
    /// <returns>How many records were cleared.</returns>
    public async Task<int> ForgetMissesAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LineOpsDbContext>();

        var count = await db.GamecastBackportMisses.ExecuteDeleteAsync(ct);

        logger.LogWarning("Gamecast backport: {Count} missed games forgotten; they will be asked for again", count);

        return count;
    }
}
