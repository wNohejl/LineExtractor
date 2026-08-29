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

    // Type styles. Named for the job, never the size. Mirrors --text-* / --leading-*.
    public const string TextTitle = "10.5px";   // panel headings
    public const string TextCaption = "11px";   // labels, units, annotation
    public const string TextBody = "13px";      // the default reading size
    public const string TextCallout = "14px";   // read this before its neighbours

    public const string LeadingTitle = "1.4";
    public const string LeadingCaption = "1.45";
    public const string LeadingBody = "1.5";
    public const string LeadingCallout = "1.45";

    // Radii. Radius is the desk default and does not move. Mirrors --radius-*.
    public const string RadiusSm = "4px";
    public const string Radius = "6px";
    public const string RadiusLg = "10px";

    /// <summary>
    /// The single theme instance. Static because it never varies per circuit — the desk
    /// is dark. A light desk is a second [data-theme] token block, not a second theme object.
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

        Typography = new Typography
        {
            // Only the default face is set here. Per-role sizing and tracking live in
            // mud-bridge.css next to the rest of the type, so there is one file to read
            // when the type scale changes rather than two that can disagree.
            Default = new DefaultTypography
            {
                FontFamily = ["Archivo", "Segoe UI", "system-ui", "sans-serif"],
                FontSize = "13px"
            }
        }
    };
}
