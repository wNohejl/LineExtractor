namespace LineOps.Reliability;

/// <summary>
/// Service-level objectives and rule thresholds. These are configuration, not constants,
/// because an SLO is a business decision that should be tunable without a rebuild.
/// </summary>
public class ReliabilityOptions
{
    public const string SectionName = "Reliability";

    /// <summary>A source is stale past this age. Daily feeds get 26h to allow schedule drift.</summary>
    public TimeSpan FreshnessSlo { get; set; } = TimeSpan.FromHours(26);

    /// <summary>Rolling-window success-rate floor, 0..1.</summary>
    public double SuccessRateSlo { get; set; } = 0.95;

    /// <summary>Window over which success rate is evaluated.</summary>
    public TimeSpan SuccessRateWindow { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Fractional drop against the trailing median that counts as a volume anomaly.
    /// 0.5 means "half the usual rows" — the signature of a silent upstream schema change.
    /// </summary>
    public double VolumeAnomalyThreshold { get; set; } = 0.5;

    /// <summary>Days of history used for the trailing volume median.</summary>
    public int VolumeBaselineDays { get; set; } = 7;

    /// <summary>Budget utilisation that raises an informational alert.</summary>
    public double BudgetWarnThreshold { get; set; } = 0.8;

    /// <summary>How often the evaluator runs.</summary>
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Consecutive critical alerts on one rule before an incident is opened automatically.</summary>
    public int AutoIncidentAfterCriticals { get; set; } = 2;

    /// <summary>
    /// Odds are pulled only when someone asks. Set by the ingestion registration from the
    /// line-polling mode, not by hand: when nothing polls on a schedule, an odds source that has
    /// not run for a day is idle, not stale, and a critical freshness alert on it would open an
    /// incident for a quiet afternoon — the "criticals are furniture" failure of ADR 0017.
    /// </summary>
    public bool OddsOnDemand { get; set; }

    /// <summary>
    /// A run still marked Running after this long belongs to a host that stopped under it.
    /// The longest real run is one backfill day or one results sweep — minutes, not hours.
    /// </summary>
    public TimeSpan OrphanRunAfter { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// How long after its start a game may go unfinished before it counts as stuck. Results are
    /// swept from four hours after the start; a game still open at twelve is one the sweep is
    /// not healing.
    /// </summary>
    public TimeSpan StuckGameAfter { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// How far back the data-quality rules look. Matches the results sweep's own lookback: a
    /// hole older than this is the backfill's to fill, and a standing alert about last season
    /// would be furniture.
    /// </summary>
    public TimeSpan DataQualityLookback { get; set; } = TimeSpan.FromDays(45);
}
