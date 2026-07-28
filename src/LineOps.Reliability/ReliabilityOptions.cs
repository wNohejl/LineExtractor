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
}
