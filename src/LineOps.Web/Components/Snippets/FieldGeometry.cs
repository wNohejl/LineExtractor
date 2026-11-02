namespace LineOps.Web.Components.Snippets;

/// <summary>
/// Where the marks on the field strip go, worked out once and apart from the markup so a test can
/// pin the geometry without parsing SVG.
///
/// <para>
/// The strip is drawn left to right in the units of its viewBox: the away side's end zone from 0
/// to <see cref="EndZone"/>, the hundred yards of field after it, and the home side's end zone on
/// the right — home on the right, as a broadcast puts the home bench. ESPN's
/// <c>situation.yardLine</c> counts from the <b>home</b> goal line (see <c>Game.YardLine</c>), so
/// a spot <c>y</c> yards out sits at <c>EndZone + 100 - y</c>.
/// </para>
///
/// <para>
/// The home side attacks the away goal, leftward; the away side attacks rightward. The line of
/// gain is the spot plus the distance toward the defence's goal, never past the goal line (a
/// goal-to-go down's distance is the goal line itself), and the red zone is the defence's 20.
/// </para>
/// </summary>
public static class FieldGeometry
{
    /// <summary>Each end zone's width, in the strip's units: ten yards, drawn to scale.</summary>
    public const double EndZone = 10;

    /// <summary>The strip's whole width: two end zones and a hundred yards.</summary>
    public const double Width = 100 + 2 * EndZone;

    /// <summary>
    /// The marks for the side with the ball at <paramref name="yardLine"/> yards from the home goal
    /// line, needing <paramref name="distance"/> for a first down. Null when the spot is unknown.
    /// </summary>
    public static FieldMarks? Marks(int? yardLine, int? distance, bool homeBall)
    {
        if (yardLine is not { } spot)
            return null;

        spot = Math.Clamp(spot, 0, 100);

        double? gain = distance is { } d && d > 0
            ? X(Math.Clamp(homeBall ? spot + d : spot - d, 0, 100))
            : null;

        // The defence's 20: the away goal (left) when home has the ball, the home goal otherwise.
        var (redFrom, redTo) = homeBall ? (EndZone, EndZone + 20) : (EndZone + 80, EndZone + 100);

        var toGo = homeBall ? 100 - spot : spot;

        return new FieldMarks(X(spot), gain, redFrom, redTo, Leftward: homeBall, InRedZone: toGo <= 20);
    }

    /// <summary>A spot on the home-goal frame, as an x on the strip.</summary>
    public static double X(int yardsFromHomeGoal) => EndZone + 100 - yardsFromHomeGoal;
}

/// <summary>
/// The strip's marks in its own units. <see cref="Leftward"/> is the way the offence is going;
/// <see cref="Gain"/> is null when there is no distance to go (a kickoff, an extra point).
/// </summary>
public sealed record FieldMarks(double Spot, double? Gain, double RedFrom, double RedTo, bool Leftward, bool InRedZone);
