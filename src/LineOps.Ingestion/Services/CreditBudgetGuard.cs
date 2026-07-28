using LineOps.Core.Entities;
using LineOps.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LineOps.Ingestion.Services;

/// <summary>
/// Enforces each provider's free-tier ceiling before a run starts.
///
/// The $0 cost model only holds if nothing silently overruns a limit, and providers meter
/// differently — odds-api.io counts requests per hour and per day, The Odds API bills
/// credits as markets x regions per call. Rather than trust the schedule to stay inside
/// those bounds, every run asks permission and is refused when the budget is spent.
/// </summary>
public class CreditBudgetGuard(LineOpsDbContext db, ILogger<CreditBudgetGuard> logger)
{
    public async Task<bool> TryReserveAsync(Source source, int estimatedRequests, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        if (source.RateLimitPerHour is { } hourly)
        {
            var since = now.AddHours(-1);
            var used = await db.IngestionRuns
                .Where(r => r.SourceId == source.Id && r.StartedAt >= since)
                .SumAsync(r => (int?)r.RequestsMade, ct) ?? 0;

            if (used + estimatedRequests > hourly)
            {
                logger.LogWarning("{Source}: hourly budget reached ({Used}/{Limit})",
                    source.Key, used, hourly);
                return false;
            }
        }

        if (source.RateLimitPerDay is { } daily)
        {
            var since = now.AddDays(-1);
            var used = await db.IngestionRuns
                .Where(r => r.SourceId == source.Id && r.StartedAt >= since)
                .SumAsync(r => (int?)r.RequestsMade, ct) ?? 0;

            if (used + estimatedRequests > daily)
            {
                logger.LogWarning("{Source}: daily budget reached ({Used}/{Limit})",
                    source.Key, used, daily);
                return false;
            }
        }

        if (source.MonthlyCreditBudget is { } monthly)
        {
            var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var used = await db.IngestionRuns
                .Where(r => r.SourceId == source.Id && r.StartedAt >= monthStart)
                .SumAsync(r => (int?)r.CreditsSpent, ct) ?? 0;

            if (used >= monthly)
            {
                logger.LogWarning("{Source}: monthly credit budget exhausted ({Used}/{Limit})",
                    source.Key, used, monthly);
                return false;
            }
        }

        return true;
    }

    /// <summary>Current consumption per source, for the Ops Center budget tiles.</summary>
    public async Task<BudgetUsage> GetUsageAsync(Source source, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var runs = db.IngestionRuns.Where(r => r.SourceId == source.Id);

        return new BudgetUsage(
            RequestsLastHour: await runs.Where(r => r.StartedAt >= now.AddHours(-1))
                .SumAsync(r => (int?)r.RequestsMade, ct) ?? 0,
            RequestsLastDay: await runs.Where(r => r.StartedAt >= now.AddDays(-1))
                .SumAsync(r => (int?)r.RequestsMade, ct) ?? 0,
            CreditsThisMonth: await runs.Where(r => r.StartedAt >= monthStart)
                .SumAsync(r => (int?)r.CreditsSpent, ct) ?? 0,
            HourlyLimit: source.RateLimitPerHour,
            DailyLimit: source.RateLimitPerDay,
            MonthlyCreditLimit: source.MonthlyCreditBudget);
    }
}

public record BudgetUsage(
    int RequestsLastHour,
    int RequestsLastDay,
    int CreditsThisMonth,
    int? HourlyLimit,
    int? DailyLimit,
    int? MonthlyCreditLimit)
{
    /// <summary>Highest utilisation across all metered dimensions, 0..1+.</summary>
    public double WorstUtilisation
    {
        get
        {
            var ratios = new List<double>();
            if (HourlyLimit is > 0) ratios.Add(RequestsLastHour / (double)HourlyLimit);
            if (DailyLimit is > 0) ratios.Add(RequestsLastDay / (double)DailyLimit);
            if (MonthlyCreditLimit is > 0) ratios.Add(CreditsThisMonth / (double)MonthlyCreditLimit);
            return ratios.Count == 0 ? 0 : ratios.Max();
        }
    }
}
