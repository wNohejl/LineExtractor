namespace LineOps.Desk.Theming;

/// <summary>
/// The colours of one token block that have to exist in C# as well as in CSS.
///
/// <para>
/// Not every token — only the ones MudBlazor derives from (it turns each palette entry into
/// <c>-rgb</c>, <c>-hover</c>, <c>-darken</c> and <c>-lighten</c>, which CSS cannot) and the
/// ones a chart needs resolved, because MudChart writes colours into SVG attributes where a
/// <c>var()</c> does not resolve. Everything else — washes, materials, shadows — lives only in
/// the stylesheet. Each property names the custom property it mirrors, and DeskThemeTests holds
/// every palette to its block value for value.
/// </para>
/// </summary>
public sealed record DeskPalette
{
    /// <summary>Which of MudBlazor's two palette slots this belongs in, and what <c>color-scheme</c> the block declares.</summary>
    public required bool IsDark { get; init; }

    /// <summary><c>--surface-0</c>: the ground everything sits on.</summary>
    public required string Surface0 { get; init; }

    /// <summary><c>--surface-1</c>: panel and window body.</summary>
    public required string Surface1 { get; init; }

    /// <summary><c>--surface-2</c>: title bars, raised rows, resting controls.</summary>
    public required string Surface2 { get; init; }

    /// <summary><c>--surface-3</c>: hover fills, pressed states.</summary>
    public required string Surface3 { get; init; }

    /// <summary><c>--text-primary</c>.</summary>
    public required string TextPrimary { get; init; }

    /// <summary><c>--text-secondary</c>. Clears AA over <see cref="Surface1"/> in every block.</summary>
    public required string TextSecondary { get; init; }

    /// <summary><c>--text-tertiary</c>. Below AA over <see cref="Surface1"/> in every block, on purpose.</summary>
    public required string TextTertiary { get; init; }

    /// <summary><c>--accent</c>: interactivity, focus, selection — nothing else.</summary>
    public required string Accent { get; init; }

    /// <summary><c>--accent-hover</c>: lighter on a dark ground, deeper on a light one.</summary>
    public required string AccentHover { get; init; }

    /// <summary><c>--on-accent</c>: the ink on a filled accent control.</summary>
    public required string OnAccent { get; init; }

    /// <summary><c>--state-positive</c>.</summary>
    public required string StatePositive { get; init; }

    /// <summary><c>--state-negative</c>.</summary>
    public required string StateNegative { get; init; }

    /// <summary><c>--state-warning</c>.</summary>
    public required string StateWarning { get; init; }

    /// <summary><c>--separator</c>.</summary>
    public required string Separator { get; init; }

    /// <summary><c>--chart-neutral</c>: opaque, so a series stays one colour over gridlines.</summary>
    public required string ChartNeutral { get; init; }

    /// <summary><c>--chart-neutral-dim</c>: the quieter neutral, which moves toward the plot background.</summary>
    public required string ChartNeutralDim { get; init; }

    /// <summary>
    /// The modal scrim. It has no CSS token: MudBlazor paints <c>.mud-overlay-scrim</c> straight
    /// from its palette, so this is the only place it can be set. Dark grounds dim with more of
    /// their own near-black; light ones with neutral ink, because a scrim subtracts attention and
    /// on any ground that means going darker.
    /// </summary>
    public required string Overlay { get; init; }
}

/// <summary>
/// Every palette the desk ships, one per token block. The blocks themselves are in
/// <c>wwwroot/css/desk.css</c> (Standard) and <c>wwwroot/css/themes.css</c> (the rest), and the
/// values here are copied from them, formatted identically, so a reader can diff the two by eye.
/// </summary>
public static class DeskPalettes
{
    public static DeskPalette AppleDark { get; } = new()
    {
        IsDark = true,
        Surface0 = DeskTheme.Surface0,
        Surface1 = DeskTheme.Surface1,
        Surface2 = DeskTheme.Surface2,
        Surface3 = DeskTheme.Surface3,
        TextPrimary = DeskTheme.TextPrimary,
        TextSecondary = DeskTheme.TextSecondary,
        TextTertiary = DeskTheme.TextTertiary,
        Accent = DeskTheme.Accent,
        AccentHover = DeskTheme.AccentHover,
        OnAccent = DeskTheme.OnAccent,
        StatePositive = DeskTheme.StatePositive,
        StateNegative = DeskTheme.StateNegative,
        StateWarning = DeskTheme.StateWarning,
        Separator = DeskTheme.Separator,
        ChartNeutral = DeskTheme.ChartNeutral,
        ChartNeutralDim = DeskTheme.ChartNeutralDim,
        Overlay = DeskTheme.Overlay
    };

    public static DeskPalette AppleLight { get; } = new()
    {
        IsDark = false,
        Surface0 = DeskTheme.LightSurface0,
        Surface1 = DeskTheme.LightSurface1,
        Surface2 = DeskTheme.LightSurface2,
        Surface3 = DeskTheme.LightSurface3,
        TextPrimary = DeskTheme.LightTextPrimary,
        TextSecondary = DeskTheme.LightTextSecondary,
        TextTertiary = DeskTheme.LightTextTertiary,
        Accent = DeskTheme.LightAccent,
        AccentHover = DeskTheme.LightAccentHover,
        OnAccent = DeskTheme.LightOnAccent,
        StatePositive = DeskTheme.LightStatePositive,
        StateNegative = DeskTheme.LightStateNegative,
        StateWarning = DeskTheme.LightStateWarning,
        Separator = DeskTheme.LightSeparator,
        ChartNeutral = DeskTheme.LightChartNeutral,
        ChartNeutralDim = DeskTheme.LightChartNeutralDim,
        Overlay = DeskTheme.LightOverlay
    };

    // ---- Nord -----------------------------------------------------------------
    // Polar Night by night, Snow Storm by day. The dark accent is Frost (nord8), pale enough that
    // a filled control takes dark ink; the light accent steps down to nord10 so white ink reads.

    public static DeskPalette NordDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#2B303B",
        Surface1 = "#2E3440",
        Surface2 = "#3B4252",
        Surface3 = "#434C5E",
        TextPrimary = "rgba(236, 239, 244, .92)",
        TextSecondary = "rgba(236, 239, 244, .66)",
        TextTertiary = "rgba(236, 239, 244, .30)",
        Accent = "#88C0D0",
        AccentHover = "#A3D0DC",
        OnAccent = "#2E3440",
        StatePositive = "#A3BE8C",
        StateNegative = "#BF616A",
        StateWarning = "#EBCB8B",
        Separator = "rgba(236, 239, 244, .10)",
        ChartNeutral = "#7B88A1",
        ChartNeutralDim = "#4C566A",
        Overlay = "rgba(43, 48, 59, .66)"
    };

    public static DeskPalette NordLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#E5E9F0",
        Surface1 = "#F4F6F9",
        Surface2 = "#D8DEE9",
        Surface3 = "#C8D0DD",
        TextPrimary = "rgba(46, 52, 64, .92)",
        TextSecondary = "rgba(46, 52, 64, .76)",
        TextTertiary = "rgba(46, 52, 64, .34)",
        Accent = "#5E81AC",
        AccentHover = "#52739C",
        OnAccent = "#FFFFFF",
        StatePositive = "#7A9A5E",
        StateNegative = "#BF616A",
        StateWarning = "#D08770",
        Separator = "rgba(46, 52, 64, .12)",
        ChartNeutral = "#7B88A1",
        ChartNeutralDim = "#A5AFC0",
        Overlay = "rgba(46, 52, 64, .30)"
    };

    // ---- Solarized ------------------------------------------------------------
    // The accents are Schoonover's and do not move between modes — that is the palette's own
    // premise. Only the ground and the ink swap, base03/base02 for base3/base2.

    public static DeskPalette SolarizedDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#00212B",
        Surface1 = "#002B36",
        Surface2 = "#073642",
        Surface3 = "#0D4250",
        TextPrimary = "rgba(238, 232, 213, .90)",
        TextSecondary = "rgba(238, 232, 213, .62)",
        TextTertiary = "rgba(238, 232, 213, .30)",
        Accent = "#268BD2",
        AccentHover = "#3E9BDC",
        OnAccent = "#FFFFFF",
        StatePositive = "#859900",
        StateNegative = "#DC322F",
        StateWarning = "#B58900",
        Separator = "rgba(238, 232, 213, .10)",
        ChartNeutral = "#839496",
        ChartNeutralDim = "#586E75",
        Overlay = "rgba(0, 33, 43, .66)"
    };

    public static DeskPalette SolarizedLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#EEE8D5",
        Surface1 = "#FDF6E3",
        Surface2 = "#E6DFC8",
        Surface3 = "#D9D1B8",
        TextPrimary = "rgba(7, 54, 66, .92)",
        TextSecondary = "rgba(7, 54, 66, .74)",
        TextTertiary = "rgba(7, 54, 66, .32)",
        Accent = "#268BD2",
        AccentHover = "#1F77B4",
        OnAccent = "#FFFFFF",
        StatePositive = "#859900",
        StateNegative = "#DC322F",
        StateWarning = "#B58900",
        Separator = "rgba(7, 54, 66, .12)",
        ChartNeutral = "#93A1A1",
        ChartNeutralDim = "#BFC6BE",
        Overlay = "rgba(0, 43, 54, .28)"
    };

    // ---- Rosé Pine ------------------------------------------------------------
    // Main by night, Dawn by day. The palette has no green, so "positive" is Foam — the colour
    // Rosé Pine itself gives to success — and the accent is Iris in both.

    public static DeskPalette RosePineDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#191724",
        Surface1 = "#1F1D2E",
        Surface2 = "#26233A",
        Surface3 = "#33304A",
        TextPrimary = "rgba(224, 222, 244, .92)",
        TextSecondary = "rgba(224, 222, 244, .62)",
        TextTertiary = "rgba(224, 222, 244, .32)",
        Accent = "#C4A7E7",
        AccentHover = "#D3BCEE",
        OnAccent = "#191724",
        StatePositive = "#9CCFD8",
        StateNegative = "#EB6F92",
        StateWarning = "#F6C177",
        Separator = "rgba(224, 222, 244, .10)",
        ChartNeutral = "#908CAA",
        ChartNeutralDim = "#6E6A86",
        Overlay = "rgba(25, 23, 36, .66)"
    };

    public static DeskPalette RosePineLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#F2E9E1",
        Surface1 = "#FFFAF3",
        Surface2 = "#EBE2D9",
        Surface3 = "#DFDAD9",
        TextPrimary = "rgba(87, 82, 121, 1)",
        TextSecondary = "rgba(87, 82, 121, .90)",
        TextTertiary = "rgba(87, 82, 121, .40)",
        Accent = "#907AA9",
        AccentHover = "#7F6899",
        OnAccent = "#FFFFFF",
        StatePositive = "#56949F",
        StateNegative = "#B4637A",
        StateWarning = "#EA9D34",
        Separator = "rgba(87, 82, 121, .14)",
        ChartNeutral = "#9893A5",
        ChartNeutralDim = "#CECACD",
        Overlay = "rgba(87, 82, 121, .28)"
    };

    // ---- High contrast --------------------------------------------------------
    // Secondary text clears AAA (7:1), hairlines are visible at a glance, and the accent carries
    // ink that clears 7:1 on it. Tertiary still sits below AA: a hint is a hint on any desk.

    public static DeskPalette ContrastDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#000000",
        Surface1 = "#0A0A0A",
        Surface2 = "#1F1F1F",
        Surface3 = "#333333",
        TextPrimary = "rgba(255, 255, 255, 1)",
        TextSecondary = "rgba(255, 255, 255, .80)",
        TextTertiary = "rgba(255, 255, 255, .40)",
        Accent = "#4DB8FF",
        AccentHover = "#80CCFF",
        OnAccent = "#000000",
        StatePositive = "#3DDC6E",
        StateNegative = "#FF5C5C",
        StateWarning = "#FFB224",
        Separator = "rgba(255, 255, 255, .38)",
        ChartNeutral = "#A0A0A0",
        ChartNeutralDim = "#6E6E6E",
        Overlay = "rgba(0, 0, 0, .75)"
    };

    public static DeskPalette ContrastLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#E6E6E6",
        Surface1 = "#FFFFFF",
        Surface2 = "#D4D4D4",
        Surface3 = "#BDBDBD",
        TextPrimary = "rgba(0, 0, 0, 1)",
        TextSecondary = "rgba(0, 0, 0, .80)",
        TextTertiary = "rgba(0, 0, 0, .45)",
        Accent = "#0050C8",
        AccentHover = "#003F9E",
        OnAccent = "#FFFFFF",
        StatePositive = "#007A1F",
        StateNegative = "#C8001E",
        StateWarning = "#A35A00",
        Separator = "rgba(0, 0, 0, .40)",
        ChartNeutral = "#6E6E6E",
        ChartNeutralDim = "#A8A8A8",
        Overlay = "rgba(0, 0, 0, .45)"
    };
}
