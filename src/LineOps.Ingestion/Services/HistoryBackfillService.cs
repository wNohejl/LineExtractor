using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineOps.Ingestion.Services;

/// <summary>
/// Walks past days to build history the scheduler alone would need months of real time to
/// accumulate.
///
/// <para>
/// The scheduler is built for the present: a daily slate, an intraday movement poll, and
/// yesterday's box scores. That is correct for keeping current and useless for answering
/// "how did this team do over the last season", because it can only ever learn one day per
/// day. This walks backwards instead, one calendar day at a time, and stores what it finds
/// through the same <see cref="StatsIngestionService"/> the scheduler uses — so a backfilled
/// day is indistinguishable from a day that arrived live, and the upsert semantics that make
/// re-running a day safe apply here too.
/// </para>
///
/// <para><b>It cannot spend credits.</b> Not "is configured not to" — cannot. Eligibility is
/// decided from the <see cref="Source"/> row, and a source is eligible only when it declares
/// no rate limit and no credit budget, which is this schema's definition of unmetered. A
/// metered provider named in configuration is refused and logged. The reasoning is that
/// backfill is the one job whose whole shape is "many requests at once", so it is the job
/// where a misconfiguration would be most expensive, and the cheapest place to make that
/// impossible is here rather than in a config file someone edits later.</para>
///
/// <para><b>Every day gets its own scope.</b> This is not tidiness — an earlier version held
/// one <see cref="LineOpsDbContext"/> for the whole walk, and its change tracker grew with
/// every game, player and stat line it had ever seen. Each day was slower than the one
/// before, and after a couple of hundred rows the walk had ground to a crawl that looked
/// exactly like a hang. A scope per day keeps the tracked set bounded to the day being
/// written.</para>
/// </summary>
public class HistoryBackfillService(
    IServiceScopeFactory scopeFactory,
    IOptions<IngestionOptions> options,
    ILogger<HistoryBackfillService> logger)
{
    public const string JobKey = "stats:backfill";

    private readonly IngestionOptions _options = options.Value;

    /// <summary>
    /// A source may be walked only if it is unmetered. Null limits mean "no ceiling declared",
    /// which for this schema is what free means — see <see cref="Source"/>.
    /// </summary>
    public static bool IsUnmetered(Source source)
        => source.RateLimitPerHour is null
           && source.RateLimitPerDay is null
           && source.MonthlyCreditBudget is null;

    /// <summary>
    /// The sources this backfill will actually use, with a reason for every one it will not.
    /// Surfaced in the UI so "why is nothing happening?" is answerable without reading logs.
    /// </summary>
    public async Task<IReadOnlyList<SourceEligibility>> GetEligibilityAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await GetEligibilityAsync(scope.ServiceProvider, ct);
    }

    private async Task<IReadOnlyList<SourceEligibility>> GetEligibilityAsync(
        IServiceProvider provider, CancellationToken ct)
    {
        var db = provider.GetRequiredService<LineOpsDbContext>();
        var registry = provider.GetRequiredService<SourceRegistry>();

        var rows = await db.Sources.Where(s => s.Kind == SourceKind.Stats)
            .AsNoTracking().ToListAsync(ct);

        var result = new List<SourceEligibility>();

        foreach (var key in _options.Backfill.EffectiveSources)
        {
            var row = rows.FirstOrDefault(s => s.Key == key);

            if (row is null)
                result.Add(new SourceEligibility(key, false, "Not a registered stats source."));
            else if (!row.Enabled)
                result.Add(new SourceEligibility(key, false, "Disabled in the source registry."));
            else if (!IsUnmetered(row))
                result.Add(new SourceEligibility(key, false,
                    "Metered provider — backfill would spend its quota, so it is refused."));
            else if (registry.FindStats(key) is null)
                result.Add(new SourceEligibility(key, false,
                    "No adapter registered. Enable it in the Ingestion section."));
            else
                result.Add(new SourceEligibility(key, true, "Unmetered and available."));
        }

        return result;
    }

    /// <summary>Days already walked, and how far back coverage currently reaches.</summary>
    public async Task<BackfillCoverage> GetCoverageAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LineOpsDbContext>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var earliest = today.AddDays(-EffectiveDays());
        var sports = EffectiveSports();

        // Scoped to the sports currently being walked. Checkpoints from a sport that has since
        // been dropped from the configuration are real history, but counting them here would
        // report coverage against a target that no longer includes them — and read as more than
        // 100% complete, which is how "447 days held of 292 wanted" happened.
        var sportIds = await db.Sports
            .Where(s => sports.Contains(s.Key))
            .Select(s => s.Id)
            .ToListAsync(ct);

        var done = await db.BackfillCheckpoints
            .Where(c => c.Date >= earliest && sportIds.Contains(c.SportId))
            .AsNoTracking()
            .ToListAsync(ct);

        var eligible = await GetEligibilityAsync(scope.ServiceProvider, ct);
        var target = EffectiveDays() * EffectiveSports().Count * eligible.Count(e => e.Eligible);

        return new BackfillCoverage(
            DaysRequested: EffectiveDays(),
            TargetDayFetches: target,
            Completed: done.Count(c => c.Error is null),
            Failed: done.Count(c => c.Error is not null),
            GamesFound: done.Sum(c => c.GamesFound),
            RowsIngested: done.Sum(c => c.RowsIngested),
            RequestsMade: done.Sum(c => c.RequestsMade),
            EarliestCovered: done.Count == 0 ? null : done.Min(c => c.Date),
            LatestCovered: done.Count == 0 ? null : done.Max(c => c.Date));
    }

    private List<string> EffectiveSports()
        => (_options.Backfill.Sports.Length > 0 ? _options.Backfill.Sports : _options.EffectiveSports)
            .Distinct()
            .ToList();

    /// <summary>
    /// How many days back the walk should reach, from either the fixed <c>Since</c> date or the
    /// rolling day count. Never negative — a <c>Since</c> in the future means "nothing to do"
    /// rather than a walk that runs backwards.
    /// </summary>
    private int EffectiveDays()
    {
        if (_options.Backfill.Since is not { } since)
            return _options.Backfill.Days;

        var span = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - since.DayNumber;
        return Math.Max(0, span);
    }

    /// <summary>
    /// Walks the configured window, newest day first.
    ///
    /// Newest-first is deliberate. A backfill is long and can be stopped at any point, so the
    /// order decides what you are left holding when it is: recent history is what the
    /// analytics actually read, and finishing "last month" beats getting a third of the way
    /// through a year that starts eleven months ago.
    /// </summary>
    public async Task<BackfillReport> RunAsync(
        IProgress<BackfillProgress>? progress,
        CancellationToken ct)
    {
        var settings = _options.Backfill;
        var eligibility = await GetEligibilityAsync(ct);
        var usable = eligibility.Where(e => e.Eligible).ToList();

        foreach (var refused in eligibility.Where(e => !e.Eligible))
            logger.LogWarning("Backfill skipping '{Source}': {Reason}", refused.Key, refused.Reason);

        if (usable.Count == 0)
        {
            logger.LogWarning("Backfill has no eligible sources; nothing to do");
            return new BackfillReport(0, 0, 0, 0, 0, "No eligible unmetered source.");
        }

        // A backfill reaches into months the live schedule has never touched. Cheap and
        // idempotent, and it keeps a month boundary from turning into a failed insert.
        await using (var scope = scopeFactory.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>()
                .EnsurePartitionsAsync(ct);

        var sports = EffectiveSports();
        var days = EffectiveDays();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (days == 0)
        {
            logger.LogInformation("Backfill target is not in the past; nothing to walk");
            return new BackfillReport(0, 0, 0, 0, 0, "Nothing older than the configured target.");
        }

        var walked = 0;
        var skipped = 0;
        var failed = 0;
        var games = 0;
        var rows = 0;
        string? abortReason = null;

        foreach (var eligible in usable)
        {
            var consecutiveFailures = 0;

            for (var back = 1; back <= days && abortReason is null; back++)
            {
                var date = today.AddDays(-back);

                foreach (var sportKey in sports)
                {
                    if (ct.IsCancellationRequested)
                        return Report("Stopped.");

                    // One scope per day: the change tracker never outlives the day it wrote.
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var sp = scope.ServiceProvider;
                    var db = sp.GetRequiredService<LineOpsDbContext>();

                    var sport = await db.Sports.FirstOrDefaultAsync(s => s.Key == sportKey, ct);
                    if (sport is null)
                        continue;

                    var sourceRow = await db.Sources.FirstAsync(s => s.Key == eligible.Key, ct);

                    var existing = await db.BackfillCheckpoints.FirstOrDefaultAsync(
                        c => c.SourceId == sourceRow.Id && c.SportId == sport.Id && c.Date == date, ct);

                    // Already walked. A failed day counts as walked unless a retry was asked
                    // for, so an endpoint that is permanently 404 for one sport does not stall
                    // the whole window every time the backfill runs.
                    if (existing is not null && (existing.Error is null || !settings.RetryFailedDays))
                    {
                        skipped++;
                        continue;
                    }

                    var source = sp.GetRequiredService<SourceRegistry>().FindStats(eligible.Key)!;
                    var stats = sp.GetRequiredService<StatsIngestionService>();

                    var outcome = await WalkDayAsync(
                        db, stats, source, sourceRow, sport, date, existing, ct);

                    walked++;
                    games += outcome.Games;
                    rows += outcome.Rows;

                    if (outcome.Error is not null)
                    {
                        failed++;
                        consecutiveFailures++;

                        if (consecutiveFailures >= settings.AbortAfterConsecutiveFailures)
                        {
                            abortReason =
                                $"Stopped after {consecutiveFailures} consecutive failures against " +
                                $"'{eligible.Key}'. Last error: {outcome.Error}";
                            logger.LogError("Backfill aborted: {Reason}", abortReason);
                            break;
                        }
                    }
                    else
                    {
                        consecutiveFailures = 0;
                    }

                    progress?.Report(new BackfillProgress(
                        eligible.Key, sportKey, date, walked, skipped, failed, games, rows));

                    // Pace only after work that actually hit the network. The adapter spaces
                    // its own calls too — a slate day is one request per finished game — so
                    // this is the gap between days rather than the only gap there is.
                    if (settings.RequestDelay > TimeSpan.Zero)
                        await Task.Delay(settings.RequestDelay, ct);
                }
            }
        }

        return Report(abortReason);

        BackfillReport Report(string? reason) => new(walked, skipped, failed, games, rows, reason);
    }

    private static async Task<DayOutcome> WalkDayAsync(
        LineOpsDbContext db,
        StatsIngestionService stats,
        IStatsSource source,
        Source sourceRow,
        Sport sport,
        DateOnly date,
        BackfillCheckpoint? existing,
        CancellationToken ct)
    {
        IngestionOutcome result;

        try
        {
            // Straight through the normal ingestion path: same run row, same budget guard,
            // same upsert. The reliability layer therefore sees backfill runs as ordinary
            // runs, which is what makes a stalled backfill visible on the Ops dashboard.
            result = await stats.IngestAsync(source, sport.Key, JobKey, date, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new IngestionOutcome(0, RunStatus.Failed, 0, $"{ex.GetType().Name}: {ex.Message}");
        }

        var run = result.RunId == 0
            ? null
            : await db.IngestionRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == result.RunId, ct);

        var error = result.Status == RunStatus.Success ? null : result.Error ?? "Run did not succeed.";

        var dayStart = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);

        var checkpoint = existing ?? new BackfillCheckpoint
        {
            SourceId = sourceRow.Id,
            SportId = sport.Id,
            Date = date
        };

        checkpoint.CompletedAt = DateTimeOffset.UtcNow;
        checkpoint.RowsIngested = result.RowsIngested;
        checkpoint.RequestsMade = run?.RequestsMade ?? 0;
        checkpoint.Error = Truncate(error, 512);

        // Rows count games, players and stat lines together; games are counted separately
        // because "did this day have a slate at all" is the question coverage really asks.
        checkpoint.GamesFound = await db.Games.CountAsync(
            g => g.SportId == sport.Id && g.StartsAt >= dayStart && g.StartsAt < dayEnd, ct);

        if (existing is null)
            db.BackfillCheckpoints.Add(checkpoint);

        await db.SaveChangesAsync(ct);

        return new DayOutcome(checkpoint.GamesFound, checkpoint.RowsIngested, error);
    }

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];

    private readonly record struct DayOutcome(int Games, int Rows, string? Error);
}

public record SourceEligibility(string Key, bool Eligible, string Reason);

public record BackfillProgress(
    string SourceKey,
    string SportKey,
    DateOnly Date,
    int Walked,
    int Skipped,
    int Failed,
    int GamesFound,
    int RowsIngested);

public record BackfillReport(
    int Walked,
    int Skipped,
    int Failed,
    int GamesFound,
    int RowsIngested,
    string? StoppedBecause);

public record BackfillCoverage(
    int DaysRequested,
    int TargetDayFetches,
    int Completed,
    int Failed,
    int GamesFound,
    int RowsIngested,
    int RequestsMade,
    DateOnly? EarliestCovered,
    DateOnly? LatestCovered);
