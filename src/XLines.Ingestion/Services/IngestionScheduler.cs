using XLines.Core.Entities;
using XLines.Data;
using XLines.Ingestion.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace XLines.Ingestion.Services;

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
///   standing between the schedule and an overrun. League by league, each on its own next
///   start, plus one closing scan just before a start (<see cref="Configuration.ClosingScanOptions"/>).</item>
///
///   <item><b>ESPN before a game</b> — the slate is refreshed when the next start is close.
///   Start times move, games get postponed, and a schedule fetched at 09:00 is stale by first
///   pitch.</item>
///
///   <item><b>ESPN while a game is live</b> — the live job reads the scoreboard every
///   <c>LivePoll</c> for any game whose start time has passed but is not yet final, and the loop
///   ticks that fast only while there is one, so a score moves on the desk while the game is
///   actually being played rather than jumping straight from nothing to the final. Its own job,
///   distinct from the pre-game refresh, which stops the moment a game has no future start left
///   to protect.</item>
///
///   <item><b>ESPN after a game</b> — results are fetched once a started game is old enough to
///   have finished, and retried until it is final here. Waiting for the next morning meant a
///   game finishing at 22:00 stayed unsettled for eleven hours.</item>
///
///   <item><b>ESPN for the history</b> — plays for finals from before the gamecast feed, a batch
///   every <c>BackportInterval</c> while any are owed and no game is in play
///   (<see cref="GamecastBackport"/>).</item>
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

    /// <summary>
    /// Each league's last scan as this host attempted it. Merged with the runs table every tick
    /// (<see cref="LinePollPlanner.LastScansAsync"/>), which survives a restart; this covers an
    /// attempt that wrote no run, so a scan refused before it reached the provider is not retried
    /// every tick.
    /// </summary>
    private readonly Dictionary<string, DateTimeOffset> _lastLeagueScan = new(StringComparer.OrdinalIgnoreCase);

    private DateTimeOffset? _lastLinePlan;

    private TimeSpan PlanRefresh => Positive(_options.LinePolling.MinimumInterval, new LinePollingOptions().MinimumInterval);

    private DateTimeOffset? _lastSlateRefresh;
    private DateTimeOffset? _lastLivePoll;
    private DateTimeOffset? _lastResultsSweep;
    private DateTimeOffset? _lastRetentionRun;
    private DateTimeOffset? _lastFighterPass;
    private DateTimeOffset? _lastBackport;

    /// <summary>The cadence currently in force, for the Ops surfaces.</summary>
    public PollPlan? CurrentPlan { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Ingestion scheduler started");

        if (!_options.Espn.Enabled)
            logger.LogInformation("ESPN is off ({Switch} is false): fixtures and scores come from The Odds API while a key is held, and no box-score pass will run. ESPN is opt-in; see the desk's Ops window",
                EspnOptIn.Switch);

        // Startup fetches the slate, which is free, and never the lines, which are not.
        //
        // This ran the odds job — the single most expensive thing in the catalog — so every
        // restart during a debugging session billed the provider, and a crash loop would have
        // billed it repeatedly. Its own summary said "runs every job once", which it never did:
        // it ran exactly the one that costs money.
        if (_options.RunOnStartup)
            await RunJobAsync(IngestionJobs.EspnSlate, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(await NextDelayAsync(DateTimeOffset.UtcNow, stoppingToken), stoppingToken);
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

    /// <summary>
    /// How long to sleep before the next tick: <see cref="EspnScheduleOptions.LivePoll"/> while a
    /// game is in play, <see cref="IngestionOptions.TickInterval"/> otherwise.
    ///
    /// <para>
    /// The live pass can only run on a tick, so a fifteen-second poll on a one-minute tick is a
    /// one-minute poll. Ticking fast all the time would buy nothing on a quiet desk but queries,
    /// so the tick drops to the live poll only while there is something live to poll, and comes
    /// back up the tick after the last game goes final. Every other job keeps its own
    /// <c>Due</c> gate, so a faster tick asks them more often and runs them no more often.
    /// </para>
    ///
    /// <para>
    /// A failure to ask — the database restarting — falls back to the ordinary tick rather than
    /// throwing: the loop's catch would otherwise go straight round again and spin.
    /// </para>
    /// </summary>
    public async Task<TimeSpan> NextDelayAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!_options.GamePasses.Enabled)
            return TickInterval;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<XLinesDbContext>();

            return await IngestionJobs.InPlay(db, now, _options.EffectiveSports).AnyAsync(ct)
                ? TimeSpan.FromTicks(Math.Min(TickInterval.Ticks, LivePoll.Ticks))
                : TickInterval;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not ask whether a game is in play; ticking at the ordinary interval");
            return TickInterval;
        }
    }

    /// <summary>
    /// The configured tick and live poll, or their defaults when configuration gives zero or less:
    /// <c>Task.Delay</c> throws on a negative delay, and the loop's catch would go straight round
    /// again on every tick, spinning on error logs.
    /// </summary>
    private TimeSpan TickInterval => Positive(_options.TickInterval, new IngestionOptions().TickInterval);

    private TimeSpan LivePoll => Positive(_options.GamePasses.LivePoll, new EspnScheduleOptions().LivePoll);

    private TimeSpan BackportInterval => Positive(_options.Gamecast.BackportInterval, new GamecastOptions().BackportInterval);

    private static TimeSpan Positive(TimeSpan configured, TimeSpan fallback)
        => configured > TimeSpan.Zero ? configured : fallback;

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
                await RunJobAsync(IngestionJobs.EspnLive, ct);
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

            // Fighters on a card ahead, and fighters who have just fought, are owed a profile
            // (ADR 0021). Free to ask, and nothing is fetched when nobody is owed one.
            if (_options.EffectiveSports.Any(Core.Analytics.FightSports.IsFight) && Due(_lastFighterPass, FighterPass, now))
            {
                await RunJobAsync(IngestionJobs.EspnFighters, ct);
                _lastFighterPass = now;
            }

            // Plays for the finals from before the feed, a batch every BackportInterval while any
            // are owed, and never while a game is in play: the live passes have first call on ESPN.
            if (_options.Gamecast.Enabled && Due(_lastBackport, BackportInterval, now) && await BackportIsDueAsync(now, ct))
            {
                await RunJobAsync(IngestionJobs.EspnGamecastBackport, ct);
                _lastBackport = now;
            }
        }

        // ESPN off: the default stats source (ADR 0026), if a key is held. Its fixtures are free and
        // keep the schedule as the slate did; its scores are metered, so they run only when the
        // operator chose automatic scanning, at most once a league a day, never into the reserve —
        // the job decides each of those (IngestionJobs.RunScoresAsync), this only asks it often
        // enough to notice a slate that has finished. With ESPN on, none of this runs.
        if (!_options.Espn.Enabled && _options.GamePasses.Enabled && await HasScheduleSourceAsync(ct))
        {
            if (FixturesAreDue(now) || await FixturesAreDueForStartAsync(now, ct))
            {
                await RunJobAsync(IngestionJobs.OddsFixtures, ct);
                _lastFixtures = now;
            }

            if (Due(_lastScoresCheck, ScoresCheck, now))
            {
                _lastScoresCheck = now;
                await RunJobAsync(IngestionJobs.OddsScores, ct);
            }
        }

        // Lines are the one thing the scheduler will not fetch on its own initiative unless
        // explicitly told it may. Everything above this line is free; this is not.
        //
        // Asked of the data rather than of configuration, so a switch made in Ops reaches this
        // host and the worker alike without a restart — and switching back stops the spending
        // at the next tick.
        //
        // Asked every tick, league by league: each league keeps its own cadence and a closing scan
        // has a ten-minute window to land in, so neither can wait on one shared interval.
        if (await LinesRunUnattendedAsync(ct))
            await ScanLinesAsync(now, ct);
    }

    private DateTimeOffset? _lastFixtures;
    private DateTimeOffset? _lastScoresCheck;

    /// <summary>The fixtures are read at least this often, games starting or not, so a week ahead stays filled.</summary>
    private static readonly TimeSpan FixtureRefresh = TimeSpan.FromHours(6);

    /// <summary>How often the scores job is asked whether a league is due. Asking costs nothing; the call does.</summary>
    private static readonly TimeSpan ScoresCheck = TimeSpan.FromMinutes(15);

    /// <summary>Whether a key is held for the default source, asked each time: one typed into the desk counts at once.</summary>
    private async Task<bool> HasScheduleSourceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<SourceRegistry>().Port == StatsPort.TheOddsApi;
    }

    private bool FixturesAreDue(DateTimeOffset now) => Due(_lastFixtures, FixtureRefresh, now);

    /// <summary>As the slate: a game starting within the pre-game lead, and not read in the last slate interval.</summary>
    private async Task<bool> FixturesAreDueForStartAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!Due(_lastFixtures, _options.GamePasses.SlateRefresh, now))
            return false;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<XLinesDbContext>();
        var horizon = now + _options.GamePasses.PreGameLead;

        return await db.Games.AnyAsync(g => g.StartsAt > now && g.StartsAt <= horizon, ct);
    }

    private async Task<bool> LinesRunUnattendedAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<XLinesDbContext>();
        return await db.LinesRunUnattendedAsync(_options.LinePolling.RunsUnattended, ct);
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
        var db = scope.ServiceProvider.GetRequiredService<XLinesDbContext>();

        if (_lastSlateRefresh is null)
            return true;

        // A fight card is announced and changed — a short-notice replacement, a scrapped bout —
        // all week, and nothing else puts it on the desk (ADR 0021), so a desk holding a fought
        // sport reads its slate at least every few hours, games starting or not.
        if (_options.EffectiveSports.Any(Core.Analytics.FightSports.IsFight) && now - _lastSlateRefresh >= FightCardRefresh)
            return true;

        var horizon = now + _options.GamePasses.PreGameLead;

        return await db.Games.AnyAsync(g => g.StartsAt > now && g.StartsAt <= horizon, ct);
    }

    /// <summary>How often a desk holding a fought sport re-reads the week's cards when nothing else asks it to.</summary>
    private static readonly TimeSpan FightCardRefresh = TimeSpan.FromHours(6);

    /// <summary>How often fighter profiles are checked for fighters owed one.</summary>
    private static readonly TimeSpan FighterPass = TimeSpan.FromHours(1);

    /// <summary>
    /// Live is due when a game is in play (<see cref="IngestionJobs.InPlay"/>) and the last live
    /// poll is stale. The same predicate sets the tick (<see cref="NextDelayAsync"/>), so the
    /// scheduler never ticks fast for a game the live job would not fetch.
    /// </summary>
    private async Task<bool> LivePollIsDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!Due(_lastLivePoll, LivePoll, now))
            return false;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<XLinesDbContext>();

        return await IngestionJobs.InPlay(db, now, _options.EffectiveSports).AnyAsync(ct);
    }

    /// <summary>
    /// The backport is due while a finished game is owed its plays and no game is in play — the
    /// backport's own question (<see cref="GamecastBackport.IsDueAsync"/>), so the scheduler and an
    /// on-demand run agree on it.
    /// </summary>
    private async Task<bool> BackportIsDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<GamecastBackport>().IsDueAsync(now, ct);
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
    /// Scans for movement league by league, each at the cadence the remaining budget affords it,
    /// and only a league with something close enough to starting for its price to still be
    /// moving; then any league owed its closing scan.
    ///
    /// <para>
    /// This ran <c>odds:lines</c>, which prices every league, whenever any one league had a game
    /// near — so one league's slate billed all of them. Each scan is now the league's own job,
    /// <c>odds:lines:{sport}</c>, the one its Pull button runs: the budget guard and the reserve
    /// see it exactly as they see a press.
    /// </para>
    /// </summary>
    private async Task ScanLinesAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<SourceRegistry>();
        var planner = scope.ServiceProvider.GetRequiredService<LinePollPlanner>();

        // Planned at most every minimum interval rather than every tick: no league's cadence is
        // shorter than that, and a plan is half a dozen queries and, on a spent month, a warning.
        if (CurrentPlan is null || Due(_lastLinePlan, PlanRefresh, now))
        {
            CurrentPlan = await planner.PlanAsync(registry.OddsSources.Select(s => s.Key).ToList(), ct);
            _lastLinePlan = now;
        }

        if (CurrentPlan is not { } plan)
            return;

        if (plan.Leagues.Count == 0)
        {
            // Nothing starting soon. The budget is best kept for when there is.
            logger.LogDebug("No games inside the movement window; skipping the line scan");
            return;
        }

        var lastScans = LinePacing.Latest(await planner.LastScansAsync(ct), _lastLeagueScan);
        var paced = LinePacing.DueLeagues(plan.Leagues, lastScans, now);

        foreach (var sport in paced)
        {
            var league = plan.Leagues.First(l => l.Sport == sport);

            logger.LogInformation(
                "Line scan, {Sport}: {Credits} credits, next in {Interval}{Bound}{Left}",
                sport, league.CreditsPerScan, league.Interval,
                plan.BoundBy is null ? "" : $", paced by {plan.BoundBy}",
                plan.ScansRemainingToday is null ? "" : $", {plan.ScansRemainingToday} scans left in {plan.WindowLabel} allowance");

            await ScanLeagueAsync(sport, now, ct);
            lastScans[sport] = now;
        }

        // Asked after the paced scans, so a league one of them just priced is not bought twice.
        // The day's cap is counted from the runs those scans leave, so it holds across a restart and
        // across the Web and Worker hosts sharing the database.
        foreach (var closing in await planner.ClosingScansDueAsync(now, lastScans, ct))
        {
            logger.LogInformation("Closing scan, {Sport}: {Start:t} starts within {Lead}",
                closing.Sport, closing.Start, _options.LinePolling.ClosingScan.Lead);

            _lastLeagueScan[closing.Sport] = now;

            await using var jobScope = scopeFactory.CreateAsyncScope();
            await jobScope.ServiceProvider.GetRequiredService<IngestionJobs>()
                .RunClosingScanAsync(closing.Sport, closing.LeagueDay, ct);
        }
    }

    private async Task ScanLeagueAsync(string sport, DateTimeOffset now, CancellationToken ct)
    {
        _lastLeagueScan[sport] = now;
        await RunJobAsync(IngestionJobs.OddsLinesPrefix + sport, ct);
    }

    private async Task RunJobAsync(string key, CancellationToken ct)
    {
        // ESPN off (EspnOptIn): its passes are not run at all, rather than run against no source
        // and logged as empty every tick. Settlement and the lines are not ESPN's and still run.
        if (!_options.Espn.Enabled && EspnOptIn.IsEspnJob(key))
            return;

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
