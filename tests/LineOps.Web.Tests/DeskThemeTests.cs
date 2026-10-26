using System.Text.RegularExpressions;
using LineOps.Desk.Primitives;
using LineOps.Desk.Theming;
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
        File.ReadAllText(Path.Combine(RepoRoot(), "src/LineOps.Desk/wwwroot/css/lineops.css"));

    /// <summary>The other families' blocks, loaded after lineops.css. See the "Every desk" section below.</summary>
    private static readonly string ThemesCss =
        File.ReadAllText(Path.Combine(RepoRoot(), "src/LineOps.Desk/wwwroot/css/themes.css"));

    /// <summary>
    /// The two token blocks, comments stripped.
    ///
    /// <para>
    /// Block-scoped rather than file-scoped because the same token name now appears twice
    /// in the file with two different values, and a whole-file regex would silently return
    /// whichever came first — which is to say the dark one, for every light assertion,
    /// which is to say a suite that passes without checking anything.
    /// </para>
    ///
    /// <para>
    /// Comments come out first because the blocks explain themselves at length and prose
    /// naming a token is not a declaration of it.
    /// </para>
    /// </summary>
    private static readonly string DarkBlock = Block("\\[data-theme=\"apple-dark\"\\]");

    private static readonly string LightBlock = Block("\\[data-theme=\"apple-light\"\\]");

    [Theory]
    [InlineData("--surface-0", "#1C1C1E")]
    [InlineData("--surface-1", "#232326")]
    [InlineData("--surface-2", "#2C2C2E")]
    [InlineData("--surface-3", "#38383A")]
    [InlineData("--accent", "#0A84FF")]
    [InlineData("--state-positive", "#30D158")]
    [InlineData("--state-negative", "#FF453A")]
    [InlineData("--state-warning", "#FF9F0A")]
    [InlineData("--state-positive-ink", "#30D158")]
    [InlineData("--state-warning-ink", "#FFB340")]
    [InlineData("--state-negative-ink", "#FF6961")]
    [InlineData("--accent-ink", "#409CFF")]
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
    [InlineData("--state-positive-ink", nameof(DeskTheme.StatePositiveInk))]
    [InlineData("--state-warning-ink", nameof(DeskTheme.StateWarningInk))]
    [InlineData("--state-negative-ink", nameof(DeskTheme.StateNegativeInk))]
    [InlineData("--accent-ink", nameof(DeskTheme.AccentInk))]
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

    // ---- The light desk -----------------------------------------------------

    [Theory]
    [InlineData("--surface-0", "#F2F2F7")]
    [InlineData("--surface-1", "#FFFFFF")]
    [InlineData("--surface-2", "#E5E5EA")]
    [InlineData("--surface-3", "#D1D1D6")]
    [InlineData("--accent", "#007AFF")]
    [InlineData("--state-positive", "#34C759")]
    [InlineData("--state-negative", "#FF3B30")]
    [InlineData("--state-warning", "#FF9500")]
    [InlineData("--state-positive-ink", "#008932")]
    [InlineData("--state-warning-ink", "#C55300")]
    [InlineData("--state-negative-ink", "#E9152D")]
    [InlineData("--accent-ink", "#0062CC")]
    [InlineData("--text-primary", "rgba(0, 0, 0, .88)")]
    [InlineData("--text-secondary", "rgba(0, 0, 0, .60)")]
    [InlineData("--text-tertiary", "rgba(0, 0, 0, .30)")]
    [InlineData("--separator", "rgba(0, 0, 0, .10)")]
    [InlineData("--chart-neutral", "#8E8E93")]
    [InlineData("--chart-neutral-dim", "#AEAEB2")]
    public void Light_block_declares_the_colour_token(string token, string expected)
    {
        Assert.Equal(expected, LightTokenValue(token));
    }

    [Theory]
    [InlineData("--surface-0", nameof(DeskTheme.LightSurface0))]
    [InlineData("--surface-1", nameof(DeskTheme.LightSurface1))]
    [InlineData("--surface-2", nameof(DeskTheme.LightSurface2))]
    [InlineData("--surface-3", nameof(DeskTheme.LightSurface3))]
    [InlineData("--accent", nameof(DeskTheme.LightAccent))]
    [InlineData("--accent-hover", nameof(DeskTheme.LightAccentHover))]
    [InlineData("--on-accent", nameof(DeskTheme.LightOnAccent))]
    [InlineData("--state-positive", nameof(DeskTheme.LightStatePositive))]
    [InlineData("--state-negative", nameof(DeskTheme.LightStateNegative))]
    [InlineData("--state-warning", nameof(DeskTheme.LightStateWarning))]
    [InlineData("--state-positive-ink", nameof(DeskTheme.LightStatePositiveInk))]
    [InlineData("--state-warning-ink", nameof(DeskTheme.LightStateWarningInk))]
    [InlineData("--state-negative-ink", nameof(DeskTheme.LightStateNegativeInk))]
    [InlineData("--accent-ink", nameof(DeskTheme.LightAccentInk))]
    [InlineData("--text-primary", nameof(DeskTheme.LightTextPrimary))]
    [InlineData("--text-secondary", nameof(DeskTheme.LightTextSecondary))]
    [InlineData("--text-tertiary", nameof(DeskTheme.LightTextTertiary))]
    [InlineData("--separator", nameof(DeskTheme.LightSeparator))]
    [InlineData("--chart-neutral", nameof(DeskTheme.LightChartNeutral))]
    [InlineData("--chart-neutral-dim", nameof(DeskTheme.LightChartNeutralDim))]
    public void Csharp_mirrors_the_light_block(string token, string constantName)
    {
        var constant = (string)typeof(DeskTheme)
            .GetField(constantName)!
            .GetValue(null)!;

        Assert.Equal(ColorValue(LightTokenValue(token)), ColorValue(constant), ignoreCase: true);
    }

    [Fact]
    public void The_theme_hands_mudblazor_the_light_palette()
    {
        var light = DeskTheme.Instance.PaletteLight;

        Assert.Equal(ColorValue(DeskTheme.LightAccent), light.Primary.Value, ignoreCase: true);
        Assert.Equal(ColorValue(DeskTheme.LightSurface0), light.Background.Value, ignoreCase: true);
        Assert.Equal(ColorValue(DeskTheme.LightSurface1), light.Surface.Value, ignoreCase: true);
        Assert.Equal(ColorValue(DeskTheme.LightStatePositive), light.Success.Value, ignoreCase: true);
        Assert.Equal(ColorValue(DeskTheme.LightStateNegative), light.Error.Value, ignoreCase: true);
        Assert.Equal(ColorValue(DeskTheme.LightStateWarning), light.Warning.Value, ignoreCase: true);
        Assert.Equal(ColorValue(DeskTheme.LightTextTertiary), light.ActionDisabled.Value, ignoreCase: true);
    }

    /// <summary>
    /// The one place the light palette deliberately refuses to mirror the dark one, pinned
    /// so nobody "fixes" it back into symmetry.
    ///
    /// The dark scrim is the desk void at 62% — dimming near-black means more near-black.
    /// The analogous light value would be the pale ground at 62%, which would *brighten*
    /// the desk behind a sheet. A scrim subtracts attention, and on any ground that means
    /// going darker, so the light scrim is neutral black and darker than what it covers.
    /// </summary>
    [Fact]
    public void The_light_scrim_darkens_rather_than_mirrors()
    {
        var light = DeskTheme.Instance.PaletteLight;

        Assert.Equal(ColorValue(DeskTheme.LightOverlay), ColorValue(light.OverlayDark), ignoreCase: true);

        var scrim = new MudColor(light.OverlayDark);
        var ground = new MudColor(DeskTheme.LightSurface0);

        Assert.True(
            Luminance(scrim.R, scrim.G, scrim.B) < Luminance(ground.R, ground.G, ground.B),
            "the light scrim must be darker than the desk it covers, or it is not a scrim");
    }

    /// <summary>
    /// The mirror gate, and the reason the two blocks cannot drift apart quietly.
    ///
    /// <para>
    /// A token the light block forgets does not fail anywhere — it simply keeps its dark
    /// value, so a light desk renders with one near-black surface in it and nothing says
    /// so. That is the defect class this test exists for, and it is checked mechanically
    /// rather than by a maintained list, because a maintained list is exactly the thing
    /// that goes stale when someone adds the twenty-ninth token.
    /// </para>
    ///
    /// <para>
    /// Only colour-valued tokens are in scope. The type ramp, spacing, radii, motion and
    /// z-layers are not theme-dependent and stay declared once in :root, where they fall
    /// through — a second copy of --space-4 is a second copy to drift.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_colour_token_the_dark_desk_declares_the_light_desk_redeclares()
    {
        var dark = Declarations(DarkBlock);
        var light = Declarations(LightBlock);

        var missing = dark
            .Where(d => IsColour(d.Value))
            .Select(d => d.Key)
            .Where(name => !light.ContainsKey(name))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"the light block does not redeclare: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// The same gate pointed the other way. A token that exists only in the light block is
    /// a token the dark desk falls through on — the identical defect, wearing the identical
    /// disguise, and the direction a one-way check would miss.
    /// </summary>
    [Fact]
    public void The_light_desk_invents_no_token_the_dark_desk_lacks()
    {
        var dark = Declarations(DarkBlock);

        var invented = Declarations(LightBlock)
            .Select(d => d.Key)
            .Where(name => !dark.ContainsKey(name))
            .ToArray();

        Assert.True(
            invented.Length == 0,
            $"declared only in the light block: {string.Join(", ", invented)}");
    }

    /// <summary>
    /// A colour token that survived the mirror without changing value is almost always a
    /// copy-paste that was never re-derived — near-black surfaces sitting in the light
    /// block, wearing a light-block comment.
    /// </summary>
    /// <remarks>
    /// The exceptions are real and each is here on its own reasoning, not on a shrug.
    /// <c>--on-accent</c> is the ink for any saturated fill, and a filled systemBlue button
    /// is exactly as saturated in either theme. <c>--chart-neutral</c> is systemGray, the
    /// one step of Apple's ramp that reads on either ground, which is why Apple itself
    /// gives it no light/dark variant. <c>--material-blur</c> is an optical constant.
    /// </remarks>
    [Fact]
    public void Every_mirrored_colour_was_actually_re_derived()
    {
        string[] deliberatelyShared = ["--on-accent", "--chart-neutral", "--material-blur"];

        var dark = Declarations(DarkBlock);
        var light = Declarations(LightBlock);

        var unchanged = light
            .Where(d => !deliberatelyShared.Contains(d.Key))
            .Where(d => dark.TryGetValue(d.Key, out var was) && was == d.Value)
            .Select(d => d.Key)
            .ToArray();

        Assert.True(
            unchanged.Length == 0,
            $"carried over from the dark block unchanged: {string.Join(", ", unchanged)}");
    }

    /// <summary>
    /// ADR 0016 measured --text-secondary at 5.71:1 on the dark desk and made that number
    /// a promise. The light desk has to keep it, and "keeps it" means the same AA pass at
    /// close to the same ratio — not merely both legible, or the two themes would
    /// de-emphasise by different amounts and the same cell would read as two different
    /// grades of important depending on the time of day.
    ///
    /// Computed rather than asserted from a table, because a token nudged by two hundredths
    /// of an alpha should fail here rather than be re-measured by hand and forgotten.
    /// </summary>
    [Fact]
    public void Secondary_text_clears_AA_on_both_desks()
    {
        var onDark = Contrast(TokenValue("--text-secondary"), TokenValue("--surface-1"));
        var onLight = Contrast(LightTokenValue("--text-secondary"), LightTokenValue("--surface-1"));

        Assert.True(onDark >= 4.5, $"dark --text-secondary measures {onDark:F2}:1");
        Assert.True(onLight >= 4.5, $"light --text-secondary measures {onLight:F2}:1");

        // Both themes de-emphasise by the same amount, within a quarter of a step.
        Assert.True(
            Math.Abs(onDark - onLight) < 0.25,
            $"the two desks de-emphasise differently: {onDark:F2}:1 dark vs {onLight:F2}:1 light");
    }

    /// <summary>
    /// The other half of the promise, and the one that is easy to break by being helpful.
    /// --text-tertiary is a hint, a placeholder, a unit nobody must read; it is below AA on
    /// purpose on both desks. A change that lifts it over 4.5:1 has not fixed a defect, it
    /// has flattened the hierarchy — the rule from ADR 0016 is fix the call site, never
    /// brighten the dim token.
    /// </summary>
    [Fact]
    public void Tertiary_text_stays_below_AA_on_both_desks()
    {
        Assert.True(Contrast(TokenValue("--text-tertiary"), TokenValue("--surface-1")) < 4.5);
        Assert.True(Contrast(LightTokenValue("--text-tertiary"), LightTokenValue("--surface-1")) < 4.5);
    }

    /// <summary>
    /// The one corner of the desk a token block cannot reach.
    ///
    /// <para>
    /// MudChart writes these into SVG attributes and legend markup, where a <c>var()</c> does
    /// not resolve, so a chart is the single place where the second theme costs a second
    /// array rather than nothing. It is also the place where forgetting would go unnoticed
    /// longest: the dark series colours are saturated enough to still look deliberate on
    /// white. The dim neutral is what gives it away — <c>#636366</c> on a pale plot is not a
    /// quiet series, it is the darkest thing in the window — so that is what this pins.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(DeskChartFamily.Movement)]
    [InlineData(DeskChartFamily.Ledger)]
    [InlineData(DeskChartFamily.Health)]
    [InlineData(DeskChartFamily.Volume)]
    public void Chart_families_carry_the_theme_they_are_drawn_on(DeskChartFamily family)
    {
        var dark = DeskChartPalette.For(family, isDark: true);
        var light = DeskChartPalette.For(family, isDark: false);

        Assert.Equal(dark.Length, light.Length);
        Assert.Contains(DeskTheme.ChartNeutralDim, dark);
        Assert.Contains(DeskTheme.LightChartNeutralDim, light);
        Assert.DoesNotContain(DeskTheme.ChartNeutralDim, light);

        // Same family, same ordering — only the theme moved. A light palette that reordered
        // its slots would silently recolour every series in the chart on a theme change.
        Assert.Equal(
            Array.IndexOf(dark, DeskTheme.Accent),
            Array.IndexOf(light, DeskTheme.LightAccent));
    }

    /// <summary>
    /// The single-argument overload is what a call site with no theme to hand still gets, and
    /// it has to keep meaning the dark desk — degrading to the product's existing appearance
    /// rather than to whichever branch of a switch came first.
    /// </summary>
    [Fact]
    public void A_chart_palette_asked_without_a_theme_is_the_dark_one()
    {
        Assert.Equal(
            DeskChartPalette.For(DeskChartFamily.Ledger, isDark: true),
            DeskChartPalette.For(DeskChartFamily.Ledger));
    }

    // ---- Every desk ---------------------------------------------------------
    //
    // Five families, each with a dark and a light block — ten desks where there were two. The
    // gates above were written for one pair and each one is the same question asked of every
    // block: does it redeclare everything, invent nothing, match its C# mirror, and keep the
    // text hierarchy's promise. They are asked here by iterating the catalogue rather than by
    // listing blocks, so an eleventh desk added to DeskThemeFamilies is checked without anyone
    // remembering to add it here.

    public static TheoryData<DeskThemeFamily, bool> EveryDesk()
    {
        var desks = new TheoryData<DeskThemeFamily, bool>();

        foreach (var family in DeskThemeFamilies.All)
        {
            desks.Add(family, true);
            desks.Add(family, false);
        }

        return desks;
    }

    public static TheoryData<DeskThemeFamily> EveryOtherFamily()
    {
        var families = new TheoryData<DeskThemeFamily>();

        foreach (var family in DeskThemeFamilies.All.Where(f => f != DeskThemeFamily.Standard))
            families.Add(family);

        return families;
    }

    /// <summary>
    /// The block for one desk, found where it lives: Standard's in lineops.css, the rest in
    /// themes.css. Found by the attribute ThemeService will stamp, so a block whose selector
    /// has drifted from <see cref="DeskThemeFamilies.Attribute"/> is a block this cannot find,
    /// and the desk it belonged to would have painted with no tokens at all.
    /// </summary>
    private static string DeskBlock(DeskThemeFamily family, bool isDark)
    {
        var selector = "\\[data-theme=\"" + Regex.Escape(DeskThemeFamilies.Attribute(family, isDark)) + "\"\\]";

        return Block(family == DeskThemeFamily.Standard ? Css : ThemesCss, selector);
    }

    /// <summary>The mirror gate, both directions, for every desk rather than one light block.</summary>
    [Theory]
    [MemberData(nameof(EveryDesk))]
    public void Every_desk_redeclares_every_colour_token_and_invents_none(DeskThemeFamily family, bool isDark)
    {
        var standard = Declarations(DarkBlock);
        var desk = Declarations(DeskBlock(family, isDark));

        var missing = standard
            .Where(d => IsColour(d.Value))
            .Select(d => d.Key)
            .Where(name => !desk.ContainsKey(name))
            .ToArray();

        var invented = desk.Keys.Where(name => !standard.ContainsKey(name)).ToArray();

        Assert.True(missing.Length == 0, $"{DeskThemeFamilies.Attribute(family, isDark)} does not redeclare: {string.Join(", ", missing)}");
        Assert.True(invented.Length == 0, $"{DeskThemeFamilies.Attribute(family, isDark)} invents: {string.Join(", ", invented)}");
    }

    /// <summary>
    /// color-scheme is what repaints the browser's own scrollbars, form controls and the canvas
    /// behind the app. A dark block that forgot it sits inside a bright browser frame.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryDesk))]
    public void Every_desk_tells_the_browser_which_way_it_faces(DeskThemeFamily family, bool isDark)
    {
        var scheme = Regex.Match(DeskBlock(family, isDark), @"color-scheme\s*:\s*([a-z]+)\s*;");

        Assert.True(scheme.Success, $"{DeskThemeFamilies.Attribute(family, isDark)} declares no color-scheme");
        Assert.Equal(isDark ? "dark" : "light", scheme.Groups[1].Value);
    }

    /// <summary>
    /// Every palette MudBlazor and the charts are handed is the colour its block declares, token
    /// for token. Compared through MudColor for the reason <see cref="Csharp_mirrors_the_stylesheet"/>
    /// gives, and pointed at the property's own name so a failure says which colour drifted.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryDesk))]
    public void Every_palette_mirrors_its_block(DeskThemeFamily family, bool isDark)
    {
        var palette = DeskThemeFamilies.Palette(family, isDark);
        var block = DeskBlock(family, isDark);
        var where = DeskThemeFamilies.Attribute(family, isDark);

        Assert.Equal(isDark, palette.IsDark);

        (string Token, string Value)[] pairs =
        [
            ("--surface-0", palette.Surface0),
            ("--surface-1", palette.Surface1),
            ("--surface-2", palette.Surface2),
            ("--surface-3", palette.Surface3),
            ("--text-primary", palette.TextPrimary),
            ("--text-secondary", palette.TextSecondary),
            ("--text-tertiary", palette.TextTertiary),
            ("--accent", palette.Accent),
            ("--accent-hover", palette.AccentHover),
            ("--on-accent", palette.OnAccent),
            ("--state-positive", palette.StatePositive),
            ("--state-negative", palette.StateNegative),
            ("--state-warning", palette.StateWarning),
            ("--separator", palette.Separator),
            ("--chart-neutral", palette.ChartNeutral),
            ("--chart-neutral-dim", palette.ChartNeutralDim)
        ];

        foreach (var (token, value) in pairs)
        {
            Assert.True(
                ColorValue(TokenValue(block, token, where)) == ColorValue(value),
                $"{where} {token} is {TokenValue(block, token, where)} in CSS but {value} in DeskPalettes");
        }
    }

    /// <summary>
    /// ADR 0016's two promises, asked of every desk: secondary text clears AA over the panel, and
    /// tertiary text stays below it. The second is the one a new family is tempted to break by
    /// being helpful, and it is kept even on High contrast — raised there, but still a hint.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryDesk))]
    public void Every_desk_keeps_the_text_hierarchy(DeskThemeFamily family, bool isDark)
    {
        var block = DeskBlock(family, isDark);
        var where = DeskThemeFamilies.Attribute(family, isDark);
        var panel = TokenValue(block, "--surface-1", where);

        var primary = Contrast(TokenValue(block, "--text-primary", where), panel);
        var secondary = Contrast(TokenValue(block, "--text-secondary", where), panel);
        var tertiary = Contrast(TokenValue(block, "--text-tertiary", where), panel);

        Assert.True(secondary >= 4.5, $"{where} --text-secondary measures {secondary:F2}:1");
        Assert.True(tertiary < 4.5, $"{where} --text-tertiary measures {tertiary:F2}:1 — a hint must not read as a value");
        Assert.True(primary > secondary && secondary > tertiary, $"{where} tiers out of order: {primary:F2} / {secondary:F2} / {tertiary:F2}");
    }

    /// <summary>What High contrast is for: secondary text at AAA, not merely AA, in both appearances.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void High_contrast_secondary_text_clears_AAA(bool isDark)
    {
        var block = DeskBlock(DeskThemeFamily.HighContrast, isDark);
        var ratio = Contrast(TokenValue(block, "--text-secondary", "contrast"), TokenValue(block, "--surface-1", "contrast"));

        Assert.True(ratio >= 7, $"High contrast --text-secondary measures {ratio:F2}:1");
    }

    /// <summary>
    /// The copy-paste defect in its family-sized form: a new block started from the Standard one
    /// and never re-derived paints as Standard under a different name. The accent and the surface
    /// ramp are what make a family recognisable, so neither may be Standard's.
    /// </summary>
    /// <remarks>
    /// The ramp is compared whole rather than rung by rung: a panel of pure white is a legitimate
    /// choice for more than one light desk (High contrast makes it), and a gate that failed on it
    /// would be asking for an off-white nobody wanted just to be different.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryOtherFamily))]
    public void No_family_is_Standard_under_another_name(DeskThemeFamily family)
    {
        string[] ramp = ["--surface-0", "--surface-1", "--surface-2", "--surface-3"];

        foreach (var isDark in new[] { true, false })
        {
            var standard = Declarations(isDark ? DarkBlock : LightBlock);
            var desk = Declarations(DeskBlock(family, isDark));
            var where = DeskThemeFamilies.Attribute(family, isDark);

            Assert.True(standard["--accent"] != desk["--accent"], $"{where} carries Standard's accent");
            Assert.False(ramp.All(token => standard[token] == desk[token]), $"{where} carries Standard's surface ramp");
        }
    }

    /// <summary>
    /// Each family's MudTheme carries that family's two palettes, in the right slots. A family
    /// whose theme fell back to Standard's would leave every Mud component — menus, grids,
    /// dialogs — in systemBlue on a Nord desk.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryDesk))]
    public void Every_family_hands_mudblazor_its_own_palette(DeskThemeFamily family, bool isDark)
    {
        var desk = DeskThemeFamilies.Palette(family, isDark);
        var theme = DeskTheme.For(family);
        MudBlazor.Palette mud = isDark ? theme.PaletteDark : theme.PaletteLight;

        Assert.Equal(ColorValue(desk.Accent), mud.Primary.Value, ignoreCase: true);
        Assert.Equal(ColorValue(desk.OnAccent), mud.PrimaryContrastText.Value, ignoreCase: true);
        Assert.Equal(ColorValue(desk.Surface0), mud.Background.Value, ignoreCase: true);
        Assert.Equal(ColorValue(desk.Surface1), mud.Surface.Value, ignoreCase: true);
        Assert.Equal(ColorValue(desk.StateNegative), mud.Error.Value, ignoreCase: true);
        Assert.Equal(ColorValue(desk.TextTertiary), mud.ActionDisabled.Value, ignoreCase: true);
        Assert.Equal(ColorValue(desk.Overlay), ColorValue(mud.OverlayDark), ignoreCase: true);
    }

    /// <summary>
    /// The scrim rule from <see cref="The_light_scrim_darkens_rather_than_mirrors"/>, for every
    /// desk: whatever the family, a scrim is darker than the ground it covers.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryDesk))]
    public void Every_scrim_darkens_its_desk(DeskThemeFamily family, bool isDark)
    {
        var desk = DeskThemeFamilies.Palette(family, isDark);
        var scrim = new MudColor(desk.Overlay);
        var ground = new MudColor(desk.Surface0);

        Assert.True(
            Luminance(scrim.R, scrim.G, scrim.B) <= Luminance(ground.R, ground.G, ground.B),
            $"{DeskThemeFamilies.Attribute(family, isDark)} scrim is lighter than its ground");
    }

    /// <summary>
    /// A chart on a Nord desk is drawn in Nord. The dim neutral is the tell, for the reason
    /// <see cref="Chart_families_carry_the_theme_they_are_drawn_on"/> gives, and the slot order
    /// is the family's, never the theme's, so a switch recolours series without swapping them.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryDesk))]
    public void Charts_are_drawn_in_the_desk_they_sit_on(DeskThemeFamily family, bool isDark)
    {
        var desk = DeskThemeFamilies.Palette(family, isDark);

        foreach (var chart in Enum.GetValues<DeskChartFamily>())
        {
            var colours = DeskChartPalette.For(chart, desk);
            var standard = DeskChartPalette.For(chart, isDark);

            Assert.Contains(desk.ChartNeutralDim, colours);
            Assert.Equal(Array.IndexOf(standard, isDark ? DeskTheme.Accent : DeskTheme.LightAccent), Array.IndexOf(colours, desk.Accent));
        }
    }

    [Fact]
    public void Every_family_has_a_distinct_name_and_attribute()
    {
        Assert.Equal(DeskThemeFamilies.All.Count, DeskThemeFamilies.All.Select(DeskThemeFamilies.Name).Distinct().Count());
        Assert.Equal(DeskThemeFamilies.All.Count, DeskThemeFamilies.All.Select(DeskThemeFamilies.Slug).Distinct().Count());
        Assert.Equal(Enum.GetValues<DeskThemeFamily>().Length, DeskThemeFamilies.All.Count);
    }

    /// <summary>Normalizes a colour literal the same way MudColor does, for apples-to-apples comparison.</summary>
    private static string ColorValue(string cssColor) => new MudColor(cssColor).Value;

    private static string TokenValue(string token) => TokenValue(DarkBlock, token, "the dark block");

    private static string LightTokenValue(string token) => TokenValue(LightBlock, token, "the light block");

    private static string TokenValue(string block, string token, string where)
    {
        var match = Regex.Match(block, Regex.Escape(token) + @"\s*:\s*([^;]+);");

        Assert.True(match.Success, $"{token} is not declared in {where} of lineops.css");

        return match.Groups[1].Value.Trim();
    }

    /// <summary>
    /// The body of one token block, with comments removed.
    /// </summary>
    /// <remarks>
    /// <c>[^}]*</c> is safe only because neither block contains a nested rule or a closing
    /// brace in its prose. If one ever does, this stops at the wrong place and takes half
    /// the tokens with it — so the assertion below is not decoration: a block that has lost
    /// its tail fails loudly here rather than quietly under-checking everywhere else.
    /// </remarks>
    private static string Block(string selectorPattern) => Block(Css, selectorPattern);

    private static string Block(string css, string selectorPattern)
    {
        var match = Regex.Match(css, selectorPattern + @"\s*\{([^}]*)\}");

        Assert.True(match.Success, $"no token block matched {selectorPattern} in lineops.css");

        var body = Regex.Replace(match.Groups[1].Value, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        Assert.Contains("--", body, StringComparison.Ordinal);

        return body;
    }

    /// <summary>Every custom property a block declares, name to value.</summary>
    private static Dictionary<string, string> Declarations(string block)
        => Regex.Matches(block, @"(--[a-z0-9-]+)\s*:\s*([^;]+);")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());

    /// <summary>
    /// Whether a token's value names a colour, and therefore whether a second theme owes it
    /// a redefinition. Shadows count: they are ink with a geometry attached, and the dark
    /// desk's alphas exist to be seen against near-black.
    /// </summary>
    private static bool IsColour(string value)
        => value.Contains('#', StringComparison.Ordinal)
           || value.Contains("rgb", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// WCAG 2.x contrast between a foreground that may be translucent and an opaque
    /// background, composited the way a browser does before either is measured.
    /// </summary>
    private static double Contrast(string foreground, string background)
    {
        var over = new MudColor(background);
        var ink = new MudColor(foreground);
        var alpha = ink.A / 255d;

        // Alpha compositing happens in gamma-encoded sRGB — the space the channels are
        // already in — and linearisation comes after. Doing it the other way round is a
        // plausible-looking mistake that shifts every ratio in the suite.
        var r = ink.R * alpha + over.R * (1 - alpha);
        var g = ink.G * alpha + over.G * (1 - alpha);
        var b = ink.B * alpha + over.B * (1 - alpha);

        var lighter = Math.Max(Luminance(r, g, b), Luminance(over.R, over.G, over.B));
        var darker = Math.Min(Luminance(r, g, b), Luminance(over.R, over.G, over.B));

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(double r, double g, double b)
        => 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);

    private static double Linearize(double channel)
    {
        var c = channel / 255d;

        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
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
