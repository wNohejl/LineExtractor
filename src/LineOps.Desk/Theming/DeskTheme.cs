using LineOps.Desk.Primitives;
using MudBlazor;

namespace LineOps.Desk.Theming;

/// <summary>
/// The desk's design tokens, and the <see cref="MudTheme"/> built from them.
///
/// <para>
/// There are two audiences for these values and they need different things.
/// <c>wwwroot/css/desk.css</c> owns the desk's own components and is the file a
/// designer reads. MudBlazor needs the same colours in C#, because it derives values
/// CSS cannot: every palette entry becomes <c>--mud-palette-x</c> plus <c>-rgb</c>,
/// <c>-darken</c>, <c>-lighten</c> and <c>-hover</c>, and its own stylesheet reaches
/// for those derivations on hover and focus. Hand a colour to only one of the two and
/// a Mud component will hover to a shade that exists nowhere else in the product.
/// </para>
///
/// <para>
/// So the constants below are the pairing seam, and they must match the <c>:root</c>
/// block in desk.css exactly. They are the Standard family; the other families' values
/// sit in <see cref="DeskPalettes"/> against their blocks in <c>themes.css</c>, and every
/// family reaches MudBlazor through <see cref="For(DeskThemeFamily)"/>. Everything that is <i>not</i> a colour — radii, type,
/// elevation, z-index — is remapped in <c>wwwroot/css/mud-bridge.css</c> instead,
/// because CSS can express it and one place beats two.
/// </para>
/// </summary>
public static class DeskTheme
{
    // Surfaces — a true-neutral elevation ramp. Mirrors --surface-0 … --surface-3.
    public const string Surface0 = "#1C1C1E"; // desk void
    public const string Surface1 = "#232326"; // panel and window body
    public const string Surface2 = "#2C2C2E"; // title bars, raised rows, resting controls
    public const string Surface3 = "#38383A"; // hover fills, pressed states

    // Text. White at opacity tiers, so it keeps its relationship to any material.
    public const string TextPrimary = "rgba(255, 255, 255, .92)";
    public const string TextSecondary = "rgba(255, 255, 255, .55)";
    public const string TextTertiary = "rgba(255, 255, 255, .30)";

    // One accent. Interactivity, focus, selection — nothing else.
    public const string Accent = "#0A84FF";
    public const string AccentHover = "#3D9BFF";
    public const string OnAccent = "#FFFFFF";

    // Semantic state. Apple's dark-mode system colours, applied where a state
    // genuinely needs a colour rather than as an always-on channel.
    public const string StatePositive = "#30D158";
    public const string StateNegative = "#FF453A";
    public const string StateWarning = "#FF9F0A";

    // Inks: the same states and the accent when they colour text or a glyph rather than a
    // fill. A fill is seen as an area; text has to clear WCAG's 4.5:1 over the panel, and the
    // accent and orange do not (4.30 and, on the light desk, far less). Mirrors --*-ink.
    public const string StatePositiveInk = "#30D158";
    public const string StateWarningInk = "#FFB340";
    public const string StateNegativeInk = "#FF6961";
    public const string AccentInk = "#409CFF";

    public const string Separator = "rgba(255, 255, 255, .10)";

    // Chart neutrals. Two steps of Apple's dark-mode system grey ramp, kept opaque on
    // purpose: a chart's neutral series must stay one fixed colour, and a translucent
    // text token composites differently depending on what is drawn beneath it. Mirrors
    // --chart-neutral / --chart-neutral-dim.
    public const string ChartNeutral = "#8E8E93";
    public const string ChartNeutralDim = "#636366";

    // ---- The light desk -----------------------------------------------------
    //
    // The same roles against a pale ground, mirroring the [data-theme="apple-light"] block
    // in desk.css value for value. They are constants on the same class rather
    // than a second class because the pairing is per-token, not per-theme: what has
    // to be kept honest is that LightSurface1 and --surface-1 agree, and a reader
    // checking that should not have to open a third file to do it.
    //
    // The surface ramp does not invert — it re-derives. The ground is grey and the
    // panel is white, which is the arrangement macOS uses and the opposite direction
    // of travel from the dark ramp; the tokens survive it because they are named for
    // jobs. The full reasoning is in the stylesheet, next to the values.

    public const string LightSurface0 = "#F2F2F7"; // desk ground — systemGray6, light
    public const string LightSurface1 = "#FFFFFF"; // panel and window body
    public const string LightSurface2 = "#E5E5EA"; // title bars, raised rows, resting controls
    public const string LightSurface3 = "#D1D1D6"; // hover fills, pressed states

    // Black at opacity tiers. Secondary measures 5.74:1 over LightSurface1 — AA, and
    // within three hundredths of the dark desk's 5.71:1.
    public const string LightTextPrimary = "rgba(0, 0, 0, .88)";
    public const string LightTextSecondary = "rgba(0, 0, 0, .60)";
    public const string LightTextTertiary = "rgba(0, 0, 0, .30)";

    // systemBlue, light. Hover deepens rather than lightens: against white, a blue
    // that lightens under the pointer reads as fading rather than as answering.
    public const string LightAccent = "#007AFF";
    public const string LightAccentHover = "#0071E3";
    public const string LightOnAccent = "#FFFFFF";

    public const string LightStatePositive = "#34C759";
    public const string LightStateNegative = "#FF3B30";
    public const string LightStateWarning = "#FF9500";

    // Apple's increased-contrast values: the light fills measure 2.2 to 3.6:1 as text on
    // white, these 4.54 to 4.56. The accent ink is --accent-press, 5.80:1.
    public const string LightStatePositiveInk = "#008932";
    public const string LightStateWarningInk = "#C55300";
    public const string LightStateNegativeInk = "#E9152D";
    public const string LightAccentInk = "#0062CC";

    public const string LightSeparator = "rgba(0, 0, 0, .10)";

    // systemGray holds across both themes; the dim step moves up rather than down,
    // because dimmer means closer to the plot background and that is now pale.
    public const string LightChartNeutral = "#8E8E93";
    public const string LightChartNeutralDim = "#AEAEB2";

    /// <summary>
    /// The modal scrim on the light desk.
    /// </summary>
    /// <remarks>
    /// The one value that is deliberately <i>not</i> the light mirror of its dark
    /// counterpart. The dark scrim is <c>--surface-0</c> at 62% because dimming a
    /// near-black desk means laying more of the same near-black over it. Doing the
    /// analogous thing here — the pale ground at 62% — would <i>brighten</i> the
    /// desk behind a sheet, which is the opposite of what a scrim is for. A scrim
    /// subtracts attention, and on any ground that means going darker. So: neutral
    /// black at 28%, the alpha tuned down from the dark desk's because black over
    /// white bites far harder than near-black over near-black.
    /// </remarks>
    public const string LightOverlay = "rgba(0, 0, 0, .28)";

    /// <summary>
    /// The spacing ramp. One 4px scale for the whole desk, mirroring <c>--space-1</c> …
    /// <c>--space-16</c>. Components take a <see cref="DeskSpace"/> step rather than a
    /// pixel count, so a layout can be re-tuned in one place.
    /// </summary>
    /// <remarks>
    /// Returns the custom property rather than the pixel value on purpose: the number
    /// then lives in exactly one file, and a layout that inlines <c>var(--space-3)</c>
    /// re-tunes with the stylesheet instead of freezing whatever C# thought 12px was.
    /// </remarks>
    public static string SpaceVar(DeskSpace step) => step switch
    {
        DeskSpace.None => "0",
        DeskSpace.Space1 => "var(--space-1)",
        DeskSpace.Space2 => "var(--space-2)",
        DeskSpace.Space3 => "var(--space-3)",
        DeskSpace.Space4 => "var(--space-4)",
        DeskSpace.Space6 => "var(--space-6)",
        DeskSpace.Space8 => "var(--space-8)",
        DeskSpace.Space12 => "var(--space-12)",
        DeskSpace.Space16 => "var(--space-16)",
        _ => "var(--space-2)"
    };

    // Type and radii used to be mirrored here as C# constants. They are not any more, and
    // deliberately so: nothing outside a colour needs to cross into C#, because MudBlazor
    // derives nothing from a font size or a radius the way it derives -hover and -darken
    // from a palette colour. Type and radius live once, in desk.css, and reach Mud
    // through mud-bridge.css. A second copy here only ever drifts — the copy this comment
    // replaces had gone stale in nine values and was read by nothing.

    /// <summary>
    /// The dark desk's modal scrim: the desk void at 62%, matching <see cref="Surface0"/> channel
    /// for channel. MudBlazor paints <c>.mud-overlay-scrim.mud-overlay-dark</c> straight from the
    /// palette's <c>OverlayDark</c>, so there is no CSS token for it to mirror.
    /// </summary>
    public const string Overlay = "rgba(28, 28, 30, .62)";

    private static readonly Dictionary<DeskThemeFamily, MudTheme> Themes = DeskThemeFamilies.All
        .ToDictionary(family => family, family => Build(
            DeskThemeFamilies.Palette(family, isDark: true),
            DeskThemeFamilies.Palette(family, isDark: false)));

    /// <summary>
    /// The Standard desk's theme, carrying both its palettes. What <see cref="For(DeskThemeFamily)"/> returns for
    /// <see cref="DeskThemeFamily.Standard"/>, kept by this name because it is what a call site
    /// with no family to hand should get.
    /// </summary>
    public static MudTheme Instance => Themes[DeskThemeFamily.Standard];

    /// <summary>
    /// One family's theme, carrying its dark and light palettes.
    ///
    /// <para>
    /// Static and built once per family, because a theme never varies per circuit: which family
    /// is showing is <c>ThemeService.Family</c> and which of its two palettes is live is
    /// <see cref="MudThemeProvider.IsDarkMode"/>, both per-circuit and both driven by
    /// <c>ThemeService</c>. The desk's own components consult neither palette — they read the
    /// <c>[data-theme]</c> token block. These exist so MudBlazor's derivations (<c>-hover</c>,
    /// <c>-darken</c>, <c>-rgb</c>) land on the desk's colours in whichever desk is showing.
    /// </para>
    /// </summary>
    public static MudTheme For(DeskThemeFamily family)
        => Themes.TryGetValue(family, out var theme) ? theme : Instance;

    /// <summary>
    /// The Standard desk's theme in every chosen accent but Blue, which is <see cref="Instance"/>
    /// itself. Built once, like the families: an accent never varies per circuit either.
    /// </summary>
    private static readonly Dictionary<DeskAccent, MudTheme> Accented = DeskAccents.All
        .Where(accent => accent != DeskAccent.Blue)
        .ToDictionary(accent => accent, accent => Build(
            DeskPalettes.AppleDark.WithAccent(DeskAccents.Dark(accent)),
            DeskPalettes.AppleLight.WithAccent(DeskAccents.Light(accent))));

    /// <summary>
    /// One family's theme wearing the operator's accent. The accent is the Standard family's
    /// alone (see <see cref="DeskAccent"/>), so every other family gets exactly what
    /// <see cref="For(DeskThemeFamily)"/> gives it, the same instance, whatever is chosen.
    /// </summary>
    public static MudTheme For(DeskThemeFamily family, DeskAccent accent)
        => family == DeskThemeFamily.Standard && Accented.TryGetValue(accent, out var theme)
            ? theme
            : For(family);

    private static MudTheme Build(DeskPalette dark, DeskPalette light) => new()
    {
        // The two palettes are filled by the same method on purpose: they used to be two
        // hand-written blocks kept structurally identical so a lost entry showed up by reading
        // down two columns. One method makes a lost entry impossible instead of visible.
        PaletteDark = Fill(new PaletteDark(), dark),
        PaletteLight = Fill(new PaletteLight(), light),

        Typography = new Typography
        {
            // Only the default face is set here. Per-role sizing and tracking live in
            // mud-bridge.css next to the rest of the type, so there is one file to read
            // when the type scale changes rather than two that can disagree.
            //
            // This mirrors --face-ui in desk.css, and it is not decoration: the bridge
            // re-points --mud-typography-*-family at --face-ui for every role it knows
            // about, but a role it misses falls through to whatever is named here. That
            // used to be Archivo, which no longer loads, so the fallback was silently
            // Segoe UI on Windows — the exact failure the bundled Inter exists to prevent.
            // FontSize matches --text-body (13px) at 100% text size; the operator's text
            // scale reaches Mud through the bridge, which reads the scaled token.
            Default = new DefaultTypography
            {
                FontFamily = ["-apple-system", "BlinkMacSystemFont", "Inter var", "Inter", "Segoe UI", "system-ui", "sans-serif"],
                FontSize = "13px"
            }
        }
    };

    private static T Fill<T>(T palette, DeskPalette desk) where T : Palette
    {
        // MudBlazor's literal black and white rather than roles. On a dark desk "black" is the
        // void and "white" is ink; on a light desk a component reaching for "black" wants ink,
        // not the ground it is printed on — so the two swap with the appearance.
        palette.Black = desk.IsDark ? desk.Surface0 : desk.TextPrimary;
        palette.White = desk.IsDark ? desk.TextPrimary : desk.Surface1;

        palette.Background = desk.Surface0;
        palette.BackgroundGray = desk.Surface0;
        palette.Surface = desk.Surface1;
        palette.DrawerBackground = desk.Surface1;
        palette.AppbarBackground = desk.Surface2;

        palette.DrawerText = desk.TextPrimary;
        palette.DrawerIcon = desk.TextSecondary;
        palette.AppbarText = desk.TextPrimary;

        palette.TextPrimary = desk.TextPrimary;
        palette.TextSecondary = desk.TextSecondary;
        palette.TextDisabled = desk.TextTertiary;

        // Cannot be out-specified: .mud-button-root:disabled sets colour with
        // !important. Agreeing with the desk beats fighting the stylesheet.
        palette.ActionDefault = desk.TextSecondary;
        palette.ActionDisabled = desk.TextTertiary;
        palette.ActionDisabledBackground = desk.Surface2;

        palette.LinesDefault = desk.Separator;
        palette.LinesInputs = desk.Separator;
        palette.Divider = desk.Separator;
        palette.DividerLight = desk.Separator;
        palette.TableLines = desk.Separator;
        palette.TableStriped = desk.Surface2;
        palette.TableHover = desk.Surface2;

        // The modal scrim, and the only place it can be set. MudBlazor paints it on
        // .mud-overlay-scrim.mud-overlay-dark, whose background-color is literally
        // var(--mud-palette-overlay-dark) — this value, whichever palette is live. The
        // overlay root carries .mud-overlay-dialog, but that class only sets z-index and
        // the scrim child covers it edge to edge, so a bridge rule aimed there paints nothing.
        palette.OverlayDark = desk.Overlay;

        palette.GrayDefault = desk.Surface2;
        palette.GrayLight = desk.Surface3;
        palette.GrayLighter = desk.TextSecondary;
        palette.GrayDark = desk.Surface2;
        palette.GrayDarker = desk.Surface1;

        // One accent. Interactivity, focus, selection — nothing else.
        palette.Primary = desk.Accent;
        palette.PrimaryContrastText = desk.OnAccent;
        palette.Info = desk.Accent;
        palette.InfoContrastText = desk.OnAccent;
        palette.Success = desk.StatePositive;
        palette.SuccessContrastText = desk.OnAccent;
        palette.Error = desk.StateNegative;
        palette.ErrorContrastText = desk.OnAccent;
        palette.Warning = desk.StateWarning;
        palette.WarningContrastText = desk.OnAccent;

        // Secondary and Tertiary have no meaning on this desk — there is no second
        // brand colour, only states. Pointing them at the accent means a component
        // that reaches for one degrades to the desk's one interactive colour rather
        // than importing Material's pink.
        palette.Secondary = desk.Accent;
        palette.SecondaryContrastText = desk.OnAccent;
        palette.Tertiary = desk.Accent;
        palette.TertiaryContrastText = desk.OnAccent;

        palette.Dark = desk.Surface2;
        palette.DarkContrastText = desk.TextPrimary;

        return palette;
    }
}
