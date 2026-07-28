using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineOps.Ingestion.Services;

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
    LineOpsDbContext db,
    IOptions<IngestionOptions> options,
    ILogger<LinePollPlanner> logger)
{
    private readonly IngestionOptions _options = options.Value;

    /// <summary>
    /// The interval to wait before the next scan, and the reasoning behind it.
    ///
    /// Returns null when scanning is pointless — no odds source, or no game close enough to
    /// start that its price is worth watching.
    /// </summary>
    public async Task<PollPlan?> PlanAsync(IReadOnlyList<string> oddsSourceKeys, CancellationToken ct = default)
    {
        if (oddsSourceKeys.Count == 0)
            return null;

        var settings = _options.LinePolling;
        var sports = _options.EffectiveSports.Length;

        // Cost of one scan: two requests per sport per source, which is the whole point of the
        // batched /odds/multi call — it does not grow with the size of the slate.
        var perScan = Math.Max(1, oddsSourceKeys.Count * sports * settings.RequestsPerSportPerScan);

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
            var plan = await PlanForAsync(source, perScan, settings, ct);

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
        var budgeted = Clamp(interval, settings);
        var urgency = await UrgencyMultiplierAsync(ct);

        return new PollPlan(
            Interval: Clamp(interval * urgency, settings),
            CostPerScan: perScan,
            CreditsPerScan: sports * settings.CreditsPerSportPerScan,
            ScansRemainingToday: scansLeft == int.MaxValue ? null : scansLeft,
            BoundBy: binding,
            Window: monthlyBound ? BudgetWindow.Month : BudgetWindow.Day,
            BudgetInterval: budgeted,
            Urgency: urgency);
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
    private async Task<(TimeSpan Interval, int ScansRemaining, bool Monthly)?> PlanForAsync(
        Source source, int perScan, LinePollingOptions settings, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        int? fromDaily = null;

        if (source.RateLimitPerDay is { } daily)
        {
            var since = now.AddDays(-1);

            var used = await db.IngestionRuns
                .Where(r => r.SourceId == source.Id && r.StartedAt >= since)
                .SumAsync(r => (int?)r.RequestsMade, ct) ?? 0;

            var usable = Math.Max(0, daily - settings.DailyReserve - used);
            fromDaily = usable / perScan;
        }

        int? fromHourly = null;

        if (source.RateLimitPerHour is { } hourly)
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

        if (source.MonthlyCreditBudget is { } monthly)
        {
            var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

            var spent = await db.IngestionRuns
                .Where(r => r.SourceId == source.Id && r.StartedAt >= monthStart)
                .SumAsync(r => (int?)r.CreditsSpent, ct) ?? 0;

            var creditsPerScan = Math.Max(
                1, _options.EffectiveSports.Length * settings.CreditsPerSportPerScan);

            var usable = Math.Max(0, monthly - settings.MonthlyCreditReserve - spent);
            fromMonthly = usable / creditsPerScan;

            // Spread across the hours the month has left. A manual pull therefore lengthens the
            // automatic cadence on its own — both spend the same pool, so the scheduler slows
            // down to make room rather than racing the operator for the last credits.
            var monthEnd = monthStart.AddMonths(1);
            var hoursLeft = Math.Max(1d, (monthEnd - now).TotalHours);

            monthlyInterval = fromMonthly > 0
                ? TimeSpan.FromHours(hoursLeft / fromMonthly.Value)
                : settings.MaximumInterval;
        }

        if (fromDaily is null && fromHourly is null && fromMonthly is null)
            return null;

        var scans = Math.Min(fromDaily ?? int.MaxValue, fromHourly ?? int.MaxValue);

        if (fromMonthly is { } monthlyScans)
            scans = Math.Min(scans, monthlyScans);

        if (scans <= 0)
        {
            // Nothing left in the budget. Back off to the maximum rather than hammering the
            // guard, which would otherwise record a run refused every tick.
            logger.LogWarning("{Source}: line budget spent; backing off", source.Key);
            return (settings.MaximumInterval, 0, fromMonthly is not null);
        }

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

        return (interval, scans, governedByMonth);
    }

    /// <summary>
    /// How much to stretch or compress the budgeted interval, by how close the next first pitch is.
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
    private async Task<double> UrgencyMultiplierAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var horizon = now + _options.MovementWindow;

        // Only the leagues this plan is for. A scan covers the configured sports and nothing
        // else, so a hockey game an hour away is no reason to spend baseball's allowance faster.
        var sports = _options.EffectiveSports;

        var nextStart = await db.Games
            .Where(g => g.StartsAt > now && g.StartsAt <= horizon)
            .Where(g => sports.Contains(g.Sport!.Key))
            .OrderBy(g => g.StartsAt)
            .Select(g => (DateTimeOffset?)g.StartsAt)
            .FirstOrDefaultAsync(ct);

        if (nextStart is null)
            return 1;

        return (nextStart.Value - now).TotalHours switch
        {
            <= 3 => 0.35,   // lineups are out; this is the window worth paying for
            <= 6 => 0.7,
            <= 24 => 1.5,
            _ => 4          // a day out and barely moving
        };
    }

    /// <summary>
    /// Whether a scan is worth making at all: something has to be starting soon enough for its
    /// price to still be moving. A quiet slate should spend nothing.
    /// </summary>
    public async Task<bool> HasWatchableGamesAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var horizon = now + _options.MovementWindow;

        return await db.Games.AnyAsync(g => g.StartsAt > now && g.StartsAt <= horizon, ct);
    }
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
}

/// <summary>The period an allowance is measured over, which differs by provider.</summary>
public enum BudgetWindow
{
    Day,
    Month
}
