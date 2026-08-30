using LineOps.Core.Entities;
using LineOps.Reliability;
using Microsoft.Extensions.Logging;

namespace LineOps.Ingestion.Services;

/// <summary>
/// Enforces each provider's free-tier ceiling before a run starts.
///
/// The $0 cost model only holds if nothing silently overruns a limit, and providers meter
/// differently — odds-api.io counts requests per hour and per day, The Odds API bills
/// credits as markets x regions per call. Rather than trust the schedule to stay inside
/// those bounds, every run asks permission and is refused when the budget is spent.
///
/// The measuring is <see cref="BudgetCalculator"/>'s job, over in the reliability layer. What
/// is left here is only the decision: this class turns consumption into a yes or a no.
/// Splitting the two is what lets the alert engine warn on budget pressure without the
/// reliability library having to reference the ingestion library — the reference only runs the
/// other way.
/// </summary>
public class CreditBudgetGuard(BudgetCalculator budget, ILogger<CreditBudgetGuard> logger)
{
    public async Task<bool> TryReserveAsync(Source source, int estimatedRequests, CancellationToken ct)
    {
        var usage = await budget.GetUsageAsync(source, ct);

        if (usage.HourlyLimit is { } hourly
            && usage.RequestsLastHour + estimatedRequests > hourly)
        {
            logger.LogWarning("{Source}: hourly budget reached ({Used}/{Limit})",
                source.Key, usage.RequestsLastHour, hourly);
            return false;
        }

        if (usage.DailyLimit is { } daily
            && usage.RequestsLastDay + estimatedRequests > daily)
        {
            logger.LogWarning("{Source}: daily budget reached ({Used}/{Limit})",
                source.Key, usage.RequestsLastDay, daily);
            return false;
        }

        // Credits are not forecast the way requests are: a call's true cost is markets x regions
        // and the provider reports it after the fact, so the ceiling is checked against what has
        // already been spent rather than against an estimate.
        if (usage.MonthlyCreditLimit is { } monthly && usage.CreditsThisMonth >= monthly)
        {
            logger.LogWarning("{Source}: monthly credit budget exhausted ({Used}/{Limit})",
                source.Key, usage.CreditsThisMonth, monthly);
            return false;
        }

        return true;
    }
}
