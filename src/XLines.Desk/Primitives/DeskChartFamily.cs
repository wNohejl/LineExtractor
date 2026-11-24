using XLines.Desk.Theming;

namespace XLines.Desk.Primitives;

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
    /// Prices and lines moving over time, one series per source/outcome. Hue is identity here,
    /// not state, so it opens on the accent (the desk's "this is the thing you are working")
    /// and walks the rest of the state colours before reaching for anything mixed.
    /// </summary>
    Movement,

    /// <summary>
    /// Money you have kept or lost — a balance, a cumulative margin, running P/L. Positive first,
    /// because the desk already spells "won" in the positive state and "lost" in the negative.
    /// </summary>
    Ledger,

    /// <summary>
    /// How a source or job is behaving — success rates, latencies, budget burn. Ordered the
    /// way the pulse strip is: healthy, then warning, then breached.
    /// </summary>
    Health,

    /// <summary>
    /// Counts with no state in them — volume by source, entries by category. Deliberately quiet:
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
    /// <para>
    /// A series is a graphical object, so it has to clear 3:1 on the panel it is drawn on. A state
    /// fill is tuned to be seen as a dot or a wash, and on several light desks it is not (Standard
    /// light's green measures 2.22:1 on white), so any series whose fill falls under 3:1 on
    /// <see cref="DeskPalette.Surface1"/> is drawn in that colour's ink instead — the same hue,
    /// taken to where text reads it (contrast audit, 20 November, §4.1 item 7). The neutral's ink is
    /// the secondary text tier laid opaque on the panel. The dim neutral is left as it is: it is
    /// the quiet one on purpose, for a reference line rather than a series anyone must read.
    /// </para>
    /// </remarks>
    public static string[] For(DeskChartFamily family, DeskPalette palette)
    {
        var panel = palette.Surface1;
        var accent = Legible(palette.Accent, palette.AccentInk, panel);
        var positive = Legible(palette.StatePositive, palette.StatePositiveInk, panel);
        var negative = Legible(palette.StateNegative, palette.StateNegativeInk, panel);
        var warning = Legible(palette.StateWarning, palette.StateWarningInk, panel);
        var neutral = Legible(palette.ChartNeutral, Opaque(palette.TextSecondary, panel), panel);
        var dim = palette.ChartNeutralDim;

        return family switch
        {
            DeskChartFamily.Ledger => [positive, negative, accent, warning, neutral, dim],
            DeskChartFamily.Health => [positive, warning, negative, accent, neutral, dim],
            DeskChartFamily.Volume => [neutral, accent, dim, positive, warning, negative],
            _ => [accent, positive, warning, negative, neutral, dim]
        };
    }

    /// <summary>The 3:1 WCAG asks of a graphical object against what it is drawn on.</summary>
    private const double SeriesContrast = 3;

    /// <summary>The fill where it clears <see cref="SeriesContrast"/> on the panel, else its ink.</summary>
    private static string Legible(string fill, string ink, string panel)
        => Contrast(fill, panel) >= SeriesContrast ? fill : ink;

    /// <summary>A translucent colour laid on an opaque ground, as the opaque hex the ground shows.</summary>
    private static string Opaque(string colour, string ground)
    {
        var (r, g, b, a) = Channels(colour);
        var (gr, gg, gb, _) = Channels(ground);

        return $"#{Blend(r, gr):X2}{Blend(g, gg):X2}{Blend(b, gb):X2}";

        int Blend(double top, double under) => (int)Math.Round(top * a + under * (1 - a));
    }

    /// <summary>WCAG's contrast ratio of an opaque colour against an opaque ground.</summary>
    private static double Contrast(string colour, string ground)
    {
        var (one, two) = (Luminance(colour), Luminance(ground));
        return (Math.Max(one, two) + .05) / (Math.Min(one, two) + .05);
    }

    private static double Luminance(string colour)
    {
        var (r, g, b, _) = Channels(colour);
        return .2126 * Linear(r) + .7152 * Linear(g) + .0722 * Linear(b);

        static double Linear(double channel)
        {
            var c = channel / 255d;
            return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4);
        }
    }

    private static (double R, double G, double B, double A) Channels(string colour)
    {
        var c = new MudBlazor.Utilities.MudColor(colour);
        return (c.R, c.G, c.B, c.A / 255d);
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

    /// <summary>A quantity across categories — by source, by region.</summary>
    Bar,

    /// <summary>Bars sharing a track, when the categories add up to something.</summary>
    StackedBar,

    /// <summary>Parts of one whole, when the parts are few and the whole is the point.</summary>
    Donut
}
