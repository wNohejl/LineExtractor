using System.Diagnostics.Metrics;
using LineOps.Core.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace LineOps.Observability;

public static class ObservabilityServiceCollectionExtensions
{
    /// <summary>
    /// Registers OpenTelemetry tracing and metrics for whichever host calls it.
    ///
    /// The instrumentation itself is BCL — the libraries emit through an <c>ActivitySource</c> and
    /// a <c>Meter</c> and know nothing about OpenTelemetry. This method is the only place the
    /// vendor choice is made, which is what makes it a choice rather than a commitment.
    /// </summary>
    public static IServiceCollection AddLineOpsObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ObservabilityOptions>(
            configuration.GetSection(ObservabilityOptions.SectionName));

        var options = configuration.GetSection(ObservabilityOptions.SectionName)
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        services.AddSingleton<KpiSnapshotCache>();

        // The gauges read the cache, so they are meaningless without the thing that fills it.
        // Both are registered together rather than separately for that reason.
        services.AddSingleton(sp => new LineOpsGauges(sp.GetRequiredService<KpiSnapshotCache>()));
        services.AddHostedService<KpiMetricsPublisher>();

        if (!options.Enabled)
            return services;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(options.ServiceName, serviceNamespace: LineOpsTelemetry.ServiceNamespace)
                .AddTelemetrySdk())
            .WithTracing(tracing => tracing
                .AddSource(LineOpsTelemetry.ActivitySourceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                // Every provider call and every query becomes a child span of the ingestion run
                // that caused it, which is the view that makes a slow run explain itself.
                .AddNpgsql()
                .AddOtlpExporter(otlp => otlp.Endpoint = new Uri(options.OtlpEndpoint)))
            .WithMetrics(metrics => metrics
                .AddMeter(LineOpsTelemetry.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(otlp => otlp.Endpoint = new Uri(options.OtlpEndpoint)));

        return services;
    }

    /// <summary>
    /// Liveness and readiness as two separate questions.
    ///
    /// Liveness is "is this process still a process" and depends on nothing, so a database outage
    /// cannot get the app killed and restarted into the same outage. Readiness is tagged, so the
    /// two endpoints can share one registration without either answering for the other.
    /// </summary>
    public static IServiceCollection AddLineOpsHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseReadyHealthCheck>(
                "database", tags: [ReadyTag]);

        return services;
    }

    /// <summary>Marks the checks that decide whether this instance should receive traffic.</summary>
    public const string ReadyTag = "ready";
}

/// <summary>
/// The observable gauges, which is where the bespoke reliability layer becomes legible to
/// standard tooling.
///
/// These are the project's own KPIs — freshness, success rate, budget utilisation, open incidents
/// — published under the same names the Ops Center shows. Anything that speaks OTLP can then hold
/// the platform to its own SLOs without knowing anything about it.
///
/// Every callback reads a cached snapshot and returns. Nothing here touches the database: a
/// metrics callback runs on the collection path, and blocking it on I/O is how a monitoring
/// system becomes the outage.
/// </summary>
public sealed class LineOpsGauges
{
    public LineOpsGauges(KpiSnapshotCache cache)
    {
        var meter = LineOpsTelemetry.Meter;

        meter.CreateObservableGauge(
            "lineops.source.freshness_minutes",
            () => cache.Current.Sources
                .Where(s => s.FreshnessMinutes is not null)
                .Select(s => new Measurement<double>(
                    s.FreshnessMinutes!.Value,
                    new KeyValuePair<string, object?>(LineOpsTelemetry.Tags.Source, s.SourceKey))),
            unit: "min",
            description: "Minutes since this source last ingested successfully.");

        meter.CreateObservableGauge(
            "lineops.source.success_rate",
            () => cache.Current.Sources
                .Select(s => new Measurement<double>(
                    s.SuccessRate,
                    new KeyValuePair<string, object?>(LineOpsTelemetry.Tags.Source, s.SourceKey))),
            description: "Successful runs over completed runs, rolling window.");

        meter.CreateObservableGauge(
            "lineops.budget.utilisation",
            () => cache.Current.Sources
                // Unmetered sources are absent rather than zero — see SourceGauge.
                .Where(s => s.BudgetUtilisation is not null)
                .Select(s => new Measurement<double>(
                    s.BudgetUtilisation!.Value,
                    new KeyValuePair<string, object?>(LineOpsTelemetry.Tags.Source, s.SourceKey),
                    new KeyValuePair<string, object?>(LineOpsTelemetry.Tags.Dimension, s.BudgetDimension))),
            description: "Consumption against the provider's tightest metered ceiling, 0..1+.");

        meter.CreateObservableGauge(
            "lineops.alerts.open",
            () => new[]
            {
                new Measurement<int>(cache.Current.OpenAlerts),
                new Measurement<int>(
                    cache.Current.OpenCriticalAlerts,
                    new KeyValuePair<string, object?>("severity", "critical"))
            },
            description: "Alerts currently open.");

        meter.CreateObservableGauge(
            "lineops.incidents.open",
            () => cache.Current.OpenIncidents,
            description: "Incidents not yet resolved.");

        meter.CreateObservableGauge(
            "lineops.incidents.awaiting_rca",
            () => cache.Current.IncidentsAwaitingRca,
            description: "Open incidents with no root cause written yet.");

        // The snapshot's own age, so a publisher that has quietly stopped is visible rather than
        // presenting its last reading as the current one indefinitely.
        meter.CreateObservableGauge(
            "lineops.kpi.snapshot_age_seconds",
            () => cache.Current.TakenAt == DateTimeOffset.MinValue
                ? -1
                : (DateTimeOffset.UtcNow - cache.Current.TakenAt).TotalSeconds,
            unit: "s",
            description: "Age of the KPI sample the gauges are reporting; -1 before the first sample.");
    }
}
