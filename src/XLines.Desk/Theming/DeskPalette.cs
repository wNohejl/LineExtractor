namespace XLines.Desk.Theming;

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

    /// <summary>
    /// <c>--state-positive-ink</c>: the positive state as text reads it, and what a chart draws a
    /// positive series in wherever <see cref="StatePositive"/> sits under 3:1 on <see cref="Surface1"/>.
    /// </summary>
    public required string StatePositiveInk { get; init; }

    /// <summary><c>--state-warning-ink</c>, as <see cref="StatePositiveInk"/> is to its fill.</summary>
    public required string StateWarningInk { get; init; }

    /// <summary><c>--state-negative-ink</c>, as <see cref="StatePositiveInk"/> is to its fill.</summary>
    public required string StateNegativeInk { get; init; }

    /// <summary><c>--accent-ink</c>, as <see cref="StatePositiveInk"/> is to its fill.</summary>
    public required string AccentInk { get; init; }

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
    /// This palette wearing a chosen accent: the fill, its hover and the ink replaced, everything
    /// else kept. Only those cross into C#, for the reason the type gives — MudBlazor derives from
    /// the fill and hover and a chart draws with the fill or the ink; the press and washes live in
    /// the stylesheet.
    /// <c>--on-accent</c> stays as it is: macOS puts white on every accent it offers, and a filled
    /// key's label is large and bold enough to be found on the paler ones (teal, graphite) where
    /// body text would not be. The ink follows the chosen hue — the accent's own where it carries
    /// one, else its fill, which then already clears 3:1 — never the family's, which would draw a
    /// chart's accent series in a hue the desk is not wearing.
    /// </summary>
    public DeskPalette WithAccent(AccentPalette accent)
        => this with { Accent = accent.Accent, AccentHover = accent.Hover, AccentInk = accent.Ink ?? accent.Accent };
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
        StatePositiveInk = DeskTheme.StatePositiveInk,
        StateWarningInk = DeskTheme.StateWarningInk,
        StateNegativeInk = DeskTheme.StateNegativeInk,
        AccentInk = DeskTheme.AccentInk,
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
        StatePositiveInk = DeskTheme.LightStatePositiveInk,
        StateWarningInk = DeskTheme.LightStateWarningInk,
        StateNegativeInk = DeskTheme.LightStateNegativeInk,
        AccentInk = DeskTheme.LightAccentInk,
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
        TextSecondary = "rgba(236, 239, 244, .69)",
        TextTertiary = "rgba(236, 239, 244, .30)",
        Accent = "#88C0D0",
        AccentHover = "#A3D0DC",
        OnAccent = "#2E3440",
        StatePositive = "#A3BE8C",
        StateNegative = "#CF888F",
        StateWarning = "#EBCB8B",
        StatePositiveInk = "#A4BF8E",
        StateWarningInk = "#EBCB8B",
        StateNegativeInk = "#E1A6AA",
        AccentInk = "#9DCAD8",
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
        StateNegative = "#B45C64",
        StateWarning = "#D08770",
        StatePositiveInk = "#4E683B",
        StateWarningInk = "#8D513E",
        StateNegativeInk = "#974851",
        AccentInk = "#45618A",
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
        TextSecondary = "rgba(238, 232, 213, .63)",
        TextTertiary = "rgba(238, 232, 213, .30)",
        Accent = "#2075B1",
        AccentHover = "#217AB9",
        OnAccent = "#FFFFFF",
        StatePositive = "#859900",
        StateNegative = "#DC322F",
        StateWarning = "#B58900",
        StatePositiveInk = "#98A927",
        StateWarningInk = "#BF9A25",
        StateNegativeInk = "#F3726F",
        AccentInk = "#63ACDD",
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
        StatePositiveInk = "#5A6800",
        StateWarningInk = "#7C5D00",
        StateNegativeInk = "#B8201D",
        AccentInk = "#186292",
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
        StatePositiveInk = "#9CCFD8",
        StateWarningInk = "#F6C177",
        StateNegativeInk = "#EB7194",
        AccentInk = "#C4A7E7",
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
        TextSecondary = "rgba(87, 82, 121, .93)",
        TextTertiary = "rgba(87, 82, 121, .40)",
        Accent = "#806D96",
        AccentHover = "#79668E",
        OnAccent = "#FFFFFF",
        StatePositive = "#56949F",
        StateNegative = "#AC5F75",
        StateWarning = "#EA9D34",
        StatePositiveInk = "#286983",
        StateWarningInk = "#8C5812",
        StateNegativeInk = "#984C62",
        AccentInk = "#6A5786",
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
        Surface3 = "#3B3C4F",
        TextPrimary = "rgba(205, 214, 244, .92)",
        TextSecondary = "rgba(205, 214, 244, .70)",
        TextTertiary = "rgba(205, 214, 244, .30)",
        Accent = "#CBA6F7",
        AccentHover = "#D3B3F8",
        OnAccent = "#1E1E2E",
        StatePositive = "#A6E3A1",
        StateNegative = "#F38BA8",
        StateWarning = "#F9E2AF",
        StatePositiveInk = "#A6E3A1",
        StateWarningInk = "#F9E2AF",
        StateNegativeInk = "#F38BA8",
        AccentInk = "#CBA6F7",
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
        TextSecondary = "rgba(76, 79, 105, .93)",
        TextTertiary = "rgba(76, 79, 105, .40)",
        Accent = "#8839EF",
        AccentHover = "#8136E3",
        OnAccent = "#FFFFFF",
        StatePositive = "#40A02B",
        StateNegative = "#D20F39",
        StateWarning = "#DF8E1D",
        StatePositiveInk = "#2D701E",
        StateWarningInk = "#885712",
        StateNegativeInk = "#B90D32",
        AccentInk = "#7337C6",
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
        Surface3 = "#46403E",
        TextPrimary = "rgba(235, 219, 178, .92)",
        TextSecondary = "rgba(235, 219, 178, .70)",
        TextTertiary = "rgba(235, 219, 178, .30)",
        Accent = "#83A598",
        AccentHover = "#96B4A8",
        OnAccent = "#282828",
        StatePositive = "#B8BB26",
        StateNegative = "#FB533F",
        StateWarning = "#FABD2F",
        StatePositiveInk = "#B8BB26",
        StateWarningInk = "#FABD2F",
        StateNegativeInk = "#FC7E6F",
        AccentInk = "#9CB4A2",
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
        TextSecondary = "rgba(60, 56, 54, .82)",
        TextTertiary = "rgba(60, 56, 54, .36)",
        Accent = "#076678",
        AccentHover = "#065C6C",
        OnAccent = "#FBF1C7",
        StatePositive = "#98971A",
        StateNegative = "#CC241D",
        StateWarning = "#D79921",
        StatePositiveInk = "#66620C",
        StateWarningInk = "#84560F",
        StateNegativeInk = "#9D0006",
        AccentInk = "#0A6374",
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
        StatePositiveInk = "#9ECE6A",
        StateWarningInk = "#E0AF68",
        StateNegativeInk = "#F7768E",
        AccentInk = "#7AA2F7",
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
        TextSecondary = "rgba(52, 59, 88, .83)",
        TextTertiary = "rgba(52, 59, 88, .34)",
        Accent = "#2A73D6",
        AccentHover = "#276BC8",
        OnAccent = "#FFFFFF",
        StatePositive = "#587539",
        StateNegative = "#E1275D",
        StateWarning = "#B15C00",
        StatePositiveInk = "#385F0D",
        StateWarningInk = "#8C4900",
        StateNegativeInk = "#8C4351",
        AccentInk = "#2959AA",
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
        TextSecondary = "rgba(248, 248, 242, .61)",
        TextTertiary = "rgba(248, 248, 242, .30)",
        Accent = "#BD93F9",
        AccentHover = "#CAA8FA",
        OnAccent = "#191A21",
        StatePositive = "#50FA7B",
        StateNegative = "#FF5555",
        StateWarning = "#FFB86C",
        StatePositiveInk = "#50FA7B",
        StateWarningInk = "#FFB86C",
        StateNegativeInk = "#FF7979",
        AccentInk = "#C6A3F8",
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
        StatePositiveInk = "#146F0A",
        StateWarningInk = "#994813",
        StateNegativeInk = "#AF3224",
        AccentInk = "#5A41B8",
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
        StatePositiveInk = "#3DDC6E",
        StateWarningInk = "#FFB224",
        StateNegativeInk = "#FF5C5C",
        AccentInk = "#4DB8FF",
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
        StatePositiveInk = "#006A1B",
        StateWarningInk = "#884B00",
        StateNegativeInk = "#B9001C",
        AccentInk = "#0050C8",
        Separator = "rgba(0, 0, 0, .40)",
        ChartNeutral = "#6E6E6E",
        ChartNeutralDim = "#A8A8A8",
        Overlay = "rgba(0, 0, 0, .45)"
    };


    // ---- Paper --------------------------------------------------------------------
    // A ledger by lamplight and by day: ivory laid paper and iron-gall ink, with one blue-black pen as the accent.

    public static DeskPalette PaperDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#15100B",
        Surface1 = "#1C1610",
        Surface2 = "#261E16",
        Surface3 = "#30271D",
        TextPrimary = "rgba(238, 226, 204, .92)",
        TextSecondary = "rgba(238, 226, 204, .62)",
        TextTertiary = "rgba(238, 226, 204, .30)",
        Accent = "#3E5F96",
        AccentHover = "#4466A0",
        OnAccent = "#F7F0E1",
        StatePositive = "#7C9A4E",
        StateNegative = "#B4452F",
        StateWarning = "#C99A3A",
        StatePositiveInk = "#9DB86E",
        StateWarningInk = "#D9AE55",
        StateNegativeInk = "#E58A6F",
        AccentInk = "#92ACD8",
        Separator = "rgba(238, 226, 204, .11)",
        ChartNeutral = "#8C7F6B",
        ChartNeutralDim = "#5A5044",
        Overlay = "rgba(21, 16, 11, .66)"
    };

    public static DeskPalette PaperLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#E8E0CC",
        Surface1 = "#FAF6EC",
        Surface2 = "#EEE7D6",
        Surface3 = "#E1D8C3",
        TextPrimary = "rgba(40, 30, 20, .94)",
        TextSecondary = "rgba(40, 30, 20, .70)",
        TextTertiary = "rgba(40, 30, 20, .32)",
        Accent = "#2A4A80",
        AccentHover = "#244170",
        OnAccent = "#FBF7EE",
        StatePositive = "#4E7A33",
        StateNegative = "#A63A2A",
        StateWarning = "#A8701A",
        StatePositiveInk = "#3F6629",
        StateWarningInk = "#84560F",
        StateNegativeInk = "#963223",
        AccentInk = "#2A4A80",
        Separator = "rgba(40, 30, 20, .13)",
        ChartNeutral = "#8A7D68",
        ChartNeutralDim = "#C4B89E",
        Overlay = "rgba(40, 30, 20, .30)"
    };

    // ---- Midnight -----------------------------------------------------------------
    // True black for an OLED panel, surfaces told apart by hairlines; an electric cyan by night, ultramarine on stark white by day.

    public static DeskPalette MidnightDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#000000",
        Surface1 = "#050505",
        Surface2 = "#0C0C0D",
        Surface3 = "#18181A",
        TextPrimary = "rgba(255, 255, 255, .95)",
        TextSecondary = "rgba(255, 255, 255, .60)",
        TextTertiary = "rgba(255, 255, 255, .30)",
        Accent = "#00C2FF",
        AccentHover = "#40D2FF",
        OnAccent = "#000000",
        StatePositive = "#00E07A",
        StateNegative = "#FF4D5E",
        StateWarning = "#FFB000",
        StatePositiveInk = "#00E07A",
        StateWarningInk = "#FFB000",
        StateNegativeInk = "#FF4D5E",
        AccentInk = "#00C2FF",
        Separator = "rgba(255, 255, 255, .15)",
        ChartNeutral = "#8A8A8E",
        ChartNeutralDim = "#48484C",
        Overlay = "rgba(0, 0, 0, .75)"
    };

    public static DeskPalette MidnightLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#F3F3F3",
        Surface1 = "#FFFFFF",
        Surface2 = "#F0F0F0",
        Surface3 = "#E1E1E1",
        TextPrimary = "rgba(0, 0, 0, .96)",
        TextSecondary = "rgba(0, 0, 0, .64)",
        TextTertiary = "rgba(0, 0, 0, .32)",
        Accent = "#0040FF",
        AccentHover = "#0036D9",
        OnAccent = "#FFFFFF",
        StatePositive = "#00A04A",
        StateNegative = "#E0001E",
        StateWarning = "#E07800",
        StatePositiveInk = "#007A36",
        StateWarningInk = "#A65500",
        StateNegativeInk = "#CC001B",
        AccentInk = "#0040FF",
        Separator = "rgba(0, 0, 0, .15)",
        ChartNeutral = "#7A7A7A",
        ChartNeutralDim = "#BDBDBD",
        Overlay = "rgba(0, 0, 0, .30)"
    };

    // ---- Obsidian -----------------------------------------------------------------
    // A terminal's carbon: blue-black panels ruled by slate hairlines, an electric sky accent and signal states; its own printout by day.

    public static DeskPalette ObsidianDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#090A0D",
        Surface1 = "#111318",
        Surface2 = "#1B1F26",
        Surface3 = "#272C36",
        TextPrimary = "rgba(241, 245, 249, .95)",
        TextSecondary = "rgba(203, 213, 225, .68)",
        TextTertiary = "rgba(203, 213, 225, .34)",
        Accent = "#38BDF8",
        AccentHover = "#63CCFA",
        OnAccent = "#06121A",
        StatePositive = "#00E676",
        StateNegative = "#FF5C5C",
        StateWarning = "#F5A524",
        StatePositiveInk = "#00E676",
        StateWarningInk = "#F5A524",
        StateNegativeInk = "#FF7070",
        AccentInk = "#38BDF8",
        Separator = "rgba(148, 163, 184, .15)",
        ChartNeutral = "#8590A2",
        ChartNeutralDim = "#3F4756",
        Overlay = "rgba(0, 0, 0, .70)"
    };

    public static DeskPalette ObsidianLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#EEF1F5",
        Surface1 = "#FFFFFF",
        Surface2 = "#E8ECF1",
        Surface3 = "#D8DEE6",
        TextPrimary = "rgba(15, 23, 42, .94)",
        TextSecondary = "rgba(15, 23, 42, .68)",
        TextTertiary = "rgba(15, 23, 42, .34)",
        Accent = "#0369A1",
        AccentHover = "#035C8E",
        OnAccent = "#FFFFFF",
        StatePositive = "#00A651",
        StateNegative = "#D92D2D",
        StateWarning = "#D97706",
        StatePositiveInk = "#00633A",
        StateWarningInk = "#8F4400",
        StateNegativeInk = "#B41818",
        AccentInk = "#075985",
        Separator = "rgba(15, 23, 42, .13)",
        ChartNeutral = "#64748B",
        ChartNeutralDim = "#B6C0CE",
        Overlay = "rgba(15, 23, 42, .30)"
    };

    // ---- Synthwave ----------------------------------------------------------------
    // Neon on an indigo sky: hot magenta keys with the night as their label, laser-green, sunset and warning-light states; pale lavender by day.

    public static DeskPalette SynthwaveDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#110A22",
        Surface1 = "#180F31",
        Surface2 = "#221643",
        Surface3 = "#2D1D56",
        TextPrimary = "rgba(247, 232, 255, .94)",
        TextSecondary = "rgba(247, 232, 255, .64)",
        TextTertiary = "rgba(247, 232, 255, .32)",
        Accent = "#FF3DC1",
        AccentHover = "#FF66CF",
        OnAccent = "#14092A",
        StatePositive = "#3DFF9A",
        StateNegative = "#FF5A3C",
        StateWarning = "#FFD23F",
        StatePositiveInk = "#3DFF9A",
        StateWarningInk = "#FFD23F",
        StateNegativeInk = "#FF6E54",
        AccentInk = "#FF6BD0",
        Separator = "rgba(247, 232, 255, .12)",
        ChartNeutral = "#8E7DBE",
        ChartNeutralDim = "#4E3D7E",
        Overlay = "rgba(17, 10, 34, .66)"
    };

    public static DeskPalette SynthwaveLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#E8DFF7",
        Surface1 = "#F7F2FF",
        Surface2 = "#E9DFF9",
        Surface3 = "#DCCEF3",
        TextPrimary = "rgba(38, 16, 82, .94)",
        TextSecondary = "rgba(38, 16, 82, .72)",
        TextTertiary = "rgba(38, 16, 82, .36)",
        Accent = "#C2148F",
        AccentHover = "#AD1080",
        OnAccent = "#FFFFFF",
        StatePositive = "#00996A",
        StateNegative = "#D42A1E",
        StateWarning = "#E06A00",
        StatePositiveInk = "#006E53",
        StateWarningInk = "#9E4800",
        StateNegativeInk = "#BA2419",
        AccentInk = "#980D70",
        Separator = "rgba(38, 16, 82, .13)",
        ChartNeutral = "#8A7CAE",
        ChartNeutralDim = "#C5B8E3",
        Overlay = "rgba(38, 16, 82, .30)"
    };

    // ---- Terra --------------------------------------------------------------------
    // Muted earth: umber and charcoal-olive by night, limestone and sand by day, terracotta keys with a sand label.

    public static DeskPalette TerraDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#141411",
        Surface1 = "#1B1A16",
        Surface2 = "#24231D",
        Surface3 = "#2E2C25",
        TextPrimary = "rgba(230, 222, 204, .90)",
        TextSecondary = "rgba(230, 222, 204, .63)",
        TextTertiary = "rgba(230, 222, 204, .30)",
        Accent = "#955439",
        AccentHover = "#9A583C",
        OnAccent = "#F5EFE3",
        StatePositive = "#8E9E5E",
        StateNegative = "#A84A55",
        StateWarning = "#C4A050",
        StatePositiveInk = "#A0B06E",
        StateWarningInk = "#CFAE63",
        StateNegativeInk = "#D98A92",
        AccentInk = "#D8977A",
        Separator = "rgba(230, 222, 204, .10)",
        ChartNeutral = "#857D6C",
        ChartNeutralDim = "#4F4B40",
        Overlay = "rgba(20, 20, 17, .66)"
    };

    public static DeskPalette TerraLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#E4DED1",
        Surface1 = "#F3F0E8",
        Surface2 = "#E7E2D6",
        Surface3 = "#DAD3C4",
        TextPrimary = "rgba(46, 44, 36, .92)",
        TextSecondary = "rgba(46, 44, 36, .75)",
        TextTertiary = "rgba(46, 44, 36, .34)",
        Accent = "#9C5236",
        AccentHover = "#8E4A31",
        OnAccent = "#FBF8F1",
        StatePositive = "#66763A",
        StateNegative = "#A13D48",
        StateWarning = "#9A6D18",
        StatePositiveInk = "#535F2E",
        StateWarningInk = "#7A5610",
        StateNegativeInk = "#8E3540",
        AccentInk = "#80432C",
        Separator = "rgba(46, 44, 36, .12)",
        ChartNeutral = "#8A8272",
        ChartNeutralDim = "#BFB7A6",
        Overlay = "rgba(46, 44, 36, .28)"
    };

    // ---- Everforest ---------------------------------------------------------------
    // sainnhe's Everforest at medium contrast both ways. The accent is the palette's blue: #7FBBB3 by
    // night, taking bg0 as ink, and the light variant's #3A94C5 deepened by day until white reads.

    public static DeskPalette EverforestDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#232A2E",
        Surface1 = "#2D353B",
        Surface2 = "#343F44",
        Surface3 = "#39444A",
        TextPrimary = "rgba(211, 198, 170, 1)",
        TextSecondary = "rgba(211, 198, 170, .82)",
        TextTertiary = "rgba(211, 198, 170, .32)",
        Accent = "#7FBBB3",
        AccentHover = "#8CC2BB",
        OnAccent = "#2D353B",
        StatePositive = "#A7C080",
        StateNegative = "#E67F81",
        StateWarning = "#DBBC7F",
        StatePositiveInk = "#A7C080",
        StateWarningInk = "#DBBC7F",
        StateNegativeInk = "#EC9C9D",
        AccentInk = "#A2CEC8",
        Separator = "rgba(211, 198, 170, .10)",
        ChartNeutral = "#859289",
        ChartNeutralDim = "#56635F",
        Overlay = "rgba(35, 42, 46, .66)"
    };

    public static DeskPalette EverforestLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#EFEBD4",
        Surface1 = "#FDF6E3",
        Surface2 = "#E6E2CC",
        Surface3 = "#E0DCC7",
        TextPrimary = "rgba(74, 85, 92, 1)",
        TextSecondary = "rgba(74, 85, 92, .91)",
        TextTertiary = "rgba(74, 85, 92, .40)",
        Accent = "#327E99",
        AccentHover = "#2E758E",
        OnAccent = "#FFFFFF",
        StatePositive = "#8DA101",
        StateNegative = "#CD4644",
        StateWarning = "#DFA000",
        StatePositiveInk = "#5D6A01",
        StateWarningInk = "#815D00",
        StateNegativeInk = "#AF3C3A",
        AccentInk = "#29677D",
        Separator = "rgba(74, 85, 92, .12)",
        ChartNeutral = "#829181",
        ChartNeutralDim = "#BDC3AF",
        Overlay = "rgba(74, 85, 92, .30)"
    };

    // ---- Kanagawa -----------------------------------------------------------------
    // rebelot's Kanagawa: Wave by night, Lotus by day. The accent is crystalBlue by night, taking sumiInk3 as ink,
    // and lotusBlue4 by day, deep enough for white.

    public static DeskPalette KanagawaDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#16161D",
        Surface1 = "#1F1F28",
        Surface2 = "#2A2A37",
        Surface3 = "#363646",
        TextPrimary = "rgba(220, 215, 186, .92)",
        TextSecondary = "rgba(220, 215, 186, .67)",
        TextTertiary = "rgba(220, 215, 186, .30)",
        Accent = "#7E9CD8",
        AccentHover = "#8BA6DC",
        OnAccent = "#1F1F28",
        StatePositive = "#98BB6C",
        StateNegative = "#E46876",
        StateWarning = "#FF9E3B",
        StatePositiveInk = "#98BB6C",
        StateWarningInk = "#FF9E3B",
        StateNegativeInk = "#E77683",
        AccentInk = "#8FA9DD",
        Separator = "rgba(220, 215, 186, .10)",
        ChartNeutral = "#727169",
        ChartNeutralDim = "#54546D",
        Overlay = "rgba(22, 22, 29, .66)"
    };

    public static DeskPalette KanagawaLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#E5DDB0",
        Surface1 = "#F2ECBC",
        Surface2 = "#E7DBA0",
        Surface3 = "#E4D794",
        TextPrimary = "rgba(84, 84, 100, 1)",
        TextSecondary = "rgba(84, 84, 100, .98)",
        TextTertiary = "rgba(84, 84, 100, .40)",
        Accent = "#4D699B",
        AccentHover = "#486290",
        OnAccent = "#FFFFFF",
        StatePositive = "#6F894E",
        StateNegative = "#C84053",
        StateWarning = "#E98A00",
        StatePositiveInk = "#52653A",
        StateWarningInk = "#895100",
        StateNegativeInk = "#A83646",
        AccentInk = "#405781",
        Separator = "rgba(84, 84, 100, .12)",
        ChartNeutral = "#8A8980",
        ChartNeutralDim = "#A09CAC",
        Overlay = "rgba(84, 84, 100, .30)"
    };

    // ---- One ----------------------------------------------------------------------
    // Atom's One: One Dark by night, One Light by day. The accent is the syntax blue in both: #61AFEF by night,
    // taking the bg as ink, and One Light's #4078F2 deepened a step by day so white reads.

    public static DeskPalette OneDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#21252B",
        Surface1 = "#282C34",
        Surface2 = "#2C313C",
        Surface3 = "#3E4451",
        TextPrimary = "rgba(203, 208, 216, .92)",
        TextSecondary = "rgba(203, 208, 216, .79)",
        TextTertiary = "rgba(203, 208, 216, .32)",
        Accent = "#61AFEF",
        AccentHover = "#71B7F1",
        OnAccent = "#282C34",
        StatePositive = "#98C379",
        StateNegative = "#E1717A",
        StateWarning = "#E5C07B",
        StatePositiveInk = "#98C379",
        StateWarningInk = "#E5C07B",
        StateNegativeInk = "#E78C93",
        AccentInk = "#82C0F2",
        Separator = "rgba(203, 208, 216, .10)",
        ChartNeutral = "#828997",
        ChartNeutralDim = "#5C6370",
        Overlay = "rgba(33, 37, 43, .66)"
    };

    public static DeskPalette OneLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#EAEAEB",
        Surface1 = "#FAFAFA",
        Surface2 = "#E5E5E6",
        Surface3 = "#DBDBDC",
        TextPrimary = "rgba(56, 58, 66, .94)",
        TextSecondary = "rgba(56, 58, 66, .77)",
        TextTertiary = "rgba(56, 58, 66, .34)",
        Accent = "#3870E2",
        AccentHover = "#3468D2",
        OnAccent = "#FFFFFF",
        StatePositive = "#50A14F",
        StateNegative = "#CA1243",
        StateWarning = "#C18401",
        StatePositiveInk = "#397238",
        StateWarningInk = "#885D01",
        StateNegativeInk = "#C71242",
        AccentInk = "#2E5BB8",
        Separator = "rgba(56, 58, 66, .12)",
        ChartNeutral = "#A0A1A7",
        ChartNeutralDim = "#C8C8CA",
        Overlay = "rgba(56, 58, 66, .30)"
    };

    // ---- Monokai ------------------------------------------------------------------
    // Wimer Hazenberg's Monokai, as TextMate and Sublime Text ship it; it publishes no light variant, so the light desk
    // turns it over — fg is the paper, bg the ink — with each hue deepened until it reads. The accent is the cyan in both.

    public static DeskPalette MonokaiDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#1E1F1C",
        Surface1 = "#272822",
        Surface2 = "#3E3D32",
        Surface3 = "#49483E",
        TextPrimary = "rgba(248, 248, 242, .92)",
        TextSecondary = "rgba(248, 248, 242, .63)",
        TextTertiary = "rgba(248, 248, 242, .30)",
        Accent = "#66D9EF",
        AccentHover = "#75DDF1",
        OnAccent = "#272822",
        StatePositive = "#A6E22E",
        StateNegative = "#FA4C8B",
        StateWarning = "#FD971F",
        StatePositiveInk = "#A6E22E",
        StateWarningInk = "#FD971F",
        StateNegativeInk = "#FB80AD",
        AccentInk = "#66D9EF",
        Separator = "rgba(248, 248, 242, .10)",
        ChartNeutral = "#75715E",
        ChartNeutralDim = "#49483E",
        Overlay = "rgba(30, 31, 28, .66)"
    };

    public static DeskPalette MonokaiLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#ECECE6",
        Surface1 = "#F8F8F2",
        Surface2 = "#E3E3DD",
        Surface3 = "#D7D7D1",
        TextPrimary = "rgba(39, 40, 34, .92)",
        TextSecondary = "rgba(39, 40, 34, .70)",
        TextTertiary = "rgba(39, 40, 34, .34)",
        Accent = "#327B86",
        AccentHover = "#2E727D",
        OnAccent = "#F8F8F2",
        StatePositive = "#729B20",
        StateNegative = "#D72163",
        StateWarning = "#CE7B19",
        StatePositiveInk = "#506D17",
        StateWarningInk = "#905611",
        StateNegativeInk = "#C01E59",
        AccentInk = "#2A6871",
        Separator = "rgba(39, 40, 34, .12)",
        ChartNeutral = "#75715E",
        ChartNeutralDim = "#B9B8AE",
        Overlay = "rgba(39, 40, 34, .30)"
    };

    // ---- Ayu ----------------------------------------------------------------------
    // ayu-theme's Ayu: Dark by night (chosen over Mirage, whose blue slate sits beside One Dark's), Light by day.
    // The accent is common.accent, the gold, in both, and it takes a dark label in both, as Ayu's own buttons do.

    public static DeskPalette AyuDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#0D1017",
        Surface1 = "#10141C",
        Surface2 = "#141821",
        Surface3 = "#1B1F29",
        TextPrimary = "rgba(252, 248, 236, .82)",
        TextSecondary = "rgba(196, 214, 254, .57)",
        TextTertiary = "rgba(196, 214, 254, .26)",
        Accent = "#E6B450",
        AccentHover = "#E8BC62",
        OnAccent = "#0D1017",
        StatePositive = "#70BF56",
        StateNegative = "#D95757",
        StateWarning = "#FF8F40",
        StatePositiveInk = "#70BF56",
        StateWarningInk = "#FF8F40",
        StateNegativeInk = "#DD6767",
        AccentInk = "#E6B450",
        Separator = "rgba(196, 214, 254, .10)",
        ChartNeutral = "#5A6673",
        ChartNeutralDim = "#343B47",
        Overlay = "rgba(13, 16, 23, .66)"
    };

    public static DeskPalette AyuLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#EBEEF0",
        Surface1 = "#FCFCFC",
        Surface2 = "#E5E8EB",
        Surface3 = "#D6DBE0",
        TextPrimary = "rgba(2, 10, 18, .84)",
        TextSecondary = "rgba(2, 26, 60, .64)",
        TextTertiary = "rgba(2, 26, 60, .30)",
        Accent = "#F29718",
        AccentHover = "#E18C16",
        OnAccent = "#020A12",
        StatePositive = "#6CBF43",
        StateNegative = "#E65050",
        StateWarning = "#C2A30A",
        StatePositiveInk = "#417328",
        StateWarningInk = "#6F5F00",
        StateNegativeInk = "#B33E3E",
        AccentInk = "#8F590E",
        Separator = "rgba(2, 26, 60, .12)",
        ChartNeutral = "#8A9199",
        ChartNeutralDim = "#C3C9D0",
        Overlay = "rgba(2, 10, 18, .30)"
    };

    // ---- Primer ------------------------------------------------------------------
    // GitHub's Primer: the dark default by night, the light default by day. The accent is Primer's blue,
    // accent.emphasis as the fill and accent.fg as the ink; the light scrim is Primer's own overlay backdrop.

    public static DeskPalette PrimerDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#010409",
        Surface1 = "#0D1117",
        Surface2 = "#151B23",
        Surface3 = "#262C36",
        TextPrimary = "rgba(240, 246, 252, .92)",
        TextSecondary = "rgba(240, 246, 252, .58)",
        TextTertiary = "rgba(240, 246, 252, .30)",
        Accent = "#1E6BE3",
        AccentHover = "#1F6FEB",
        OnAccent = "#FFFFFF",
        StatePositive = "#3FB950",
        StateNegative = "#DA3633",
        StateWarning = "#D29922",
        StatePositiveInk = "#3FB950",
        StateWarningInk = "#D29922",
        StateNegativeInk = "#F85149",
        AccentInk = "#4493F8",
        Separator = "rgba(240, 246, 252, .15)",
        ChartNeutral = "#9198A1",
        ChartNeutralDim = "#3D444D",
        Overlay = "rgba(1, 4, 9, .66)"
    };

    public static DeskPalette PrimerLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#F6F8FA",
        Surface1 = "#FFFFFF",
        Surface2 = "#EFF2F5",
        Surface3 = "#E6EAEF",
        TextPrimary = "rgba(31, 35, 40, .92)",
        TextSecondary = "rgba(31, 35, 40, .74)",
        TextTertiary = "rgba(31, 35, 40, .34)",
        Accent = "#0969DA",
        AccentHover = "#0860CA",
        OnAccent = "#FFFFFF",
        StatePositive = "#1F883D",
        StateNegative = "#CF222E",
        StateWarning = "#9A6700",
        StatePositiveInk = "#197A35",
        StateWarningInk = "#906000",
        StateNegativeInk = "#C9232D",
        AccentInk = "#0550AE",
        Separator = "rgba(31, 35, 40, .14)",
        ChartNeutral = "#818B98",
        ChartNeutralDim = "#C8D1DA",
        Overlay = "rgba(31, 35, 40, .30)"
    };

    // ---- Night Owl ---------------------------------------------------------------
    // Sarah Drasner's Night Owl by night, Light Owl by day. The accent is the palette's blue in both: #82AAFF,
    // pale enough that a filled control takes the night ground as ink, and Light Owl's #4876D6, deepened to carry white.

    public static DeskPalette NightOwlDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#01111D",
        Surface1 = "#011627",
        Surface2 = "#0B253A",
        Surface3 = "#1D3B53",
        TextPrimary = "rgba(214, 222, 235, .92)",
        TextSecondary = "rgba(214, 222, 235, .68)",
        TextTertiary = "rgba(214, 222, 235, .32)",
        Accent = "#82AAFF",
        AccentHover = "#8FB3FF",
        OnAccent = "#011627",
        StatePositive = "#22DA6E",
        StateNegative = "#EF5350",
        StateWarning = "#FFEB95",
        StatePositiveInk = "#22DA6E",
        StateWarningInk = "#FFEB95",
        StateNegativeInk = "#EF5855",
        AccentInk = "#82AAFF",
        Separator = "rgba(214, 222, 235, .10)",
        ChartNeutral = "#637777",
        ChartNeutralDim = "#4B6479",
        Overlay = "rgba(1, 17, 29, .66)"
    };

    public static DeskPalette NightOwlLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#F0F0F0",
        Surface1 = "#FBFBFB",
        Surface2 = "#E8E8E8",
        Surface3 = "#E0E0E0",
        TextPrimary = "rgba(64, 63, 83, .92)",
        TextSecondary = "rgba(64, 63, 83, .82)",
        TextTertiary = "rgba(64, 63, 83, .36)",
        Accent = "#4571CD",
        AccentHover = "#4069BF",
        OnAccent = "#FFFFFF",
        StatePositive = "#08916A",
        StateNegative = "#D63B39",
        StateWarning = "#DAAA01",
        StatePositiveInk = "#067556",
        StateWarningInk = "#806301",
        StateNegativeInk = "#B0302F",
        AccentInk = "#3A5FAC",
        Separator = "rgba(64, 63, 83, .12)",
        ChartNeutral = "#989FB1",
        ChartNeutralDim = "#CDD1D9",
        Overlay = "rgba(64, 63, 83, .30)"
    };

    // ---- Palenight ---------------------------------------------------------------
    // Material Theme's Palenight by night, Material Lighter by day. Purple is the accent in both: Palenight's
    // #C792EA takes the ground as ink; Lighter's #7C4DFF carries white.

    public static DeskPalette PalenightDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#202331",
        Surface1 = "#292D3E",
        Surface2 = "#303348",
        Surface3 = "#3A3F58",
        TextPrimary = "rgba(238, 255, 255, .86)",
        TextSecondary = "#A6ACCD",
        TextTertiary = "#676E95",
        Accent = "#C792EA",
        AccentHover = "#CE9FED",
        OnAccent = "#292D3E",
        StatePositive = "#C3E88D",
        StateNegative = "#F07178",
        StateWarning = "#FFCB6B",
        StatePositiveInk = "#C3E88D",
        StateWarningInk = "#FFCB6B",
        StateNegativeInk = "#F38C92",
        AccentInk = "#D3A9EE",
        Separator = "rgba(166, 172, 205, .12)",
        ChartNeutral = "#676E95",
        ChartNeutralDim = "#515772",
        Overlay = "rgba(32, 35, 49, .66)"
    };

    public static DeskPalette PalenightLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#EEEEEE",
        Surface1 = "#FAFAFA",
        Surface2 = "#E7E7E8",
        Surface3 = "#D2D4D5",
        TextPrimary = "rgba(39, 39, 39, .92)",
        TextSecondary = "#495D66",
        TextTertiary = "#94A7B0",
        Accent = "#7C4DFF",
        AccentHover = "#7348ED",
        OnAccent = "#FFFFFF",
        StatePositive = "#91B859",
        StateNegative = "#D83632",
        StateWarning = "#F6A434",
        StatePositiveInk = "#566D35",
        StateWarningInk = "#8A5C1D",
        StateNegativeInk = "#B02C29",
        AccentInk = "#6841D6",
        Separator = "rgba(39, 39, 39, .11)",
        ChartNeutral = "#94A7B0",
        ChartNeutralDim = "#CCD7DA",
        Overlay = "rgba(39, 39, 39, .30)"
    };

    // ---- Zenburn -----------------------------------------------------------------
    // Jani Nurminen's Zenburn, as zenburn-emacs publishes it, lifted only where a rule needs it, and a warm
    // light counterpart built from the same hues. The accent is Zenburn blue: #8CD0D3 by night, blue-4 by day.

    public static DeskPalette ZenburnDark { get; } = new()
    {
        IsDark = true,
        Surface0 = "#383838",
        Surface1 = "#3F3F3F",
        Surface2 = "#494949",
        Surface3 = "#4F4F4F",
        TextPrimary = "rgba(220, 220, 204, 1)",
        TextSecondary = "rgba(220, 220, 204, .84)",
        TextTertiary = "rgba(220, 220, 204, .40)",
        Accent = "#8CD0D3",
        AccentHover = "#ACE0E3",
        OnAccent = "#2B2B2B",
        StatePositive = "#7F9F7F",
        StateNegative = "#CC9393",
        StateWarning = "#DFAF8F",
        StatePositiveInk = "#9FC59F",
        StateWarningInk = "#E8BD9F",
        StateNegativeInk = "#ECB3B3",
        AccentInk = "#BDE0F3",
        Separator = "rgba(220, 220, 204, .10)",
        ChartNeutral = "#989890",
        ChartNeutralDim = "#656555",
        Overlay = "rgba(43, 43, 43, .66)"
    };

    public static DeskPalette ZenburnLight { get; } = new()
    {
        IsDark = false,
        Surface0 = "#F0F0E0",
        Surface1 = "#FFFFEF",
        Surface2 = "#E8E8D8",
        Surface3 = "#DCDCCC",
        TextPrimary = "rgba(63, 63, 63, .94)",
        TextSecondary = "rgba(63, 63, 63, .82)",
        TextTertiary = "rgba(63, 63, 63, .38)",
        Accent = "#4C7073",
        AccentHover = "#46676A",
        OnAccent = "#FFFFEF",
        StatePositive = "#5F7F5F",
        StateNegative = "#9C6363",
        StateWarning = "#DFAF8F",
        StatePositiveInk = "#3F5F3F",
        StateWarningInk = "#7B604F",
        StateNegativeInk = "#8C5353",
        AccentInk = "#366060",
        Separator = "rgba(63, 63, 63, .12)",
        ChartNeutral = "#989890",
        ChartNeutralDim = "#C4C4B4",
        Overlay = "rgba(63, 63, 63, .30)"
    };
}
