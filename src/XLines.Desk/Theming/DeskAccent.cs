namespace XLines.Desk.Theming;

/// <summary>
/// The one accent, chosen.
///
/// <para>
/// The desk spends one hue on interactivity, focus and selection, and nothing else. Which hue is
/// not a design decision; it is the operator's, exactly as it is in macOS's own appearance
/// settings. Every position here is a hue the state colours do not use — there is no red, orange
/// or green, because a positive tag and a primary key that shared a hue would stop meaning
/// different things.
/// </para>
///
/// <para>
/// These are the Standard family's accents. Every family offers a list of its own
/// (<see cref="DeskThemeFamilyInfo.Accents"/>), its own accent first; Standard's list is built from
/// this enum, so the five hues it has always offered, and the names stored under
/// <c>desk.accent</c> before the lists were per family, keep working. Another family's accents are
/// designed for its ground — a teal laid over Nord's would be a palette nobody drew — so they are
/// declared in its own row and blocks, never borrowed from here. ThemeService remembers the choice
/// per family and spends the one belonging to the family showing.
/// </para>
///
/// <para>
/// Each accent is four tokens in CSS (<c>--accent</c>, its hover and press, and the ink) redeclared
/// under <c>[data-theme="apple-…"][data-accent="…"]</c> in themes.css, its two washes derived from
/// the fill. Only what runs crosses into C#: the fill, which MudBlazor derives its hover and darken
/// shades from and the charts draw with, and its hover beside it (<see cref="AccentPalette"/>). The
/// pairing is tested, for the reason every other colour crosses the seam twice: a primary handed
/// only to CSS would hover to a blue that exists nowhere else.
/// </para>
/// </summary>
public enum DeskAccent
{
    /// <summary>systemBlue. The default, and the desk the product has always been.</summary>
    Blue,

    /// <summary>systemTeal.</summary>
    Teal,

    /// <summary>systemIndigo.</summary>
    Indigo,

    /// <summary>systemPurple.</summary>
    Purple,

    /// <summary>systemGray. Interactivity without a hue at all — macOS's graphite.</summary>
    Graphite
}

/// <summary>
/// One accent on one desk, as far as it crosses into C#: the fill MudBlazor and the charts wear,
/// and its hover, which <see cref="DeskPalette.WithAccent"/> carries beside it, and the ink where a
/// chart needs it. The press and the washes live in the stylesheet alone, where the contrast tests
/// measure them. The
/// literals are formatted exactly as the block writes them, so a diff reads as the same text.
/// </summary>
/// <param name="Accent"><c>--accent</c>: the fill.</param>
/// <param name="Hover"><c>--accent-hover</c>: lighter on a dark ground, deeper on a light one.</param>
/// <param name="Ink">
/// <c>--accent-ink</c>, carried only where the fill measures under 3:1 on its family's panel: that
/// is the one case a chart draws the accent series in the ink rather than the fill
/// (<see cref="Primitives.DeskChartPalette"/>). Left null, the fill stands in for it.
/// </param>
public sealed record AccentPalette(string Accent, string Hover, string? Ink = null);

/// <summary>
/// The accent table: every <see cref="DeskAccent"/> on the Standard desk's two appearances, with
/// its label and the attribute value that selects its block.
/// </summary>
/// <remarks>
/// <para>
/// Hover travels the way the desk's ground demands, not the way the name suggests. On the dark
/// desk prominence is lightness, so hover lightens; against white a colour that lightens under
/// the pointer reads as fading, so on the light desk hover and press both deepen. The press, the
/// washes and the ink are the blocks' alone (themes.css explains them); ThemeContrastTests
/// measures every ink there.
/// </para>
/// </remarks>
public static class DeskAccents
{
    /// <summary>In the order the picker shows them — Blue first, because it is the default.</summary>
    public static IReadOnlyList<DeskAccent> All { get; } =
        [DeskAccent.Blue, DeskAccent.Teal, DeskAccent.Indigo, DeskAccent.Purple, DeskAccent.Graphite];

    /// <summary>
    /// The <c>data-accent</c> value stamped on <c>&lt;html&gt;</c>, matching the selectors in
    /// themes.css. Blue has a key but no block: it is what the Standard blocks already declare.
    /// </summary>
    public static string Key(DeskAccent accent) => accent.ToString().ToLowerInvariant();

    /// <summary>What the operator hears on the swatch.</summary>
    public static string Label(DeskAccent accent) => accent switch
    {
        DeskAccent.Teal => "Teal",
        DeskAccent.Indigo => "Indigo",
        DeskAccent.Purple => "Purple",
        DeskAccent.Graphite => "Graphite",
        _ => "Blue"
    };

    /// <summary>The accent on the Standard dark desk.</summary>
    public static AccentPalette Dark(DeskAccent accent) => accent switch
    {
        DeskAccent.Teal => new("#277A89", "#297F8F"),
        DeskAccent.Indigo => new("#5E5CE6", "#6260E7"),
        DeskAccent.Purple => new("#9E4AC8", "#A44DD0"),
        DeskAccent.Graphite => new("#707074", "#747479"),
        _ => new(DeskTheme.Accent, DeskTheme.AccentHover)
    };

    /// <summary>The accent on the Standard light desk.</summary>
    public static AccentPalette Light(DeskAccent accent) => accent switch
    {
        DeskAccent.Teal => new("#238090", "#217888"),
        DeskAccent.Indigo => new("#5856D6", "#4F4DC4"),
        DeskAccent.Purple => new("#A44DD1", "#9A48C4"),
        DeskAccent.Graphite => new("#747479", "#6D6D71"),
        _ => new(DeskTheme.LightAccent, DeskTheme.LightAccentHover)
    };

    /// <summary>The accent in the appearance showing.</summary>
    public static AccentPalette For(DeskAccent accent, bool isDark) => isDark ? Dark(accent) : Light(accent);

    /// <summary>
    /// Standard's row of <see cref="DeskThemeFamilyInfo.Accents"/>: every hue above, Blue first, with
    /// both palettes — Blue's included, which DeskThemeTests holds to the Standard blocks themselves.
    /// </summary>
    public static IReadOnlyList<DeskFamilyAccent> Standard { get; } = All
        .Select(accent => new DeskFamilyAccent(Key(accent), Label(accent), Dark(accent), Light(accent)))
        .ToList();
}

/// <summary>
/// Every family's accents but Standard's, one list per family, its own accent first. Each list is
/// what its catalogue row hands <see cref="DeskThemeFamilyInfo.Accents"/>: the first entry names the
/// accent the family's own blocks declare and carries no palettes; every later one carries both,
/// formatted exactly as its <c>[data-theme="&lt;slug&gt;-dark"][data-accent="&lt;key&gt;"]</c> and
/// <c>-light</c> blocks in themes.css write them. High contrast offers its own alone.
/// </summary>
/// <remarks>
/// A family's accents are drawn from its own palette, so the names are its own where it has them
/// (Nord's Frost, Dracula's Comment). A neutral that is the family's grey is called Graphite, as
/// Standard's is, wherever the family gives it no name of its own.
/// </remarks>
public static class DeskFamilyAccents
{
    public static IReadOnlyList<DeskFamilyAccent> Paper { get; } =
    [
        new("ink", "Ink blue"),
        new("sepia", "Sepia",
            Dark: new AccentPalette("#7E5B37", "#85603A", Ink: "#D2AA80"),
            Light: new AccentPalette("#7A5430", "#6D4A2A")),
        new("plum", "Plum",
            Dark: new AccentPalette("#7A4470", "#834A78", Ink: "#CF9CC4"),
            Light: new AccentPalette("#6E3A64", "#623358")),
        new("verdigris", "Verdigris",
            Dark: new AccentPalette("#2D6A64", "#32716B", Ink: "#84C4BA"),
            Light: new AccentPalette("#2A5F5A", "#24544F"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> Midnight { get; } =
    [
        new("electric", "Electric"),
        new("violet", "Violet",
            Dark: new AccentPalette("#A98BFF", "#BCA3FF"),
            Light: new AccentPalette("#5B21F0", "#4F1AD6")),
        new("magenta", "Magenta",
            Dark: new AccentPalette("#FF4FE0", "#FF78E8"),
            Light: new AccentPalette("#B8009E", "#A0008A")),
        new("mono", "Mono",
            Dark: new AccentPalette("#EDEDED", "#FFFFFF"),
            Light: new AccentPalette("#1C1C1C", "#0E0E0E"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> Obsidian { get; } =
    [
        new("sky", "Sky"),
        new("violet", "Violet",
            Dark: new AccentPalette("#A78BFA", "#BAA4FB"),
            Light: new AccentPalette("#6D28D9", "#6023BF")),
        new("slate", "Slate",
            Dark: new AccentPalette("#94A3B8", "#ACB7C8"),
            Light: new AccentPalette("#475569", "#3E4B5C"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> Synthwave { get; } =
    [
        new("magenta", "Magenta"),
        new("cyan", "Cyan",
            Dark: new AccentPalette("#2DE2FF", "#63EAFF"),
            Light: new AccentPalette("#006F8A", "#00647C")),
        new("ultraviolet", "Ultraviolet",
            Dark: new AccentPalette("#A77BFF", "#B994FF"),
            Light: new AccentPalette("#6A24D9", "#5E1FC2")),
        new("chrome", "Chrome",
            Dark: new AccentPalette("#C9C1EA", "#DAD4F2"),
            Light: new AccentPalette("#564C78", "#4C436B"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> Terra { get; } =
    [
        new("clay", "Clay"),
        new("slate", "Slate",
            Dark: new AccentPalette("#4E6878", "#536E7F", Ink: "#9DB6C5"),
            Light: new AccentPalette("#4A6372", "#425866")),
        new("heather", "Heather",
            Dark: new AccentPalette("#77597C", "#7D5E82", Ink: "#C3A6C7"),
            Light: new AccentPalette("#6E5373", "#634A67")),
        new("stone", "Stone",
            Dark: new AccentPalette("#69655B", "#6E6A60", Ink: "#B4AE9F"),
            Light: new AccentPalette("#67635A", "#5C5850"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> Everforest { get; } =
    [
        new("blue", "Blue"),
        new("purple", "Purple",
            Dark: new AccentPalette("#D699B6", "#DAA3BD"),
            Light: new AccentPalette("#B35391", "#A64D87")),
        new("aqua", "Aqua",
            Dark: new AccentPalette("#83C092", "#8FC69D"),
            Light: new AccentPalette("#328361", "#2E7A5A")),
        new("graphite", "Graphite",
            Dark: new AccentPalette("#A0ACA3", "#AAB4AC"),
            Light: new AccentPalette("#6A7969", "#637162"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> Kanagawa { get; } =
    [
        new("blue", "Blue"),
        new("violet", "Violet",
            Dark: new AccentPalette("#9E88C1", "#A894C7"),
            Light: new AccentPalette("#624C83", "#5B477A")),
        new("aqua", "Aqua",
            Dark: new AccentPalette("#7AA89F", "#87B1A9"),
            Light: new AccentPalette("#597B75", "#53726D")),
        new("graphite", "Graphite",
            Dark: new AccentPalette("#93928A", "#9E9D96"),
            Light: new AccentPalette("#716E61", "#69665A"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> One { get; } =
    [
        new("blue", "Blue"),
        new("purple", "Purple",
            Dark: new AccentPalette("#CE82DD", "#D38EE0"),
            Light: new AccentPalette("#A626A4", "#9A2399")),
        new("cyan", "Cyan",
            Dark: new AccentPalette("#56B6C2", "#67BDC8"),
            Light: new AccentPalette("#017DAE", "#0174A2")),
        new("graphite", "Graphite",
            Dark: new AccentPalette("#989FAD", "#A2A9B5"),
            Light: new AccentPalette("#696C77", "#62646F"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> Monokai { get; } =
    [
        new("cyan", "Cyan"),
        new("purple", "Purple",
            Dark: new AccentPalette("#AE81FF", "#B68EFF"),
            Light: new AccentPalette("#8060BB", "#7759AE")),
        new("yellow", "Yellow",
            Dark: new AccentPalette("#E6DB74", "#E8DF82"),
            Light: new AccentPalette("#787332", "#706B2E")),
        new("graphite", "Graphite",
            Dark: new AccentPalette("#9E9A8D", "#A8A498"),
            Light: new AccentPalette("#75715E", "#6D6957"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> Ayu { get; } =
    [
        new("gold", "Gold"),
        new("blue", "Blue",
            Dark: new AccentPalette("#59C2FF", "#6AC8FF"),
            Light: new AccentPalette("#22A4E6", "#2099D6", Ink: "#166A94")),
        new("purple", "Purple",
            Dark: new AccentPalette("#D2A6FF", "#D6AFFF"),
            Light: new AccentPalette("#A37ACC", "#9871BE")),
        new("teal", "Teal",
            Dark: new AccentPalette("#95E6CB", "#A0E8D0"),
            Light: new AccentPalette("#4CBF99", "#47B28E", Ink: "#2D715A"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> Primer { get; } =
    [
        new("blue", "Blue"),
        new("purple", "Purple",
            Dark: new AccentPalette("#8454DD", "#8957E5"),
            Light: new AccentPalette("#8250DF", "#7445CC")),
        new("pink", "Pink",
            Dark: new AccentPalette("#B84885", "#BF4B8A"),
            Light: new AccentPalette("#BF3989", "#A8337C")),
        new("graphite", "Graphite",
            Dark: new AccentPalette("#656C76", "#6E757F"),
            Light: new AccentPalette("#59636E", "#4F5862"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> NightOwl { get; } =
    [
        new("blue", "Blue"),
        new("purple", "Purple",
            Dark: new AccentPalette("#C792EA", "#CE9FED"),
            Light: new AccentPalette("#994CC3", "#8E47B5")),
        new("teal", "Teal",
            Dark: new AccentPalette("#7FDBCA", "#8EDFD0"),
            Light: new AccentPalette("#22827A", "#207971")),
        new("slate", "Slate",
            Dark: new AccentPalette("#7390A6", "#819BAF"),
            Light: new AccentPalette("#5B7890", "#546E84"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> Palenight { get; } =
    [
        new("purple", "Purple"),
        new("blue", "Blue",
            Dark: new AccentPalette("#82AAFF", "#91B4FF"),
            Light: new AccentPalette("#5775A6", "#516D9A")),
        new("cyan", "Cyan",
            Dark: new AccentPalette("#89DDFF", "#97E1FF"),
            Light: new AccentPalette("#2A8086", "#27777D")),
        new("pale-blue", "Pale blue",
            Dark: new AccentPalette("#B2CCD6", "#BBD2DB"),
            Light: new AccentPalette("#697589", "#626D7F"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> Zenburn { get; } =
    [
        new("blue", "Blue"),
        new("sky", "Sky",
            Dark: new AccentPalette("#94BFF3", "#A4C9F5"),
            Light: new AccentPalette("#5C7697", "#566E8C")),
        new("magenta", "Magenta",
            Dark: new AccentPalette("#DC8CC3", "#E19DCC"),
            Light: new AccentPalette("#9A6288", "#8F5B7E")),
        new("graphite", "Graphite",
            Dark: new AccentPalette("#A0A098", "#ACACA4"),
            Light: new AccentPalette("#656555", "#5E5E4F"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> Nord { get; } =
    [
        new("frost", "Frost"),
        new("teal", "Frost teal",
            Dark: new AccentPalette("#8FBCBB", "#A0C6C5"),
            Light: new AccentPalette("#5D7A7A", "#577373")),
        new("purple", "Aurora purple",
            Dark: new AccentPalette("#BF9FB9", "#C9ADC4"),
            Light: new AccentPalette("#896C83", "#81667B"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> Solarized { get; } =
    [
        new("blue", "Blue"),
        new("violet", "Violet",
            Dark: new AccentPalette("#6469B6", "#6A6FB9"),
            Light: new AccentPalette("#696EBE", "#6367B3")),
        new("cyan", "Cyan",
            Dark: new AccentPalette("#207C75", "#29817B"),
            Light: new AccentPalette("#22827B", "#207A74")),
        new("slate", "Slate",
            Dark: new AccentPalette("#586E75", "#5F747B", Ink: "#97A5A9"),
            Light: new AccentPalette("#586E75", "#53676E"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> RosePine { get; } =
    [
        new("iris", "Iris"),
        new("subtle", "Subtle",
            Dark: new AccentPalette("#908CAA", "#A19DB7"),
            Light: new AccentPalette("#75718F", "#6E6A86"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> Catppuccin { get; } =
    [
        new("mauve", "Mauve"),
        new("pink", "Pink",
            Dark: new AccentPalette("#F5C2E7", "#F6CBEB"),
            Light: new AccentPalette("#AD5796", "#A3528D")),
        new("blue", "Blue",
            Dark: new AccentPalette("#89B4FA", "#9BBFFB"),
            Light: new AccentPalette("#1E66F5", "#1C60E6")),
        new("sapphire", "Sapphire",
            Dark: new AccentPalette("#74C7EC", "#89CFEF"),
            Light: new AccentPalette("#1A8193", "#18798A"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> Gruvbox { get; } =
    [
        new("blue", "Blue"),
        new("purple", "Purple",
            Dark: new AccentPalette("#D3869B", "#DA98AA"),
            Light: new AccentPalette("#8F3F71", "#863B6A")),
        new("graphite", "Graphite",
            Dark: new AccentPalette("#A5988C", "#B2A79D"),
            Light: new AccentPalette("#776B60", "#70655A"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> TokyoNight { get; } =
    [
        new("blue", "Blue"),
        new("magenta", "Magenta",
            Dark: new AccentPalette("#BB9AF7", "#C5A9F8"),
            Light: new AccentPalette("#9351EA", "#8A4CDC")),
        new("purple", "Purple",
            Dark: new AccentPalette("#9F7FD9", "#AD92DF"),
            Light: new AccentPalette("#7847BD", "#7143B2")),
        new("cyan", "Cyan",
            Dark: new AccentPalette("#7DCFFF", "#90D6FF"),
            Light: new AccentPalette("#007197", "#006A8E"))
    ];

    public static IReadOnlyList<DeskFamilyAccent> Dracula { get; } =
    [
        new("purple", "Purple"),
        new("cyan", "Cyan",
            Dark: new AccentPalette("#8BE9FD", "#9CECFD"),
            Light: new AccentPalette("#036A96", "#03648D")),
        new("comment", "Comment",
            Dark: new AccentPalette("#808DB5", "#939EC0"),
            Light: new AccentPalette("#6C664B", "#666046"))
    ];
}
