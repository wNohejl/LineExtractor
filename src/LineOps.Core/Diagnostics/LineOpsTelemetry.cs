using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace LineOps.Core.Diagnostics;

/// <summary>
/// The names everything else in the platform emits under.
///
/// This lives in Core, and deliberately uses nothing but the BCL. <c>ActivitySource</c> and
/// <c>Meter</c> are framework types, so the libraries that do the work can be instrumented
/// without any of them taking a dependency on OpenTelemetry: emitting a span and choosing where
/// spans are sent are separate decisions, and only the host needs to make the second one. Swapping
/// the exporter later touches one project.
///
/// The names are constants rather than literals at the call sites, because an
/// <c>ActivitySource</c> name that does not match the one registered with the tracer provider
/// produces spans that are created, dropped, and never missed — the most quietly wasteful failure
/// in telemetry.
/// </summary>
public static class LineOpsTelemetry
{
    public const string ServiceNamespace = "lineops";

    /// <summary>Spans for work the platform initiates: ingestion runs and reliability evaluations.</summary>
    public const string ActivitySourceName = "LineOps";

    /// <summary>Instruments mirroring the KPIs the reliability layer already computes.</summary>
    public const string MeterName = "LineOps";

    public static readonly ActivitySource Source = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    /// <summary>
    /// Counters for work as it happens, as opposed to the gauges that sample state.
    ///
    /// These count what the run actually did, so they stay meaningful under store-on-change:
    /// a run that writes no rows because nothing moved still increments the run counter, and the
    /// difference between that and a run that failed is carried in the status tag rather than in
    /// the absence of a data point.
    /// </summary>
    public static class Instruments
    {
        public static readonly Counter<long> Runs = Meter.CreateCounter<long>(
            "lineops.ingestion.runs",
            description: "Ingestion runs completed, tagged by source, job and terminal status.");

        public static readonly Counter<long> Rows = Meter.CreateCounter<long>(
            "lineops.ingestion.rows",
            description: "Rows written by ingestion runs.");

        public static readonly Counter<long> Requests = Meter.CreateCounter<long>(
            "lineops.ingestion.requests",
            description: "Upstream HTTP requests made by ingestion runs.");

        public static readonly Counter<long> Credits = Meter.CreateCounter<long>(
            "lineops.ingestion.credits",
            description: "Provider credits spent, for the credit-billed providers.");
    }

    /// <summary>Tag keys, fixed in one place so a metric and the span beside it agree.</summary>
    public static class Tags
    {
        public const string Source = "lineops.source";
        public const string Job = "lineops.job";
        public const string Sport = "lineops.sport";
        public const string Status = "lineops.status";
        public const string Rule = "lineops.rule";
        public const string Dimension = "lineops.budget_dimension";
    }
}
