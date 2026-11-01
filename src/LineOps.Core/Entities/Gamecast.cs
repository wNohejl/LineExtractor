namespace LineOps.Core.Entities;

/// <summary>
/// One play of one game, as the provider last stated it (spec §6.2).
///
/// <para>
/// Plays are the unit of the gamecast and they are append-mostly: each carries a stable provider
/// id, so a poll upserts by <c>(GameId, SourceId, SourcePlayId)</c> rather than reloading the
/// game. Mostly, not only: ESPN rewrites a play after a review or a stat correction and stamps it
/// <see cref="Modified"/>, which is why the writer compares and updates — <see cref="PlayerGameStat"/>'s
/// pattern — and never deletes. A play the provider stops listing is still a play that happened.
/// </para>
///
/// <para>
/// The win probability lives on the play that produced it rather than in a table of its own:
/// the provider pairs them by play id, and the window reads them together.
/// </para>
/// </summary>
public class GamePlay
{
    public long Id { get; set; }

    public int GameId { get; set; }
    public Game? Game { get; set; }

    public int SourceId { get; set; }
    public Source? Source { get; set; }

    public string SourcePlayId { get; set; } = string.Empty;

    /// <summary>The provider's order of play; the window asks for plays after the last one it holds.</summary>
    public long Sequence { get; set; }

    /// <summary>The <see cref="GameDrive.Sequence"/> of the drive this play was part of, or null when it names none we hold.</summary>
    public int? DriveSeq { get; set; }

    public int? Period { get; set; }
    public string? Clock { get; set; }
    public int? ClockSeconds { get; set; }
    public string? TypeId { get; set; }
    public string? Type { get; set; }
    public string? Text { get; set; }
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }
    public bool IsScoring { get; set; }
    public bool IsTurnover { get; set; }
    public bool IsPenalty { get; set; }
    public int? Yardage { get; set; }

    /// <summary>The side with the ball — one of this game's two teams, or null when the provider named neither.</summary>
    public int? TeamId { get; set; }
    public Team? Team { get; set; }

    /// <summary>jsonb: down, distance and field position before the snap.</summary>
    public string? StartSituation { get; set; }

    /// <summary>jsonb: the same, after it.</summary>
    public string? EndSituation { get; set; }

    /// <summary>The home side's chance of winning after this play, 0–1.</summary>
    public decimal? HomeWinPct { get; set; }

    /// <summary>The home side's chance of covering the spread after this play, 0–1.</summary>
    public decimal? SpreadCoverPctHome { get; set; }

    /// <summary>The chance the game goes over the total after this play, 0–1.</summary>
    public decimal? TotalOverPct { get; set; }

    /// <summary>When the play happened, by the wall clock.</summary>
    public DateTimeOffset? Wallclock { get; set; }

    /// <summary>When the provider last changed the play; a later stamp is a review or a correction.</summary>
    public DateTimeOffset? Modified { get; set; }

    /// <summary>When we last wrote the row.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// One drive of one game: the drive log the gamecast groups its plays under, and the
/// <see cref="IsCurrent"/> one the field strip is drawn from. Upserted by
/// <c>(GameId, SourceId, SourceDriveId)</c> exactly as <see cref="GamePlay"/> is, and never
/// deleted. The MLB equivalent is the half-inning.
/// </summary>
public class GameDrive
{
    public long Id { get; set; }

    public int GameId { get; set; }
    public Game? Game { get; set; }

    public int SourceId { get; set; }
    public Source? Source { get; set; }

    public string SourceDriveId { get; set; } = string.Empty;

    /// <summary>The drive's place in the game, from 1; what <see cref="GamePlay.DriveSeq"/> names.</summary>
    public int Sequence { get; set; }

    /// <summary>The side with the ball — one of this game's two teams, or null when the provider named neither.</summary>
    public int? TeamId { get; set; }
    public Team? Team { get; set; }

    public string? Description { get; set; }
    public string? Result { get; set; }
    public int? Yards { get; set; }
    public int? OffensivePlays { get; set; }
    public string? TimeElapsed { get; set; }
    public bool IsScore { get; set; }

    /// <summary>The drive in progress. At most one per game while it is on; none once it is over.</summary>
    public bool IsCurrent { get; set; }

    public int? StartPeriod { get; set; }
    public string? StartClock { get; set; }
    public int? StartYardLine { get; set; }
    public int? EndPeriod { get; set; }
    public string? EndClock { get; set; }
    public int? EndYardLine { get; set; }

    /// <summary>When we last wrote the row.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
