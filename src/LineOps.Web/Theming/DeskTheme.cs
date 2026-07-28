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
    // Surface — cool graphite, five steps. Mirrors --ink-900 … --ink-400.
    public const string Ink900 = "#0E1119"; // desk void
    public const string Ink800 = "#141926"; // rail
    public const string Ink700 = "#1B2231"; // window body
    public const string Ink600 = "#232C3E"; // title bars, raised rows
    public const string Ink500 = "#2E3950"; // borders, dividers
    public const string Ink400 = "#3C4A66"; // hover borders

    // Text.
    public const string Chalk = "#E6EBF4";
    public const string Haze = "#8695B0";
    public const string HazeDim = "#5D6B85";

    // The dial. Hue = state, always.
    public const string Steam = "#35E0A1"; // healthy · moved your way · win
    public const string Drift = "#FF6B81"; // breached · moved against you · loss
    public const string Flag = "#FFB84D"; // warn · budget pressure · pending
    public const string Iris = "#7C8CFF"; // interactive · focus · selection

    /// <summary>
    /// Ink for text sitting on a saturated key. The dial colours are light enough that
    /// only a near-black label clears contrast on them, and each is tinted toward its
    /// own hue so the cap reads as one moulded piece rather than a sticker on a colour.
    /// Kept in step with the <c>--key-ink</c> values in lineops.css.
    /// </summary>
    public const string OnIris = "#0C1020";
    public const string OnSteam = "#06170F";
    public const string OnDrift = "#1E070C";
    public const string OnFlag = "#1C1204";

    /// <summary>
    /// The single theme instance. Static because it never varies per circuit — the desk
    /// is dark, and a light palette would have to answer what gloss means on paper.
    /// </summary>
    public static readonly MudTheme Instance = new()
    {
        PaletteDark = new PaletteDark
        {
            Black = Ink900,
            White = Chalk,

            Background = Ink900,
            BackgroundGray = Ink800,
            Surface = Ink700,
            DrawerBackground = Ink800,
            AppbarBackground = Ink800,

            DrawerText = Chalk,
            DrawerIcon = Haze,
            AppbarText = Chalk,

            TextPrimary = Chalk,
            TextSecondary = Haze,
            TextDisabled = HazeDim,

            // MudBlazor forces the disabled colour with !important, which is the one
            // place its stylesheet cannot be out-specified. Setting it to the desk's dim
            // text is how a disabled key ends up the right colour instead of Material's.
            ActionDefault = Haze,
            ActionDisabled = HazeDim,
            ActionDisabledBackground = Ink600,

            LinesDefault = Ink500,
            LinesInputs = Ink400,
            Divider = Ink500,
            DividerLight = Ink600,
            TableLines = Ink600,
            TableStriped = Ink600,
            TableHover = Ink600,

            OverlayDark = "rgba(8,10,16,0.62)",

            GrayDefault = Ink500,
            GrayLight = Ink400,
            GrayLighter = Haze,
            GrayDark = Ink600,
            GrayDarker = Ink700,

            // The dial. A Mud component that colours itself Success is saying "healthy",
            // which is the same sentence a green pulse strip makes.
            Primary = Iris,
            PrimaryContrastText = OnIris,
            Info = Iris,
            InfoContrastText = OnIris,
            Success = Steam,
            SuccessContrastText = OnSteam,
            Error = Drift,
            ErrorContrastText = OnDrift,
            Warning = Flag,
            WarningContrastText = OnFlag,

            // Secondary and Tertiary have no meaning on this desk — there is no second
            // brand colour, only states. Pointing them at the neutral cap means a
            // component that reaches for one degrades to graphite rather than importing
            // Material's pink.
            Secondary = Ink600,
            SecondaryContrastText = Chalk,
            Tertiary = Ink600,
            TertiaryContrastText = Chalk,

            Dark = Ink600,
            DarkContrastText = Chalk
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
