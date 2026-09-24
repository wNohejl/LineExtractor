using LineOps.Core.Analytics;

namespace LineOps.Web.Components;

/// <summary>
/// Every time the desk shows, in the zone the leagues keep.
///
/// <para>
/// The desk used to call <c>ToLocalTime()</c>. Blazor Server renders on the server, so "local"
/// meant the host's zone, not the reader's: under <c>dotnet run</c> on a desktop in New York it
/// happened to be right, and in the web container (TZ=UTC) a 6:40pm first pitch read 22:40. The
/// zone is now a decision rather than an accident of where the process runs — US Eastern, via
/// <see cref="LeagueClock"/>, the day ESPN's scoreboard and both leagues keep. Operational
/// stamps (runs, incidents, journal entries) use it too, so the desk never shows two zones.
/// </para>
///
/// <para>
/// A reading that names a clock time says so with <see cref="Label"/>. A bare day does not: it
/// is already the league's day, which is the one the schedule, the slate and the board count.
/// </para>
/// </summary>
public static class DisplayTime
{
    /// <summary>The zone's short name, for any reading that shows a clock time.</summary>
    public const string Label = "ET";

    /// <summary>A date and time, labelled: "Sep 23 18:40 ET".</summary>
    public static string Stamp(DateTimeOffset at) => $"{Format(at, "MMM d HH:mm")} {Label}";

    /// <summary>Just the day: "Sep 23".</summary>
    public static string Day(DateTimeOffset at) => Format(at, "MMM d");

    /// <summary>
    /// Any other shape, unlabelled — for chart axes and the like, where the caller names
    /// <see cref="Label"/> once rather than on every tick.
    /// </summary>
    public static string Format(DateTimeOffset at, string format)
        => LeagueClock.LocalTime(at).ToString(format);
}
