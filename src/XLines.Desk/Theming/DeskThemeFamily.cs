namespace XLines.Desk.Theming;

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
    HighContrast,

    /// <summary>A ledger by lamplight and by day: ivory laid paper and iron-gall ink, with one blue-black pen as the accent.</summary>
    Paper,

    /// <summary>True black for an OLED panel, surfaces told apart by hairlines; an electric cyan by night, ultramarine on stark white by day.</summary>
    Midnight,

    /// <summary>A terminal's carbon: blue-black panels ruled by slate hairlines, an electric sky accent; its own printout by day.</summary>
    Obsidian,

    /// <summary>Neon on an indigo sky: hot magenta keys with the night as their label, laser-green, sunset and warning-light states; pale lavender by day.</summary>
    Synthwave,

    /// <summary>Muted earth: umber and charcoal-olive by night, limestone and sand by day, terracotta keys with a sand label.</summary>
    Terra,

    /// <summary>Warm green-greys and soft pastels; Everforest medium, dark and light.</summary>
    Everforest,

    /// <summary>Sumi ink and faded woodblock hues; Kanagawa Wave by night, Lotus by day.</summary>
    Kanagawa,

    /// <summary>Atom's cool slate and clean primaries; One Dark by night, One Light by day.</summary>
    One,

    /// <summary>Wimer Hazenberg's olive-black and neon syntax hues, and the same palette turned over onto its paper by day.</summary>
    Monokai,

    /// <summary>Ink-black and paper-white with a warm gold accent; Ayu Dark by night, Ayu Light by day.</summary>
    Ayu,

    /// <summary>GitHub's Primer: its dark default by night, its light default by day, with Primer's blue as the accent.</summary>
    Primer,

    /// <summary>Sarah Drasner's deep-sea navy with a soft blue accent; Night Owl by night, Light Owl by day.</summary>
    NightOwl,

    /// <summary>Material's lavender slate with a purple accent; Palenight by night, Material Lighter by day.</summary>
    Palenight,

    /// <summary>Zenburn's low-contrast warm greys for long sittings, with its pale blue as the accent, and a warm light desk from the same hues.</summary>
    Zenburn
}

/// <summary>
/// One accent a family offers: the <c>data-accent</c> key that selects its blocks, the name the
/// picker gives it, and its fill and hover (<see cref="AccentPalette"/>) on the family's two desks.
/// </summary>
/// <remarks>
/// A family's first accent is the one its own blocks already declare. It stamps nothing, has no
/// block of its own, and its palettes may be left null — the family's <see cref="DeskPalette"/> is
/// what MudBlazor and the charts wear while it is chosen. Every later accent carries both palettes,
/// formatted exactly as its <c>[data-theme="&lt;slug&gt;-dark"][data-accent="&lt;key&gt;"]</c> and
/// <c>-light</c> blocks in themes.css write them; DeskThemeTests holds the two to each other.
/// </remarks>
/// <param name="Key">Lower-case, hyphenated: the <c>data-accent</c> value, and what is stored.</param>
/// <param name="Name">What the operator hears on the swatch.</param>
/// <param name="Dark">Its fill and hover on the family's dark desk, all of an accent block that crosses into C#; null only for the family's own.</param>
/// <param name="Light">Its fill and hover on the family's light desk; null only for the family's own.</param>
public sealed record DeskFamilyAccent(string Key, string Name, AccentPalette? Dark = null, AccentPalette? Light = null)
{
    /// <summary>The accent in the appearance showing, or null for a family's own (see the remarks).</summary>
    public AccentPalette? For(bool isDark) => isDark ? Dark : Light;
}

/// <summary>
/// Everything the desk knows about one <see cref="DeskThemeFamily"/>, in one row: the picker, the
/// stylesheet's selectors, the boot script's table, MudBlazor's palettes and the tests all read
/// this and nothing else, so adding a family is adding a row (README, "Adding a family").
/// </summary>
/// <param name="Family">The enum member. Its name is what is stored, read case-insensitively.</param>
/// <param name="Name">What the operator reads under the tile.</param>
/// <param name="Slug">The first half of the <c>[data-theme]</c> value: <c>nord</c> in <c>nord-dark</c>.</param>
/// <param name="Group">The plain heading the picker files the tile under (<see cref="DeskThemeGroups"/>).</param>
/// <param name="Dark">The dark desk's colours, mirroring <c>[data-theme="&lt;slug&gt;-dark"]</c>.</param>
/// <param name="Light">The light desk's colours, mirroring <c>[data-theme="&lt;slug&gt;-light"]</c>.</param>
/// <param name="Accents">The accents it offers, its own first. One entry means the picker hides the row.</param>
public sealed record DeskThemeFamilyInfo(
    DeskThemeFamily Family,
    string Name,
    string Slug,
    string Group,
    DeskPalette Dark,
    DeskPalette Light,
    IReadOnlyList<DeskFamilyAccent> Accents)
{
    /// <summary>The accent the family's own blocks declare — the first, which stamps nothing.</summary>
    public DeskFamilyAccent OwnAccent => Accents[0];

    /// <summary>The accent with this key, read case-insensitively, or null when the family offers none by it.</summary>
    public DeskFamilyAccent? FindAccent(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var wanted = key.Trim();
        foreach (var accent in Accents)
        {
            if (string.Equals(accent.Key, wanted, StringComparison.OrdinalIgnoreCase))
                return accent;
        }

        return null;
    }
}

/// <summary>
/// The plain headings the theme picker files its tiles under. A family names one; the picker shows
/// them in the order their first family appears in <see cref="DeskThemeFamilies.Catalog"/>. A new
/// heading is a new constant here, used by at least one row — never a heading with nothing under it.
/// </summary>
public static class DeskThemeGroups
{
    /// <summary>The desk's own: Standard, and High contrast for the eyes that need more.</summary>
    public const string BuiltIn = "Built in";

    /// <summary>Desks drawn from a material rather than an editor: paper, a black panel, neon, earth.</summary>
    public const string Character = "Character";

    /// <summary>Low-contrast, muted palettes that keep the desk quiet: pastels, frost, forest greens, warm greys.</summary>
    public const string Soft = "Soft";

    /// <summary>Saturated palettes with clear, bright hues on deep grounds and crisp light ones.</summary>
    public const string Vivid = "Vivid";
}

/// <summary>What the desk knows about each <see cref="DeskThemeFamily"/>: its name, its attribute, its palettes.</summary>
public static class DeskThemeFamilies
{
    /// <summary>
    /// The families, one row each, in the order the picker shows them — Standard first, because it
    /// is the desk the product is, and each group's families together.
    /// </summary>
    public static IReadOnlyList<DeskThemeFamilyInfo> Catalog { get; } =
    [
        new(DeskThemeFamily.Standard, "Standard", "apple", DeskThemeGroups.BuiltIn,
            DeskPalettes.AppleDark, DeskPalettes.AppleLight, DeskAccents.Standard),
        new(DeskThemeFamily.HighContrast, "High contrast", "contrast", DeskThemeGroups.BuiltIn,
            DeskPalettes.ContrastDark, DeskPalettes.ContrastLight, [new("blue", "Blue")]),
        new(DeskThemeFamily.Paper, "Paper", "paper", DeskThemeGroups.Character,
            DeskPalettes.PaperDark, DeskPalettes.PaperLight, DeskFamilyAccents.Paper),
        new(DeskThemeFamily.Midnight, "Midnight", "midnight", DeskThemeGroups.Character,
            DeskPalettes.MidnightDark, DeskPalettes.MidnightLight, DeskFamilyAccents.Midnight),
        new(DeskThemeFamily.Obsidian, "Obsidian", "obsidian", DeskThemeGroups.Character,
            DeskPalettes.ObsidianDark, DeskPalettes.ObsidianLight, DeskFamilyAccents.Obsidian),
        new(DeskThemeFamily.Synthwave, "Synthwave", "synthwave", DeskThemeGroups.Character,
            DeskPalettes.SynthwaveDark, DeskPalettes.SynthwaveLight, DeskFamilyAccents.Synthwave),
        new(DeskThemeFamily.Terra, "Terra", "terra", DeskThemeGroups.Character,
            DeskPalettes.TerraDark, DeskPalettes.TerraLight, DeskFamilyAccents.Terra),
        new(DeskThemeFamily.Nord, "Nord", "nord", DeskThemeGroups.Soft,
            DeskPalettes.NordDark, DeskPalettes.NordLight, DeskFamilyAccents.Nord),
        new(DeskThemeFamily.RosePine, "Rosé Pine", "rose-pine", DeskThemeGroups.Soft,
            DeskPalettes.RosePineDark, DeskPalettes.RosePineLight, DeskFamilyAccents.RosePine),
        new(DeskThemeFamily.Catppuccin, "Catppuccin", "catppuccin", DeskThemeGroups.Soft,
            DeskPalettes.CatppuccinDark, DeskPalettes.CatppuccinLight, DeskFamilyAccents.Catppuccin),
        new(DeskThemeFamily.Everforest, "Everforest", "everforest", DeskThemeGroups.Soft,
            DeskPalettes.EverforestDark, DeskPalettes.EverforestLight, DeskFamilyAccents.Everforest),
        new(DeskThemeFamily.Kanagawa, "Kanagawa", "kanagawa", DeskThemeGroups.Soft,
            DeskPalettes.KanagawaDark, DeskPalettes.KanagawaLight, DeskFamilyAccents.Kanagawa),
        new(DeskThemeFamily.Zenburn, "Zenburn", "zenburn", DeskThemeGroups.Soft,
            DeskPalettes.ZenburnDark, DeskPalettes.ZenburnLight, DeskFamilyAccents.Zenburn),
        new(DeskThemeFamily.Palenight, "Palenight", "palenight", DeskThemeGroups.Soft,
            DeskPalettes.PalenightDark, DeskPalettes.PalenightLight, DeskFamilyAccents.Palenight),
        new(DeskThemeFamily.Solarized, "Solarized", "solarized", DeskThemeGroups.Soft,
            DeskPalettes.SolarizedDark, DeskPalettes.SolarizedLight, DeskFamilyAccents.Solarized),
        new(DeskThemeFamily.Gruvbox, "Gruvbox", "gruvbox", DeskThemeGroups.Soft,
            DeskPalettes.GruvboxDark, DeskPalettes.GruvboxLight, DeskFamilyAccents.Gruvbox),
        new(DeskThemeFamily.Dracula, "Dracula", "dracula", DeskThemeGroups.Vivid,
            DeskPalettes.DraculaDark, DeskPalettes.DraculaLight, DeskFamilyAccents.Dracula),
        new(DeskThemeFamily.TokyoNight, "Tokyo Night", "tokyo-night", DeskThemeGroups.Vivid,
            DeskPalettes.TokyoNightDark, DeskPalettes.TokyoNightLight, DeskFamilyAccents.TokyoNight),
        new(DeskThemeFamily.Monokai, "Monokai", "monokai", DeskThemeGroups.Vivid,
            DeskPalettes.MonokaiDark, DeskPalettes.MonokaiLight, DeskFamilyAccents.Monokai),
        new(DeskThemeFamily.One, "One", "one", DeskThemeGroups.Vivid,
            DeskPalettes.OneDark, DeskPalettes.OneLight, DeskFamilyAccents.One),
        new(DeskThemeFamily.NightOwl, "Night Owl", "night-owl", DeskThemeGroups.Vivid,
            DeskPalettes.NightOwlDark, DeskPalettes.NightOwlLight, DeskFamilyAccents.NightOwl),
        new(DeskThemeFamily.Ayu, "Ayu", "ayu", DeskThemeGroups.Vivid,
            DeskPalettes.AyuDark, DeskPalettes.AyuLight, DeskFamilyAccents.Ayu),
        new(DeskThemeFamily.Primer, "Primer", "primer", DeskThemeGroups.Vivid,
            DeskPalettes.PrimerDark, DeskPalettes.PrimerLight, DeskFamilyAccents.Primer)
    ];

    private static readonly Dictionary<DeskThemeFamily, DeskThemeFamilyInfo> ByFamily =
        Catalog.ToDictionary(info => info.Family);

    /// <summary>The families alone, in the catalogue's order.</summary>
    public static IReadOnlyList<DeskThemeFamily> All { get; } = Catalog.Select(info => info.Family).ToList();

    /// <summary>The picker's headings, in the order their first family appears.</summary>
    public static IReadOnlyList<string> Groups { get; } = Catalog.Select(info => info.Group).Distinct().ToList();

    /// <summary>One family's row; Standard's for a member the catalogue does not hold.</summary>
    public static DeskThemeFamilyInfo Info(DeskThemeFamily family)
        => ByFamily.TryGetValue(family, out var info) ? info : ByFamily[DeskThemeFamily.Standard];

    /// <summary>What the operator reads on the swatch.</summary>
    public static string Name(DeskThemeFamily family) => Info(family).Name;

    /// <summary>
    /// The first half of the <c>[data-theme]</c> value. Standard is <c>apple</c> because that is
    /// what its blocks were already called in desk.css, and renaming the default desk to
    /// satisfy a picker label would have been churn in the one file every component reads.
    /// </summary>
    public static string Slug(DeskThemeFamily family) => Info(family).Slug;

    /// <summary>The accents one family offers, its own first.</summary>
    public static IReadOnlyList<DeskFamilyAccent> Accents(DeskThemeFamily family) => Info(family).Accents;

    /// <summary>
    /// The value stamped on <c>&lt;html&gt;</c>, and the selector of the token block that answers
    /// it: <c>apple-dark</c>, <c>nord-light</c> and so on.
    /// </summary>
    public static string Attribute(DeskThemeFamily family, bool isDark)
        => $"{Slug(family)}-{(isDark ? "dark" : "light")}";

    /// <summary>The colours of one family in one appearance, as MudBlazor and the charts need them.</summary>
    public static DeskPalette Palette(DeskThemeFamily family, bool isDark)
        => isDark ? Info(family).Dark : Info(family).Light;
}
