using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Reliability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineOps.Observability;

/// <summary>
/// Samples the reliability layer's KPIs on a timer and publishes them for the gauges to read.
///
/// This is the seam between the platform's own judgments and standard tooling. The numbers are
/// computed by <see cref="KpiCalculator"/> and <see cref="BudgetCalculator"/> — the same code the
/// Ops Center and the alert engine use, not a second implementation — and this only moves them
/// somewhere a metrics scrape can reach without touching the database.
/// </summary>
public class KpiMetricsPublisher(
    IServiceScopeFactory scopeFactory,
    KpiSnapshotCache cache,
    IOptions<ObservabilityOptions> options,
    ILogger<KpiMetricsPublisher> logger) : BackgroundService
{
    private readonly ObservabilityOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.MetricsSampleInterval);

        // Sample once immediately, so a scrape arriving in the first interval gets real numbers
        // rather than the zeroes of an empty snapshot, which read as a healthy idle platform.
        await SampleSafelyAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await SampleSafelyAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SampleSafelyAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            cache.Publish(await SampleAsync(scope.ServiceProvider, ct));
        }
        catch (Exception ex)
        {
            // The previous snapshot stays published. Telemetry that takes the process down with
            // it would be worse than telemetry that goes stale, and the freshness of the snapshot
            // is itself observable through lineops.kpi.snapshot_age_seconds.
            logger.LogError(ex, "KPI metrics sampling failed; keeping the previous snapshot");
        }
    }

    private async Task<KpiSnapshot> SampleAsync(IServiceProvider provider, CancellationToken ct)
    {
        var db = provider.GetRequiredService<LineOpsDbContext>();
        var kpi = provider.GetRequiredService<KpiCalculator>();
        var budget = provider.GetRequiredService<BudgetCalculator>();
        var reliability = provider.GetRequiredService<IOptions<ReliabilityOptions>>().Value;

        var sources = await db.Sources.Where(s => s.Enabled).OrderBy(s => s.Key).ToListAsync(ct);
        var gauges = new List<SourceGauge>(sources.Count);

        foreach (var source in sources)
        {
            var health = await kpi.GetHealthAsync(
                source, reliability.SuccessRateWindow, reliability.VolumeBaselineDays, ct);

            var usage = await budget.GetUsageAsync(source, ct);

            // Unmetered stays null rather than becoming zero. A gauge reading 0% utilisation is a
            // claim that the provider has a ceiling and is nowhere near it.
            var worst = usage.IsUnmetered ? null : usage.Worst;

            gauges.Add(new SourceGauge(
                source.Key,
                health.FreshnessMinutes,
                health.SuccessRate,
                worst?.Ratio,
                worst is { } w ? BudgetUsage.Describe(w.Dimension) : null));
        }

        var openAlerts = await db.Alerts.Where(a => a.ResolvedAt == null)
            .Select(a => a.Severity).ToListAsync(ct);

        return new KpiSnapshot(
            TakenAt: DateTimeOffset.UtcNow,
            Sources: gauges,
            OpenAlerts: openAlerts.Count,
            OpenCriticalAlerts: openAlerts.Count(s => s == AlertSeverity.Critical),
            OpenIncidents: await db.Incidents.CountAsync(i => i.Status != IncidentStatus.Resolved, ct),

            // The discipline the incident log exists to enforce, as a number some other tool can
            // watch: incidents closed out without a written cause should be zero, always.
            IncidentsAwaitingRca: await db.Incidents.CountAsync(
                i => i.Status != IncidentStatus.Resolved
                     && (i.RootCause == null || i.RootCause == ""), ct));
    }
}
