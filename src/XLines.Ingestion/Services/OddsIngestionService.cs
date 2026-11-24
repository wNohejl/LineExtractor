using System.Diagnostics;
using XLines.Core.Analytics;
using XLines.Core.Contracts;
using XLines.Core.Diagnostics;
using XLines.Core.Entities;
using XLines.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace XLines.Ingestion.Services;

public record IngestionOutcome(long RunId, RunStatus Status, int RowsIngested, string? Error);

/// <summary>
/// Runs one odds source over one sport and persists the result.
///
/// Every execution writes an <see cref="IngestionRun"/> row whether it succeeds or fails —
/// that table is the raw feed the entire reliability layer is computed from, so a run that
/// silently vanishes would blind the KPIs.
/// </summary>
public class OddsIngestionService(
    XLinesDbContext db,
    EntityResolver resolver,
    CreditBudgetGuard budget,
    ILogger<OddsIngestionService> logger)
{
    public async Task<IngestionOutcome> IngestAsync(
        IOddsSource source,
        string sportKey,
        string jobKey,
        CancellationToken ct,
        DateOnly? closingScanDay = null)
    {
        var sourceRow = await RunRecord.SourceRowAsync(db, source.Key, ct);

        // Stamped on the row before the call, not after: a closing scan another host is still
        // running already counts against the day's cap, so two hosts cannot both take the last one.
        var run = new IngestionRun
        {
            SourceId = sourceRow.Id,
            JobKey = jobKey,
            StartedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Running,
            ClosingScanDay = closingScanDay
        };

        db.IngestionRuns.Add(run);
        await db.SaveChangesAsync(ct);

        // The span covers the whole run, so every provider call and every query underneath it
        // becomes a child and a slow run explains itself without extra instrumentation.
        using var activity = XLinesTelemetry.Source.StartActivity("ingest.odds");
        activity?.SetTag(XLinesTelemetry.Tags.Source, source.Key);
        activity?.SetTag(XLinesTelemetry.Tags.Sport, sportKey);
        activity?.SetTag(XLinesTelemetry.Tags.Job, jobKey);

        try
        {
            // Refuse to start rather than blow a free-tier ceiling mid-run.
            var permitted = await budget.TryReserveAsync(sourceRow, estimatedRequests: 2, ct);
            if (!permitted)
            {
                run.Status = RunStatus.Partial;
                run.Error = RunRecord.BudgetRefused;
                run.FinishedAt = DateTimeOffset.UtcNow;

                // Refused before any call, so it bought nothing and does not use up a closing scan.
                run.ClosingScanDay = null;
                await db.SaveChangesAsync(ct);

                logger.LogWarning("{Source}/{Sport}: skipped, budget exhausted", source.Key, sportKey);
                return Complete(activity, run, source.Key, sportKey, jobKey, rows: 0);
            }

            // Failure injection is stored against the source row rather than held in memory,
            // so a drill persists across restarts and is visible to anyone reading the config.
            if (source is IFailureInjectable injectable)
                injectable.FailureMode = sourceRow.FailureMode;

            var result = await source.FetchSlateAsync(sportKey, source.MarketsFor(sportKey), ct);

            run.Charge(result.Cost);

            var rows = await PersistAsync(sourceRow, result, sportKey, run.Id, ct);

            run.RowsIngested = rows;
            run.FinishedAt = DateTimeOffset.UtcNow;

            // Different zeroes, and conflating them would be a real operational bug (ADR 0003):
            //   - games were listed but no prices at all   -> Partial, the signature of a
            //     silent upstream break (HTTP 200 with a restructured payload);
            //   - prices came back but none had moved      -> Success, a quiet market;
            //   - no games were listed                     -> Success, an empty slate. The
            //     feeds ask only for games yet to start, so a league between seasons or with
            //     every game under way answers with an empty list, free of charge. A provider
            //     that had broken would list games it cannot price, or throw; it does not
            //     invent an empty calendar.
            // Only the first should ever page anyone.
            if (result.Odds.Count == 0 && result.Games.Count > 0)
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

            return Complete(activity, run, source.Key, sportKey, jobKey, rows);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            run.Status = RunStatus.Failed;
            run.Error = RunRecord.Describe(ex);
            run.FinishedAt = DateTimeOffset.UtcNow;

            // A call that was answered and billed but could not be used still spent the month.
            // The failed run records it as a successful one would, so the budget guard, the
            // monthly count and pacing see the spend now rather than after the next good call.
            if (ex is OddsFetchFailedException { Cost: var cost })
                run.Charge(cost);

            await db.SaveChangesAsync(ct);

            activity?.AddException(ex);
            logger.LogError(ex, "{Source}/{Sport}: ingestion failed", source.Key, sportKey);
            return Complete(activity, run, source.Key, sportKey, jobKey, rows: 0);
        }
    }

    /// <summary>
    /// The single exit for a run: records what it cost, tags the span with how it ended, and
    /// returns the outcome.
    ///
    /// Every path funnels through here rather than each incrementing its own counters, because a
    /// terminal path that forgets to count is invisible in exactly the case you most want to see
    /// — the counter simply reads lower and nothing indicates why.
    /// </summary>
    private static IngestionOutcome Complete(
        Activity? activity, IngestionRun run, string sourceKey, string sportKey, string jobKey, int rows)
    {
        var tags = new TagList
        {
            { XLinesTelemetry.Tags.Source, sourceKey },
            { XLinesTelemetry.Tags.Sport, sportKey },
            { XLinesTelemetry.Tags.Job, jobKey },
            { XLinesTelemetry.Tags.Status, run.Status.ToString() }
        };

        XLinesTelemetry.Instruments.Runs.Add(1, tags);
        XLinesTelemetry.Instruments.Rows.Add(rows, tags);
        XLinesTelemetry.Instruments.Requests.Add(run.RequestsMade, tags);
        XLinesTelemetry.Instruments.Credits.Add(run.CreditsSpent, tags);

        activity?.SetTag(XLinesTelemetry.Tags.Status, run.Status.ToString());
        activity?.SetTag("xlines.rows", rows);
        activity?.SetTag("xlines.requests", run.RequestsMade);

        // Partial is an error in the operational sense — the provider returned nothing usable —
        // so the span says so rather than looking like a fast, clean run.
        activity?.SetStatus(
            run.Status is RunStatus.Success ? ActivityStatusCode.Ok : ActivityStatusCode.Error,
            run.Error);

        return new IngestionOutcome(run.Id, run.Status, rows, run.Error);
    }

    /// <summary>
    /// A book's side of a fight as the bout's own fighter: the book's home or away by its own name,
    /// then by the fighter's name with accents and suffixes folded. Null for a side that is neither
    /// fighter — a draw offered as a third outcome, which a two-way board does not carry.
    /// </summary>
    internal static Func<string, string?> FighterOutcomes(CanonicalGame canonical, Game bout, bool swapped)
    {
        var home = bout.HomeTeam?.Name;
        var away = bout.AwayTeam?.Name;

        return outcome =>
        {
            if (home is null || away is null)
                return null;

            if (outcome == canonical.HomeTeamName)
                return swapped ? away : home;

            if (outcome == canonical.AwayTeamName)
                return swapped ? home : away;

            var isHome = FighterNames.Same(outcome, home);
            var isAway = FighterNames.Same(outcome, away);

            return isHome == isAway ? null : isHome ? home : away;
        };
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

        // A fight is attached to the bout ESPN holds and is never created from a book, and its
        // sides are renamed to the bout's own fighters so the board can pair them (ADR 0021).
        var fights = FightSports.IsFight(sport.Key);
        var sides = new Dictionary<string, Func<string, string?>>();

        foreach (var canonical in result.Games)
        {
            if (!fights)
            {
                gameMap[canonical.SourceGameId] = await resolver.ResolveGameAsync(sport, sourceRow.Key, canonical, ct);
                continue;
            }

            if (await resolver.FindBoutAsync(sport, sourceRow.Key, canonical, ct) is not { } bout)
                continue;

            gameMap[canonical.SourceGameId] = bout.Game;
            sides[canonical.SourceGameId] = FighterOutcomes(canonical, bout.Game, bout.Swapped);
        }

        if (fights)
        {
            logger.LogInformation("{Source}/{Sport}: {Matched} of {Listed} fights are UFC bouts on the desk",
                sourceRow.Key, sportKey, gameMap.Count, result.Games.Count);
        }

        var snapshots = new List<OddsSnapshot>();

        foreach (var odds in result.Odds)
        {
            if (!gameMap.TryGetValue(odds.SourceGameId, out var game))
                continue;

            var outcome = odds.Outcome;

            if (sides.TryGetValue(odds.SourceGameId, out var rename) && odds.Market != Markets.Total)
            {
                if (rename(outcome) is not { } fighter)
                    continue;

                outcome = fighter;
            }

            snapshots.Add(new OddsSnapshot
            {
                GameId = game.Id,
                SourceId = sourceRow.Id,
                Book = odds.Book,
                Market = odds.Market,
                Outcome = outcome,
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
            .Where(c => snapshots.Select(s => s.GameId).Contains(c.GameId)
                        && c.Source!.Kind == SourceKind.Odds)
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
