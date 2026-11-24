using XLines.Data;
using XLines.Ingestion.Configuration;

namespace XLines.Ingestion.Services;

/// <summary>
/// The decisions behind unattended line scans, kept apart from the database and the clock so
/// each can be asked directly: how urgent a league is, which leagues are due, and whether a
/// start is owed its closing scan. <see cref="LinePollPlanner"/> gathers the facts and
/// <see cref="IngestionScheduler"/> acts on the answers; neither decides anything here.
/// </summary>
public static class LinePacing
{
    /// <summary>
    /// How much to stretch or compress the budgeted interval, by how close a league's next start is.
    ///
    /// <para>
    /// The tiers follow where the money is. A line more than a day out is a placeholder that
    /// barely moves. Inside six hours it starts responding to news. The window after lineups are
    /// posted — roughly three hours out for baseball — is where the market does most of its
    /// price discovery, and it is the last chance to take a number before the close that CLV is
    /// measured against.
    /// </para>
    ///
    /// <para>
    /// Multiplies the budgeted interval rather than replacing it, so the allowance still governs
    /// how much there is to spend and this only decides when.
    /// </para>
    /// </summary>
    public static double Urgency(TimeSpan untilStart) => untilStart.TotalHours switch
    {
        <= 3 => 0.35,   // lineups are out; this is the window worth paying for
        <= 6 => 0.7,
        <= 24 => 1.5,
        _ => 4          // a day out and barely moving
    };

    /// <summary>
    /// The leagues whose own interval has run out since their own last scan. A league never
    /// scanned is due; a league not in the plan — nothing of it inside the movement window — is
    /// never due, which is what stops a quiet league being billed for a busy one's slate.
    /// </summary>
    public static IReadOnlyList<string> DueLeagues(
        IEnumerable<LeaguePollPlan> leagues, IReadOnlyDictionary<string, DateTimeOffset> lastScans, DateTimeOffset now)
        => leagues
            .Where(l => !lastScans.TryGetValue(l.Sport, out var last) || now - last >= l.Interval)
            .Select(l => l.Sport)
            .ToList();

    /// <summary>
    /// Whether a start is owed its closing scan: it is inside <paramref name="lead"/> of now and
    /// its league has not been scanned since the lead began. A scan taken inside the lead —
    /// paced, manual or an earlier closing one — already priced the close, so it is not bought twice.
    /// </summary>
    public static bool ClosingScanDue(DateTimeOffset start, DateTimeOffset? lastScan, TimeSpan lead, DateTimeOffset now)
        => start > now
           && start - now <= lead
           && (lastScan is null || lastScan < start - lead);

    /// <summary>
    /// Every league's latest scan: its own job's latest run or the all-league pull's, whichever is
    /// later — <c>odds:lines</c> prices every league, so it counts as a scan of each.
    /// </summary>
    public static Dictionary<string, DateTimeOffset> LastScans(
        IEnumerable<string> leagues, IReadOnlyDictionary<string, DateTimeOffset> latestByJobKey)
    {
        latestByJobKey.TryGetValue(IngestionJobs.OddsLines, out var all);

        var scans = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

        foreach (var league in leagues)
        {
            var own = latestByJobKey.TryGetValue(IngestionJobs.OddsLinesPrefix + league, out var at) ? at : default;
            var latest = own > all ? own : all;

            if (latest != default)
                scans[league] = latest;
        }

        return scans;
    }

    /// <summary>
    /// How often one source can afford to be scanned for the rest of its budget window — the day
    /// for a request-metered provider, the month for a credit-metered one — from what it allows and
    /// what is already gone. Null when it declares no ceiling at all.
    ///
    /// <para>
    /// The planner reads the facts from the runs table and passes them in with its own instant, so
    /// the pacing rule is pure arithmetic over numbers and times and can be asked directly.
    /// </para>
    /// </summary>
    public static SourceBudget? Budget(BudgetFacts facts, LinePollingOptions settings, DateTimeOffset now)
    {
        var perScan = Math.Max(1, facts.RequestsPerScan);

        int? fromDaily = null;

        if (facts.DailyLimit is { } daily)
        {
            var usable = Math.Max(0, daily - settings.DailyReserve - facts.RequestsLastDay);
            fromDaily = usable / perScan;
        }

        int? fromHourly = null;

        if (facts.HourlyLimit is { } hourly)
        {
            // Expressed as scans per day so the two ceilings compare directly.
            fromHourly = Math.Max(0, hourly - settings.HourlyReserve) / perScan * 24;
        }

        // A monthly credit budget is paced over the month, not the day.
        //
        // This is the only ceiling The Odds API publishes — it declares no per-day or per-hour
        // limit at all — so without this branch both numbers above stay null, the source reads as
        // unmetered, and the cadence falls to the politeness floor. At five minutes a scan that
        // spends a 500-credit month inside a single day. A budget with no pacing attached to it
        // is not a budget.
        int? fromMonthly = null;
        TimeSpan? monthlyInterval = null;

        if (facts.MonthlyCreditBudget is { } monthly)
        {
            // Scores share the month (ADR 0026): what the scheduler will spend on them to the month's
            // end is set aside before lines are paced, so the cadence shown is the one both can afford.
            var usable = Math.Max(0, monthly - settings.MonthlyCreditReserve - facts.CreditsSpent - facts.ScoresForecast);
            fromMonthly = usable / Math.Max(1, facts.CreditsPerScan);

            // Spread across the hours the month has left. A manual pull therefore lengthens the
            // automatic cadence on its own — both spend the same pool, so the scheduler slows
            // down to make room rather than racing the operator for the last credits.
            var hoursLeft = Math.Max(1d, (MonthlyCredits.NextReset(now) - now).TotalHours);

            monthlyInterval = fromMonthly > 0
                ? TimeSpan.FromHours(hoursLeft / fromMonthly.Value)
                : settings.MaximumInterval;
        }

        if (fromDaily is null && fromHourly is null && fromMonthly is null)
            return null;

        var scans = Math.Min(fromDaily ?? int.MaxValue, fromHourly ?? int.MaxValue);

        if (fromMonthly is { } monthlyScans)
            scans = Math.Min(scans, monthlyScans);

        // Nothing left in the budget. Back off to the maximum rather than hammering the guard,
        // which would otherwise record a run refused every tick.
        if (scans <= 0)
            return new SourceBudget(settings.MaximumInterval, 0, fromMonthly is not null);

        // Spread what is left over the day rather than spending it in the first hour. Where a
        // monthly budget also applies, the slower of the two governs — a daily allowance says
        // nothing about whether the month can afford to keep spending it.
        var interval = TimeSpan.FromHours(24d / scans);

        var governedByMonth = false;

        if (monthlyInterval is { } fromCredits && fromCredits > interval)
        {
            interval = fromCredits;
            governedByMonth = true;
        }

        // Also the month's when it is the only ceiling there is.
        governedByMonth |= fromDaily is null && fromHourly is null && fromMonthly is not null;

        return new SourceBudget(interval, scans, governedByMonth);
    }

    /// <summary>The later of two last-scan records, league by league.</summary>
    public static Dictionary<string, DateTimeOffset> Latest(
        IReadOnlyDictionary<string, DateTimeOffset> first, IReadOnlyDictionary<string, DateTimeOffset> second)
    {
        var merged = new Dictionary<string, DateTimeOffset>(first, StringComparer.OrdinalIgnoreCase);

        foreach (var (league, at) in second)
        {
            if (!merged.TryGetValue(league, out var known) || at > known)
                merged[league] = at;
        }

        return merged;
    }
}

/// <summary>One source's ceilings and what has been spent against them, as <see cref="LinePacing.Budget"/> reads them.</summary>
/// <param name="DailyLimit">Requests a day, where the provider caps them; null where it does not.</param>
/// <param name="RequestsLastDay">Requests this source's runs made in the last 24 hours.</param>
/// <param name="HourlyLimit">Requests an hour, where the provider caps them; null where it does not.</param>
/// <param name="RequestsPerScan">HTTP calls one scan makes, across every odds source and league.</param>
/// <param name="MonthlyCreditBudget">The month's credit allowance, where the provider bills in credits; null where it does not.</param>
/// <param name="CreditsSpent">Credits gone this month by the larger account (<see cref="XLines.Data.MonthlyCredits"/>).</param>
/// <param name="ScoresForecast">Credits automatic scores will spend to the month's end on the same account.</param>
/// <param name="CreditsPerScan">Credits one scan of the watched leagues bills this source.</param>
public readonly record struct BudgetFacts(
    int? DailyLimit,
    int RequestsLastDay,
    int? HourlyLimit,
    int RequestsPerScan,
    int? MonthlyCreditBudget,
    int CreditsSpent,
    int ScoresForecast,
    int CreditsPerScan);

/// <summary>What one source's allowance affords: the interval, the scans left, and whether the month (not the day) governs.</summary>
public readonly record struct SourceBudget(TimeSpan Interval, int ScansRemaining, bool Monthly);

/// <summary>
/// A league owed its closing scan, and the league day the scan counts against.
///
/// <para>
/// The day is stamped on the scan's run rows (<see cref="Core.Entities.IngestionRun.ClosingScanDay"/>),
/// and the cap in <see cref="Configuration.ClosingScanOptions.MaxPerLeaguePerDay"/> is counted
/// from them (<see cref="LinePollPlanner.ClosingScansTakenAsync"/>). The count used to be held in
/// memory, so a restart started it again and could grant one more day's worth of closing scans.
/// </para>
/// </summary>
public record ClosingScan(string Sport, DateOnly LeagueDay, DateTimeOffset Start);
