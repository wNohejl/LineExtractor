using LineOps.Core.Entities;
using LineOps.Data;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Reliability;

/// <summary>Live health of one source, as rendered on the Ops Center cards.</summary>
public record SourceHealth(
    Source Source,
    DateTimeOffset? LastSuccessAt,
    double? FreshnessMinutes,
    double SuccessRate,
    int RunsInWindow,
    int RowsToday,
    double? VolumeRatio,
    RunStatus? LastStatus,
    string? LastError)
{
    public bool IsStale(TimeSpan slo)
        => FreshnessMinutes is null || FreshnessMinutes > slo.TotalMinutes;

    /// <summary>Never ingested at all — distinct from stale, and not worth alerting on.</summary>
    public bool NeverRun => LastSuccessAt is null && RunsInWindow == 0;
}

/// <summary>
/// Computes operational KPIs from the ingestion_run history.
///
/// Everything here is derived, never stored twice: the run table is the single source of
/// truth, and <see cref="RollupDailyAsync"/> only materialises what would otherwise be an
/// expensive repeat aggregation.
/// </summary>
public class KpiCalculator(LineOpsDbContext db)
{
    /// <summary>Freshness: minutes since this source last produced rows successfully.</summary>
    public async Task<double?> GetFreshnessMinutesAsync(int sourceId, CancellationToken ct = default)
    {
        var lastSuccess = await db.IngestionRuns
            .Where(r => r.SourceId == sourceId && r.Status == RunStatus.Success)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => (DateTimeOffset?)r.StartedAt)
            .FirstOrDefaultAsync(ct);

        return lastSuccess is null
            ? null
            : (DateTimeOffset.UtcNow - lastSuccess.Value).TotalMinutes;
    }

    /// <summary>Successful runs as a fraction of all completed runs in the window.</summary>
    public async Task<(double Rate, int Count)> GetSuccessRateAsync(
        int sourceId, TimeSpan window, CancellationToken ct = default)
    {
        var since = DateTimeOffset.UtcNow - window;

        var runs = await db.IngestionRuns
            .Where(r => r.SourceId == sourceId && r.StartedAt >= since && r.Status != RunStatus.Running)
            .Select(r => r.Status)
            .ToListAsync(ct);

        if (runs.Count == 0)
            return (1.0, 0);

        // Partial counts against the rate: a run that returned nothing is not a success.
        var successes = runs.Count(s => s == RunStatus.Success);
        return (successes / (double)runs.Count, runs.Count);
    }

    /// <summary>
    /// Today's row count over the trailing median. A ratio well below 1 means the feed is
    /// returning far less than usual — the failure mode that returns HTTP 200 and no data.
    /// Null when there is not enough history to judge.
    /// </summary>
    public async Task<double?> GetVolumeRatioAsync(
        int sourceId, int baselineDays, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = DateTimeOffset.UtcNow.AddDays(-baselineDays - 1);

        var runs = await db.IngestionRuns
            .Where(r => r.SourceId == sourceId && r.StartedAt >= since)
            .Select(r => new { r.StartedAt, r.RowsIngested })
            .ToListAsync(ct);

        var byDay = runs
            .GroupBy(r => DateOnly.FromDateTime(r.StartedAt.UtcDateTime))
            .ToDictionary(g => g.Key, g => g.Sum(r => r.RowsIngested));

        var baseline = byDay
            .Where(kv => kv.Key < today)
            .Select(kv => kv.Value)
            .OrderBy(v => v)
            .ToList();

        // Need a few days before a median means anything.
        if (baseline.Count < 3)
            return null;

        // And those days have to be most of the window, not merely three of them. Grouping by
        // day drops the days a source did not run, so a median taken over what is left quietly
        // assumes the source runs daily. That holds for a scheduled feed and breaks for one
        // pulled on request: three scattered days clear the floor above and establish a "usual"
        // that is really a record of how often somebody pressed the button. A source that
        // skipped more days than it ran has no daily volume to fall short of, and saying so is
        // the same judgment as NeverRun in AlertEngine — an absence of cadence is not a fault.
        if (baseline.Count * 2 <= baselineDays)
            return null;

        var median = baseline[baseline.Count / 2];
        if (median == 0)
            return null;

        var todayRows = byDay.GetValueOrDefault(today, 0);
        return todayRows / (double)median;
    }

    public async Task<IReadOnlyList<SourceHealth>> GetAllHealthAsync(
        TimeSpan successWindow, int baselineDays, CancellationToken ct = default)
    {
        var sources = await db.Sources.Where(s => s.Enabled).OrderBy(s => s.Key).ToListAsync(ct);
        var health = new List<SourceHealth>(sources.Count);

        foreach (var source in sources)
            health.Add(await GetHealthAsync(source, successWindow, baselineDays, ct));

        return health;
    }

    public async Task<SourceHealth> GetHealthAsync(
        Source source, TimeSpan successWindow, int baselineDays, CancellationToken ct = default)
    {
        var freshness = await GetFreshnessMinutesAsync(source.Id, ct);
        var (rate, count) = await GetSuccessRateAsync(source.Id, successWindow, ct);
        var volumeRatio = await GetVolumeRatioAsync(source.Id, baselineDays, ct);

        var lastSuccess = await db.IngestionRuns
            .Where(r => r.SourceId == source.Id && r.Status == RunStatus.Success)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => (DateTimeOffset?)r.StartedAt)
            .FirstOrDefaultAsync(ct);

        var lastRun = await db.IngestionRuns
            .Where(r => r.SourceId == source.Id)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new { r.Status, r.Error })
            .FirstOrDefaultAsync(ct);

        // Must carry an explicit zero offset: DateTimeOffset.UtcNow.Date drops to a
        // DateTime, which Npgsql then binds using the machine's local offset and rejects
        // against a `timestamptz` column.
        var dayStart = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var rowsToday = await db.IngestionRuns
            .Where(r => r.SourceId == source.Id && r.StartedAt >= dayStart)
            .SumAsync(r => (int?)r.RowsIngested, ct) ?? 0;

        return new SourceHealth(
            source, lastSuccess, freshness, rate, count, rowsToday, volumeRatio,
            lastRun?.Status, lastRun?.Error);
    }

    /// <summary>
    /// Materialises yesterday-and-today KPI rows. Idempotent: recomputes rather than appends,
    /// so it is safe to run on every evaluation tick.
    /// </summary>
    public async Task RollupDailyAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var days = new[] { today.AddDays(-1), today };
        var sources = await db.Sources.ToListAsync(ct);

        foreach (var day in days)
        {
            var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var end = start.AddDays(1);

            foreach (var source in sources)
            {
                var runs = await db.IngestionRuns
                    .Where(r => r.SourceId == source.Id && r.StartedAt >= start && r.StartedAt < end)
                    .Select(r => new { r.Status, r.RowsIngested, r.RequestsMade, r.CreditsSpent })
                    .ToListAsync(ct);

                if (runs.Count == 0)
                    continue;

                var completed = runs.Count(r => r.Status != RunStatus.Running);
                var successes = runs.Count(r => r.Status == RunStatus.Success);

                var existing = await db.KpiDailies
                    .FirstOrDefaultAsync(k => k.Day == day && k.SourceId == source.Id, ct);

                var isNew = existing is null;
                existing ??= new KpiDaily { Day = day, SourceId = source.Id };

                existing.RunCount = runs.Count;
                existing.RowsIngested = runs.Sum(r => r.RowsIngested);
                existing.RequestsMade = runs.Sum(r => r.RequestsMade);
                existing.ApiCreditsUsed = runs.Sum(r => r.CreditsSpent);
                existing.SuccessRate = completed == 0 ? 1.0 : successes / (double)completed;
                existing.FreshnessMinutes = await GetFreshnessMinutesAsync(source.Id, ct) ?? 0;

                if (isNew)
                    db.KpiDailies.Add(existing);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
