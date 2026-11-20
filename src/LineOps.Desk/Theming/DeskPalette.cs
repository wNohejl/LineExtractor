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

    /// <summary>
    /// This palette wearing a chosen accent: the fill and its hover replaced, everything else
    /// kept. Only those two cross into C#, for the reason the type gives — MudBlazor derives from
    /// them and a chart draws with the fill; the press, washes and ink live in the stylesheet.
    /// <c>--on-accent</c> stays as it is: macOS puts white on every accent it offers, and a filled
    /// key's label is large and bold enough to be found on the paler ones (teal, graphite) where
    /// body text would not be.
    /// </summary>
    public DeskPalette WithAccent(AccentPalette accent)
        => this with { Accent = accent.Accent, AccentHover = accent.Hover };
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
        Accent = "#57779F",
        AccentHover = "#527096",
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
        Accent = "#2075B1",
        AccentHover = "#217AB9",
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
        Accent = "#217AB9",
        AccentHover = "#1F73AD",
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
        Accent = "#806D96",
        AccentHover = "#79668E",
        OnAccent = "#FFFFFF",
        StatePositive = "#56949F",
        StateNegative = "#B4637A",
        StateWarning = "#EA9D34",
        Separator = "rgba(87, 82, 121, .14)",
        ChartNeutral = "#9893A5",
        ChartNeutralDim = "#CECACD",
        Overlay = "rgba(87, 82, 121, .28)"
    };

    // ---- Catppuccin -----------------------------------------------------------
    // Mocha by night, Latte by day. Mauve, the palette's signature, is the accent in both: pale
    // on Mocha, where a filled control takes base as ink, and deep enough on Latte for white.

    public static DeskPalette CatppuccinDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#181825",
        Surface1 = "#1E1E2E",
        Surface2 = "#313244",
        Surface3 = "#45475A",
        TextPrimary = "rgba(205, 214, 244, .92)",
        TextSecondary = "rgba(205, 214, 244, .66)",
        TextTertiary = "rgba(205, 214, 244, .30)",
        Accent = "#CBA6F7",
        AccentHover = "#D3B3F8",
        OnAccent = "#1E1E2E",
        StatePositive = "#A6E3A1",
        StateNegative = "#F38BA8",
        StateWarning = "#F9E2AF",
        Separator = "rgba(205, 214, 244, .10)",
        ChartNeutral = "#7F849C",
        ChartNeutralDim = "#585B70",
        Overlay = "rgba(24, 24, 37, .66)"
    };

    public static DeskPalette CatppuccinLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#E6E9EF",
        Surface1 = "#EFF1F5",
        Surface2 = "#DCE0E8",
        Surface3 = "#CCD0DA",
        TextPrimary = "rgba(76, 79, 105, 1)",
        TextSecondary = "rgba(76, 79, 105, .92)",
        TextTertiary = "rgba(76, 79, 105, .40)",
        Accent = "#8839EF",
        AccentHover = "#8136E3",
        OnAccent = "#FFFFFF",
        StatePositive = "#40A02B",
        StateNegative = "#D20F39",
        StateWarning = "#DF8E1D",
        Separator = "rgba(76, 79, 105, .12)",
        ChartNeutral = "#8C8FA1",
        ChartNeutralDim = "#BCC0CC",
        Overlay = "rgba(76, 79, 105, .30)"
    };


    // ---- Gruvbox --------------------------------------------------------------
    // morhetz's retro palette at medium contrast: dark0 by night, light0 by day. The accent is blue
    // in both — bright blue taking dark ink, faded blue taking light0 — because green, red and
    // yellow are the states and aqua is too near green to tell from positive.

    public static DeskPalette GruvboxDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#1D2021",
        Surface1 = "#282828",
        Surface2 = "#3C3836",
        Surface3 = "#504945",
        TextPrimary = "rgba(235, 219, 178, .92)",
        TextSecondary = "rgba(235, 219, 178, .67)",
        TextTertiary = "rgba(235, 219, 178, .30)",
        Accent = "#83A598",
        AccentHover = "#96B4A8",
        OnAccent = "#282828",
        StatePositive = "#B8BB26",
        StateNegative = "#FB4934",
        StateWarning = "#FABD2F",
        Separator = "rgba(235, 219, 178, .10)",
        ChartNeutral = "#928374",
        ChartNeutralDim = "#665C54",
        Overlay = "rgba(29, 32, 33, .66)"
    };

    public static DeskPalette GruvboxLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#F2E5BC",
        Surface1 = "#FBF1C7",
        Surface2 = "#EBDBB2",
        Surface3 = "#D5C4A1",
        TextPrimary = "rgba(60, 56, 54, .92)",
        TextSecondary = "rgba(60, 56, 54, .79)",
        TextTertiary = "rgba(60, 56, 54, .36)",
        Accent = "#076678",
        AccentHover = "#065C6C",
        OnAccent = "#FBF1C7",
        StatePositive = "#98971A",
        StateNegative = "#CC241D",
        StateWarning = "#D79921",
        Separator = "rgba(60, 56, 54, .12)",
        ChartNeutral = "#928374",
        ChartNeutralDim = "#BDAE93",
        Overlay = "rgba(60, 56, 54, .30)"
    };


    // ---- Tokyo Night ----------------------------------------------------------
    // Night by night, Day by day. The accent is the palette's blue in both: #7AA2F7 is pale enough
    // that a filled control takes the ground as ink; Day's #2E7DE9 is deepened until white reads.

    public static DeskPalette TokyoNightDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#16161E",
        Surface1 = "#1A1B26",
        Surface2 = "#24283B",
        Surface3 = "#292E42",
        TextPrimary = "rgba(192, 202, 245, .92)",
        TextSecondary = "rgba(192, 202, 245, .69)",
        TextTertiary = "rgba(192, 202, 245, .32)",
        Accent = "#7AA2F7",
        AccentHover = "#93B4F9",
        OnAccent = "#1A1B26",
        StatePositive = "#9ECE6A",
        StateNegative = "#F7768E",
        StateWarning = "#E0AF68",
        Separator = "rgba(192, 202, 245, .10)",
        ChartNeutral = "#737AA2",
        ChartNeutralDim = "#414868",
        Overlay = "rgba(22, 22, 30, .66)"
    };

    public static DeskPalette TokyoNightLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#E1E2E7",
        Surface1 = "#F2F3F6",
        Surface2 = "#D0D5E3",
        Surface3 = "#C4C8DA",
        TextPrimary = "rgba(52, 59, 88, .92)",
        TextSecondary = "rgba(52, 59, 88, .80)",
        TextTertiary = "rgba(52, 59, 88, .34)",
        Accent = "#2A73D6",
        AccentHover = "#276BC8",
        OnAccent = "#FFFFFF",
        StatePositive = "#587539",
        StateNegative = "#F52A65",
        StateWarning = "#B15C00",
        Separator = "rgba(52, 59, 88, .12)",
        ChartNeutral = "#68709A",
        ChartNeutralDim = "#A8AECB",
        Overlay = "rgba(52, 59, 88, .30)"
    };


    // ---- Dracula --------------------------------------------------------------
    // Dracula by night, Alucard — the spec's own light variant — by day. Purple is the accent in
    // both: pale on Dracula, where a filled control takes near-black ink, deeper on Alucard's cream,
    // where it takes white. The states are the palette's own green, red and orange.

    public static DeskPalette DraculaDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#21222C",
        Surface1 = "#282A36",
        Surface2 = "#343746",
        Surface3 = "#424450",
        TextPrimary = "rgba(248, 248, 242, .92)",
        TextSecondary = "rgba(248, 248, 242, .59)",
        TextTertiary = "rgba(248, 248, 242, .30)",
        Accent = "#BD93F9",
        AccentHover = "#CAA8FA",
        OnAccent = "#191A21",
        StatePositive = "#50FA7B",
        StateNegative = "#FF5555",
        StateWarning = "#FFB86C",
        Separator = "rgba(248, 248, 242, .10)",
        ChartNeutral = "#6272A4",
        ChartNeutralDim = "#44475A",
        Overlay = "rgba(33, 34, 44, .66)"
    };

    public static DeskPalette DraculaLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#ECE9DF",
        Surface1 = "#FFFBEB",
        Surface2 = "#DEDCCF",
        Surface3 = "#CECCC0",
        TextPrimary = "rgba(31, 31, 31, .92)",
        TextSecondary = "rgba(31, 31, 31, .68)",
        TextTertiary = "rgba(31, 31, 31, .32)",
        Accent = "#644AC9",
        AccentHover = "#5A41B8",
        OnAccent = "#FFFFFF",
        StatePositive = "#14710A",
        StateNegative = "#CB3A2A",
        StateWarning = "#A34D14",
        Separator = "rgba(31, 31, 31, .12)",
        ChartNeutral = "#8A8573",
        ChartNeutralDim = "#BCBAB3",
        Overlay = "rgba(31, 31, 31, .28)"
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
