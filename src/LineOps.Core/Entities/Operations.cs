namespace LineOps.Core.Entities;

public enum RunStatus
{
    Running,
    Success,
    Partial,
    Failed
}

/// <summary>
/// One execution of one source's ingestion job. Written on every run, success or failure —
/// this table is the raw feed for the entire reliability layer.
/// </summary>
public class IngestionRun
{
    public long Id { get; set; }

    public int SourceId { get; set; }
    public Source? Source { get; set; }

    /// <summary>Which job produced this run, e.g. "odds:slate", "stats:boxscore".</summary>
    public string JobKey { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    public RunStatus Status { get; set; } = RunStatus.Running;

    public int RowsIngested { get; set; }
    public int RequestsMade { get; set; }

    /// <summary>Credits consumed for credit-billed providers.</summary>
    public int CreditsSpent { get; set; }

    public string? Error { get; set; }

    public TimeSpan? Duration => FinishedAt is null ? null : FinishedAt - StartedAt;
}

/// <summary>Daily KPI rollup per source. Primary key is (Day, SourceId).</summary>
public class KpiDaily
{
    public DateOnly Day { get; set; }
    public int SourceId { get; set; }
    public Source? Source { get; set; }

    /// <summary>Minutes since the last successful snapshot, sampled at rollup time.</summary>
    public double FreshnessMinutes { get; set; }

    /// <summary>Successful runs / total runs, 0..1.</summary>
    public double SuccessRate { get; set; }

    public int RowsIngested { get; set; }
    public int RunCount { get; set; }
    public int ApiCreditsUsed { get; set; }
    public int RequestsMade { get; set; }
}

/// <summary>
/// One (source, sport, day) the history backfill has already walked.
///
/// The backfill exists to reach back over months of past days, one request per day per
/// sport, against an unmetered provider. That only stays polite — and only finishes — if a
/// restart resumes rather than starts again, so completed days are recorded here and
/// skipped on the next pass.
///
/// A day with no games is still a completed day. Without that, every offseason date would
/// be re-fetched forever, which is exactly the traffic the checkpoint is meant to avoid.
/// Failures are recorded too, with <see cref="Error"/> set, so a retry pass can target only
/// the days that actually need one.
/// </summary>
public class BackfillCheckpoint
{
    public long Id { get; set; }

    public int SourceId { get; set; }
    public Source? Source { get; set; }

    public int SportId { get; set; }
    public Sport? Sport { get; set; }

    /// <summary>The calendar day fetched, in the provider's own scheduling terms.</summary>
    public DateOnly Date { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public int GamesFound { get; set; }
    public int RowsIngested { get; set; }
    public int RequestsMade { get; set; }

    /// <summary>Null when the day was walked successfully.</summary>
    public string? Error { get; set; }

    public bool Succeeded => Error is null;
}

public enum AlertSeverity
{
    Info,
    Warn,
    Critical
}

public class Alert
{
    public long Id { get; set; }

    /// <summary>Rule that produced this alert, e.g. "freshness", "volume_anomaly".</summary>
    public string RuleKey { get; set; } = string.Empty;

    public int? SourceId { get; set; }
    public Source? Source { get; set; }

    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;

    public DateTimeOffset TriggeredAt { get; set; }

    /// <summary>Set when the underlying condition clears. Open alerts have null.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    public int? IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public bool IsOpen => ResolvedAt is null;
}

public enum IncidentStatus
{
    Open,
    Mitigated,
    Resolved
}

/// <summary>
/// A tracked operational event with a written root-cause analysis.
/// Discipline: every real ingestion failure gets one.
/// </summary>
public class Incident
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; }
    public IncidentStatus Status { get; set; } = IncidentStatus.Open;

    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>jsonb array of { at, note } — the running timeline.</summary>
    public string Timeline { get; set; } = "[]";

    public string? RootCause { get; set; }
    public string? CorrectiveActions { get; set; }

    public List<Alert> Alerts { get; set; } = [];

    public TimeSpan? TimeToResolve => ResolvedAt is null ? null : ResolvedAt - OpenedAt;
}
