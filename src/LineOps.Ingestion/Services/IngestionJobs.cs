using LineOps.Core.Analytics;
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

    /// <summary>
    /// The scoreboard for games being played (spec 2026-10-24 §5.2). Its own job rather than the
    /// slate run more often: the slate resolves every event on the day, refreshes every team's
    /// identity and syncs MLB's spine, which is right twice an hour and wasteful every fifteen
    /// seconds.
    /// </summary>
    public const string EspnLive = "espn:live";

    /// <summary>
    /// Games in play: started within the last day and neither final nor postponed here, in a
    /// configured league. The one definition of "a game is on", shared by the live job (which
    /// league-days to fetch) and the scheduler (whether it is due, and how fast to tick).
    ///
    /// <para>
    /// Keyed on start time having passed rather than on <see cref="GameStatus.Live"/>, because a
    /// game that just started is not yet marked live here — finding that out is what the next
    /// poll is for. Bounded to the last day so a stuck fixture cannot pin the fast cadence on for
    /// ever, and to configured leagues because a league nobody polls cannot be refreshed by it.
    /// </para>
    /// </summary>
    public static IQueryable<Game> InPlay(LineOpsDbContext db, DateTimeOffset now, IReadOnlyList<string> sports)
        => db.Games.Where(g => g.StartsAt <= now
                               && g.StartsAt >= now.AddDays(-1)
                               && g.Status != GameStatus.Final
                               && g.Status != GameStatus.Postponed
                               && sports.Contains(g.Sport!.Key));

    /// <summary>The league-days with a game in play, each one scoreboard request per stats source.</summary>
    private async Task<IReadOnlyList<(string Sport, DateOnly Day)>> LeagueDaysInPlayAsync(
        LineOpsDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var sports = _options.EffectiveSports;

        var inPlay = await InPlay(db, now, sports)
            .Select(g => new { Sport = g.Sport!.Key, g.StartsAt })
            .ToListAsync(ct);

        // The configured spelling, so the key handed to the adapter is the one config named.
        return inPlay
            .Select(g => (Sport: sports.First(s => string.Equals(s, g.Sport, StringComparison.OrdinalIgnoreCase)),
                          Day: LeagueClock.DateOf(g.StartsAt)))
            .Distinct()
            .OrderBy(x => x.Sport, StringComparer.Ordinal)
            .ThenBy(x => x.Day)
            .ToList();
    }
    public const string EspnResults = "espn:results";

    /// <summary>
    /// A game in play's box score as it stands, written by the gamecast feed every
    /// <see cref="GamecastOptions.BoxPoll"/> (spec 2026-10-24 §6.1). Its own job, apart from the
    /// feed's final box (<see cref="GamecastFeed.JobKey"/>), because a line from it is not a
    /// final's stats: a final holding nothing newer is still owed (<see cref="StatsOwed"/>), or a
    /// final box that never came would leave the last mid-game box standing for good. The
    /// reliability layer's data rule names the same key (<c>DataQuality.LiveBoxJobKey</c>).
    /// </summary>
    public const string EspnGamecastLive = "espn:gamecast:live";

    /// <summary>
    /// Plays, drives and probabilities for finished games that have none — the games that ended
    /// before the feed existed — a batch a run, newest first (<see cref="GamecastBackport"/>).
    /// </summary>
    public const string EspnGamecastBackport = "espn:gamecast:backport";

    /// <summary>
    /// A league's games over a range of days the operator names, with each final's box score and
    /// its close (<see cref="RangePull"/>): history the backfill seasons do not reach, or a range
    /// that came in incomplete. Not in the catalogue the menu lists, since it cannot run without a
    /// league and dates; the desk asks for them in a sheet, the Worker as <c>--sport --from --to</c>.
    /// </summary>
    public const string EspnRange = "espn:range";
    public const string OddsLines = "odds:lines";
    public const string Settle = "settle";

    /// <summary>
    /// The closing lines a finished game is still owed, asked of ESPN's odds endpoint one game at
    /// a time. The backport for a season walked while the summary carried no odds block.
    /// </summary>
    public const string EspnClosingLines = "espn:closing-lines";

    /// <summary>How many owed games one press asks about, per sport: a season, not a decade.</summary>
    private const int ClosingLinesPerRun = 600;

    /// <summary>
    /// Fighter profiles owed (ADR 0021): fighters on a card ahead, and fighters who have fought
    /// since theirs was taken. Four requests a fighter, against ESPN's athlete pages.
    /// </summary>
    public const string EspnFighters = "espn:fighters";

    /// <summary>Fighters one press describes: two full cards' worth, a few minutes at ESPN's pace.</summary>
    private const int FightersPerRun = 60;

    /// <summary>Requests one fighter's profile costs: athlete page, stat sheets, career rates, record.</summary>
    public const int RequestsPerFighter = 4;

    /// <summary>
    /// Prefix for a lines pull narrowed to one sport, e.g. <c>odds:lines:mlb</c>.
    ///
    /// A credit-billed provider charges per sport per call, so "fetch lines" is not one price —
    /// it is one price per league in season. Naming the sport in the job is what lets the menu
    /// quote a real cost and lets an operator spend on the league they are actually working.
    /// </summary>
    public const string OddsLinesPrefix = "odds:lines:";

    /// <summary>
    /// The league an on-call drill should exercise: the one actually in play.
    ///
    /// <para>
    /// A drill with no fault injected performs a real pull against a metered provider, so the
    /// rows it buys should be rows somebody is about to read. Naming a league in the code
    /// instead meant the drill bought fixtures weeks away while the slate on screen went
    /// unpriced, and the volume it wrote was measured against pulls for a different sport.
    /// </para>
    ///
    /// <para>
    /// A league nobody configured is never chosen however busy it looks, which is the rule
    /// <see cref="RunOddsAsync"/> already applies to a named sport: quietly scanning a league
    /// the operator did not ask for is what a credit budget cannot absorb.
    /// </para>
    /// </summary>
    /// <param name="upcomingBySport">Games inside the movement window, keyed by sport slug.</param>
    /// <param name="configured">The configured leagues, in preference order.</param>
    /// <returns>The league to drill, or null when none is configured.</returns>
    public static string? DrillSport(
        IReadOnlyDictionary<string, int> upcomingBySport, IReadOnlyList<string> configured)
    {
        if (configured.Count == 0)
            return null;

        var busiest = upcomingBySport
            .Where(kv => kv.Value > 0
                         && configured.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => kv.Key)
            .FirstOrDefault();

        // Match the configured spelling rather than whatever case the games table holds, so the
        // key handed to an adapter is the one config named.
        return busiest is null
            ? configured[0]
            : configured.First(s => string.Equals(s, busiest, StringComparison.OrdinalIgnoreCase));
    }

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
        var closers = registry.StatsSources.OfType<Core.Contracts.IClosingLineSource>().Count();

        // What one sport's lines actually cost, in the unit the provider bills. Stated per sport
        // because that is how it is charged and how it is now spent.
        // The adapters say, per sport, from the markets they will actually ask for; the
        // configured figure stands in only for a source that declares nothing.
        int CreditsFor(string sport)
        {
            var declared = registry.OddsSources.Sum(s => s.CreditsPerScan(sport));
            return declared > 0 ? declared : odds * _options.LinePolling.CreditsPerSportPerScan;
        }

        string MarketsFor(string sport)
        {
            var names = registry.OddsSources.SelectMany(s => s.MarketsFor(sport)).Distinct()
                .Select(m => m switch { Markets.Moneyline => "moneyline", Markets.Spread => "spread", Markets.Total => "total", _ => m })
                .ToList();
            return names.Count == 0 ? "Lines" : char.ToUpperInvariant(names[0][0]) + string.Join(", ", names)[1..];
        }

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
                Key: EspnLive,
                Label: "Games in play",
                Description: "Live scores and status for games under way, and the final for any that have just finished. "
                             + "Nothing is fetched when no game is on.",
                EstimatedRequests: stats * (await LeagueDaysInPlayAsync(
                    scope.ServiceProvider.GetRequiredService<LineOpsDbContext>(), DateTimeOffset.UtcNow, ct)).Count,
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

            await ClosingLinesJobAsync(scope.ServiceProvider, sports, closers, ct),

        };

        // Plays for the finals from before the feed: two requests a game, a batch a press.
        var gamecasts = scope.ServiceProvider.GetService<Adapters.IGamecastSource>() is not null;
        var batch = Math.Max(0, _options.Gamecast.BackportBatch);

        if (scope.ServiceProvider.GetService<GamecastBackport>() is { } backport)
        {
            jobs.Add(new IngestionJob(
                Key: EspnGamecastBackport,
                Label: "Play-by-play owed",
                Description: "Plays, drives and win, spread and total probabilities for finished games that have none, "
                             + $"newest first, {batch} games a press at two requests each. A game ESPN has no plays for "
                             + "is set aside rather than asked for again.",
                EstimatedRequests: gamecasts ? Math.Min(await backport.OwedCountAsync(DateTimeOffset.UtcNow, ct), batch) * 2 : 0,
                Available: gamecasts && batch > 0,
                Unavailable: !gamecasts ? "No gamecast source registered."
                    : batch > 0 ? null : "Switched off (Gamecast:BackportBatch)."));
        }

        // Only a desk that holds a fought sport has fighters to describe.
        if (sports.Any(FightSports.IsFight))
        {
            var owedFighters = 0;
            var fighters = scope.ServiceProvider.GetRequiredService<FighterProfileService>();

            foreach (var sport in sports.Where(FightSports.IsFight))
                owedFighters += await fighters.OwedCountAsync(sport, ct);

            jobs.Add(new IngestionJob(
                Key: EspnFighters,
                Label: "Fighter profiles owed",
                Description: "Bio, career rates and every recorded fight with ESPN's stat sheet, for fighters on a card "
                             + $"ahead or who have fought since theirs was taken — {RequestsPerFighter} requests a fighter, "
                             + $"{FightersPerRun} fighters a press.",
                EstimatedRequests: Math.Min(owedFighters, FightersPerRun) * RequestsPerFighter,
                Available: stats > 0,
                Unavailable: stats > 0 ? null : "No stats source registered."));
        }

        jobs.Add(new IngestionJob(
            Key: Settle,
            Label: "Settle finished games",
            Description: "Grades journal entries whose games are final and resolves their CLV.",
            EstimatedRequests: 0,
            Available: true,
            Unavailable: null));

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
                Description: $"{MarketsFor(sport)} across every book for {sport.ToUpperInvariant()} "
                             + "games that have not started.",
                EstimatedRequests: odds,
                EstimatedCredits: CreditsFor(sport),
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
                EspnSlate => await RunSlateAsync(ct),
                EspnLive => await RunLiveAsync(ct),
                EspnResults => await RunResultsAsync(ct),
                EspnClosingLines => await RunClosingLinesAsync(ct),
                EspnFighters => await RunFightersAsync(FightersPerRun, ct),
                EspnGamecastBackport => await RunGamecastBackportAsync(ct),
                OddsLines => await RunOddsAsync(null, ct),
                Settle => await RunSettleAsync(ct),

                // A range is nothing without its league and dates, so it is refused by name here
                // rather than reported as an unknown job.
                EspnRange => new JobOutcome(key, false, 0, 0, RangePull.Usage),

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
    /// Today's scoreboard, and the day of any game still in play from before midnight.
    ///
    /// <para>
    /// A game that starts at 10:10pm Eastern is still being played at half past midnight, and it
    /// is listed on the scoreboard of the day it started, not the day it is now — so a slate
    /// pressed after midnight reads that day too. The live poll used to run this pass; it is
    /// <see cref="RunLiveAsync"/> now, and this one is the pre-game refresh it was built as.
    /// </para>
    /// </summary>
    private async Task<JobOutcome> RunSlateAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        List<DateTimeOffset> inPlay;

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            inPlay = await scope.ServiceProvider.GetRequiredService<LineOpsDbContext>().Games
                .Where(g => g.StartsAt <= now
                            && g.StartsAt >= now.AddDays(-1)
                            && g.Status != GameStatus.Final
                            && g.Status != GameStatus.Postponed)
                .Select(g => g.StartsAt)
                .ToListAsync(ct);
        }

        var days = inPlay.Select(LeagueClock.DateOf).Append(LeagueClock.Today()).Distinct().Order();

        var rows = 0;
        var failures = 0;

        foreach (var day in days)
        {
            var outcome = await RunStatsAsync(EspnSlate, day, schedulesOnly: true, ct);
            rows += outcome.Rows;
            failures += outcome.Failures;
        }

        await SyncSpineAsync(ct);
        await SyncInjuriesAsync(ct);

        return new JobOutcome(EspnSlate, failures == 0, rows, failures, null);
    }

    /// <summary>
    /// The scoreboard for each league-day with a game in play, written only for the events being
    /// played or just finished (<see cref="StatsIngestionService.IngestLiveAsync"/>).
    ///
    /// <para>
    /// Only the leagues and days that have a game on: a Sunday of NFL with no MLB game live is
    /// one request, not one per league. No team-identity refresh and no MLB spine — both are
    /// the slate's, and neither changes between one pitch and the next. A desk with nothing in
    /// play makes no request at all.
    /// </para>
    /// </summary>
    private async Task<JobOutcome> RunLiveAsync(CancellationToken ct)
    {
        IReadOnlyList<(string Sport, DateOnly Day)> leagueDays;

        await using (var scope = scopeFactory.CreateAsyncScope())
            leagueDays = await LeagueDaysInPlayAsync(
                scope.ServiceProvider.GetRequiredService<LineOpsDbContext>(), DateTimeOffset.UtcNow, ct);

        if (leagueDays.Count == 0)
            return new JobOutcome(EspnLive, true, 0, 0, "No game in play.");

        var rows = 0;
        var failures = 0;

        foreach (var (sportKey, day) in leagueDays)
        {
            if (ct.IsCancellationRequested)
                break;

            // A scope per league-day, as the slate does per sport: the change tracker stays small.
            await using var scope = scopeFactory.CreateAsyncScope();
            var registry = scope.ServiceProvider.GetRequiredService<SourceRegistry>();
            var stats = scope.ServiceProvider.GetRequiredService<StatsIngestionService>();

            foreach (var source in registry.StatsSources)
            {
                var outcome = await stats.IngestLiveAsync(source, sportKey, EspnLive, day, ct);
                rows += outcome.RowsIngested;

                if (outcome.Status == RunStatus.Failed)
                    failures++;
            }
        }

        return new JobOutcome(EspnLive, failures == 0, rows, failures,
            $"{leagueDays.Count} {(leagueDays.Count == 1 ? "league-day" : "league-days")} in play, {rows:N0} rows.");
    }

    /// <summary>When MLB's schedule was last read. Probable pitchers do not change often, and one
    /// call per half hour is plenty however often the slate is pressed.</summary>
    private DateTimeOffset _spineSyncedAt = DateTimeOffset.MinValue;

    private static readonly TimeSpan SpineInterval = TimeSpan.FromMinutes(30);

    /// <summary>
    /// MLB's ids, doubleheader numbers and probable pitchers for today and tomorrow — tomorrow's
    /// starters are usually announced the day before. After ESPN's pass, because it only
    /// annotates games ESPN has created. Best effort: a failure here is logged and costs the
    /// annotations, never the slate.
    /// </summary>
    private async Task SyncSpineAsync(CancellationToken ct)
    {
        if (!_options.EffectiveSports.Contains("mlb", StringComparer.OrdinalIgnoreCase)
            || DateTimeOffset.UtcNow - _spineSyncedAt < SpineInterval)
            return;

        await using var scope = scopeFactory.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<MlbSpineService>() is not { } spine)
            return;

        _spineSyncedAt = DateTimeOffset.UtcNow;
        var today = LeagueClock.Today();

        foreach (var day in new[] { today, today.AddDays(1) })
        {
            try
            {
                var outcome = await spine.SyncAsync(day, ct);
                logger.LogInformation("MLB spine {Date}: {Matched}/{Scheduled} matched, {Probables} with probables",
                    day, outcome.Matched, outcome.Scheduled, outcome.Probables);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "MLB spine {Date} failed; the slate is unaffected", day);
            }
        }
    }

    /// <summary>When the injury report was last read. A club's word on a player changes a few
    /// times a week; one request per half hour is plenty however often the slate is pressed.</summary>
    private DateTimeOffset _injuriesSyncedAt = DateTimeOffset.MinValue;

    private static readonly TimeSpan InjuriesInterval = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The league injury report for each configured league that has one, as its own run
    /// (<see cref="InjuryReportService.JobKey"/>). Best effort: ESPN down costs the statuses,
    /// never the slate.
    /// </summary>
    private async Task SyncInjuriesAsync(CancellationToken ct)
    {
        // The leagues with a report worth reading are the adapter's to say (EspnStatsAdapter.InjuryLeagues):
        // the NFL, whose starter the Board infers from a box score and so needs telling when the
        // club rules him out. MLB names its probable pitchers and a fight's card is its fighters.
        var sports = _options.EffectiveSports.Where(Adapters.EspnStatsAdapter.InjuryLeagues.Contains).ToList();

        if (sports.Count == 0 || DateTimeOffset.UtcNow - _injuriesSyncedAt < InjuriesInterval)
            return;

        foreach (var sport in sports)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var injuries = scope.ServiceProvider.GetRequiredService<InjuryReportService>();
                var outcome = await injuries.RefreshAsync(sport, InjuryReportService.JobKey, ct);

                logger.LogInformation("Injury report {Sport}: {Status}, {Rows} players written",
                    sport, outcome.Status, outcome.RowsIngested);

                // The half hour starts from a report that was read, not from an attempt: a press
                // after a Failed run row asks ESPN again rather than waiting it out.
                if (outcome.Status != RunStatus.Failed)
                    _injuriesSyncedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Injury report {Sport} failed; the slate is unaffected", sport);
            }
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
        var now = DateTimeOffset.UtcNow;
        var owed = await DatesAwaitingResultsAsync(_options.GamePasses.ResultsAfterStart, ct);

        // Nothing outstanding still means yesterday, so pressing the button by hand does
        // something sensible on a desk that is already up to date.
        if (owed.Count == 0)
            owed = [LeagueClock.Today().AddDays(-1)];

        // A date that has been swept and is still owed is one a sweep does not heal — a game
        // ESPN never lists as final, or one whose event has moved to another date. Walking it
        // every retry re-fetched every box score on that date, tens of thousands of requests
        // across the lookback from one row. Each fruitless sweep doubles the wait before the
        // next, up to a day, and a date that heals leaves the table.
        var due = owed.Where(d => !_sweptDates.TryGetValue(d, out var s) || now >= s.NextTry).ToList();

        var rows = 0;
        var failures = 0;

        foreach (var date in due)
        {
            if (ct.IsCancellationRequested)
                break;

            var outcome = await RunStatsAsync(EspnResults, date, schedulesOnly: false, ct);

            rows += outcome.Rows;
            failures += outcome.Failures;
        }

        var stillOwed = (await DatesAwaitingResultsAsync(_options.GamePasses.ResultsAfterStart, ct)).ToHashSet();

        foreach (var date in due)
        {
            if (!stillOwed.Contains(date))
            {
                _sweptDates.TryRemove(date, out _);
                continue;
            }

            var attempts = _sweptDates.TryGetValue(date, out var s) ? s.Attempts + 1 : 1;
            var wait = TimeSpan.FromTicks(Math.Min(
                _options.GamePasses.ResultsRetry.Ticks * (1L << Math.Min(attempts, 8)),
                TimeSpan.FromDays(1).Ticks));

            _sweptDates[date] = (attempts, DateTimeOffset.UtcNow + wait);
        }

        var deferred = owed.Count - due.Count;

        return new JobOutcome(
            EspnResults, failures == 0, rows, failures,
            $"{due.Count} {(due.Count == 1 ? "day" : "days")} swept, {rows:N0} rows"
            + (deferred > 0 ? $", {deferred} still owed and backing off." : "."));
    }

    /// <summary>Dates swept without healing, and when each is worth trying again.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<DateOnly, (int Attempts, DateTimeOffset NextTry)> _sweptDates = new();

    /// <summary>
    /// Every configured sport's finals owed a close (<see cref="StatsIngestionService.OwedMarket"/>),
    /// asked of each stats source that can state one — less the games a source has set aside
    /// until their retry date. Each sport's run row says what it asked; the roll-up keeps "without
    /// a close" apart from "asked", because 523 owed and 3 asked is the difference between a
    /// two-minute press and a one-second one. Settles afterwards: an entry waiting on its close for
    /// CLV gets it now rather than at the next settle.
    /// </summary>
    private async Task<JobOutcome> RunClosingLinesAsync(CancellationToken ct)
    {
        var rows = 0;
        var failures = 0;
        var asked = 0;

        OwedCloses before;
        await using (var scope = scopeFactory.CreateAsyncScope())
            before = await OwedClosesAsync(scope.ServiceProvider, _options.EffectiveSports, ct);

        foreach (var sportKey in _options.EffectiveSports)
        {
            if (ct.IsCancellationRequested)
                break;

            await using var scope = scopeFactory.CreateAsyncScope();
            var registry = scope.ServiceProvider.GetRequiredService<SourceRegistry>();
            var stats = scope.ServiceProvider.GetRequiredService<StatsIngestionService>();
            var db = scope.ServiceProvider.GetRequiredService<LineOpsDbContext>();

            foreach (var source in registry.StatsSources.OfType<Core.Contracts.IClosingLineSource>())
            {
                var outcome = await stats.RefillClosingLinesAsync(source, sportKey, EspnClosingLines, ClosingLinesPerRun, ct);
                rows += outcome.RowsIngested;
                asked += await db.IngestionRuns.Where(r => r.Id == outcome.RunId).Select(r => r.RequestsMade).FirstOrDefaultAsync(ct);

                if (outcome.Status == RunStatus.Failed)
                    failures++;
            }
        }

        if (rows > 0)
            await RunSettleAsync(ct);

        OwedCloses after;
        await using (var scope = scopeFactory.CreateAsyncScope())
            after = await OwedClosesAsync(scope.ServiceProvider, _options.EffectiveSports, ct);

        return new JobOutcome(EspnClosingLines, failures == 0, rows, failures,
            ClosingLinesSentence(rows, asked, before.WithoutClose - after.WithoutClose, after));
    }

    /// <summary>
    /// "0 closing lines written; 0 of 3 games asked got a close; 520 set aside until their retry date."
    /// </summary>
    internal static string ClosingLinesSentence(int rows, int asked, int closed, OwedCloses after)
    {
        if (asked == 0 && after.WithoutClose == 0)
            return $"{rows:N0} closing lines written; no game is owed a close.";

        return $"{rows:N0} closing lines written; {Math.Max(0, closed):N0} of {asked:N0} games asked got a close"
               + (after.SetAside > 0 ? $"; {after.SetAside:N0} set aside until their retry date" : "")
               + (after.ToAsk > 0 ? $"; {after.ToAsk:N0} still to ask" : "")
               + ".";
    }

    /// <summary>
    /// The closing-line refill as the menu offers it: the estimate counts only the games a press
    /// will ask, and the description says how many are without a close and how many of those
    /// that is — "523 without a close · 3 to ask".
    /// </summary>
    private static async Task<IngestionJob> ClosingLinesJobAsync(
        IServiceProvider provider, IReadOnlyList<string> sports, int closers, CancellationToken ct)
    {
        var owed = closers > 0 ? await OwedClosesAsync(provider, sports, ct) : new OwedCloses(0, 0);

        return new IngestionJob(
            Key: EspnClosingLines,
            Label: "Closing lines owed",
            Description: "ESPN's closing moneyline, spread and total for finished games without the close they are owed "
                         + "(a fight's moneyline, a game's total), one request per game. Where ESPN has no line, none is "
                         + "written, and the game is set aside — two weeks the first time, longer each time after — before it is asked again."
                         + (closers > 0 ? $" {owed.WithoutClose:N0} without a close · {owed.ToAsk:N0} to ask." : ""),
            EstimatedRequests: owed.ToAsk,
            Available: closers > 0,
            Unavailable: closers > 0 ? null : "No stats source states closing lines.");
    }

    /// <summary>
    /// Profiles for the fighters owed one, in every configured fought sport (ADR 0021). A fighter
    /// ESPN cannot describe is retried on the next press; the rest are not held up by it.
    /// </summary>
    private async Task<JobOutcome> RunFightersAsync(int limit, CancellationToken ct)
    {
        var rows = 0;
        var failures = 0;
        var stillOwed = 0;

        foreach (var sportKey in _options.EffectiveSports.Where(FightSports.IsFight))
        {
            if (ct.IsCancellationRequested)
                break;

            await using var scope = scopeFactory.CreateAsyncScope();
            var fighters = scope.ServiceProvider.GetRequiredService<FighterProfileService>();

            var outcome = await fighters.RefreshAsync(sportKey, EspnFighters, limit, ct);
            rows += outcome.RowsIngested;

            if (outcome.Status == RunStatus.Failed)
                failures++;

            stillOwed += await fighters.OwedCountAsync(sportKey, ct);
        }

        return new JobOutcome(EspnFighters, failures == 0, rows, failures,
            $"{rows:N0} fighter {(rows == 1 ? "profile" : "profiles")} taken"
            + (stillOwed > 0 ? $"; {stillOwed:N0} still owed." : "."));
    }

    /// <summary>
    /// One batch of the gamecast backport. One batch, not the whole debt, so the scheduler's tick
    /// is never held for hours; the outcome says whether more is owed, which is what
    /// <c>--until-done</c> on the Worker repeats on.
    /// </summary>
    private async Task<JobOutcome> RunGamecastBackportAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var backport = scope.ServiceProvider.GetRequiredService<GamecastBackport>();

        var outcome = await backport.RunAsync(DateTimeOffset.UtcNow, ct);

        // A game with no plays is set aside, not failed; only a batch that came away with none
        // because every request threw is.
        return new JobOutcome(EspnGamecastBackport, outcome.Status != RunStatus.Failed, outcome.Rows,
            outcome.Status == RunStatus.Failed ? outcome.Misses : 0, outcome.Detail,
            More: outcome.RunId != 0 && outcome.StillOwed > 0);
    }

    /// <summary>
    /// Finals without the close they are owed, and of those the ones a press would ask each
    /// closing-line source about now — not set aside, and within a run's ceiling.
    /// </summary>
    private static async Task<OwedCloses> OwedClosesAsync(IServiceProvider provider, IReadOnlyList<string> sports, CancellationToken ct)
    {
        var stats = provider.GetRequiredService<StatsIngestionService>();
        var closers = provider.GetRequiredService<SourceRegistry>().StatsSources
            .OfType<Core.Contracts.IClosingLineSource>()
            .Select(s => s.Key)
            .ToList();
        var now = DateTimeOffset.UtcNow;
        var without = 0;
        var toAsk = 0;
        var setAside = 0;

        foreach (var sport in sports)
        {
            without += await stats.ClosingLinesOwed(sport).CountAsync(ct);

            foreach (var closer in closers)
            {
                toAsk += await stats.ClosingLinesToAskAsync(sport, closer, now, ClosingLinesPerRun, ct);
                setAside += await stats.ClosingLinesSetAsideAsync(sport, closer, now, ct);
            }
        }

        // Set aside is counted from the misses, not derived: "owed less askable" also took in
        // finals the source has no id for, which wait on no retry, and a run's ceiling, which is
        // not a setting-aside — 700 owed and none set aside is 600 to ask now and 100 after.
        return new OwedCloses(without, toAsk, setAside);
    }

    /// <summary>
    /// Finals without the close they are owed; how many of them a press would ask now (within a
    /// run's ceiling); and how many are set aside until their retry date.
    /// </summary>
    internal readonly record struct OwedCloses(int WithoutClose, int ToAsk, int SetAside = 0);

    private async Task<JobOutcome> RunSettleAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var settlement = scope.ServiceProvider.GetRequiredService<SettlementService>();

        var summary = await settlement.SettleAsync(ct);

        return new JobOutcome(Settle, true, summary.Graded + summary.ClvResolved, 0,
            $"{summary.Graded} graded, {summary.Voided} voided, {summary.ParlaysSettled} parlays, {summary.ClvResolved} CLV resolved, {summary.LeftPending} pending.");
    }

    /// <summary>
    /// Days we are still owed results for: a game that has started and is not final here, or a
    /// final in a sport we ingest that has no box score.
    ///
    /// <para>
    /// The second half is the one that was missing. The live poll reads the scoreboard, which
    /// carries the score and the final status but not the box score; a game it saw go final was
    /// never owed again, so the results sweep never fetched its stats. All fifteen games of
    /// 22 September 2026 sat final without a stat line until this — the data-quality alert
    /// named them and nothing healed them. A final ESPN never gives a box score for is retried on
    /// the sweep's own back-off, not every tick.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<DateOnly>> DatesAwaitingResultsAsync(
        TimeSpan settleAfter, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LineOpsDbContext>();

        var cutoff = DateTimeOffset.UtcNow - settleAfter;
        var sports = _options.EffectiveSports;

        var statsOwed = StatsOwed(db, sports);

        var starts = await db.Games
            .Where(g => g.StartsAt <= cutoff
                        && g.StartsAt >= cutoff - _options.GamePasses.ResultsLookback
                        && g.Status != GameStatus.Postponed
                        && (g.Status != GameStatus.Final || statsOwed.Any(o => o.Id == g.Id)))
            .Select(g => g.StartsAt)
            .ToListAsync(ct);

        return starts
            .Select(LeagueClock.DateOf)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
    }

    /// <summary>
    /// Finals in a configured league still owed their stats: no stat line from a run other than
    /// the live box's (<see cref="EspnGamecastLive"/>). A mid-game box is not a final's, so a game
    /// whose final box never came stays owed until the sweep's or the feed's final write lands —
    /// and that write re-stamps every line it touches, unchanged ones included, so it settles the
    /// debt (<see cref="StatsIngestionService"/>). Any window is the caller's.
    /// </summary>
    public static IQueryable<Game> StatsOwed(LineOpsDbContext db, IReadOnlyList<string> sports)
        => db.Games.Where(g => g.Status == GameStatus.Final
                               && sports.Contains(g.Sport!.Key)
                               && !db.PlayerGameStats.Any(s => s.GameId == g.Id
                                   && !db.IngestionRuns.Any(r => r.Id == s.IngestionRunId
                                                                 && r.JobKey == EspnGamecastLive)));
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

/// <summary>What one run of a job did.</summary>
/// <param name="More">
/// The job left work a further run would do — a backport taken a batch at a time. False for a job
/// that does all it can in one run.
/// </param>
public record JobOutcome(string Key, bool Succeeded, int Rows, int Failures, string? Detail, bool More = false);
