using System.Globalization;
using LineOps.Core.Analytics;
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
/// A league's games over a range of days the operator names — NFL 1–30 September 2024, MLB
/// April 2025 — with each final's box score and its closing line (<see cref="IngestionJobs.EspnRange"/>).
///
/// <para>
/// For the history the backfill seasons do not reach, and for re-pulling a range that came in
/// incomplete. The backfill walks a configured season day by day; this walks the days named, and
/// only the ones with games: the provider's calendar says which (<see cref="IGameDaySource"/>),
/// one request a season, where the scoreboard alone would be asked about every empty Tuesday.
/// </para>
///
/// <para>
/// Each day goes through the passes the scheduler already runs (<see cref="StatsIngestionService"/>):
/// a day gone by through the box-score pass — games, finals, box scores and the close from the
/// summary — and today and after through the schedule pass, since a game not yet played has
/// nothing to summarise. A day already complete is left alone unless a re-pull is asked for:
/// complete is every game on it over, and every final holding its stats and a closing total, by
/// the same predicates the results sweep and the closing-line backport are owed by.
/// </para>
///
/// <para>
/// One range at a time on a host, and at most <see cref="MaxDays"/> days a range — a season. The
/// provider is a courtesy, and two walks at once, or a decade in one press, is the shape of
/// request that courtesy does not stretch to.
/// </para>
/// </summary>
public class RangePull(
    IServiceScopeFactory scopeFactory,
    IOptions<IngestionOptions> options,
    ILogger<RangePull> logger,
    TimeProvider? clock = null)
{
    /// <summary>The widest range one pull takes: about a season, an MLB one being the longest.</summary>
    public const int MaxDays = 200;

    /// <summary>How the Worker is asked for a range, for every place that has to say so.</summary>
    public const string Usage =
        "A range pull takes a league and two dates: --job espn:range --sport nfl --from 2024-09-01 --to 2024-09-30 "
        + "[--refresh true]. Dates are league days (Eastern), inclusive, at most 200 days apart.";

    private readonly IngestionOptions _options = options.Value;

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// What is wrong with a range as asked, or null when nothing is: a league named, the dates in
    /// order and no more than <see cref="MaxDays"/> of them. The configured leagues are the
    /// service's to check; this is the shape every caller can check before asking.
    /// </summary>
    public static string? Check(string? sport, DateOnly? from, DateOnly? to)
    {
        if (string.IsNullOrWhiteSpace(sport))
            return "Name a league.";

        if (from is not { } first || to is not { } last)
            return "Give a from date and a to date.";

        if (first > last)
            return $"From ({first:yyyy-MM-dd}) is after To ({last:yyyy-MM-dd}).";

        var days = last.DayNumber - first.DayNumber + 1;

        return days > MaxDays
            ? $"That range is {days} days; a pull takes at most {MaxDays}, about a season. Split it."
            : null;
    }

    /// <summary>
    /// The leagues a range can be pulled for, as the desk's pickers offer them: every enabled sport
    /// (<c>Sports.Enabled</c>) the ESPN port can address, in name order.
    /// </summary>
    public async Task<IReadOnlyList<RangeLeague>> LeaguesAsync(CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LineOpsDbContext>();

        return (await db.Sports.AsNoTracking().Where(s => s.Enabled).OrderBy(s => s.Name).ToListAsync(ct))
            .Where(s => Adapters.EspnLeagues.Supports(s.Key))
            .Select(s => new RangeLeague(s.Key, s.Name))
            .ToList();
    }

    /// <summary>
    /// Walks the range and says what it did. A refusal — a range <see cref="Check"/> rejects, a
    /// league not configured, a pull already running — is returned before any request is made.
    /// </summary>
    public async Task<RangePullSummary> RunAsync(
        RangePullRequest request, IProgress<RangePullProgress>? progress, CancellationToken ct)
    {
        var sport = _options.EffectiveSports.FirstOrDefault(
            s => string.Equals(s, request.Sport?.Trim(), StringComparison.OrdinalIgnoreCase));

        var refusal = Check(request.Sport, request.From, request.To)
                      ?? (sport is null ? $"'{request.Sport}' is not a configured league." : null);

        if (refusal is not null)
            return RangePullSummary.Refusal(request, refusal);

        // Single-flight: a second press waits for nothing, it is told.
        if (!await _gate.WaitAsync(0, ct))
            return RangePullSummary.Refusal(request, "A range pull is already running on this host.");

        try
        {
            return await WalkAsync(request with { Sport = sport! }, progress, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<RangePullSummary> WalkAsync(
        RangePullRequest request, IProgress<RangePullProgress>? progress, CancellationToken ct)
    {
        var (sport, from, to, refresh) = request;
        var today = LeagueClock.Today(clock);
        var summary = RangePullSummary.Start(request);

        List<IStatsSource> sources;

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            sources = scope.ServiceProvider.GetRequiredService<SourceRegistry>().StatsSources.ToList();

            // A range reaches months the live schedule has never touched, as a backfill does.
            if (scope.ServiceProvider.GetService<DatabaseInitializer>() is { } initialiser)
                await initialiser.EnsurePartitionsAsync(ct);
        }

        foreach (var source in sources)
        {
            var calendar = source is IGameDaySource days
                ? await days.FetchGameDaysAsync(sport, from, to, ct)
                : new GameDays(Every(from, to), false, new FetchCost(0));

            summary = summary with
            {
                GameDays = Math.Max(summary.GameDays, calendar.Days.Count),
                FromCalendar = calendar.FromCalendar,
                Requests = summary.Requests + calendar.Cost.Requests
            };

            var done = 0;

            foreach (var day in calendar.Days)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new RangePullProgress(sport, day, done, calendar.Days.Count, summary.DaysWalked, summary.DaysSkipped));
                done++;

                // A scope per day, as the backfill keeps one: the change tracker stays the day's size.
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<LineOpsDbContext>();
                var stats = scope.ServiceProvider.GetRequiredService<StatsIngestionService>();

                if (!refresh && await IsCompleteAsync(db, stats, sport, day, ct))
                {
                    summary = summary with { DaysSkipped = summary.DaysSkipped + 1 };
                    continue;
                }

                var closesBefore = await ClosesOnAsync(db, sport, day, ct);

                var outcome = await stats.IngestRangeDayAsync(
                    source, sport, IngestionJobs.EspnRange, day, schedulesOnly: day >= today, ct);

                var run = outcome.RunId == 0
                    ? null
                    : await db.IngestionRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == outcome.RunId, ct);

                var statRows = run is null ? 0 : await db.PlayerGameStats.CountAsync(s => s.IngestionRunId == run.Id, ct);
                var held = OnDay(db, sport, day);

                summary = summary with
                {
                    DaysWalked = summary.DaysWalked + 1,
                    Games = summary.Games + await held.CountAsync(ct),
                    Finals = summary.Finals + await held.CountAsync(g => g.Status == GameStatus.Final, ct),
                    StatRows = summary.StatRows + statRows,
                    ClosingLines = summary.ClosingLines + await ClosesOnAsync(db, sport, day, ct) - closesBefore,
                    Requests = summary.Requests + (run?.RequestsMade ?? 0),
                    Failures = summary.Failures + (outcome.Status == RunStatus.Failed ? 1 : 0)
                };

                // The gap between days, as the backfill leaves one; the adapter spaces its own calls too.
                if (_options.Backfill.RequestDelay > TimeSpan.Zero)
                    await Task.Delay(_options.Backfill.RequestDelay, ct);
            }
        }

        logger.LogInformation("Range pull: {Summary}", summary.Sentence);
        return summary;
    }

    /// <summary>
    /// Whether a league day is held complete: it has games, none still to be played or in play,
    /// and no final on it owed its stats (<see cref="IngestionJobs.StatsOwed"/>, where a line from
    /// the live box does not count) or its close (<see cref="StatsIngestionService.ClosingLinesOwed"/>).
    /// A day with no game held is not complete — it is a day nobody has pulled.
    /// </summary>
    public static async Task<bool> IsCompleteAsync(
        LineOpsDbContext db, StatsIngestionService stats, string sport, DateOnly day, CancellationToken ct)
    {
        var games = OnDay(db, sport, day);

        if (!await games.AnyAsync(ct))
            return false;

        if (await games.AnyAsync(g => g.Status != GameStatus.Final && g.Status != GameStatus.Postponed, ct))
            return false;

        var statsOwed = IngestionJobs.StatsOwed(db, [sport]);
        var closesOwed = stats.ClosingLinesOwed(sport);

        return !await games.AnyAsync(g => statsOwed.Any(o => o.Id == g.Id) || closesOwed.Any(o => o.Id == g.Id), ct);
    }

    /// <summary>A league's games on a league day: from its Eastern midnight to the next.</summary>
    private static IQueryable<Game> OnDay(LineOpsDbContext db, string sport, DateOnly day)
    {
        var start = LeagueClock.StartOf(day);
        var end = LeagueClock.StartOf(day.AddDays(1));

        return db.Games.Where(g => g.Sport!.Key == sport && g.StartsAt >= start && g.StartsAt < end);
    }

    private static Task<int> ClosesOnAsync(LineOpsDbContext db, string sport, DateOnly day, CancellationToken ct)
    {
        var games = OnDay(db, sport, day);
        return db.ClosingLines.CountAsync(c => games.Any(g => g.Id == c.GameId), ct);
    }

    private static List<DateOnly> Every(DateOnly from, DateOnly to)
        => Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays).ToList();
}

/// <summary>A league a range can be pulled for: its sport key, and the name the desk shows.</summary>
public sealed record RangeLeague(string Key, string Name);

/// <summary>A range the operator names: one league, its days from and to, inclusive.</summary>
/// <param name="Refresh">Pull the days already complete as well, rather than leaving them alone.</param>
public sealed record RangePullRequest(string Sport, DateOnly From, DateOnly To, bool Refresh = false)
{
    /// <summary>
    /// A range from the Worker's arguments — <c>--sport nfl --from 2024-09-01 --to 2024-09-30
    /// [--refresh true]</c> — or the reason it is not one. Dates are ISO (yyyy-MM-dd); the shape is
    /// then held to <see cref="RangePull.Check"/>.
    /// </summary>
    public static bool TryParse(
        string? sport, string? from, string? to, string? refresh, out RangePullRequest? request, out string? error)
    {
        request = null;

        if (!TryDate(from, out var first))
        {
            error = from is null ? "--from is missing." : $"--from '{from}' is not a date (yyyy-MM-dd).";
            return false;
        }

        if (!TryDate(to, out var last))
        {
            error = to is null ? "--to is missing." : $"--to '{to}' is not a date (yyyy-MM-dd).";
            return false;
        }

        var again = false;

        if (refresh is not null && !bool.TryParse(refresh, out again))
        {
            error = $"--refresh '{refresh}' is not true or false.";
            return false;
        }

        error = RangePull.Check(sport, first, last);

        if (error is not null)
            return false;

        request = new RangePullRequest(sport!.Trim(), first, last, again);
        return true;
    }

    private static bool TryDate(string? raw, out DateOnly date)
    {
        date = default;
        return raw is not null
               && DateOnly.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}

/// <summary>Where a range pull has got to: the day it is on, and how many of the game days are behind it.</summary>
public sealed record RangePullProgress(string Sport, DateOnly Day, int Done, int Of, int Walked, int Skipped);

/// <summary>What a range pull did, or why it did nothing.</summary>
/// <param name="GameDays">The days in the range the calendar says have games — every day, where it could not say.</param>
/// <param name="StatRows">Stat lines written or changed, on the runs this pull recorded.</param>
/// <param name="ClosingLines">Closing lines written: a close already held is never rewritten.</param>
/// <param name="Refused">Why nothing was walked, or null for a pull that ran.</param>
public sealed record RangePullSummary(
    string Sport,
    DateOnly From,
    DateOnly To,
    int DaysInRange,
    int GameDays,
    bool FromCalendar,
    int DaysWalked,
    int DaysSkipped,
    int Games,
    int Finals,
    int StatRows,
    int ClosingLines,
    int Requests,
    int Failures,
    string? Refused)
{
    internal static RangePullSummary Start(RangePullRequest r)
        => new(r.Sport, r.From, r.To, Math.Max(0, r.To.DayNumber - r.From.DayNumber + 1), 0, false, 0, 0, 0, 0, 0, 0, 0, 0, null);

    internal static RangePullSummary Refusal(RangePullRequest r, string reason) => Start(r) with { Refused = reason };

    /// <summary>The summary as one sentence, for the Worker's console and the desk's toast.</summary>
    public string Sentence
        => Refused is not null
            ? Refused
            : $"{Sport.ToUpperInvariant()} {From:yyyy-MM-dd} to {To:yyyy-MM-dd}: "
              + $"{GameDays} of {DaysInRange} days with games{(FromCalendar ? "" : " (no calendar; every day asked)")}, "
              + $"{DaysWalked} walked, {DaysSkipped} already complete; "
              + $"{Games:N0} games, {Finals:N0} finals, {StatRows:N0} stat lines, {ClosingLines:N0} closing lines; "
              + $"{Requests:N0} requests"
              + (Failures > 0 ? $"; {Failures} {(Failures == 1 ? "day" : "days")} failed." : ".");

    /// <summary>As the catalogue's runs report: succeeded unless refused or a day failed.</summary>
    public JobOutcome ToOutcome()
        => new(IngestionJobs.EspnRange, Refused is null && Failures == 0, Games + StatRows + ClosingLines,
            Failures, Sentence);
}
