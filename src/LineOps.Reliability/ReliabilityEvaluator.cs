using LineOps.Core.Entities;
using LineOps.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineOps.Reliability;

/// <summary>
/// Periodically rolls up KPIs, evaluates alert rules, and opens incidents for conditions
/// that persist. This is the "who watches the watcher" piece: it runs independently of
/// ingestion so a wedged ingestion loop still gets noticed.
/// </summary>
public class ReliabilityEvaluator(
    IServiceScopeFactory scopeFactory,
    IOptions<ReliabilityOptions> options,
    ILogger<ReliabilityEvaluator> logger) : BackgroundService
{
    private readonly ReliabilityOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Reliability evaluator started, interval {Interval}",
            _options.EvaluationInterval);

        using var timer = new PeriodicTimer(_options.EvaluationInterval);

        // Evaluate once at startup so the Ops Center is populated immediately rather
        // than after a full interval of looking empty.
        await EvaluateSafelyAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await EvaluateSafelyAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Reliability evaluator stopped");
    }

    private async Task EvaluateSafelyAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            var kpi = scope.ServiceProvider.GetRequiredService<KpiCalculator>();
            var alerts = scope.ServiceProvider.GetRequiredService<AlertEngine>();

            await kpi.RollupDailyAsync(ct);
            await alerts.EvaluateAsync(ct);
            await AutoOpenIncidentsAsync(scope.ServiceProvider, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reliability evaluation failed; will retry next interval");
        }
    }

    /// <summary>
    /// Opens an incident for a critical alert that has stayed open across enough evaluation
    /// cycles to rule out a transient blip. Anything shorter would generate incidents for
    /// noise, and an incident log full of noise stops being read.
    /// </summary>
    private async Task AutoOpenIncidentsAsync(IServiceProvider provider, CancellationToken ct)
    {
        var db = provider.GetRequiredService<LineOpsDbContext>();
        var incidents = provider.GetRequiredService<IncidentService>();

        var threshold = DateTimeOffset.UtcNow
                        - _options.EvaluationInterval * _options.AutoIncidentAfterCriticals;

        var sustained = await db.Alerts
            .Where(a => a.ResolvedAt == null
                        && a.IncidentId == null
                        && a.Severity == AlertSeverity.Critical
                        && a.TriggeredAt <= threshold)
            .ToListAsync(ct);

        foreach (var alert in sustained)
        {
            await incidents.PromoteAsync(alert.Id, ct: ct);
            logger.LogWarning("Auto-opened incident for sustained critical alert {AlertId}", alert.Id);
        }
    }
}
