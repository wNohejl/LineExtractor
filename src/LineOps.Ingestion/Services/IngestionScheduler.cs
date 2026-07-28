using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineOps.Ingestion.Services;

/// <summary>
/// Drives unattended ingestion.
///
/// <para>
/// Three jobs, each triggered by the thing that actually makes it due rather than by the hour:
/// </para>
///
/// <list type="bullet">
///   <item><b>Lines</b> — paced by the provider's remaining allowance, so the day's quota is
///   spent evenly and never overrun. The old fixed three-hour cadence could not be right for
///   two providers with different ceilings, and left the budget guard as the only thing
///   standing between the schedule and an overrun.</item>
///
///   <item><b>ESPN before a game</b> — the slate is refreshed when the next start is close.
///   Start times move, games get postponed, and a schedule fetched at 09:00 is stale by first
///   pitch.</item>
///
///   <item><b>ESPN while a game is live</b> — the same scoreboard call is repeated on a tight
///   interval for any game whose start time has passed but is not yet final, so a score moves
///   on the desk while the game is actually being played rather than jumping straight from
///   nothing to the final. Distinct from the pre-game refresh, which stops the moment a game
///   has no future start left to protect.</item>
///
///   <item><b>ESPN after a game</b> — results are fetched once a started game is old enough to
///   have finished, and retried until it is final here. Waiting for the next morning meant a
///   game finishing at 22:00 stayed unsettled for eleven hours.</item>
/// </list>
///
/// <para>
/// Every job is an entry in <see cref="IngestionJobs"/>, the same catalog the operator's pull
/// menu runs. Automatic and manual paths therefore cannot drift, and neither can their costs.
/// </para>
///
/// <para>
/// The loop ticks on a short interval and asks what is due rather than sleeping until the next
/// job, so a restart never skips a window and the schedule survives clock drift. Any exception
/// inside a tick is logged and the loop continues — a crashed scheduler would take the whole
/// platform silent, which is worse than one failed run.
/// </para>
/// </summary>
public class IngestionScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<IngestionOptions> options,
    ILogger<IngestionScheduler> logger) : BackgroundService
{
    private readonly IngestionOptions _options = options.Value;

    private DateTimeOffset? _lastLineScan;
    private DateTimeOffset? _lastSlateRefresh;
    private DateTimeOffset? _lastLivePoll;
    private DateTimeOffset? _lastResultsSweep;
    private DateTimeOffset? _lastRetentionRun;

    private TimeSpan _lineInterval = TimeSpan.FromMinutes(30);

    /// <summary>The cadence currently in force, for the Ops surfaces.</summary>
    public PollPlan? CurrentPlan { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Ingestion scheduler started");

        // Startup fetches the slate, which is free, and never the lines, which are not.
        //
        // This ran the odds job — the single most expensive thing in the catalog — so every
        // restart during a debugging session billed the provider, and a crash loop would have
        // billed it repeatedly. Its own summary said "runs every job once", which it never did:
        // it ran exactly the one that costs money.
        if (_options.RunOnStartup)
            await RunJobAsync(IngestionJobs.EspnSlate, stoppingToken);

        using var timer = new PeriodicTimer(_options.TickInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduler tick failed; continuing");
            }
        }

        logger.LogInformation("Ingestion scheduler stopped");
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Retention first: what makes a game promotable is its start time passing, which
        // happens whether or not anything was polled. Tying this to a poll would leave a
        // late-night slate unpromoted until the next one.
        if (_options.OddsRetention.Enabled && Due(_lastRetentionRun, _options.OddsRetention.Interval, now))
        {
            await RunRetentionAsync(ct);
            _lastRetentionRun = now;
        }

        if (_options.GamePasses.Enabled)
        {
            if (await SlateIsDueAsync(now, ct))
            {
                await RunJobAsync(IngestionJobs.EspnSlate, ct);
                _lastSlateRefresh = now;
            }

            if (await LivePollIsDueAsync(now, ct))
            {
                await RunJobAsync(IngestionJobs.EspnSlate, ct);
                _lastLivePoll = now;
            }

            if (Due(_lastResultsSweep, _options.GamePasses.ResultsRetry, now) && await ResultsAreOwedAsync(ct))
            {
                await RunJobAsync(IngestionJobs.EspnResults, ct);

                // Results are what settlement grades against, so settle in the same breath
                // rather than leaving graded-able entries waiting for a separate cadence.
                await RunJobAsync(IngestionJobs.Settle, ct);
                _lastResultsSweep = now;
            }
        }

        // Lines are the one thing the scheduler will not fetch on its own initiative unless
        // explicitly told it may. Everything above this line is free; this is not.
        if (_options.LinePolling.RunsUnattended && Due(_lastLineScan, _lineInterval, now))
        {
            await ScanLinesAsync(ct);
            _lastLineScan = now;
        }
    }

    private static bool Due(DateTimeOffset? last, TimeSpan every, DateTimeOffset now)
        => last is null || now - last >= every;

    /// <summary>
    /// The slate is due when the next game is close enough that its details could still change,
    /// and we have not already refreshed recently.
    /// </summary>
    private async Task<bool> SlateIsDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!Due(_lastSlateRefresh, _options.GamePasses.SlateRefresh, now))
            return false;

        // A cold desk has no games at all, so the first pass has nothing to key off — take it.
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LineOpsDbContext>();

        if (_lastSlateRefresh is null)
            return true;

        var horizon = now + _options.GamePasses.PreGameLead;

        return await db.Games.AnyAsync(g => g.StartsAt > now && g.StartsAt <= horizon, ct);
    }

    /// <summary>
    /// Live is due when a game that should already be under way is neither final nor postponed
    /// here, and the last live poll is stale.
    ///
    /// Keyed on start time having passed rather than on <see cref="GameStatus.Live"/>, because a
    /// game that just started is not yet marked live in our data — that is exactly what the next
    /// poll is for. Bounded to the last day so a stuck fixture cannot pin this pass on forever.
    /// </summary>
    private async Task<bool> LivePollIsDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!Due(_lastLivePoll, _options.GamePasses.LivePoll, now))
            return false;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LineOpsDbContext>();

        return await db.Games.AnyAsync(g =>
            g.StartsAt <= now
            && g.StartsAt >= now.AddDays(-1)
            && g.Status != GameStatus.Final
            && g.Status != GameStatus.Postponed, ct);
    }

    /// <summary>True when a game has started long enough ago to be final and is not final here.</summary>
    private async Task<bool> ResultsAreOwedAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IngestionJobs>();

        var dates = await jobs.DatesAwaitingResultsAsync(_options.GamePasses.ResultsAfterStart, ct);

        return dates.Count > 0;
    }

    /// <summary>
    /// Scans for movement at whatever cadence the remaining budget affords, and only when
    /// something is close enough to starting for its price to still be moving.
    /// </summary>
    private async Task ScanLinesAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<SourceRegistry>();
        var planner = scope.ServiceProvider.GetRequiredService<LinePollPlanner>();

        var plan = await planner.PlanAsync(registry.OddsSources.Select(s => s.Key).ToList(), ct);

        if (plan is null)
            return;

        CurrentPlan = plan;
        _lineInterval = plan.Interval;

        if (!await planner.HasWatchableGamesAsync(ct))
        {
            // Nothing starting soon. The budget is best kept for when there is.
            logger.LogDebug("No games inside the movement window; skipping the line scan");
            return;
        }

        logger.LogInformation(
            "Line scan: {Cost} requests, next in {Interval}{Bound}{Left}",
            plan.CostPerScan, plan.Interval,
            plan.BoundBy is null ? "" : $", paced by {plan.BoundBy}",
            plan.ScansRemainingToday is null ? "" : $", {plan.ScansRemainingToday} scans left in {plan.WindowLabel} allowance");

        await RunJobAsync(IngestionJobs.OddsLines, ct);
    }

    private async Task RunJobAsync(string key, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IngestionJobs>();

        await jobs.RunAsync(key, ct);
    }

    private async Task RunRetentionAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var retention = scope.ServiceProvider.GetRequiredService<OddsRetentionService>();

        try
        {
            await retention.RunAsync(ct);
            await retention.DropEmptyPartitionsAsync(ct);
        }
        catch (Exception ex)
        {
            // Retention is housekeeping. It failing must not stop the scheduler from polling —
            // stale scans cost disk, a silent scheduler costs the product.
            logger.LogError(ex, "Odds retention pass failed");
        }
    }
}
