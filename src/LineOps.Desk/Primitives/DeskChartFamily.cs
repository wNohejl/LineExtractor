using LineOps.Desk.Theming;

namespace LineOps.Desk.Primitives;

/// <summary>
/// What a chart is plotting, stated as the kind of dataset rather than a list of colours.
///
/// <para>
/// This is <see cref="DeskState"/>'s rule applied to a chart. A panel names the family and
/// the palette follows, so the same kind of number is the same colour in every window and
/// nobody picks a hex at a call site. "Is this a Ledger?" is a reviewable question; "should
/// series two be orange?" is not.
/// </para>
///
/// <para>
/// The desk normally spends hue on state and nothing else. A multi-series chart cannot — it
/// needs hue to separate one line from another — so the families below are the sanctioned
/// exception, and each one still starts from the colour that already means what the chart is
/// about: the accent for something you interact with, the positive state for money you kept.
/// </para>
/// </summary>
public enum DeskChartFamily
{
    /// <summary>
    /// Prices and lines moving over time, one series per book/outcome. Hue is identity here,
    /// not state, so it opens on the accent (the desk's "this is the thing you are working")
    /// and walks the rest of the state colours before reaching for anything mixed.
    /// </summary>
    Movement,

    /// <summary>
    /// Money you have kept or lost — bankroll, cumulative CLV, running P/L. Positive first,
    /// because the desk already spells "won" in the positive state and "lost" in the negative.
    /// </summary>
    Ledger,

    /// <summary>
    /// How a source or job is behaving — success rates, latencies, budget burn. Ordered the
    /// way the pulse strip is: healthy, then warning, then breached.
    /// </summary>
    Health,

    /// <summary>
    /// Counts with no state in them — volume by book, entries by market. Deliberately quiet:
    /// chart neutrals, so a bar chart of "how many" cannot be misread as "how bad".
    /// </summary>
    Volume
}

/// <summary>
/// The palettes behind <see cref="DeskChartFamily"/>, in one place rather than at every call
/// site. Colours come from the showing theme's <see cref="DeskPalette"/> so the chart and the rest
/// of the desk move together; a chart cannot read <c>var(--accent)</c> because MudBlazor hands
/// these values to SVG attributes and to its own legend markup, which need resolved colours.
/// </summary>
public static class DeskChartPalette
{
    /// <summary>The colour order for a family on the Standard dark desk.</summary>
    public static string[] For(DeskChartFamily family) => For(family, isDark: true);

    /// <summary>The colour order for a family on the Standard desk in the given appearance.</summary>
    /// <remarks>
    /// Kept, and kept meaning Standard, so a call site that has no palette to hand degrades to the
    /// desk the product already was rather than to a compile error.
    /// </remarks>
    public static string[] For(DeskChartFamily family, bool isDark)
        => For(family, isDark ? DeskPalettes.AppleDark : DeskPalettes.AppleLight);

    /// <summary>
    /// The colour order for a family on whichever desk is showing — pass <c>ThemeService.Palette</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is per family and never per theme. A theme change recolours every series in
    /// place and moves none of them, so the accent series is the accent series in Nord as it is
    /// in Standard. A theme that reordered its slots would silently swap two lines' colours on a
    /// switch.
    /// </para>
    /// <para>
    /// The neutrals come in opaque on purpose: these reach MudBlazor's SVG renderer directly, and
    /// a translucent colour would composite differently over gridlines and overlapping series
    /// instead of staying one fixed hue. The dim neutral is what gives a forgotten theme away —
    /// a dark desk's dim grey on a pale plot is not a quiet series, it is the darkest thing in the
    /// window — which is why every palette carries its own.
    /// </para>
    /// </remarks>
    public static string[] For(DeskChartFamily family, DeskPalette palette)
    {
        var (accent, positive, negative, warning) =
            (palette.Accent, palette.StatePositive, palette.StateNegative, palette.StateWarning);
        var (neutral, dim) = (palette.ChartNeutral, palette.ChartNeutralDim);

        return family switch
        {
            DeskChartFamily.Ledger => [positive, negative, accent, warning, neutral, dim],
            DeskChartFamily.Health => [positive, warning, negative, accent, neutral, dim],
            DeskChartFamily.Volume => [neutral, accent, dim, positive, warning, negative],
            _ => [accent, positive, warning, negative, neutral, dim]
        };
    }
}

/// <summary>
/// The shapes a desk chart comes in. A curated subset of MudBlazor's <c>ChartType</c>: the
/// ones the desk has a reason for, named the same way so the mapping stays obvious.
/// </summary>
public enum DeskChartKind
{
    /// <summary>A quantity over time. The default, and what every chart on the desk is today.</summary>
    Line,

    /// <summary>A quantity across categories — by book, by market.</summary>
    Bar,

    /// <summary>Bars sharing a track, when the categories add up to something.</summary>
    StackedBar,

    /// <summary>Parts of one whole, when the parts are few and the whole is the point.</summary>
    Donut
}
