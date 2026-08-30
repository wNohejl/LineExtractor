using System.Text.RegularExpressions;
using LineOps.Web.Theming;
using MudBlazor.Utilities;

namespace LineOps.Web.Tests;

/// <summary>
/// The pairing seam from ADR 0008, still load-bearing under ADR 0016.
///
/// MudBlazor derives values CSS cannot — every palette entry becomes
/// --mud-palette-x plus -rgb, -darken, -lighten and -hover, and its stylesheet
/// reaches for those on hover and focus. A colour handed to only one of the two
/// sides produces a hover shade that exists nowhere else in the product. These
/// tests are what stops the two drifting.
/// </summary>
public class DeskThemeTests
{
    private static readonly string Css =
        File.ReadAllText(Path.Combine(RepoRoot(), "src/LineOps.Web/wwwroot/css/lineops.css"));

    [Theory]
    [InlineData("--surface-0", "#1C1C1E")]
    [InlineData("--surface-1", "#232326")]
    [InlineData("--surface-2", "#2C2C2E")]
    [InlineData("--surface-3", "#38383A")]
    [InlineData("--accent", "#0A84FF")]
    [InlineData("--state-positive", "#30D158")]
    [InlineData("--state-negative", "#FF453A")]
    [InlineData("--state-warning", "#FF9F0A")]
    [InlineData("--text-primary", "rgba(255, 255, 255, .92)")]
    [InlineData("--text-secondary", "rgba(255, 255, 255, .55)")]
    [InlineData("--text-tertiary", "rgba(255, 255, 255, .30)")]
    [InlineData("--separator", "rgba(255, 255, 255, .10)")]
    [InlineData("--chart-neutral", "#8E8E93")]
    [InlineData("--chart-neutral-dim", "#636366")]
    public void Css_declares_the_colour_token(string token, string expected)
    {
        Assert.Equal(expected, TokenValue(token));
    }

    /// <summary>
    /// Each C# constant must be the same colour the stylesheet declares. Compared through
    /// <see cref="MudColor"/> rather than as raw strings: MudBlazor's own alpha/channel
    /// parsing tolerates ".92" vs "0.92" and comma spacing, and normalizing both sides the
    /// same way still fails on a genuinely wrong channel or alpha value. The C# literals
    /// are also kept formatted identically to the CSS (see DeskTheme.cs) so a human
    /// diffing the two files sees the same text, not just the same colour.
    /// </summary>
    [Theory]
    [InlineData("--surface-0", nameof(DeskTheme.Surface0))]
    [InlineData("--surface-1", nameof(DeskTheme.Surface1))]
    [InlineData("--surface-2", nameof(DeskTheme.Surface2))]
    [InlineData("--surface-3", nameof(DeskTheme.Surface3))]
    [InlineData("--accent", nameof(DeskTheme.Accent))]
    [InlineData("--state-positive", nameof(DeskTheme.StatePositive))]
    [InlineData("--state-negative", nameof(DeskTheme.StateNegative))]
    [InlineData("--state-warning", nameof(DeskTheme.StateWarning))]
    [InlineData("--text-primary", nameof(DeskTheme.TextPrimary))]
    [InlineData("--text-secondary", nameof(DeskTheme.TextSecondary))]
    [InlineData("--text-tertiary", nameof(DeskTheme.TextTertiary))]
    [InlineData("--separator", nameof(DeskTheme.Separator))]
    [InlineData("--chart-neutral", nameof(DeskTheme.ChartNeutral))]
    [InlineData("--chart-neutral-dim", nameof(DeskTheme.ChartNeutralDim))]
    public void Csharp_mirrors_the_stylesheet(string token, string constantName)
    {
        var constant = (string)typeof(DeskTheme)
            .GetField(constantName)!
            .GetValue(null)!;

        Assert.Equal(ColorValue(TokenValue(token)), ColorValue(constant), ignoreCase: true);
    }

    [Fact]
    public void The_theme_hands_mudblazor_the_desk_palette()
    {
        var theme = DeskTheme.Instance;
        var dark = theme.PaletteDark;

        // MudColor.Value always normalizes to a lowercase 8-digit "#rrggbbaa" string,
        // regardless of whether the source was a 6-digit hex or an rgba(...) literal
        // (see MudBlazor.Utilities.MudColor). Comparing DeskTheme's raw constant against
        // that normalized form directly can never match character-for-character, so both
        // sides are normalized through MudColor before comparing.
        Assert.Equal(ColorValue(DeskTheme.Accent), dark.Primary.Value, ignoreCase: true);
        Assert.Equal(ColorValue(DeskTheme.Surface0), dark.Background.Value, ignoreCase: true);
        Assert.Equal(ColorValue(DeskTheme.Surface1), dark.Surface.Value, ignoreCase: true);
        Assert.Equal(ColorValue(DeskTheme.StatePositive), dark.Success.Value, ignoreCase: true);
        Assert.Equal(ColorValue(DeskTheme.StateNegative), dark.Error.Value, ignoreCase: true);
        Assert.Equal(ColorValue(DeskTheme.StateWarning), dark.Warning.Value, ignoreCase: true);

        // The modal scrim. There is no bridge rule for it and there cannot usefully be one:
        // MudBlazor paints .mud-overlay-scrim.mud-overlay-dark straight from
        // --mud-palette-overlay-dark, so this palette entry IS the scrim every DeskSheet
        // and DeskAlert opens over. Pinned to the desk void's own channels at 62% — the
        // pre-migration value was a blue graphite (8,10,16) and differs in all three, so a
        // regression cannot slip past a semantic MudColor comparison.
        Assert.Equal(ColorValue("rgba(28, 28, 30, .62)"), ColorValue(dark.OverlayDark), ignoreCase: true);

        var scrim = new MudColor(dark.OverlayDark);
        var voidTier = new MudColor(DeskTheme.Surface0);
        Assert.Equal((voidTier.R, voidTier.G, voidTier.B), (scrim.R, scrim.G, scrim.B));
    }

    /// <summary>
    /// ADR 0008's hard-won consequence: `.mud-button-root:disabled` sets its colour with
    /// !important, so it cannot be out-specified from the bridge. The palette therefore has
    /// to agree with the desk rather than fight it.
    /// </summary>
    [Fact]
    public void Disabled_action_colour_agrees_with_the_desks_dim_text()
    {
        var dark = DeskTheme.Instance.PaletteDark;

        Assert.Equal(ColorValue(DeskTheme.TextTertiary), dark.ActionDisabled.Value, ignoreCase: true);
    }

    /// <summary>Normalizes a colour literal the same way MudColor does, for apples-to-apples comparison.</summary>
    private static string ColorValue(string cssColor) => new MudColor(cssColor).Value;

    private static string TokenValue(string token)
    {
        var match = Regex.Match(Css, Regex.Escape(token) + @"\s*:\s*([^;]+);");

        Assert.True(match.Success, $"{token} is not declared in lineops.css");

        return match.Groups[1].Value.Trim();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LineOps.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);

        return dir!.FullName;
    }
}
