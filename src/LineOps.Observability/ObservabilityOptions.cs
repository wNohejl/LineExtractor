namespace LineOps.Observability;

/// <summary>
/// How the platform reports itself to whatever is collecting. Configuration rather than
/// constants, because where telemetry goes is an operator's decision, not a build-time one.
/// </summary>
public class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>
    /// Turns the exporter on. Off by default: a cold clone should not spend its first run
    /// retrying a connection to a collector nobody started. Compose sets it.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Reported as service.name, so the two hosts are distinguishable in one trace view.</summary>
    public string ServiceName { get; set; } = "lineops";

    /// <summary>OTLP endpoint. The Aspire dashboard listens on 18889 for gRPC by default.</summary>
    public string OtlpEndpoint { get; set; } = "http://localhost:18889";

    /// <summary>
    /// How often the KPI gauges are resampled. Slower than the scrape on purpose — these are
    /// aggregates over hours and days, so sampling them every few seconds would cost database
    /// work to redraw a line that has not moved.
    /// </summary>
    public TimeSpan MetricsSampleInterval { get; set; } = TimeSpan.FromSeconds(30);
}
