using LineOps.Core.Entities;
using LineOps.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineOps.Reliability;

/// <summary>A condition detected during evaluation, before it is reconciled against open alerts.</summary>
public record AlertCandidate(string RuleKey, int? SourceId, AlertSeverity Severity, string Message);

public static class AlertRules
{
    public const string Freshness = "freshness";
    public const string SuccessRate = "success_rate";
    public const string VolumeAnomaly = "volume_anomaly";
    public const string BudgetPressure = "budget_pressure";
}

/// <summary>
/// Evaluates KPI rules and reconciles the result against currently open alerts.
///
/// The reconciliation is the important part: a rule that is still failing must not spam a
/// new row every cycle, and a rule that has recovered must auto-resolve without anyone
/// clicking. So each (rule, source) pair has at most one open alert at a time.
/// </summary>
public class AlertEngine(
    LineOpsDbContext db,
    KpiCalculator kpi,
    BudgetCalculator budget,
    IOptions<ReliabilityOptions> options,
    ILogger<AlertEngine> logger)
{
    private readonly ReliabilityOptions _options = options.Value;

    public async Task<IReadOnlyList<AlertCandidate>> EvaluateAsync(CancellationToken ct = default)
    {
        var candidates = new List<AlertCandidate>();
        var sources = await db.Sources.Where(s => s.Enabled).ToListAsync(ct);

        foreach (var source in sources)
        {
            var health = await kpi.GetHealthAsync(
                source, _options.SuccessRateWindow, _options.VolumeBaselineDays, ct);

            // A source that has never run is not "stale" — it is unconfigured. Alerting on
            // it would be noise on a fresh clone, which is how alert fatigue starts.
            if (health.NeverRun)
                continue;

            if (health.IsStale(_options.FreshnessSlo))
            {
                var age = health.FreshnessMinutes is { } m
                    ? $"{m / 60:F1}h"
                    : "never";

                candidates.Add(new AlertCandidate(
                    AlertRules.Freshness, source.Id, AlertSeverity.Critical,
                    $"{source.Name}: no successful ingestion for {age} (SLO {_options.FreshnessSlo.TotalHours:F0}h)."));
            }

            if (health.RunsInWindow >= 3 && health.SuccessRate < _options.SuccessRateSlo)
            {
                candidates.Add(new AlertCandidate(
                    AlertRules.SuccessRate, source.Id, AlertSeverity.Warn,
                    $"{source.Name}: success rate {health.SuccessRate:P0} over {health.RunsInWindow} runs "
                    + $"(SLO {_options.SuccessRateSlo:P0})."));
            }

            if (health.VolumeRatio is { } ratio && ratio < _options.VolumeAnomalyThreshold)
            {
                candidates.Add(new AlertCandidate(
                    AlertRules.VolumeAnomaly, source.Id, AlertSeverity.Warn,
                    $"{source.Name}: today's volume is {ratio:P0} of the trailing median — "
                    + "possible upstream schema drift."));
            }

            if (await BudgetPressureAsync(source, ct) is { } pressure)
                candidates.Add(pressure);
        }

        await ReconcileAsync(candidates, ct);
        return candidates;
    }

    /// <summary>
    /// Raises budget pressure for a provider approaching or past its free-tier ceiling.
    ///
    /// Two severities, because they are two different situations. At the warn threshold the
    /// budget is merely tight and the lever is scheduling. At 100% it is spent, which means
    /// <c>CreditBudgetGuard</c> is already refusing runs — data has stopped arriving, and that
    /// is a degradation rather than a heads-up.
    ///
    /// Neither is Critical, so neither auto-opens an incident: exhausting a free tier is a
    /// planned consequence of the cost model, not an outage to be paged on.
    /// </summary>
    private async Task<AlertCandidate?> BudgetPressureAsync(Source source, CancellationToken ct)
    {
        var usage = await budget.GetUsageAsync(source, ct);

        // An unmetered provider has no ceiling to press against. Reporting it as 0% used would
        // read as healthy, which is a different claim from "this does not apply".
        if (usage.IsUnmetered || usage.Worst is not { } worst)
            return null;

        if (worst.Ratio < _options.BudgetWarnThreshold)
            return null;

        var exhausted = worst.Ratio >= 1.0;
        var dimension = BudgetUsage.Describe(worst.Dimension);

        return new AlertCandidate(
            AlertRules.BudgetPressure, source.Id,
            exhausted ? AlertSeverity.Warn : AlertSeverity.Info,
            exhausted
                ? $"{source.Name}: {dimension} budget exhausted ({worst.Used}/{worst.Limit}) — runs are being refused."
                : $"{source.Name}: {worst.Ratio:P0} of the {dimension} budget used ({worst.Used}/{worst.Limit}).");
    }

    /// <summary>
    /// Opens alerts for new conditions, leaves existing ones alone, and resolves any open
    /// alert whose condition no longer appears in this cycle's candidates.
    /// </summary>
    private async Task ReconcileAsync(IReadOnlyList<AlertCandidate> candidates, CancellationToken ct)
    {
        var open = await db.Alerts.Where(a => a.ResolvedAt == null).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var candidate in candidates)
        {
            var existing = open.FirstOrDefault(a =>
                a.RuleKey == candidate.RuleKey && a.SourceId == candidate.SourceId);

            if (existing is not null)
            {
                // Keep the message current — the numbers in it drift as the outage continues.
                existing.Message = candidate.Message;

                // And the severity with it. A rule can escalate in place — budget pressure at
                // 80% and a budget that is spent are the same (rule, source) pair — so pinning
                // severity at whatever it was when the alert opened would under-report the
                // condition for as long as it lasts.
                existing.Severity = candidate.Severity;
                continue;
            }

            db.Alerts.Add(new Alert
            {
                RuleKey = candidate.RuleKey,
                SourceId = candidate.SourceId,
                Severity = candidate.Severity,
                Message = candidate.Message,
                TriggeredAt = now
            });

            logger.LogWarning("Alert opened [{Rule}] {Message}", candidate.RuleKey, candidate.Message);
        }

        foreach (var stale in open.Where(a => !candidates.Any(c =>
                     c.RuleKey == a.RuleKey && c.SourceId == a.SourceId)))
        {
            stale.ResolvedAt = now;
            logger.LogInformation("Alert auto-resolved [{Rule}] {Message}", stale.RuleKey, stale.Message);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Alert>> GetOpenAlertsAsync(CancellationToken ct = default)
        => await db.Alerts
            .Include(a => a.Source)
            .Where(a => a.ResolvedAt == null)
            .OrderByDescending(a => a.Severity)
            .ThenByDescending(a => a.TriggeredAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Alert>> GetRecentAlertsAsync(int take = 50, CancellationToken ct = default)
        => await db.Alerts
            .Include(a => a.Source)
            .OrderByDescending(a => a.TriggeredAt)
            .Take(take)
            .ToListAsync(ct);
}
