using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LineOps.Ingestion.Services;

public record IngestionOutcome(long RunId, RunStatus Status, int RowsIngested, string? Error);

/// <summary>
/// Runs one odds source over one sport and persists the result.
///
/// Every execution writes an <see cref="IngestionRun"/> row whether it succeeds or fails —
/// that table is the raw feed the entire reliability layer is computed from, so a run that
/// silently vanishes would blind the KPIs.
/// </summary>
public class OddsIngestionService(
    LineOpsDbContext db,
    EntityResolver resolver,
    CreditBudgetGuard budget,
    ILogger<OddsIngestionService> logger)
{
    public async Task<IngestionOutcome> IngestAsync(
        IOddsSource source,
        string sportKey,
        string jobKey,
        CancellationToken ct)
    {
        var sourceRow = await db.Sources.FirstOrDefaultAsync(s => s.Key == source.Key, ct)
            ?? throw new InvalidOperationException($"Source '{source.Key}' is not registered.");

        var run = new IngestionRun
        {
            SourceId = sourceRow.Id,
            JobKey = jobKey,
            StartedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Running
        };

        db.IngestionRuns.Add(run);
        await db.SaveChangesAsync(ct);

        try
        {
            // Refuse to start rather than blow a free-tier ceiling mid-run.
            var permitted = await budget.TryReserveAsync(sourceRow, estimatedRequests: 2, ct);
            if (!permitted)
            {
                run.Status = RunStatus.Partial;
                run.Error = "Skipped: source is at its configured rate/credit budget.";
                run.FinishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);

                logger.LogWarning("{Source}/{Sport}: skipped, budget exhausted", source.Key, sportKey);
                return new IngestionOutcome(run.Id, run.Status, 0, run.Error);
            }

            // Failure injection is stored against the source row rather than held in memory,
            // so a drill persists across restarts and is visible to anyone reading the config.
            if (source is IFailureInjectable injectable)
                injectable.FailureMode = sourceRow.FailureMode;

            var result = await source.FetchSlateAsync(sportKey, source.SupportedMarkets, ct);

            run.RequestsMade = result.Cost.Requests;
            run.CreditsSpent = result.Cost.Credits;

            var rows = await PersistAsync(sourceRow, result, sportKey, run.Id, ct);

            run.RowsIngested = rows;
            run.FinishedAt = DateTimeOffset.UtcNow;

            // Two different zeroes, and conflating them would be a real operational bug:
            //   - the provider returned no prices at all  -> Partial, the signature of a
            //     silent upstream break (HTTP 200 with an empty or changed payload);
            //   - prices came back but none had moved     -> Success, a quiet market.
            // Only the first should ever page anyone.
            if (result.Odds.Count == 0)
            {
                run.Status = RunStatus.Partial;
                run.Error = "Provider returned no prices — possible upstream schema drift.";
            }
            else
            {
                run.Status = RunStatus.Success;
            }

            await db.SaveChangesAsync(ct);

            logger.LogInformation("{Source}/{Sport}: {Rows} rows in {Ms}ms",
                source.Key, sportKey, rows, (run.FinishedAt - run.StartedAt)?.TotalMilliseconds);

            return new IngestionOutcome(run.Id, run.Status, rows, run.Error);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            run.Status = RunStatus.Failed;
            run.Error = $"{ex.GetType().Name}: {ex.Message}";
            run.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogError(ex, "{Source}/{Sport}: ingestion failed", source.Key, sportKey);
            return new IngestionOutcome(run.Id, run.Status, 0, run.Error);
        }
    }

    private async Task<int> PersistAsync(
        Source sourceRow,
        OddsFetchResult result,
        string sportKey,
        long runId,
        CancellationToken ct)
    {
        if (result.Odds.Count == 0)
            return 0;

        var sport = await resolver.ResolveSportAsync(sportKey, ct);

        // Resolve every referenced game once, then index by the provider's id.
        var gameMap = new Dictionary<string, Game>();
        foreach (var canonical in result.Games)
        {
            var game = await resolver.ResolveGameAsync(sport, sourceRow.Key, canonical, ct);
            gameMap[canonical.SourceGameId] = game;
        }

        var snapshots = new List<OddsSnapshot>();

        foreach (var odds in result.Odds)
        {
            if (!gameMap.TryGetValue(odds.SourceGameId, out var game))
                continue;

            snapshots.Add(new OddsSnapshot
            {
                GameId = game.Id,
                SourceId = sourceRow.Id,
                Book = odds.Book,
                Market = odds.Market,
                Outcome = odds.Outcome,
                Line = odds.Line,
                PriceAmerican = odds.PriceAmerican,
                CapturedAt = odds.CapturedAt,
                IngestionRunId = runId
            });
        }

        if (snapshots.Count == 0)
            return 0;

        // Games whose close is already on record are done with the scan tier. Their market is
        // now in-play, which this product does not price against, and anything written here
        // would be deleted by the next retention pass anyway — a write, a WAL record and a
        // delete to store nothing. Cheaper to not write it.
        var closed = await db.ClosingLines
            .Where(c => snapshots.Select(s => s.GameId).Contains(c.GameId))
            .Select(c => c.GameId)
            .Distinct()
            .ToListAsync(ct);

        if (closed.Count > 0)
        {
            snapshots.RemoveAll(s => closed.Contains(s.GameId));

            if (snapshots.Count == 0)
            {
                logger.LogInformation(
                    "{Source}/{Sport}: every game in the payload has already closed", sourceRow.Key, sportKey);
                return 0;
            }
        }

        // Store on change, not on poll.
        //
        // A price that has not moved since the last observation carries no information, so
        // re-running an ingestion window is a no-op rather than a pile of duplicate rows.
        // Besides making re-runs idempotent, this is what keeps the table honest: every row
        // in it represents an actual line move, so the movement chart is signal, not sampling
        // noise, and storage tracks market activity instead of poll frequency.
        var gameIds = snapshots.Select(s => s.GameId).Distinct().ToList();

        var latestPerKey = await db.OddsSnapshots
            .Where(s => s.SourceId == sourceRow.Id && gameIds.Contains(s.GameId))
            .GroupBy(s => new { s.GameId, s.Book, s.Market, s.Outcome })
            .Select(g => g.OrderByDescending(s => s.CapturedAt).First())
            .ToListAsync(ct);

        var lastSeen = latestPerKey.ToDictionary(
            s => (s.GameId, s.Book, s.Market, s.Outcome),
            s => (s.Line, s.PriceAmerican));

        var fresh = new List<OddsSnapshot>();

        foreach (var snapshot in snapshots)
        {
            var key = (snapshot.GameId, snapshot.Book, snapshot.Market, snapshot.Outcome);

            if (lastSeen.TryGetValue(key, out var previous)
                && previous.Line == snapshot.Line
                && previous.PriceAmerican == snapshot.PriceAmerican)
                continue;

            fresh.Add(snapshot);

            // Track within the batch too, so one payload cannot contain its own duplicate.
            lastSeen[key] = (snapshot.Line, snapshot.PriceAmerican);
        }

        if (fresh.Count == 0)
        {
            logger.LogInformation("{Source}/{Sport}: no line movement since last poll", sourceRow.Key, sportKey);
            return 0;
        }

        db.OddsSnapshots.AddRange(fresh);
        await db.SaveChangesAsync(ct);

        return fresh.Count;
    }
}
