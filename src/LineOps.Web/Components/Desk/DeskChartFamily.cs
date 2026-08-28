using LineOps.Web.Theming;

namespace LineOps.Web.Components.Desk;

/// <summary>
/// What a chart is plotting, stated as the kind of dataset rather than a list of colours.
///
/// <para>
/// This is <see cref="DeskTone"/>'s rule applied to a chart. A panel names the family and
/// the palette follows, so the same kind of number is the same colour in every window and
/// nobody picks a hex at a call site. "Is this a Ledger?" is a reviewable question; "should
/// series two be orange?" is not.
/// </para>
///
/// <para>
/// The desk's dial normally spends hue on state. A multi-series chart cannot — it needs hue
/// to separate one line from another — so the families below are the sanctioned exception,
/// and each one still starts from the dial colour that already means what the chart is
/// about: iris for something you interact with, steam for money you kept.
/// </para>
/// </summary>
public enum DeskChartFamily
{
    /// <summary>
    /// Prices and lines moving over time, one series per book/outcome. Hue is identity here,
    /// not state, so it opens on iris (the desk's "this is the thing you are working") and
    /// walks the rest of the dial before reaching for anything mixed.
    /// </summary>
    Movement,

    /// <summary>
    /// Money you have kept or lost — bankroll, cumulative CLV, running P/L. Steam first,
    /// because the desk already spells "won" in steam and "lost" in drift.
    /// </summary>
    Ledger,

    /// <summary>
    /// How a source or job is behaving — success rates, latencies, budget burn. Ordered the
    /// way the pulse strip is: healthy, then warning, then breached.
    /// </summary>
    Health,

    /// <summary>
    /// Counts with no state in them — volume by book, entries by market. Deliberately quiet:
    /// haze neutrals, so a bar chart of "how many" cannot be misread as "how bad".
    /// </summary>
    Volume
}

/// <summary>
/// The palettes behind <see cref="DeskChartFamily"/>, in one place rather than at every call
/// site. Colours come from <see cref="DeskTheme"/> so the chart and the rest of the desk move
/// together; a chart cannot read <c>var(--iris)</c> because MudBlazor hands these values to
/// SVG attributes and to its own legend markup, which need resolved colours.
/// </summary>
public static class DeskChartPalette
{
    /// <summary>Cycled once the named colours run out — the dial is four wide, a game is not.</summary>
    private const string Haze = DeskTheme.Haze;
    private const string HazeDim = DeskTheme.HazeDim;

    private static readonly string[] Movement =
        [DeskTheme.Iris, DeskTheme.Steam, DeskTheme.Flag, DeskTheme.Drift, Haze, HazeDim];

    private static readonly string[] Ledger =
        [DeskTheme.Steam, DeskTheme.Drift, DeskTheme.Iris, DeskTheme.Flag, Haze, HazeDim];

    private static readonly string[] Health =
        [DeskTheme.Steam, DeskTheme.Flag, DeskTheme.Drift, DeskTheme.Iris, Haze, HazeDim];

    private static readonly string[] Volume =
        [Haze, DeskTheme.Iris, HazeDim, DeskTheme.Steam, DeskTheme.Flag, DeskTheme.Drift];

    /// <summary>The colour order for a family. The array is shared, so treat it as read-only.</summary>
    public static string[] For(DeskChartFamily family) => family switch
    {
        DeskChartFamily.Ledger => Ledger,
        DeskChartFamily.Health => Health,
        DeskChartFamily.Volume => Volume,
        _ => Movement
    };
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
