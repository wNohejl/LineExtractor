namespace LineOps.Desk;

/// <summary>
/// The formats the desk shows a moment in, read on a <see cref="DeskClock"/> so the zone is the
/// one the application named rather than the host's.
///
/// <para>
/// A reading that names a clock time carries the clock's <see cref="DeskClock.Label"/>; a bare
/// day does not, because it is already the day in the desk's zone.
/// </para>
/// </summary>
public static class DeskTime
{
    /// <summary>A date and time, labelled: "Sep 23 18:40 ET".</summary>
    public static string Stamp(this DeskClock clock, DateTimeOffset at)
        => $"{clock.Format(at, "MMM d HH:mm")} {clock.Label}";

    /// <summary>Just the day: "Sep 23".</summary>
    public static string Day(this DeskClock clock, DateTimeOffset at) => clock.Format(at, "MMM d");

    /// <summary>
    /// Any other shape, unlabelled — for chart axes and the like, where the caller names
    /// <see cref="DeskClock.Label"/> once rather than on every tick.
    /// </summary>
    public static string Format(this DeskClock clock, DateTimeOffset at, string format)
        => clock.At(at).ToString(format);
}
