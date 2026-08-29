// TEMPLATE — rename the namespace below to your own (this file assumes
// YourApp.Theming). Wire it up in your layout:
//
//     <MudThemeProvider Theme="AppleTheme.Instance" IsDarkMode="true" />
//
// Every colour constant here MUST equal the matching custom property in
// apple-tokens.css, character for character. See references/mudblazor-seam.md.

using MudBlazor;

namespace YourApp.Theming;

/// <summary>
/// The design tokens, and the <see cref="MudTheme"/> built from them.
///
/// <para>
/// There are two audiences for these values and they need different things.
/// <c>apple-tokens.css</c> owns the application's own components and is the file a
/// designer reads. MudBlazor needs the same colours in C#, because it derives values
/// CSS cannot: every palette entry becomes <c>--mud-palette-x</c> plus <c>-rgb</c>,
/// <c>-darken</c>, <c>-lighten</c> and <c>-hover</c>, and its own stylesheet reaches
/// for those derivations on hover and focus. Hand a colour to only one of the two and
/// a Mud component will hover to a shade that exists nowhere else in the product.
/// </para>
///
/// <para>
/// So the constants below are the pairing seam, and they must match the <c>:root</c>
/// block in apple-tokens.css exactly. Everything that is <i>not</i> a colour — radii,
/// type, elevation, z-index — is remapped in <c>apple-bridge.css</c> instead, because
/// CSS can express it and one place beats two.
/// </para>
/// </summary>
public static class AppleTheme
{
    // Surfaces — a true-neutral elevation ramp. Mirrors --surface-0 … --surface-3.
    public const string Surface0 = "#1C1C1E"; // the ground everything sits on
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

    /// <summary>
    /// The single theme instance. Static because it never varies per circuit. A light
    /// theme is a second [data-theme] token block in CSS, not a second theme object —
    /// the palette below is the dark one because MudBlazor needs *a* palette in C#, not
    /// because themes live here.
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
            // !important. Agreeing with the system beats fighting the stylesheet, and
            // this is the single strongest reason the palette must exist in C# at all.
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

            OverlayDark = "rgba(8,10,16,0.62)",

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

            // Secondary and Tertiary have no meaning in this system — there is no second
            // brand colour, only states. Pointing them at the accent means a component
            // that reaches for one degrades to the one interactive colour rather than
            // importing Material's pink.
            Secondary = Accent,
            SecondaryContrastText = OnAccent,
            Tertiary = Accent,
            TertiaryContrastText = OnAccent,

            Dark = Surface2,
            DarkContrastText = TextPrimary
        },

        Typography = new Typography
        {
            // Only the default face is set here. Per-role sizing and tracking live in
            // apple-bridge.css next to the rest of the type, so there is one file to read
            // when the type scale changes rather than two that can disagree.
            Default = new DefaultTypography
            {
                FontFamily = ["-apple-system", "BlinkMacSystemFont", "Inter var", "Inter", "Segoe UI", "system-ui", "sans-serif"],
                FontSize = "13px"
            }
        }
    };
}
