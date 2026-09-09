using LineOps.Web.Components.Desk;
using MudBlazor;

namespace LineOps.Web.Theming;

/// <summary>
/// The desk's design tokens, and the <see cref="MudTheme"/> built from them.
///
/// <para>
/// There are two audiences for these values and they need different things.
/// <c>wwwroot/css/lineops.css</c> owns the desk's own components and is the file a
/// designer reads. MudBlazor needs the same colours in C#, because it derives values
/// CSS cannot: every palette entry becomes <c>--mud-palette-x</c> plus <c>-rgb</c>,
/// <c>-darken</c>, <c>-lighten</c> and <c>-hover</c>, and its own stylesheet reaches
/// for those derivations on hover and focus. Hand a colour to only one of the two and
/// a Mud component will hover to a shade that exists nowhere else in the product.
/// </para>
///
/// <para>
/// So the constants below are the pairing seam, and they must match the <c>:root</c>
/// block in lineops.css exactly. Everything that is <i>not</i> a colour — radii, type,
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

    public const string Separator = "rgba(255, 255, 255, .10)";

    // Chart neutrals. Two steps of Apple's dark-mode system grey ramp, kept opaque on
    // purpose: a chart's neutral series must stay one fixed colour, and a translucent
    // text token composites differently depending on what is drawn beneath it. Mirrors
    // --chart-neutral / --chart-neutral-dim.
    public const string ChartNeutral = "#8E8E93";
    public const string ChartNeutralDim = "#636366";

    // ---- The light desk -----------------------------------------------------
    //
    // The same roles against a pale ground, mirroring the [data-theme="light"] block
    // in lineops.css value for value. They are constants on the same class rather
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
    // from a palette colour. Type and radius live once, in lineops.css, and reach Mud
    // through mud-bridge.css. A second copy here only ever drifts — the copy this comment
    // replaces had gone stale in nine values and was read by nothing.

    /// <summary>
    /// The single theme instance, carrying both palettes.
    ///
    /// <para>
    /// Static because it still never varies per circuit: which of the two palettes is
    /// live is <see cref="MudThemeProvider.IsDarkMode"/>, a per-circuit flag driven by
    /// <c>ThemeService</c>, not a second theme object. The desk's own components do not
    /// consult either palette — they read the <c>[data-theme]</c> token block, and these
    /// two are here only so MudBlazor's derivations (<c>-hover</c>, <c>-darken</c>,
    /// <c>-rgb</c>) land on the desk's colours in whichever theme is showing.
    /// </para>
    /// </summary>
    public static readonly MudTheme Instance = new()
    {
        PaletteDark = new PaletteDark
        {
            Black = Surface0,
            White = TextPrimary,

            Background = Surface0,
            BackgroundGray = Surface0,
            Surface = Surface1,
            DrawerBackground = Surface1,
            AppbarBackground = Surface2,

            DrawerText = TextPrimary,
            DrawerIcon = TextSecondary,
            AppbarText = TextPrimary,

            TextPrimary = TextPrimary,
            TextSecondary = TextSecondary,
            TextDisabled = TextTertiary,

            // Cannot be out-specified: .mud-button-root:disabled sets colour with
            // !important. Agreeing with the desk beats fighting the stylesheet.
            ActionDefault = TextSecondary,
            ActionDisabled = TextTertiary,
            ActionDisabledBackground = Surface2,

            LinesDefault = Separator,
            LinesInputs = Separator,
            Divider = Separator,
            DividerLight = Separator,
            TableLines = Separator,
            TableStriped = Surface2,
            TableHover = Surface2,

            // The modal scrim, and the only place it can be set. MudBlazor paints it on
            // .mud-overlay-scrim.mud-overlay-dark, whose background-color is literally
            // var(--mud-palette-overlay-dark) — this value. The overlay root carries
            // .mud-overlay-dialog, but that class only sets z-index and the scrim child
            // covers it edge to edge, so a bridge rule aimed there paints nothing. The
            // desk void at 62%, matching Surface0 channel for channel.
            OverlayDark = "rgba(28, 28, 30, .62)",

            GrayDefault = Surface2,
            GrayLight = Surface3,
            GrayLighter = TextSecondary,
            GrayDark = Surface2,
            GrayDarker = Surface1,

            // One accent. Interactivity, focus, selection — nothing else.
            Primary = Accent,
            PrimaryContrastText = OnAccent,
            Info = Accent,
            InfoContrastText = OnAccent,
            Success = StatePositive,
            SuccessContrastText = OnAccent,
            Error = StateNegative,
            ErrorContrastText = OnAccent,
            Warning = StateWarning,
            WarningContrastText = OnAccent,

            // Secondary and Tertiary have no meaning on this desk — there is no second
            // brand colour, only states. Pointing them at the accent means a component
            // that reaches for one degrades to the desk's one interactive colour rather
            // than importing Material's pink.
            Secondary = Accent,
            SecondaryContrastText = OnAccent,
            Tertiary = Accent,
            TertiaryContrastText = OnAccent,

            Dark = Surface2,
            DarkContrastText = TextPrimary
        },

        // The same entries, in the same order, pointing at the light constants. Kept
        // structurally identical to the block above on purpose: the two palettes are a
        // diff, and a reader checking that the light desk did not quietly lose an entry
        // should be able to do it by reading down two columns rather than by reasoning.
        PaletteLight = new PaletteLight
        {
            // Not a mirror of PaletteDark's. These two entries are MudBlazor's literal
            // black and white rather than roles, and a component reaching for "black" on
            // a light desk wants ink, not the ground it is printed on.
            Black = LightTextPrimary,
            White = LightSurface1,

            Background = LightSurface0,
            BackgroundGray = LightSurface0,
            Surface = LightSurface1,
            DrawerBackground = LightSurface1,
            AppbarBackground = LightSurface2,

            DrawerText = LightTextPrimary,
            DrawerIcon = LightTextSecondary,
            AppbarText = LightTextPrimary,

            TextPrimary = LightTextPrimary,
            TextSecondary = LightTextSecondary,
            TextDisabled = LightTextTertiary,

            // Same reason as the dark desk: .mud-button-root:disabled sets colour with
            // !important, so the palette agrees with the desk instead of fighting it.
            ActionDefault = LightTextSecondary,
            ActionDisabled = LightTextTertiary,
            ActionDisabledBackground = LightSurface2,

            LinesDefault = LightSeparator,
            LinesInputs = LightSeparator,
            Divider = LightSeparator,
            DividerLight = LightSeparator,
            TableLines = LightSeparator,
            TableStriped = LightSurface2,
            TableHover = LightSurface2,

            // Still the only place the scrim can be set — MudBlazor paints
            // .mud-overlay-scrim.mud-overlay-dark straight from this entry, whichever
            // palette is live. See LightOverlay for why it is not the pale ground at 62%.
            OverlayDark = LightOverlay,

            GrayDefault = LightSurface2,
            GrayLight = LightSurface3,
            GrayLighter = LightTextSecondary,
            GrayDark = LightSurface2,
            GrayDarker = LightSurface1,

            Primary = LightAccent,
            PrimaryContrastText = LightOnAccent,
            Info = LightAccent,
            InfoContrastText = LightOnAccent,
            Success = LightStatePositive,
            SuccessContrastText = LightOnAccent,
            Error = LightStateNegative,
            ErrorContrastText = LightOnAccent,
            Warning = LightStateWarning,
            WarningContrastText = LightOnAccent,

            Secondary = LightAccent,
            SecondaryContrastText = LightOnAccent,
            Tertiary = LightAccent,
            TertiaryContrastText = LightOnAccent,

            Dark = LightSurface2,
            DarkContrastText = LightTextPrimary
        },

        Typography = new Typography
        {
            // Only the default face is set here. Per-role sizing and tracking live in
            // mud-bridge.css next to the rest of the type, so there is one file to read
            // when the type scale changes rather than two that can disagree.
            //
            // This mirrors --face-ui in lineops.css, and it is not decoration: the bridge
            // re-points --mud-typography-*-family at --face-ui for every role it knows
            // about, but a role it misses falls through to whatever is named here. That
            // used to be Archivo, which no longer loads, so the fallback was silently
            // Segoe UI on Windows — the exact failure the bundled Inter exists to prevent.
            // FontSize matches --text-body (13px).
            Default = new DefaultTypography
            {
                FontFamily = ["-apple-system", "BlinkMacSystemFont", "Inter var", "Inter", "Segoe UI", "system-ui", "sans-serif"],
                FontSize = "13px"
            }
        }
    };
}
