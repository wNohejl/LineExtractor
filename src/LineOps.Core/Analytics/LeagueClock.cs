namespace LineOps.Core.Analytics;

/// <summary>
/// What day it is, in the day the leagues keep.
///
/// <para>
/// ESPN's scoreboard is addressed by a US-local date: <c>dates=20260904</c> lists the games
/// played on the fourth of September in the United States, including the 10:10pm first pitch
/// in Oakland that is already the fifth in UTC. The platform used to build every such date from
/// UTC, so from 8pm Eastern onward the slate pass fetched tomorrow's board and the results sweep
/// asked for a game on a date that did not list it. Sunday and Monday night football and most
/// West Coast baseball were owed results on the wrong day, and three games sat at Live for a
/// week while the host ran.
/// </para>
///
/// <para>
/// Both leagues in scope keep Eastern time — the NFL's and MLB's schedules, ESPN's scoreboard
/// day and the books' daily cut-offs all agree on it — so there is one clock rather than one per
/// sport. Every "which day" question about games asks it: the slate, the results sweep, the
/// backfill walk, the season rules and the board's midnight. Operational measures (runs per
/// day, KPI days) stay in UTC; they are about the host, not the schedule.
/// </para>
/// </summary>
public static class LeagueClock
{
    /// <summary>US Eastern, from the host's zone database, or the US rule where it has none.</summary>
    public static readonly TimeZoneInfo Zone = ResolveZone();

    /// <summary>The league date a moment falls on.</summary>
    public static DateOnly DateOf(DateTimeOffset instant)
        => DateOnly.FromDateTime(LocalTime(instant));

    /// <summary>The league's wall-clock time at a moment.</summary>
    public static DateTime LocalTime(DateTimeOffset instant)
        => TimeZoneInfo.ConvertTime(instant, Zone).DateTime;

    /// <summary>Today, as the scoreboard counts it.</summary>
    public static DateOnly Today(TimeProvider? clock = null)
        => DateOf((clock ?? TimeProvider.System).GetUtcNow());

    /// <summary>
    /// The instant a league date begins — its midnight, Eastern — expressed in UTC, because
    /// that is the only offset Npgsql will write to a <c>timestamptz</c> parameter.
    /// </summary>
    public static DateTimeOffset StartOf(DateOnly date)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(midnight, Zone.GetUtcOffset(midnight)).ToUniversalTime();
    }

    private static TimeZoneInfo ResolveZone()
    {
        // IANA first (Linux containers, and Windows with ICU), then the Windows id.
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
                return zone;
        }

        // A container image without a zone database still has to answer. Since 2007 the US
        // rule is: daylight time from 2am on the second Sunday of March to 2am on the first
        // Sunday of November.
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday));

        return TimeZoneInfo.CreateCustomTimeZone(
            "US/Eastern (rule)", TimeSpan.FromHours(-5), "US Eastern", "EST", "EDT", [rule]);
    }
}
