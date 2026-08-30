using LineOps.Core.Entities;
using LineOps.Data;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Reliability;

/// <summary>The metered dimension a provider bills on. Providers meter differently, and the
/// one under pressure is the first thing triage needs to know.</summary>
public enum BudgetDimension
{
    HourlyRequests,
    DailyRequests,
    MonthlyCredits
}

/// <summary>Consumption against one provider's ceilings. Null limits mean that dimension is unmetered.</summary>
public record BudgetUsage(
    int RequestsLastHour,
    int RequestsLastDay,
    int CreditsThisMonth,
    int? HourlyLimit,
    int? DailyLimit,
    int? MonthlyCreditLimit)
{
    /// <summary>True when the provider bills on nothing we track — utilisation is undefined, not zero.</summary>
    public bool IsUnmetered
        => HourlyLimit is not > 0 && DailyLimit is not > 0 && MonthlyCreditLimit is not > 0;

    /// <summary>Utilisation per metered dimension, 0..1+. Unmetered dimensions are absent.</summary>
    public IReadOnlyList<(BudgetDimension Dimension, double Ratio)> Utilisations
    {
        get
        {
            var ratios = new List<(BudgetDimension, double)>(3);

            if (HourlyLimit is > 0 and var h)
                ratios.Add((BudgetDimension.HourlyRequests, RequestsLastHour / (double)h));

            if (DailyLimit is > 0 and var d)
                ratios.Add((BudgetDimension.DailyRequests, RequestsLastDay / (double)d));

            if (MonthlyCreditLimit is > 0 and var m)
                ratios.Add((BudgetDimension.MonthlyCredits, CreditsThisMonth / (double)m));

            return ratios;
        }
    }

    /// <summary>Highest utilisation across all metered dimensions, 0..1+.</summary>
    public double WorstUtilisation
    {
        get
        {
            var ratios = Utilisations;
            return ratios.Count == 0 ? 0 : ratios.Max(r => r.Ratio);
        }
    }

    /// <summary>
    /// The dimension under the most pressure, with the numbers behind it. Null when unmetered.
    ///
    /// Triage step 1 in the runbook asks which dimension is under pressure, so the alert has to
    /// be able to answer that from its own text.
    /// </summary>
    public (BudgetDimension Dimension, double Ratio, int Used, int Limit)? Worst
    {
        get
        {
            var ratios = Utilisations;
            if (ratios.Count == 0)
                return null;

            var (dimension, ratio) = ratios.MaxBy(r => r.Ratio);

            return dimension switch
            {
                BudgetDimension.HourlyRequests
                    => (dimension, ratio, RequestsLastHour, HourlyLimit!.Value),
                BudgetDimension.DailyRequests
                    => (dimension, ratio, RequestsLastDay, DailyLimit!.Value),
                _ => (dimension, ratio, CreditsThisMonth, MonthlyCreditLimit!.Value)
            };
        }
    }

    public static string Describe(BudgetDimension dimension) => dimension switch
    {
        BudgetDimension.HourlyRequests => "hourly requests",
        BudgetDimension.DailyRequests => "daily requests",
        _ => "monthly credits"
    };
}

/// <summary>
/// Reads how much of each provider's free-tier ceiling has been consumed.
///
/// This lives in the reliability layer rather than alongside the ingestion guard that enforces
/// it, because measuring consumption and refusing a run are different concerns: the alert engine
/// needs the measurement without taking a dependency on the ingestion library, and the reference
/// direction only runs the other way.
///
/// Nothing is stored — consumption is derived from <c>ingestion_run</c>, the same single source
/// of truth the rest of the KPIs come from.
/// </summary>
public class BudgetCalculator(LineOpsDbContext db)
{
    public async Task<BudgetUsage> GetUsageAsync(Source source, CancellationToken ct = default)
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
