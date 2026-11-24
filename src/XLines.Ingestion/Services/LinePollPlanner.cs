using XLines.Core.Entities;
using XLines.Data;
using XLines.Ingestion.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace XLines.Ingestion.Services;

/// <summary>
/// Decides how often to scan for line movement, from the budget rather than the clock.
///
/// <para>
/// The cadence used to be a constant: every three hours. That number is either wasteful or
/// wrong for any given provider, and it cannot be both right at 500 requests a day and right
/// at 100 an hour — it simply ignores the ceiling and trusts <c>CreditBudgetGuard</c> to catch
/// an overrun. The guard refusing a run is a failure state, not a plan.
/// </para>
///
/// <para>
/// So the interval is derived: take what the provider allows in a day, subtract what has
/// already been spent, divide the remainder by the cost of one scan, and spread the scans
/// that are left across the hours that remain. The result is a cadence that uses the
/// allotment and cannot exceed it, and that speeds up or slows down on its own as the day's
/// usage changes.
/// </para>
///
/// <para>
/// Providers meter differently, and the window has to match: a request allowance is spread over
/// the day, a <b>credit</b> allowance over the month. The Odds API publishes only the second —
/// 500 credits a month, no daily or hourly cap — so a planner that understood requests alone
/// read it as unmetered and fell to the floor, which spends the month in a day.
/// </para>
///
/// <para>
/// Two things bound it at the edges. A <b>reserve</b> is held back so a manual pull, a retry
/// or a second sport coming into season never finds the quota already spent to the last
/// request. And a <b>floor</b> stops the maths asking for a scan every few seconds on a
/// generous tier, because line movement does not repay polling faster than a book updates.
/// </para>
/// </summary>
public class LinePollPlanner(
    XLinesDbContext db,
    IOptions<IngestionOptions> options,
    ILogger<LinePollPlanner> logger,
    SourceRegistry? registry = null)
{
    private readonly IngestionOptions _options = options.Value;

    /// <summary>The leagues a scan prices (<see cref="IngestionOptions.PricedSportsWith"/>): all of them with ESPN on.</summary>
    private string[] Priced => registry is null ? _options.EffectiveSports : _options.PricedSportsWith(registry.Espn is not null);

    /// <summary>
    /// What one scan of the given leagues bills a source, in credits. The adapter's own
    /// forecast where it gives one — it knows its market list and its provider's rule — and the
    /// configured per-sport figure only for a source that declares nothing.
    /// </summary>
    private int CreditsPerScan(string sourceKey, IReadOnlyCollection<string> leagues)
    {
        var declared = registry?.FindOdds(sourceKey) is { } source
            ? leagues.Sum(source.CreditsPerScan)
            : 0;

        return declared > 0
            ? declared
            : leagues.Count * _options.LinePolling.CreditsPerSportPerScan;
    }

    /// <summary>The markets each sport is scanned for, and what that costs, per source that bills credits.</summary>
    public IReadOnlyList<SportMarkets> MarketsBySport(IReadOnlyList<string> oddsSourceKeys)
        => Priced
            .Select(sport =>
            {
                var sources = oddsSourceKeys.Select(k => registry?.FindOdds(k)).OfType<Core.Contracts.IOddsSource>().ToList();
                var markets = sources.SelectMany(s => s.MarketsFor(sport)).Distinct().ToList();
                return new SportMarkets(sport, markets, sources.Sum(s => s.CreditsPerScan(sport)));
            })
            .ToList();

    /// <summary>
    /// The interval to wait before the next scan of each league, and the reasoning behind it.
    ///
    /// <para>
    /// Returns null when scanning is pointless — no odds source. A league with nothing starting
    /// inside the movement window is left out of <see cref="PollPlan.Leagues"/> and out of the
    /// price: an all-league scan used to bill every priced league whenever any one of them had a
    /// game near, so a Sunday's football paid for a baseball slate that was days away.
    /// </para>
    ///
    /// <para>
    /// The budget is paced against what the watchable leagues cost together, and each of them
    /// then gets its own urgency from its own next start. While nothing is watchable the plan
    /// still prices a scan of every priced league, so Ops can say what the next one will cost;
    /// nothing is scanned on it.
    /// </para>
    /// </summary>
    public async Task<PollPlan?> PlanAsync(IReadOnlyList<string> oddsSourceKeys, CancellationToken ct = default)
    {
        if (oddsSourceKeys.Count == 0)
            return null;

        var settings = _options.LinePolling;
        var now = DateTimeOffset.UtcNow;

        var nextStarts = await NextStartsAsync(now, ct);
        var watchable = Priced.Where(nextStarts.ContainsKey).ToArray();
        var costed = watchable.Length > 0 ? watchable : Priced;

        // Cost of one scan: two requests per sport per source, which is the whole point of the
        // batched /odds/multi call — it does not grow with the size of the slate.
        var perScan = Math.Max(1, oddsSourceKeys.Count * costed.Length * settings.RequestsPerSportPerScan);

        var sources = await db.Sources
            .Where(s => oddsSourceKeys.Contains(s.Key))
            .AsNoTracking()
            .ToListAsync(ct);

        // The tightest provider governs: scanning is one action across all of them, so the
        // cadence has to fit inside the smallest remaining allowance.
        TimeSpan? tightest = null;
        string? binding = null;
        var scansLeft = int.MaxValue;

        // Whether the number being reported is a day's worth or a month's. They are not
        // interchangeable on screen: "200 scans left" means something very different if the
        // allowance refills tomorrow than if it has to last until the first.
        var monthlyBound = false;

        foreach (var source in sources)
        {
            var plan = await PlanForAsync(source, perScan, costed, settings, now, ct);

            if (plan is null)
                continue;

            if (tightest is null || plan.Value.Interval > tightest)
            {
                tightest = plan.Value.Interval;
                binding = source.Key;
                monthlyBound = plan.Value.Monthly;
            }

            scansLeft = Math.Min(scansLeft, plan.Value.ScansRemaining);
        }

        // Unmetered sources declare no ceiling, so there is nothing to pace against; the floor
        // is then the only thing deciding, which is correct — it is a politeness limit.
        var interval = tightest ?? settings.MinimumInterval;

        // Then spend it where it is worth something.
        //
        // An evenly spread cadence treats a game thirty hours out the same as one an hour from
        // first pitch, and they are not the same: the distant line barely moves, while the hours
        // after lineups are posted are where the market actually finds its number. Polling both
        // at one rate buys duplicate rows in the morning and misses the move in the afternoon.
        //
        // This is a redistribution rather than an increase. The budget maths above still sets the
        // baseline, and because it recomputes from credits *actually spent* every tick, going
        // faster near first pitch slows the rest on its own. The allowance cannot be exceeded by
        // being spent impatiently — only sooner.
        //
        // League by league: baseball an hour from first pitch and football two days out are on
        // one budget but not on one clock, so each league's interval is the budgeted one stretched
        // or compressed by its own next start.
        var budgeted = Clamp(interval, settings);

        var leagues = watchable
            .Select(sport =>
            {
                var start = nextStarts[sport];
                var urgency = LinePacing.Urgency(start - now);

                return new LeaguePollPlan(
                    Sport: sport,
                    Interval: Clamp(interval * urgency, settings),
                    Urgency: urgency,
                    CreditsPerScan: oddsSourceKeys.Sum(k => CreditsPerScan(k, [sport])),
                    NextStart: start);
            })
            .OrderBy(l => l.NextStart)
            .ToList();

        // The headline is the soonest league's, the one the scheduler will scan next.
        var soonest = leagues.FirstOrDefault();

        return new PollPlan(
            Interval: soonest?.Interval ?? budgeted,
            CostPerScan: perScan,
            CreditsPerScan: oddsSourceKeys.Sum(k => CreditsPerScan(k, costed)),
            ScansRemainingToday: scansLeft == int.MaxValue ? null : scansLeft,
            BoundBy: binding,
            Window: monthlyBound ? BudgetWindow.Month : BudgetWindow.Day,
            BudgetInterval: budgeted,
            Urgency: soonest?.Urgency ?? 1)
        {
            Leagues = leagues
        };
    }

    /// <summary>
    /// Each priced league's next start inside the movement window, by its configured spelling. A
    /// league with nothing in the window is absent: its price is not moving, so it is not watched.
    /// </summary>
    private async Task<Dictionary<string, DateTimeOffset>> NextStartsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var horizon = now + _options.MovementWindow;
        var sports = Priced;

        var starts = await db.Games
            .Where(g => g.StartsAt > now && g.StartsAt <= horizon)
            .Where(g => g.Status != GameStatus.Final && g.Status != GameStatus.Postponed)
            .Where(g => sports.Contains(g.Sport!.Key))
            .GroupBy(g => g.Sport!.Key)
            .Select(g => new { Sport = g.Key, Next = g.Min(x => x.StartsAt) })
            .ToListAsync(ct);

        return starts.ToDictionary(
            s => sports.First(k => string.Equals(k, s.Sport, StringComparison.OrdinalIgnoreCase)),
            s => s.Next,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Each priced league's latest line scan on record: its own job's or the all-league pull's.
    ///
    /// <para>
    /// Read from the runs table rather than remembered, so a restart picks up where the last host
    /// left off. The scheduler used to start with no last scan at all, which made the first tick
    /// after every restart a paid scan of every league — a debugging session's worth of restarts
    /// was a debugging session's worth of credits. A manual pull counts too: it spent the same
    /// pool, and the price it took is as fresh as one the scheduler would have.
    /// </para>
    /// </summary>
    public async Task<Dictionary<string, DateTimeOffset>> LastScansAsync(CancellationToken ct = default)
    {
        var leagues = Priced;
        var keys = leagues.Select(s => IngestionJobs.OddsLinesPrefix + s).Append(IngestionJobs.OddsLines).ToList();

        var latest = await db.IngestionRuns
            .Where(r => keys.Contains(r.JobKey))
            .GroupBy(r => r.JobKey)
            .Select(g => new { JobKey = g.Key, Latest = g.Max(r => r.StartedAt) })
            .ToListAsync(ct);

        return LinePacing.LastScans(leagues, latest.ToDictionary(r => r.JobKey, r => r.Latest, StringComparer.Ordinal));
    }

    /// <summary>
    /// The leagues owed a closing scan now (<see cref="ClosingScanOptions"/>): a start inside the
    /// lead with no scan of its league since the lead began, a league at most once each, and none
    /// past its day's cap. One scan prices every game in the league, so a cluster of starts inside
    /// the lead is covered by one; a later start that enters the lead after it is owed its own,
    /// which is what the cap is for.
    ///
    /// <para>
    /// The cap is counted from the runs table (<see cref="ClosingScansTakenAsync"/>), not held in
    /// memory, so a restart does not start the day's count again and the Web and Worker hosts
    /// sharing the database share one count.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<ClosingScan>> ClosingScansDueAsync(
        DateTimeOffset now,
        IReadOnlyDictionary<string, DateTimeOffset> lastScans,
        CancellationToken ct = default)
    {
        var closing = _options.LinePolling.ClosingScan;

        if (!closing.Enabled || closing.Lead <= TimeSpan.Zero || closing.MaxPerLeaguePerDay <= 0)
            return [];

        var horizon = now + closing.Lead;
        var sports = Priced;

        var games = db.Games
            .Where(g => g.StartsAt > now && g.StartsAt <= horizon)
            .Where(g => g.Status != GameStatus.Final && g.Status != GameStatus.Postponed)
            .Where(g => sports.Contains(g.Sport!.Key));

        // A parlay's legs are journal entries on their own games, so they are found the same way.
        if (closing.Scope == ClosingScanScope.Wagered)
            games = games.Where(g => db.JournalEntries.Any(e => e.GameId == g.Id && e.Result == EntryResult.Pending));

        var starts = await games
            .Select(g => new { Sport = g.Sport!.Key, g.StartsAt })
            .ToListAsync(ct);

        var due = new List<ClosingScan>();

        foreach (var league in starts.GroupBy(s => sports.First(k => string.Equals(k, s.Sport, StringComparison.OrdinalIgnoreCase))))
        {
            DateTimeOffset? last = lastScans.TryGetValue(league.Key, out var at) ? at : null;

            var owed = league
                .Select(s => s.StartsAt)
                .Order()
                .Where(start => LinePacing.ClosingScanDue(start, last, closing.Lead, now))
                .Cast<DateTimeOffset?>()
                .FirstOrDefault();

            if (owed is not { } start)
                continue;

            var day = Core.Analytics.LeagueClock.DateOf(start);

            if (await ClosingScansTakenAsync(league.Key, day, ct) >= closing.MaxPerLeaguePerDay)
            {
                logger.LogDebug("{Sport}: closing scan for {Start:t} skipped; {Max} already taken on {Day}",
                    league.Key, start, closing.MaxPerLeaguePerDay, day);
                continue;
            }

            due.Add(new ClosingScan(league.Key, day, start));
        }

        return due;
    }

    /// <summary>
    /// The closing scans a league has taken on a league day, read from the run rows they left
    /// (<see cref="IngestionRun.ClosingScanDay"/>).
    ///
    /// <para>
    /// Every row stamped counts, whatever its status: a scan that reached the provider and failed
    /// was still billed, and one another host is still running will be. A scan the budget guard
    /// refused before any call carries no stamp, so it does not use up the cap. One scan writes a
    /// row a source, so the count is the most any one source recorded, not the rows in total — a
    /// second odds source would otherwise halve the cap.
    /// </para>
    /// </summary>
    public async Task<int> ClosingScansTakenAsync(string sport, DateOnly leagueDay, CancellationToken ct = default)
    {
        var jobKey = IngestionJobs.OddsLinesPrefix + sport;

        var bySource = await db.IngestionRuns
            .Where(r => r.JobKey == jobKey && r.ClosingScanDay == leagueDay)
            .GroupBy(r => r.SourceId)
            .Select(g => g.Count())
            .ToListAsync(ct);

        return bySource.Count == 0 ? 0 : bySource.Max();
    }

    private static TimeSpan Clamp(TimeSpan interval, LinePollingOptions settings)
    {
        if (interval < settings.MinimumInterval)
            return settings.MinimumInterval;

        return interval > settings.MaximumInterval ? settings.MaximumInterval : interval;
    }

    /// <summary>
    /// How often one source can afford to be scanned for the rest of its budget window — the
    /// day for a request-metered provider, the month for a credit-metered one. Null when it
    /// declares no ceiling at all.
    /// </summary>
    /// <remarks>
    /// Gathers the facts — requests in the last day, credits gone this month, the scores set aside —
    /// and leaves the arithmetic to <see cref="LinePacing.Budget"/>. Each fact is read only where
    /// the source has the ceiling it feeds, as before. <paramref name="now"/> is the plan's own
    /// instant, so every source in one plan is measured against the same moment.
    /// </remarks>
    private async Task<SourceBudget?> PlanForAsync(
        Source source, int perScan, IReadOnlyCollection<string> leagues, LinePollingOptions settings,
        DateTimeOffset now, CancellationToken ct)
    {
        var requestsLastDay = source.RateLimitPerDay is null
            ? 0
            : await RunSpend.RequestsSinceAsync(db, source.Id, now.AddDays(-1), ct);

        int creditsSpent = 0, scoresForecast = 0, creditsPerScan = 1;

        if (source.MonthlyCreditBudget is not null)
        {
            // What the key has spent, not only what this database recorded: the laptop and the
            // desktop share the allowance, and only the provider sees both (MonthlyCredits).
            creditsSpent = await MonthlyCredits.SpentAsync(db, source, now, ct);
            creditsPerScan = Math.Max(1, CreditsPerScan(source.Key, leagues));
            scoresForecast = await ScoresForecastAsync(source, now, ct);
        }

        var budget = LinePacing.Budget(
            new BudgetFacts(
                DailyLimit: source.RateLimitPerDay,
                RequestsLastDay: requestsLastDay,
                HourlyLimit: source.RateLimitPerHour,
                RequestsPerScan: perScan,
                MonthlyCreditBudget: source.MonthlyCreditBudget,
                CreditsSpent: creditsSpent,
                ScoresForecast: scoresForecast,
                CreditsPerScan: creditsPerScan),
            settings, now);

        // Nothing left in the budget. Said here rather than in the arithmetic, which stays pure.
        if (budget is { ScansRemaining: 0 })
            logger.LogWarning("{Source}: line budget spent; backing off", source.Key);

        return budget;
    }

    /// <summary>
    /// What the default source's scores will spend (ADR 0026): the leagues with a game in the last
    /// three days or the coming week, at one call a league a day. Null where scores are not metered
    /// here — ESPN on, or no key — so a desk on ESPN plans exactly as it did.
    /// </summary>
    public Task<ScoresPlan?> ScoresPlanAsync(CancellationToken ct = default)
        => ScoresPlanAsync(DateTimeOffset.UtcNow, ct);

    /// <summary>The scores plan as of <paramref name="now"/>, so a lines plan that sets it aside reads one instant.</summary>
    public async Task<ScoresPlan?> ScoresPlanAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        if (registry?.Scores is not { } scores)
            return null;

        var monthEnd = MonthlyCredits.NextReset(now);
        var sports = _options.EffectiveSports.Where(scores.CoversScores).ToList();
        var from = now.AddDays(-scores.ScoresReachDays);
        var to = now.AddDays(7);

        var active = await db.Games
            .Where(g => sports.Contains(g.Sport!.Key) && g.StartsAt >= from && g.StartsAt <= to)
            .Select(g => g.Sport!.Key)
            .Distinct()
            .ToListAsync(ct);

        var automatic = await db.LinesRunUnattendedAsync(_options.LinePolling.RunsUnattended, ct);
        var perDay = active.Count * scores.CreditsPerScoresCall;
        var daysLeft = Math.Max(1, (int)Math.Ceiling((monthEnd - now).TotalDays));

        return new ScoresPlan(active.Order(StringComparer.Ordinal).ToList(), scores.CreditsPerScoresCall, perDay, daysLeft, automatic);
    }

    /// <summary>The scores spend to the month's end, set aside from a source on the same account; zero otherwise.</summary>
    private async Task<int> ScoresForecastAsync(Source source, DateTimeOffset now, CancellationToken ct)
    {
        if (!Core.Entities.SourceAccounts.Sharing(source.Key).Contains(Core.Entities.SourceAccounts.TheOddsApiSchedule))
            return 0;

        return await ScoresPlanAsync(now, ct) is { Automatic: true } plan ? plan.ToMonthEnd : 0;
    }

    /// <summary>
    /// Whether a scan is worth making at all: something in a priced league has to be starting
    /// soon enough for its price to still be moving. A quiet slate should spend nothing. It used
    /// to ask of every game in the database, so a league nobody prices kept the scans going.
    /// </summary>
    public async Task<bool> HasWatchableGamesAsync(CancellationToken ct = default)
        => (await NextStartsAsync(DateTimeOffset.UtcNow, ct)).Count > 0;
}

/// <summary>The chosen cadence, and enough context to explain it on screen.</summary>
/// <param name="ScansRemainingToday">
/// Scans the allowance still affords. Read together with <paramref name="Window"/>: the same
/// number means something different depending on whether it refills tomorrow or has to last to
/// the first of the month.
/// </param>
/// <param name="BudgetInterval">
/// The cadence the allowance alone implies, before proximity to first pitch redistributes it.
/// Kept separate because the two answer different questions — this one is "what can be afforded",
/// <paramref name="Interval"/> is "when it is worth spending" — and only this one is pure
/// arithmetic over the budget.
/// </param>
/// <param name="CreditsPerScan">
/// What one scan bills, where the provider bills in credits. Distinct from
/// <paramref name="CostPerScan"/>, which counts HTTP calls: a single request can cost several
/// credits, and on this desk the two happen to be equal, which makes showing one in place of the
/// other look correct until a market or a sport is added.
/// </param>
/// <param name="Urgency">
/// The multiplier applied for how close the next start is. Below 1 means scanning faster than the
/// even spread, above 1 slower; 1 means nothing is close enough to matter either way.
/// </param>
/// <summary>One sport's scan: the markets asked for and the credits they bill.</summary>
public record SportMarkets(string Sport, IReadOnlyList<string> Markets, int Credits);

/// <summary>
/// The default source's scores against the month (ADR 0026): the leagues in play this week, the
/// price of one call, and what automatic scanning would spend on them a day and to the month's end.
/// </summary>
/// <param name="Automatic">Whether the operator chose automatic: only then does the scheduler spend any of it.</param>
public record ScoresPlan(IReadOnlyList<string> Leagues, int CreditsPerCall, int PerDay, int DaysLeft, bool Automatic)
{
    /// <summary>What automatic scores would spend from now to the month's end, at one call a league a day.</summary>
    public int ToMonthEnd => PerDay * DaysLeft;
}

public record PollPlan(
    TimeSpan Interval,
    int CostPerScan,
    int? ScansRemainingToday,
    string? BoundBy,
    BudgetWindow Window = BudgetWindow.Day,
    TimeSpan BudgetInterval = default,
    double Urgency = 1,
    int CreditsPerScan = 0)
{
    /// <summary>How to name the allowance in a sentence: "today's" or "this month's".</summary>
    public string WindowLabel => Window == BudgetWindow.Month ? "this month's" : "today's";

    /// <summary>
    /// The leagues with a game inside the movement window, soonest start first, each on its own
    /// cadence. Empty when nothing is close enough to watch, and the scheduler then scans nothing.
    /// </summary>
    public IReadOnlyList<LeaguePollPlan> Leagues { get; init; } = [];
}

/// <summary>One league's cadence: the budgeted interval stretched or compressed by its own next start.</summary>
/// <param name="CreditsPerScan">What one scan of this league bills, summed over the odds sources.</param>
public record LeaguePollPlan(string Sport, TimeSpan Interval, double Urgency, int CreditsPerScan, DateTimeOffset NextStart);

/// <summary>The period an allowance is measured over, which differs by provider.</summary>
public enum BudgetWindow
{
    Day,
    Month
}
