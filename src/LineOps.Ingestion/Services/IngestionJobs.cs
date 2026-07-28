using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineOps.Ingestion.Services;

/// <summary>
/// The pulls this platform can make, named individually.
///
/// <para>
/// There used to be one button called "Ingest now". It ran every odds source across every
/// sport and nothing else — no schedules, no results — so the one manual control on the desk
/// could not fetch the thing an operator most often wants, and gave no clue what it was about
/// to spend. "Pointless" was a fair description.
/// </para>
///
/// <para>
/// A job here is a unit of work an operator would actually ask for: get today's games, get the
/// results, get fresh lines. Each declares what it costs before it runs, so the menu can show
/// the price and the budget guard is a backstop rather than the only guard.
/// </para>
///
/// <para>
/// The scheduler runs these same jobs. That is the point of the catalog rather than a pile of
/// handlers: automatic and manual paths cannot drift, and a job fixed for one is fixed for both.
/// </para>
/// </summary>
public class IngestionJobs(
    IServiceScopeFactory scopeFactory,
    IOptions<IngestionOptions> options,
    ILogger<IngestionJobs> logger)
{
    public const string EspnSlate = "espn:slate";
    public const string EspnResults = "espn:results";
    public const string OddsLines = "odds:lines";
    public const string Settle = "settle";

    /// <summary>
    /// Prefix for a lines pull narrowed to one sport, e.g. <c>odds:lines:mlb</c>.
    ///
    /// A credit-billed provider charges per sport per call, so "fetch lines" is not one price —
    /// it is one price per league in season. Naming the sport in the job is what lets the menu
    /// quote a real cost and lets an operator spend on the league they are actually working.
    /// </summary>
    public const string OddsLinesPrefix = "odds:lines:";

    private readonly IngestionOptions _options = options.Value;

    /// <summary>
    /// Every job, with what it would cost right now and whether it can run.
    ///
    /// Availability is computed from the registered adapters rather than configuration, so a
    /// provider that is configured but not registered — the usual "enabled with no key" case —
    /// reads as unavailable with a reason instead of failing when pressed.
    /// </summary>
    public async Task<IReadOnlyList<IngestionJob>> DescribeAsync(CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<SourceRegistry>();

        var sports = _options.EffectiveSports;
        var stats = registry.StatsSources.Count;
        var odds = registry.OddsSources.Count;

        // What one sport's lines actually cost, in the unit the provider bills. Stated per sport
        // because that is how it is charged and how it is now spent.
        var creditsPerSport = odds * _options.LinePolling.CreditsPerSportPerScan;

        var jobs = new List<IngestionJob>
        {
            new IngestionJob(
                Key: EspnSlate,
                Label: "Today's games",
                Description: "Schedules, start times and live scores for every sport in play.",
                EstimatedRequests: stats * sports.Length,
                Available: stats > 0,
                Unavailable: stats > 0 ? null : "No stats source registered."),

            new IngestionJob(
                Key: EspnResults,
                Label: "Results owed",
                Description: "Final scores and box scores for every day still missing them.",
                // A results pass is one scoreboard call per sport plus one per finished game,
                // so the estimate is a floor rather than a promise.
                EstimatedRequests: stats * sports.Length,
                Available: stats > 0,
                Unavailable: stats > 0 ? null : "No stats source registered."),

            new IngestionJob(
                Key: Settle,
                Label: "Settle finished games",
                Description: "Grades journal entries whose games are final and resolves their CLV.",
                EstimatedRequests: 0,
                Available: true,
                Unavailable: null)
        };

        // One entry per sport rather than one for all of them.
        //
        // "Fresh lines" used to scan every configured league at once, which on a credit-billed
        // provider is the whole month's allowance being spent on leagues that are out of season
        // and quoting nothing. Naming the sport makes the price legible before the press and lets
        // an operator buy only the league they are working.
        foreach (var sport in sports)
        {
            jobs.Add(new IngestionJob(
                Key: OddsLinesPrefix + sport,
                Label: $"Pull lines — {sport.ToUpperInvariant()}",
                Description: $"Moneyline and spread across every book for {sport.ToUpperInvariant()} "
                             + "games that have not started.",
                EstimatedRequests: odds,
                EstimatedCredits: creditsPerSport,
                Available: odds > 0,
                Unavailable: odds > 0 ? null : "No odds source registered."));
        }

        return jobs;
    }

    /// <summary>Runs one job by key. Unknown keys are refused rather than silently ignored.</summary>
    public async Task<JobOutcome> RunAsync(string key, CancellationToken ct = default)
    {
        var started = DateTimeOffset.UtcNow;

        try
        {
            var outcome = key switch
            {
                EspnSlate => await RunStatsAsync(EspnSlate, DateOnly.FromDateTime(DateTime.UtcNow), schedulesOnly: true, ct),
                EspnResults => await RunResultsAsync(ct),
                OddsLines => await RunOddsAsync(null, ct),
                Settle => await RunSettleAsync(ct),

                _ when key.StartsWith(OddsLinesPrefix, StringComparison.Ordinal)
                    => await RunOddsAsync(key[OddsLinesPrefix.Length..], ct),

                _ => new JobOutcome(key, false, 0, 0, $"Unknown job '{key}'.")
            };

            logger.LogInformation("Job {Job}: {Rows} rows, {Failures} failures in {Elapsed:N0}ms",
                key, outcome.Rows, outcome.Failures, (DateTimeOffset.UtcNow - started).TotalMilliseconds);

            return outcome;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Job {Job} failed", key);
            return new JobOutcome(key, false, 0, 1, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Schedules and, when asked, box scores.
    ///
    /// Both come from the same adapter call — a box-score pass fetches the scoreboard first
    /// anyway — so "today's games" and "yesterday's results" differ only in the date and in
    /// whether the per-game summaries are worth the requests. Before the games are played
    /// there is nothing to summarise, which is why the slate pass stops at the scoreboard.
    /// </summary>
    private async Task<JobOutcome> RunStatsAsync(
        string jobKey, DateOnly date, bool schedulesOnly, CancellationToken ct)
    {
        var rows = 0;
        var failures = 0;

        foreach (var sportKey in _options.EffectiveSports)
        {
            if (ct.IsCancellationRequested)
                break;

            // A scope per sport keeps the change tracker bounded, the lesson from the backfill.
            await using var scope = scopeFactory.CreateAsyncScope();
            var registry = scope.ServiceProvider.GetRequiredService<SourceRegistry>();
            var stats = scope.ServiceProvider.GetRequiredService<StatsIngestionService>();

            foreach (var source in registry.StatsSources)
            {
                var outcome = schedulesOnly
                    ? await stats.IngestScheduleAsync(source, sportKey, jobKey, date, ct)
                    : await stats.IngestAsync(source, sportKey, jobKey, date, ct);

                rows += outcome.RowsIngested;

                if (outcome.Status == RunStatus.Failed)
                    failures++;
            }
        }

        return new JobOutcome(jobKey, failures == 0, rows, failures, null);
    }

    /// <summary>
    /// A price scan, over one sport or over every configured one.
    /// </summary>
    /// <param name="onlySport">
    /// The single league to scan, or null for all of them. A named sport is refused rather than
    /// silently widened when it is not configured — quietly scanning four leagues because one was
    /// misspelled is exactly the accident a credit budget cannot absorb.
    /// </param>
    private async Task<JobOutcome> RunOddsAsync(string? onlySport, CancellationToken ct)
    {
        var jobKey = onlySport is null ? OddsLines : OddsLinesPrefix + onlySport;

        var sports = onlySport is null
            ? _options.EffectiveSports
            : _options.EffectiveSports
                .Where(s => string.Equals(s, onlySport, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        if (sports.Length == 0)
            return new JobOutcome(jobKey, false, 0, 0, $"'{onlySport}' is not a configured sport.");

        var rows = 0;
        var failures = 0;
        var credits = 0;

        await using var scope = scopeFactory.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<SourceRegistry>();
        var ingestion = scope.ServiceProvider.GetRequiredService<OddsIngestionService>();
        var initialiser = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        var db = scope.ServiceProvider.GetRequiredService<LineOpsDbContext>();

        await initialiser.EnsurePartitionsAsync(ct);

        foreach (var source in registry.OddsSources)
        {
            foreach (var sportKey in sports)
            {
                if (ct.IsCancellationRequested)
                    break;

                var outcome = await ingestion.IngestAsync(source, sportKey, jobKey, ct);
                rows += outcome.RowsIngested;

                if (outcome.Status == RunStatus.Failed)
                    failures++;
            }
        }

        // What it actually cost, read back from the runs just written rather than estimated.
        // The provider reports its own billing in response headers, so this is their number.
        credits = await db.IngestionRuns
            .Where(r => r.JobKey == jobKey && r.StartedAt >= DateTimeOffset.UtcNow.AddMinutes(-5))
            .SumAsync(r => (int?)r.CreditsSpent, ct) ?? 0;

        var detail = credits > 0
            ? $"{rows:N0} price {(rows == 1 ? "move" : "moves")}, {credits} credits spent."
            : null;

        return new JobOutcome(jobKey, failures == 0, rows, failures, detail);
    }

    /// <summary>
    /// Box scores for every day still missing them, not merely for yesterday.
    ///
    /// <para>
    /// This used to fetch a hardcoded <c>UtcNow.AddDays(-1)</c> while the scheduler decided
    /// whether to run it by asking <see cref="DatesAwaitingResultsAsync"/> which days were
    /// actually owed. The two disagreed, and the disagreement was silent: a day whose results
    /// were missed — the host down, ESPN briefly unavailable, a game finishing after the sweep —
    /// kept the trigger permanently true and kept re-fetching yesterday, which already had its
    /// results. The gap never closed and nothing said so.
    /// </para>
    ///
    /// <para>
    /// Walking the owed days is what makes the daily poll self-healing rather than merely
    /// repetitive.
    /// </para>
    /// </summary>
    private async Task<JobOutcome> RunResultsAsync(CancellationToken ct)
    {
        var owed = await DatesAwaitingResultsAsync(_options.GamePasses.ResultsAfterStart, ct);

        // Nothing outstanding still means yesterday, so pressing the button by hand does
        // something sensible on a desk that is already up to date.
        if (owed.Count == 0)
            owed = [DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)];

        var rows = 0;
        var failures = 0;

        foreach (var date in owed)
        {
            if (ct.IsCancellationRequested)
                break;

            var outcome = await RunStatsAsync(EspnResults, date, schedulesOnly: false, ct);

            rows += outcome.Rows;
            failures += outcome.Failures;
        }

        return new JobOutcome(
            EspnResults, failures == 0, rows, failures,
            $"{owed.Count} {(owed.Count == 1 ? "day" : "days")} swept, {rows:N0} rows.");
    }

    private async Task<JobOutcome> RunSettleAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var settlement = scope.ServiceProvider.GetRequiredService<SettlementService>();

        var summary = await settlement.SettleAsync(ct);

        return new JobOutcome(Settle, true, summary.Graded + summary.ClvResolved, 0,
            $"{summary.Graded} graded, {summary.ClvResolved} CLV resolved, {summary.LeftPending} pending.");
    }

    /// <summary>Games that have started but are not final here, i.e. results we are still owed.</summary>
    public async Task<IReadOnlyList<DateOnly>> DatesAwaitingResultsAsync(
        TimeSpan settleAfter, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LineOpsDbContext>();

        var cutoff = DateTimeOffset.UtcNow - settleAfter;

        var starts = await db.Games
            .Where(g => g.StartsAt <= cutoff
                        && g.StartsAt >= cutoff.AddDays(-3)
                        && g.Status != GameStatus.Final
                        && g.Status != GameStatus.Postponed)
            .Select(g => g.StartsAt)
            .ToListAsync(ct);

        return starts
            .Select(s => DateOnly.FromDateTime(s.UtcDateTime))
            .Distinct()
            .OrderBy(d => d)
            .ToList();
    }
}

/// <summary>One named pull, and what it would cost.</summary>
/// <param name="EstimatedCredits">
/// What the provider will bill, where it bills in credits rather than requests. Zero for the
/// unmetered ones. Quoted separately because requests and credits are not interchangeable — a
/// single call can cost several credits, and it is the credits that run out.
/// </param>
public record IngestionJob(
    string Key,
    string Label,
    string Description,
    int EstimatedRequests,
    bool Available,
    string? Unavailable,
    int EstimatedCredits = 0);

public record JobOutcome(string Key, bool Succeeded, int Rows, int Failures, string? Detail);
