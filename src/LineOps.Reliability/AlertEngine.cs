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
        }

        await ReconcileAsync(candidates, ct);
        return candidates;
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
