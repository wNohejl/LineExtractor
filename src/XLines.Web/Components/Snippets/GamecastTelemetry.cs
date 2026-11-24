using System.Globalization;
using System.Text.RegularExpressions;
using XLines.Data.CrossReference;
using XLines.Data.Gamecast;

namespace XLines.Web.Components.Snippets;

/// <summary>
/// The numbers behind the Gamecast's field graphic and its team comparison, worked out apart
/// from the markup so a test can pin them without parsing SVG — as <see cref="FieldGeometry"/>
/// does for the strip.
///
/// <para>
/// Every position is in the field graphic's own units: the viewBox is
/// <see cref="FieldGeometry.Width"/> (120) wide and <see cref="Height"/> tall, the away end zone
/// 0–10 on the left, the field 10–110, the home end zone 110–120, and a spot <c>y</c> yards from
/// the <b>home</b> goal line at <c>10 + 100 - y</c> (<see cref="FieldGeometry.X"/>). The home side
/// attacks leftward, toward a higher yard line on the home-goal frame. Every share is 0–100.
/// </para>
///
/// <para>
/// Nothing here is derived from a new read: the comparison is the header, drives, plays and box
/// score the window already holds, and a figure the data does not carry is left out rather than
/// drawn as a zero or split down the middle.
/// </para>
/// </summary>
public static partial class GamecastTelemetry
{
    /// <summary>The field graphic's height in its viewBox units (its width is <see cref="FieldGeometry.Width"/>).</summary>
    public const double Height = 32;

    /// <summary>The top of the band the drive's plays are stacked in.</summary>
    public const double BandTop = 7;

    /// <summary>The bottom of that band; the yard numbers sit above and below it.</summary>
    public const double BandBottom = 25;

    /// <summary>The band's height.</summary>
    public const double BandHeight = BandBottom - BandTop;

    /// <summary>The vertical middle of the field, where the ball sits.</summary>
    public const double Middle = Height / 2;

    /// <summary>The fifty's x.</summary>
    public const double Midfield = FieldGeometry.EndZone + 50;

    /// <summary>A five-yard tick's length, in from the top and the bottom edge.</summary>
    public const double TickLength = 1.5;

    /// <summary>Where the bottom edge's ticks start.</summary>
    public const double BottomTickTop = Height - TickLength;

    /// <summary>The baselines of the yard numbers above and below the band.</summary>
    public const double TopNumberY = 4.6;

    /// <inheritdoc cref="TopNumberY"/>
    public const double BottomNumberY = Height - 3.4;

    /// <summary>The ends of the line of scrimmage and the line to gain, just inside the field's edges.</summary>
    public const double LineTop = 0.5;

    /// <inheritdoc cref="LineTop"/>
    public const double LineBottom = Height - 0.5;

    /// <summary>The middle of each end zone, where its side's abbreviation is set.</summary>
    public const double AwayMarkX = FieldGeometry.EndZone / 2;

    /// <inheritdoc cref="AwayMarkX"/>
    public const double HomeMarkX = FieldGeometry.EndZone * 1.5 + 100;

    /// <summary>The tallest one play's lane may be, so a three-play drive does not draw slabs.</summary>
    private const double MaxLane = 3;

    /// <summary>A play that went nowhere still leaves a mark this wide, so it can be seen.</summary>
    public const double MinSegment = 0.6;

    // ---- The field ------------------------------------------------------------------------

    /// <summary>
    /// The yard numbers along the field, at each ten: 10 20 30 40 50 40 30 20 10, left to right,
    /// each at its line's x.
    /// </summary>
    public static IReadOnlyList<YardNumber> YardNumbers { get; } =
        Enumerable.Range(1, 9)
            .Select(i => new YardNumber(FieldGeometry.EndZone + i * 10, i <= 5 ? i * 10 : 100 - i * 10))
            .ToList();

    /// <summary>The x of every ten-yard hairline (the goal lines are the end zones' edges).</summary>
    public static IReadOnlyList<double> TenYardLines { get; } =
        Enumerable.Range(1, 9).Select(i => FieldGeometry.EndZone + i * 10d).ToList();

    /// <summary>The x of every five-yard tick between the ten-yard lines.</summary>
    public static IReadOnlyList<double> FiveYardTicks { get; } =
        Enumerable.Range(0, 10).Select(i => FieldGeometry.EndZone + 5 + i * 10d).ToList();

    /// <summary>
    /// The stretch the drive has covered, from where it started to where the ball is now, on the
    /// field's x; null when either end is unknown. Clamped to the field; never narrower than
    /// <see cref="MinSegment"/>.
    /// </summary>
    public static DriveBand? Band(int? driveStartYardLine, int? yardLine)
    {
        if (driveStartYardLine is not { } start || yardLine is not { } spot)
            return null;

        var a = FieldGeometry.X(Math.Clamp(start, 0, 100));
        var b = FieldGeometry.X(Math.Clamp(spot, 0, 100));

        return new DriveBand(Math.Min(a, b), Math.Max(Math.Abs(a - b), MinSegment));
    }

    /// <summary>
    /// One segment per play of the drive, from each play's <see cref="GamecastPlay.Start"/> to its
    /// <see cref="GamecastPlay.End"/> yard line, in sequence, each in a lane of its own so a play
    /// that went backwards does not hide under the one before it.
    ///
    /// <para>
    /// <paramref name="homeOffence"/> is the side with the ball on this drive: it says which way
    /// is a gain. A play lacking either end is skipped, as are the plays that are not the offence
    /// moving the ball — an "Official Timeout" (ESPN's play type, which repeats the spot) and a
    /// kickoff (whose ball travels toward the receiving side's own goal, and would read as a
    /// loss). Both ends are clamped to the field.
    /// </para>
    ///
    /// <para>
    /// The kind is, first, what the play was — a turnover, a score, a penalty — and otherwise
    /// what it did: a gain, a loss, or no gain. <see cref="DriveSegment.Latest"/> marks the last
    /// segment, the play that put the ball where it is.
    /// </para>
    /// </summary>
    public static IReadOnlyList<DriveSegment> DriveSegments(IReadOnlyList<GamecastPlay>? plays, bool homeOffence)
    {
        if (plays is null || plays.Count == 0)
            return [];

        var moves = new List<(double From, double To, DriveSegmentKind Kind)>();

        foreach (var play in plays)
        {
            if (play.Start?.YardLine is not { } start || play.End?.YardLine is not { } end)
                continue;

            if (IsNotAMove(play.Type))
                continue;

            start = Math.Clamp(start, 0, 100);
            end = Math.Clamp(end, 0, 100);

            // Home attacks the away goal: a gain takes its yard line up the home-goal frame.
            var gained = homeOffence ? end - start : start - end;

            var kind = play.IsTurnover ? DriveSegmentKind.Turnover
                : play.IsScoring ? DriveSegmentKind.Score
                : play.IsPenalty ? DriveSegmentKind.Penalty
                : gained > 0 ? DriveSegmentKind.Gain
                : gained < 0 ? DriveSegmentKind.Loss
                : DriveSegmentKind.NoGain;

            moves.Add((FieldGeometry.X(start), FieldGeometry.X(end), kind));
        }

        if (moves.Count == 0)
            return [];

        const double band = BandHeight;
        var step = Math.Min(MaxLane, band / moves.Count);
        var thickness = step * 0.6;
        var first = BandTop + (band - step * moves.Count) / 2 + (step - thickness) / 2;

        return moves
            .Select((m, i) => new DriveSegment(
                m.From, m.To, m.Kind,
                Left: Math.Min(m.From, m.To),
                Width: Math.Max(Math.Abs(m.To - m.From), MinSegment),
                Top: first + i * step,
                Thickness: thickness,
                Latest: i == moves.Count - 1))
            .ToList();
    }

    private static bool IsNotAMove(string? type)
        => type is not null
           && (type.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
               || type.Contains("Kickoff", StringComparison.OrdinalIgnoreCase)
               || type.Contains("Two-minute warning", StringComparison.OrdinalIgnoreCase)
               || type.StartsWith("End ", StringComparison.OrdinalIgnoreCase));

    // ---- The comparison -------------------------------------------------------------------

    /// <summary>
    /// The two teams side by side, in a fixed order, each row only where the data the window
    /// holds can say it for both sides:
    /// <list type="bullet">
    /// <item>"Time of possession" — each side's drives' <see cref="GamecastDrive.TimeElapsed"/>
    /// ("m:ss") summed; a drive whose time does not read is skipped.</item>
    /// <item>"Total yards" — each side's drives' <see cref="GamecastDrive.Yards"/> summed.</item>
    /// <item>"Passing yards", "Rushing yards" — the box score's <c>passing.YDS</c> and
    /// <c>rushing.YDS</c> (see <see cref="SumStat"/>); only when both sides have a line carrying
    /// the key.</item>
    /// <item>"Drives" and "Scoring drives" — the drives each side has had, and those with
    /// <see cref="GamecastDrive.IsScore"/>.</item>
    /// <item>"Turnovers" — plays with <see cref="GamecastPlay.IsTurnover"/>, charged to the side
    /// that had the ball (<see cref="GamecastPlay.TeamId"/>, else the start's). Lower is better.</item>
    /// </list>
    /// Penalties are not compared: a play says it drew a flag, not on whom, and the side with the
    /// ball is as often the one who gained by it.
    /// </summary>
    public static IReadOnlyList<ComparisonRow> Compare(
        GamecastHeader header,
        IReadOnlyList<GamecastDrive>? drives,
        IReadOnlyList<GamecastPlay>? plays,
        GameBoxScore? box)
    {
        var rows = new List<ComparisonRow>();
        var home = header.Home.Id;
        var away = header.Away.Id;

        var owned = (drives ?? [])
            .Where(d => d.TeamId == home || d.TeamId == away)
            .ToList();

        // Time of possession: seconds, so the shares are of the clock and not of minutes.
        var timed = owned
            .Select(d => (d.TeamId, Seconds: Seconds(d.TimeElapsed)))
            .Where(d => d.Seconds is not null)
            .ToList();

        if (timed.Count > 0)
        {
            var a = timed.Where(d => d.TeamId == away).Sum(d => d.Seconds!.Value);
            var h = timed.Where(d => d.TeamId == home).Sum(d => d.Seconds!.Value);
            rows.Add(Row("Time of possession", Clock(a), Clock(h), a, h));
        }

        var yarded = owned.Where(d => d.Yards is not null).ToList();
        if (yarded.Count > 0)
        {
            var a = yarded.Where(d => d.TeamId == away).Sum(d => d.Yards!.Value);
            var h = yarded.Where(d => d.TeamId == home).Sum(d => d.Yards!.Value);
            rows.Add(Row("Total yards", Int(a), Int(h), a, h));
        }

        if (box is not null)
        {
            foreach (var (label, key) in new[] { ("Passing yards", "passing.YDS"), ("Rushing yards", "rushing.YDS") })
            {
                if (SumStat(box.Away, key) is { } a && SumStat(box.Home, key) is { } h)
                    rows.Add(Row(label, Int(a), Int(h), a, h));
            }
        }

        if (owned.Count > 0)
        {
            var a = owned.Count(d => d.TeamId == away);
            var h = owned.Count(d => d.TeamId == home);
            rows.Add(Row("Drives", Int(a), Int(h), a, h));

            var sa = owned.Count(d => d.TeamId == away && d.IsScore);
            var sh = owned.Count(d => d.TeamId == home && d.IsScore);
            rows.Add(Row("Scoring drives", Int(sa), Int(sh), sa, sh));
        }

        var run = (plays ?? [])
            .Select(p => (Team: p.TeamId ?? p.Start?.TeamId, p.IsTurnover))
            .Where(p => p.Team == home || p.Team == away)
            .ToList();

        if (run.Count > 0)
        {
            var a = run.Count(p => p.Team == away && p.IsTurnover);
            var h = run.Count(p => p.Team == home && p.IsTurnover);
            rows.Add(Row("Turnovers", Int(a), Int(h), a, h, higherIsBetter: false));
        }

        return rows;
    }

    /// <summary>
    /// One side's total of a box-score stat, summed over its players' lines; null when no line
    /// on that side carries the key, or one that does cannot be read as a whole number.
    ///
    /// <para>
    /// The keys are the ESPN parser's (<c>EspnStatsAdapter.ParseBoxScore</c>) as the box-score
    /// read hands them over (<see cref="StatLineKeys.Canonical"/>): a bare <c>YDS</c> is always
    /// qualified with the groups on its line that print it, so <c>passing.YDS</c> is a passer's
    /// yards and <c>rushing.YDS</c> a runner's, whatever else the player did. Team passing yards
    /// here are the sum of every passer's <c>passing.YDS</c> — the yards the passes gained, before
    /// the yards lost to sacks, which ESPN's team line nets off — and rushing yards the sum of
    /// every runner's <c>rushing.YDS</c>.
    /// </para>
    /// </summary>
    public static int? SumStat(IReadOnlyList<BoxScoreLine>? side, string key)
    {
        int? total = null;

        foreach (var line in side ?? [])
        {
            if (!line.Stats.TryGetValue(key, out var raw))
                continue;

            if (!int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
                return null;

            total = (total ?? 0) + value;
        }

        return total;
    }

    /// <summary>A drive's "m:ss" as seconds; null when it does not read as one.</summary>
    public static int? Seconds(string? elapsed)
    {
        if (elapsed is null || ClockPattern().Match(elapsed.Trim()) is not { Success: true } m)
            return null;

        return int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) * 60
               + int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The away side's share of a pair, 0–100, the home side's the rest; both zero when neither
    /// side has any, rather than an even split nobody earned. A negative figure (a side that has
    /// lost yards) counts as none toward the bar; its number still says what it is.
    /// </summary>
    public static (double Away, double Home) Shares(double away, double home)
    {
        away = Math.Max(away, 0);
        home = Math.Max(home, 0);

        if (away + home <= 0)
            return (0, 0);

        var a = Math.Round(100 * away / (away + home), 1);
        return (a, 100 - a);
    }

    private static ComparisonRow Row(string label, string awayText, string homeText, double away, double home, bool higherIsBetter = true)
    {
        var (a, h) = Shares(away, home);
        return new ComparisonRow(label, awayText, homeText, a, h, higherIsBetter);
    }

    private static string Clock(int seconds)
        => $"{seconds / 60}:{(seconds % 60).ToString("00", CultureInfo.InvariantCulture)}";

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<m>\d{1,3}):(?<s>[0-5]\d)$")]
    private static partial Regex ClockPattern();
}

/// <summary>A yard number on the field, at its line's x.</summary>
public sealed record YardNumber(double X, int Number);

/// <summary>The stretch the drive has covered so far: its left edge and width on the field's x.</summary>
public sealed record DriveBand(double Left, double Width);

/// <summary>What one play of the drive was, as its segment draws it.</summary>
public enum DriveSegmentKind
{
    Gain,
    NoGain,
    Loss,
    Penalty,
    Score,
    Turnover,
}

/// <summary>
/// One play of the drive on the field. <see cref="From"/> and <see cref="To"/> are where the ball
/// started and ended, on the field's x (so a home gain has <c>To &lt; From</c>); <see cref="Left"/>
/// and <see cref="Width"/> are the rectangle that draws it, at least <see cref="GamecastTelemetry.MinSegment"/>
/// wide; <see cref="Top"/> and <see cref="Thickness"/> are its lane, in the same units.
/// </summary>
public sealed record DriveSegment(
    double From,
    double To,
    DriveSegmentKind Kind,
    double Left,
    double Width,
    double Top,
    double Thickness,
    bool Latest);

/// <summary>
/// One line of the team comparison: the label, each side's figure as it is shown, and each
/// side's share of the bar, 0–100 (both zero when neither side has any).
/// <see cref="HigherIsBetter"/> is false for a count a side wants fewer of (turnovers), so
/// nothing reads the longer bar as a lead.
/// </summary>
public sealed record ComparisonRow(
    string Label,
    string AwayText,
    string HomeText,
    double AwayShare,
    double HomeShare,
    bool HigherIsBetter = true);
