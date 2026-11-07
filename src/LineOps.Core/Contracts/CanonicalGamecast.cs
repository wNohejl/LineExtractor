namespace LineOps.Core.Contracts;

/// <summary>
/// One game's play-by-play as the provider states it at one moment: the drives, every play in
/// them, the scoring plays and the per-play probabilities. Plays carry stable provider ids and
/// sequence numbers, which is what lets the store upsert instead of reload (spec §6.2).
/// </summary>
/// <param name="HomeLinescore">
/// Points by period from the summary's header, first period first, overtime included. Null when
/// the header carries none. Read by the backport, which fills a finished game's linescore the
/// scoreboard never gave it; the live feed leaves the game's columns to the scoreboard pass.
/// </param>
/// <param name="AwayLinescore">The same, for the away side.</param>
/// <param name="StatusDetail">ESPN's <c>status.type.shortDetail</c> on the header: "Final", "Final/OT".</param>
public record CanonicalGamecast(
    string SourceGameId,
    string? State,
    IReadOnlyList<CanonicalDrive> Drives,
    IReadOnlyList<CanonicalPlay> Plays,
    IReadOnlyList<int>? HomeLinescore = null,
    IReadOnlyList<int>? AwayLinescore = null,
    string? StatusDetail = null);

public record CanonicalDrive(
    string SourceDriveId,
    int Sequence,
    string? SourceTeamId,
    string? Description,
    string? Result,
    int? Yards,
    int? OffensivePlays,
    string? TimeElapsed,
    bool IsScore,
    bool IsCurrent,
    int? StartPeriod,
    string? StartClock,
    int? StartYardLine,
    int? EndPeriod,
    string? EndClock,
    int? EndYardLine);

public record CanonicalPlaySituation(
    int? Down,
    int? Distance,
    int? YardLine,
    int? YardsToEndzone,
    string? DownDistanceText,
    string? PossessionText,
    string? SourceTeamId);

public record CanonicalPlay(
    string SourcePlayId,
    long Sequence,
    string? SourceDriveId,
    int? Period,
    string? Clock,
    int? ClockSeconds,
    string? TypeId,
    string? Type,
    string? Text,
    int? HomeScore,
    int? AwayScore,
    bool IsScoring,
    bool IsTurnover,
    bool IsPenalty,
    int? Yardage,
    string? SourceTeamId,
    CanonicalPlaySituation? Start,
    CanonicalPlaySituation? End,
    decimal? HomeWinPct,
    decimal? SpreadCoverPctHome,
    decimal? TotalOverPct,
    DateTimeOffset? Wallclock,
    DateTimeOffset? Modified);
