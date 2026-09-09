using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineOps.Ingestion.Services;

/// <summary>
/// Keeps the reference rows in step with what is actually configured, every start.
///
/// <para>
/// <c>Sports.Enabled</c> and <c>Sources.Enabled</c> are what the desk's pickers, the KPI
/// rollup and the alert rules read — and until now nothing in code ever wrote them. The two
/// leagues out of scope were switched off by hand in one database and travelled in the
/// snapshot, so a fresh volume seeded all four; and a provider removed from configuration kept
/// its source row enabled, so it went on being counted against its SLO, alerted on for never
/// running, and promoted into incidents about a feed nobody had asked for.
/// </para>
///
/// <para>
/// The rule is the configuration: a sport is enabled if <c>Ingestion:Sports</c> names it, a
/// source is enabled if its adapter was registered — the same decision the container made,
/// read from the registry rather than re-derived. A source that drops out takes its open alerts
/// with it, resolved rather than deleted so the history of a provider that was once live is
/// kept; and an incident those alerts opened, that nobody wrote up, is not evidence of anything
/// and goes. One with a root cause is a record a person made and stays.
/// </para>
/// </summary>
public sealed class ReferenceReconciler(
    IServiceScopeFactory scopeFactory,
    IOptions<IngestionOptions> options,
    ILogger<ReferenceReconciler> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LineOpsDbContext>();
        var registry = scope.ServiceProvider.GetRequiredService<SourceRegistry>();

        await ReconcileSportsAsync(db, ct);
        await ReconcileSourcesAsync(db, registry, ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task ReconcileSportsAsync(LineOpsDbContext db, CancellationToken ct)
    {
        var wanted = options.Value.EffectiveSports.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = 0;

        foreach (var sport in await db.Sports.ToListAsync(ct))
        {
            var enabled = wanted.Contains(sport.Key);
            if (sport.Enabled == enabled)
                continue;

            sport.Enabled = enabled;
            changed++;
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Sports in scope: {Sports}", string.Join(", ", wanted));
        }
    }

    private async Task ReconcileSourcesAsync(LineOpsDbContext db, SourceRegistry registry, CancellationToken ct)
    {
        var registered = registry.OddsSources.Select(s => s.Key)
            .Concat(registry.StatsSources.Select(s => s.Key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var retired = 0;

        foreach (var source in await db.Sources.ToListAsync(ct))
        {
            var enabled = registered.Contains(source.Key);
            if (source.Enabled == enabled)
                continue;

            source.Enabled = enabled;
            if (!enabled)
                retired++;
        }

        await db.SaveChangesAsync(ct);

        // Alerts of a source that is no longer configured are answered by that fact, not by an
        // operator — and while they stayed open they were the desk's only critical alerts.
        var disabledIds = await db.Sources.Where(s => !s.Enabled).Select(s => s.Id).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var open = await db.Alerts
            .Where(a => a.ResolvedAt == null && a.SourceId != null && disabledIds.Contains(a.SourceId.Value))
            .ToListAsync(ct);

        foreach (var alert in open)
            alert.ResolvedAt = now;

        await db.SaveChangesAsync(ct);

        // An incident opened from those alerts and never written up is bookkeeping about a feed
        // nobody asked for. It cannot be closed honestly — closing needs a root cause and there
        // is none — so it is removed, and its alerts are left standing on their own as history.
        var incidents = await db.Incidents
            .Where(i => (i.RootCause == null || i.RootCause == string.Empty)
                        && db.Alerts.Any(a => a.IncidentId == i.Id)
                        && db.Alerts.Where(a => a.IncidentId == i.Id)
                            .All(a => a.SourceId != null && disabledIds.Contains(a.SourceId.Value)))
            .Select(i => i.Id)
            .ToListAsync(ct);

        if (incidents.Count > 0)
        {
            await db.Alerts.Where(a => a.IncidentId != null && incidents.Contains(a.IncidentId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.IncidentId, (int?)null), ct);
            await db.Incidents.Where(i => incidents.Contains(i.Id)).ExecuteDeleteAsync(ct);
        }

        if (retired > 0 || open.Count > 0 || incidents.Count > 0)
            logger.LogInformation(
                "Sources not configured: {Sources}; resolved {Alerts} alert(s), removed {Incidents} unwritten incident(s)",
                string.Join(", ", await db.Sources.Where(s => !s.Enabled).Select(s => s.Key).ToListAsync(ct)),
                open.Count, incidents.Count);
    }
}
