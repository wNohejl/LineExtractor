namespace LineOps.Desk.Theming;

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
/// The choice belongs to the Standard family alone. Nord, Solarized, Rosé Pine, Catppuccin,
/// Gruvbox, Tokyo Night, Dracula and High contrast each chose their accent as part of what makes them themselves — Frost is half of what Nord
/// is — so a teal laid over them would be a fifth palette nobody designed. ThemeService carries
/// the choice whatever family is showing, and spends it only while Standard is.
/// </para>
///
/// <para>
/// Each accent is six tokens in CSS (<c>--accent</c>, its hover and press, the two washes and the
/// ink) redeclared under <c>[data-theme="apple-…"][data-accent="…"]</c> in themes.css, and the
/// same six here in <see cref="DeskAccents"/>. The pairing is tested, for the reason every other
/// colour crosses the seam twice: MudBlazor derives hover and darken shades from its palette, and
/// a primary handed only to CSS would hover to a blue that exists nowhere else.
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
/// One accent's six tokens on one desk. The literals are formatted exactly as the stylesheet
/// writes them, so a diff between the two files reads as the same text.
/// </summary>
/// <param name="Accent"><c>--accent</c>: the fill.</param>
/// <param name="Hover"><c>--accent-hover</c>: lighter on a dark ground, deeper on a light one.</param>
/// <param name="Press"><c>--accent-press</c>.</param>
/// <param name="Wash"><c>--accent-wash</c>: the selected-row tint.</param>
/// <param name="WashStrong"><c>--accent-wash-strong</c>: the focus halo and the pressed tint.</param>
/// <param name="Ink"><c>--accent-ink</c>: the accent as text, at 4.5:1 or better over the panel.</param>
public sealed record AccentPalette(string Accent, string Hover, string Press, string Wash, string WashStrong, string Ink);

/// <summary>
/// The accent table: every <see cref="DeskAccent"/> on the Standard desk's two appearances, with
/// its label and the attribute value that selects its block.
/// </summary>
/// <remarks>
/// <para>
/// Hover travels the way the desk's ground demands, not the way the name suggests. On the dark
/// desk prominence is lightness, so hover lightens; against white a colour that lightens under
/// the pointer reads as fading, so on the light desk hover and press both deepen. Washes drop
/// from .15/.24 to .12/.20 on the light desk because a tint over white composites to a far more
/// saturated result than the same tint over near-black.
/// </para>
/// <para>
/// The ink follows the rule Blue already keeps: on the dark desk it is the hover value, lifted
/// further where even that does not reach 4.5:1 over <c>--surface-1</c> (indigo's hover measures
/// 4.36); on the light desk it is the press value, deepened further where that does not (teal's
/// press measures 4.09 on white). A fill is seen as an area and only has to be found; an ink has
/// to be read. ThemeContrastTests computes every one.
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
        DeskAccent.Teal => new("#277A89", "#297F8F", "#24717F", "rgba(39, 122, 137, .15)", "rgba(39, 122, 137, .24)", "#5FD3E7"),
        DeskAccent.Indigo => new("#5E5CE6", "#6260E7", "#4B49D9", "rgba(94, 92, 230, .15)", "rgba(94, 92, 230, .24)", "#8E8CF0"),
        DeskAccent.Purple => new("#9E4AC8", "#A44DD0", "#9144B8", "rgba(158, 74, 200, .15)", "rgba(158, 74, 200, .24)", "#CB78F5"),
        DeskAccent.Graphite => new("#707074", "#747479", "#67676B", "rgba(112, 112, 116, .15)", "rgba(112, 112, 116, .24)", "#A1A1A6"),
        _ => new(DeskTheme.Accent, DeskTheme.AccentHover, "#0866C6", "rgba(8, 111, 215, .15)", "rgba(8, 111, 215, .24)", DeskTheme.AccentInk)
    };

    /// <summary>The accent on the Standard light desk.</summary>
    public static AccentPalette Light(DeskAccent accent) => accent switch
    {
        DeskAccent.Teal => new("#238090", "#217888", "#1F707F", "rgba(35, 128, 144, .12)", "rgba(35, 128, 144, .20)", "#1C6F7F"),
        DeskAccent.Indigo => new("#5856D6", "#4F4DC4", "#4644AD", "rgba(88, 86, 214, .12)", "rgba(88, 86, 214, .20)", "#4644AD"),
        DeskAccent.Purple => new("#A44DD1", "#9A48C4", "#9043B7", "rgba(164, 77, 209, .12)", "rgba(164, 77, 209, .20)", "#8F3DB8"),
        DeskAccent.Graphite => new("#747479", "#6D6D71", "#66666A", "rgba(116, 116, 121, .12)", "rgba(116, 116, 121, .20)", "#656569"),
        _ => new(DeskTheme.LightAccent, DeskTheme.LightAccentHover, "#0063CE", "rgba(0, 113, 235, .12)", "rgba(0, 113, 235, .20)", DeskTheme.LightAccentInk)
    };

    /// <summary>The accent in the appearance showing.</summary>
    public static AccentPalette For(DeskAccent accent, bool isDark) => isDark ? Dark(accent) : Light(accent);
}
