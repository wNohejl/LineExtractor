namespace LineOps.Observability;

/// <summary>One source's KPI values at the moment they were sampled.</summary>
public record SourceGauge(
    string SourceKey,
    double? FreshnessMinutes,
    double SuccessRate,
    double? BudgetUtilisation,
    string? BudgetDimension);

/// <summary>
/// Everything the observable gauges report, sampled together.
///
/// One snapshot rather than a value per instrument so the numbers a scrape returns are mutually
/// consistent: an alert count and an incident count read a second apart can disagree in ways that
/// look like a bug in the platform rather than in the sampling.
/// </summary>
public record KpiSnapshot(
    DateTimeOffset TakenAt,
    IReadOnlyList<SourceGauge> Sources,
    int OpenAlerts,
    int OpenCriticalAlerts,
    int OpenIncidents,
    int IncidentsAwaitingRca)
{
    public static readonly KpiSnapshot Empty =
        new(DateTimeOffset.MinValue, [], 0, 0, 0, 0);
}

/// <summary>
/// Holds the most recent snapshot for the gauges to read.
///
/// The gauges exist because the KPI layer is worth exposing to standard tooling, but a metrics
/// callback is synchronous and runs on the collection path — querying Postgres from inside one
/// would either block that thread or, worse, deadlock under load. So the reading is done on a
/// timer by <see cref="KpiMetricsPublisher"/> and the callback only reads a reference.
///
/// A stale-by-one-interval number is the right trade here: these are trend gauges, and the
/// alerting that actually pages anyone does not run off them.
/// </summary>
public class KpiSnapshotCache
{
    private volatile KpiSnapshot _current = KpiSnapshot.Empty;

    public KpiSnapshot Current => _current;

    public void Publish(KpiSnapshot snapshot) => _current = snapshot;
}
