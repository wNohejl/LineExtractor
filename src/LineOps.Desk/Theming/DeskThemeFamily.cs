namespace LineOps.Desk.Theming;

/// <summary>
/// Which palette the desk is painted in — the other half of a theme from
/// <see cref="DeskThemeMode"/>.
///
/// <para>
/// A family is not a fourth position on the Dark/Light/System control, and that is the whole
/// design. Every family ships a dark and a light variant, so the appearance setting keeps meaning
/// what it meant: an operator on Nord under System still gets a desk that turns at sunset. Had
/// Nord been a position beside Dark and Light, choosing it would have been choosing a fixed desk,
/// and System would have stopped working for exactly the people who changed anything.
/// </para>
/// </summary>
public enum DeskThemeFamily
{
    /// <summary>The Apple desk the product was: neutral surfaces, systemBlue. The default.</summary>
    Standard,

    /// <summary>Arctic blue-greys with a frost accent.</summary>
    Nord,

    /// <summary>Ethan Schoonover's precision palette: blue-teal ground, one blue accent.</summary>
    Solarized,

    /// <summary>Muted purples and soft rose; Rosé Pine by night, Dawn by day.</summary>
    RosePine,

    /// <summary>Soft pastels on a cool slate, with a mauve accent; Catppuccin Mocha by night, Latte by day.</summary>
    Catppuccin,

    /// <summary>Warm retro browns and creams with a muted blue accent; Gruvbox dark by night, light by day.</summary>
    Gruvbox,

    /// <summary>Indigo night with a soft blue accent; Tokyo Night by night, Day by day.</summary>
    TokyoNight,

    /// <summary>Vampire purples on a blue-black night; Dracula by night, Alucard's cream by day.</summary>
    Dracula,

    /// <summary>
    /// Near-black on white, white on near-black, and hairlines you can see. For a desk read from
    /// across a room, and for eyes that need more than AA.
    /// </summary>
    HighContrast
}

/// <summary>What the desk knows about each <see cref="DeskThemeFamily"/>: its name, its attribute, its palettes.</summary>
public static class DeskThemeFamilies
{
    /// <summary>In the order the picker shows them — Standard first, because it is the desk the product is.</summary>
    public static IReadOnlyList<DeskThemeFamily> All { get; } =
    [
        DeskThemeFamily.Standard,
        DeskThemeFamily.Nord,
        DeskThemeFamily.Solarized,
        DeskThemeFamily.RosePine,
        DeskThemeFamily.Catppuccin,
        DeskThemeFamily.Gruvbox,
        DeskThemeFamily.TokyoNight,
        DeskThemeFamily.Dracula,
        DeskThemeFamily.HighContrast
    ];

    /// <summary>What the operator reads on the swatch.</summary>
    public static string Name(DeskThemeFamily family) => family switch
    {
        DeskThemeFamily.Nord => "Nord",
        DeskThemeFamily.Solarized => "Solarized",
        DeskThemeFamily.RosePine => "Rosé Pine",
        DeskThemeFamily.Catppuccin => "Catppuccin",
        DeskThemeFamily.Gruvbox => "Gruvbox",
        DeskThemeFamily.TokyoNight => "Tokyo Night",
        DeskThemeFamily.Dracula => "Dracula",
        DeskThemeFamily.HighContrast => "High contrast",
        _ => "Standard"
    };

    /// <summary>
    /// The first half of the <c>[data-theme]</c> value. Standard is <c>apple</c> because that is
    /// what its blocks were already called in desk.css, and renaming the default desk to
    /// satisfy a picker label would have been churn in the one file every component reads.
    /// </summary>
    public static string Slug(DeskThemeFamily family) => family switch
    {
        DeskThemeFamily.Nord => "nord",
        DeskThemeFamily.Solarized => "solarized",
        DeskThemeFamily.RosePine => "rose-pine",
        DeskThemeFamily.Catppuccin => "catppuccin",
        DeskThemeFamily.Gruvbox => "gruvbox",
        DeskThemeFamily.TokyoNight => "tokyo-night",
        DeskThemeFamily.Dracula => "dracula",
        DeskThemeFamily.HighContrast => "contrast",
        _ => "apple"
    };

    /// <summary>
    /// The value stamped on <c>&lt;html&gt;</c>, and the selector of the token block that answers
    /// it: <c>apple-dark</c>, <c>nord-light</c> and so on.
    /// </summary>
    public static string Attribute(DeskThemeFamily family, bool isDark)
        => $"{Slug(family)}-{(isDark ? "dark" : "light")}";

    /// <summary>The colours of one family in one appearance, as MudBlazor and the charts need them.</summary>
    public static DeskPalette Palette(DeskThemeFamily family, bool isDark) => (family, isDark) switch
    {
        (DeskThemeFamily.Nord, true) => DeskPalettes.NordDark,
        (DeskThemeFamily.Nord, false) => DeskPalettes.NordLight,
        (DeskThemeFamily.Solarized, true) => DeskPalettes.SolarizedDark,
        (DeskThemeFamily.Solarized, false) => DeskPalettes.SolarizedLight,
        (DeskThemeFamily.RosePine, true) => DeskPalettes.RosePineDark,
        (DeskThemeFamily.RosePine, false) => DeskPalettes.RosePineLight,
        (DeskThemeFamily.Catppuccin, true) => DeskPalettes.CatppuccinDark,
        (DeskThemeFamily.Catppuccin, false) => DeskPalettes.CatppuccinLight,
        (DeskThemeFamily.Gruvbox, true) => DeskPalettes.GruvboxDark,
        (DeskThemeFamily.Gruvbox, false) => DeskPalettes.GruvboxLight,
        (DeskThemeFamily.TokyoNight, true) => DeskPalettes.TokyoNightDark,
        (DeskThemeFamily.TokyoNight, false) => DeskPalettes.TokyoNightLight,
        (DeskThemeFamily.Dracula, true) => DeskPalettes.DraculaDark,
        (DeskThemeFamily.Dracula, false) => DeskPalettes.DraculaLight,
        (DeskThemeFamily.HighContrast, true) => DeskPalettes.ContrastDark,
        (DeskThemeFamily.HighContrast, false) => DeskPalettes.ContrastLight,
        (_, true) => DeskPalettes.AppleDark,
        (_, false) => DeskPalettes.AppleLight
    };
}
