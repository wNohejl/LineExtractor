using System.Text.RegularExpressions;
using XLines.Desk.Primitives;
using XLines.Desk.Tests.Support;
using XLines.Desk.Theming;
using MudBlazor.Utilities;
using static XLines.Desk.Tests.Support.Wcag;

namespace XLines.Desk.Tests;

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
    private static string Css => DeskCss.Desk;

    /// <summary>The other families' blocks, loaded after desk.css. See the "Every desk" section below.</summary>
    private static string ThemesCss => DeskCss.Themes;

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
    [InlineData("--accent", "#086FD7")]
    [InlineData("--state-positive", "#30D158")]
    [InlineData("--state-negative", "#D73A31")]
    [InlineData("--state-warning", "#FF9F0A")]
    [InlineData("--state-positive-ink", "#30D158")]
    [InlineData("--state-warning-ink", "#FFB340")]
    [InlineData("--state-negative-ink", "#FF6961")]
    [InlineData("--accent-ink", "#449EFF")]
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

    // ---- The light desk -----------------------------------------------------

    [Theory]
    [InlineData("--surface-0", "#F2F2F7")]
    [InlineData("--surface-1", "#FFFFFF")]
    [InlineData("--surface-2", "#E5E5EA")]
    [InlineData("--surface-3", "#D1D1D6")]
    [InlineData("--accent", "#0071EB")]
    [InlineData("--state-positive", "#34C759")]
    [InlineData("--state-negative", "#DF342A")]
    [InlineData("--state-warning", "#FF9500")]
    [InlineData("--state-positive-ink", "#00762B")]
    [InlineData("--state-warning-ink", "#AA4800")]
    [InlineData("--state-negative-ink", "#CA1227")]
    [InlineData("--accent-ink", "#0060C8")]
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
    /// gives it no light/dark variant. <c>--knob</c> is a switch's thumb, a white disc in either
    /// appearance on Apple's platforms. (<c>--material-blur</c>, an optical constant, is no longer
    /// in either block: it is declared once with the derived tokens.)
    /// </remarks>
    [Fact]
    public void Every_mirrored_colour_was_actually_re_derived()
    {
        string[] deliberatelyShared = ["--on-accent", "--chart-neutral", "--knob"];

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
    // Twenty-two families, each with a dark and a light block — forty-four desks where there were two. The
    // gates above were written for one pair and each one is the same question asked of every
    // block: does it redeclare everything, invent nothing, match its C# mirror, and keep the
    // text hierarchy's promise. They are asked here by iterating the catalogue rather than by
    // listing blocks, so another desk added to DeskThemeFamilies is checked without anyone
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

    /// <summary>Every row of the catalogue — the one list every per-family test in the suite reads.</summary>
    public static TheoryData<DeskThemeFamily> EveryFamily()
    {
        var families = new TheoryData<DeskThemeFamily>();

        foreach (var family in DeskThemeFamilies.All)
            families.Add(family);

        return families;
    }

    public static TheoryData<DeskThemeFamily> EveryOtherFamily()
    {
        var families = new TheoryData<DeskThemeFamily>();

        foreach (var family in DeskThemeFamilies.All.Where(f => f != DeskThemeFamily.Standard))
            families.Add(family);

        return families;
    }

    /// <summary>
    /// The block for one desk, found where it lives: Standard's in desk.css, the rest in
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

        // A block may also state an alpha where it departs from its appearance's default; it may not
        // declare a derived token, which is computed once (The_derived_tokens_are_declared_once).
        var invented = desk.Keys.Where(name => !standard.ContainsKey(name) && !DeskCss.AlphaTokens.Contains(name)).ToArray();

        Assert.True(missing.Length == 0, $"{DeskThemeFamilies.Attribute(family, isDark)} does not redeclare: {string.Join(", ", missing)}");
        Assert.True(invented.Length == 0, $"{DeskThemeFamilies.Attribute(family, isDark)} invents: {string.Join(", ", invented)}");
    }

    // ---- The derived tokens -------------------------------------------------
    //
    // The washes, a key's top line and sheen, and the blur are computed once in desk.css
    // ("Derived tokens") rather than written out in every block. These hold the rule's shape and
    // what it computes; the contrast suites measure the computed colours (DeskCss resolves them).

    /// <summary>
    /// Every block declares exactly the declared set, and the derived set is declared once: on
    /// <c>:root, [data-theme]</c> and the two appearance rules, never in a family or accent block —
    /// a block that wrote one out again would freeze it, and a later fill would leave its wash behind.
    /// </summary>
    [Fact]
    public void The_derived_tokens_are_declared_once()
    {
        string[] expected =
        [
            "--accent-wash", "--accent-wash-strong", "--key-gloss", "--key-highlight", "--material-blur",
            "--state-negative-wash", "--state-positive-wash", "--state-warning-wash"
        ];
        Assert.Equal(expected, DeskCss.DerivedTokens);

        foreach (var (name, tokens) in DeskCss.ThemeBlocks())
            Assert.All(expected, token => Assert.False(tokens.ContainsKey(token), $"{name} declares the derived {token}"));
        foreach (var (theme, accent, tokens) in DeskCss.AccentBlocks())
            Assert.All(expected, token => Assert.False(tokens.ContainsKey(token), $"{theme} {accent} declares the derived {token}"));

        // Nowhere in themes.css at all, inside a block or out of one.
        foreach (var token in expected)
            Assert.DoesNotMatch(Regex.Escape(token) + @"\s*:", ThemesCss);

        // Both appearances default every alpha, so <html> never takes the other appearance's from :root.
        Assert.Equal(DeskCss.AlphaTokens, DeskCss.LightDefaults.Keys.Where(DeskCss.IsAlpha).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The defaults are the Standard desk's, so Standard's blocks state no alpha: its washes at
    /// .15/.24 by night and .12/.20 by day, its states at .15, its key's line and sheen at .16/.09
    /// on its sheen and .55/.18 white.
    /// </summary>
    [Theory]
    [InlineData(true, ".15", ".24", ".15", ".16", ".09")]
    [InlineData(false, ".12", ".20", ".15", ".55", ".18")]
    public void The_alphas_default_to_the_Standard_desk_s(bool isDark, string wash, string strong, string state, string highlight, string gloss)
    {
        var defaults = isDark ? DeskCss.DarkDefaults : DeskCss.LightDefaults;

        Assert.Equal(wash, defaults["--accent-wash-alpha"]);
        Assert.Equal(strong, defaults["--accent-wash-strong-alpha"]);
        Assert.All(new[] { "positive", "negative", "warning" }, s => Assert.Equal(state, defaults[$"--state-{s}-wash-alpha"]));
        Assert.Equal(highlight, defaults["--key-highlight-alpha"]);
        Assert.Equal(gloss, defaults["--key-gloss-alpha"]);

        foreach (var block in new[] { DarkBlock, LightBlock })
            Assert.DoesNotContain(Declarations(block).Keys, DeskCss.IsAlpha);
    }

    /// <summary>
    /// What the derived rule computes, on every family block and every accent block: each wash is
    /// its fill's own channels at the block's alpha token; a key's line and sheen are the family's
    /// sheen by night and white by day, at theirs; the blur is the one constant. A block's own alpha
    /// is what the cascade lands on, and an accent block's washes follow its new fill.
    /// </summary>
    [Fact]
    public void Every_derived_token_resolves_to_its_source_at_its_alpha()
    {
        var failures = new List<string>();
        var blocks = DeskCss.ResolvedThemeBlocks().Select(b => (Where: b.Name, Dark: b.Name.EndsWith("-dark", StringComparison.Ordinal), b.Tokens))
            .Concat(DeskCss.ResolvedAccentBlocks().Select(b => (Where: $"{b.Theme} {b.Accent}", Dark: b.Theme.EndsWith("-dark", StringComparison.Ordinal), b.Tokens)))
            .ToList();

        Assert.Equal(DeskThemeFamilies.All.Count * 2 + DeskThemeFamilies.Catalog.Sum(f => f.Accents.Count - 1) * 2, blocks.Count);

        foreach (var (where, dark, t) in blocks)
        {
            Expect(where, t, "--accent-wash", t["--accent"], "--accent-wash-alpha");
            Expect(where, t, "--accent-wash-strong", t["--accent"], "--accent-wash-strong-alpha");
            foreach (var state in new[] { "positive", "negative", "warning" })
                Expect(where, t, $"--state-{state}-wash", t[$"--state-{state}"], $"--state-{state}-wash-alpha");
            Expect(where, t, "--key-highlight", dark ? t["--sheen"] : "#FFFFFF", "--key-highlight-alpha");
            Expect(where, t, "--key-gloss", dark ? t["--sheen"] : "#FFFFFF", "--key-gloss-alpha");

            if (t["--material-blur"] != "saturate(180%) blur(30px)")
                failures.Add($"{where} --material-blur is {t["--material-blur"]}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

        void Expect(string where, Dictionary<string, string> t, string token, string source, string alpha)
        {
            var (r, g, b, a) = Parse(t[token]);
            var (sr, sg, sb, _) = Parse(source);
            var expected = double.Parse(t[alpha], System.Globalization.CultureInfo.InvariantCulture);

            if ((r, g, b) != (sr, sg, sb) || Math.Abs(a - expected) > 1e-9)
                failures.Add($"{where} {token} resolves to {t[token]}, not {source} at {t[alpha]}");
        }
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
            ("--state-positive-ink", palette.StatePositiveInk),
            ("--state-warning-ink", palette.StateWarningInk),
            ("--state-negative-ink", palette.StateNegativeInk),
            ("--accent-ink", palette.AccentInk),
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

    /// <summary>
    /// Selected text is drawn in the strong accent wash behind the primary ink (desk.css
    /// <c>::selection</c>), and has to stay readable once selected: AA over the panel, the ground
    /// every other ink in this file is measured on, on every desk. (On the raised --surface-2 tier
    /// Rosé Pine light measures 4.47:1 — readable, a hair under AA; the palette's call, not this rule's.)
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryDesk))]
    public void Selected_text_stays_readable_on_every_desk(DeskThemeFamily family, bool isDark)
    {
        var desk = Resolved(family, isDark);
        var where = DeskThemeFamilies.Attribute(family, isDark);

        AssertSelectionReads(desk["--text-primary"], desk["--accent-wash-strong"], [desk["--surface-1"]], where);
    }

    /// <summary>The same on every family's desk in each chosen accent, whose block brings its own wash.</summary>
    [Theory]
    [MemberData(nameof(EveryChosenAccent))]
    public void Selected_text_stays_readable_in_every_accent(DeskThemeFamily family, string key, bool isDark)
    {
        var block = DeskBlock(family, isDark);
        var where = DeskThemeFamilies.Attribute(family, isDark) + "/" + key;

        AssertSelectionReads(
            TokenValue(block, "--text-primary", where),
            ResolvedAccent(family, key, isDark)["--accent-wash-strong"],
            [TokenValue(block, "--surface-1", where)],
            where);
    }

    private static void AssertSelectionReads(string ink, string wash, string[] grounds, string where)
    {
        foreach (var ground in grounds)
        {
            var selected = Composite(wash, ground);
            var ratio = Contrast(ink, selected);

            Assert.True(ratio >= 4.5, $"{where}: selected text measures {ratio:F2}:1 over {wash} on {ground}");
        }
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

            // The accent's slot is the same on every desk, whether it is drawn in the fill or the ink.
            var slot = Array.IndexOf(colours, desk.Accent);
            if (slot < 0)
                slot = Array.IndexOf(colours, desk.AccentInk);
            Assert.Equal(Array.IndexOf(standard, isDark ? DeskTheme.Accent : DeskTheme.LightAccent), slot);
        }
    }

    /// <summary>
    /// A series is a graphical object, held to 3:1 on the panel it is drawn on (contrast audit,
    /// 20 November, §4.1 item 7): every slot but the dim neutral, which is for reference lines, in
    /// every family block. Six light desks' state fills measured 1.95 to 2.98:1 before the inks
    /// crossed the seam; the order is the family's still, so each slot is either its fill or its ink.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryDesk))]
    public void Every_chart_series_but_the_dim_clears_3_to_1_on_its_panel(DeskThemeFamily family, bool isDark)
        => AssertSeriesRead(DeskThemeFamilies.Palette(family, isDark), DeskThemeFamilies.Attribute(family, isDark));

    /// <summary>The same in every chosen accent, which brings its own fill and, where that fill is under 3:1, its own ink.</summary>
    [Theory]
    [MemberData(nameof(EveryChosenAccent))]
    public void Every_chart_series_but_the_dim_clears_3_to_1_in_every_accent(DeskThemeFamily family, string key, bool isDark)
    {
        var chosen = DeskThemeFamilies.Info(family).FindAccent(key)!.For(isDark)!;

        AssertSeriesRead(DeskThemeFamilies.Palette(family, isDark).WithAccent(chosen), $"{DeskThemeFamilies.Attribute(family, isDark)} {key}");
    }

    private static void AssertSeriesRead(DeskPalette desk, string where)
    {
        var failures = new List<string>();

        foreach (var chart in Enum.GetValues<DeskChartFamily>())
        {
            var colours = DeskChartPalette.For(chart, desk);
            var standard = DeskChartPalette.For(chart, DeskPalettes.AppleDark);

            for (var slot = 0; slot < colours.Length; slot++)
            {
                if (standard[slot] == DeskTheme.ChartNeutralDim)
                {
                    Assert.Equal(desk.ChartNeutralDim, colours[slot]);
                    continue;
                }

                var ratio = Contrast(colours[slot], desk.Surface1);
                if (ratio < 3)
                    failures.Add($"{where} {chart} slot {slot} {colours[slot]} measures {ratio:F2}:1 over {desk.Surface1}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Every_family_has_a_distinct_name_and_attribute()
    {
        Assert.Equal(DeskThemeFamilies.All.Count, DeskThemeFamilies.All.Select(DeskThemeFamilies.Name).Distinct().Count());
        Assert.Equal(DeskThemeFamilies.All.Count, DeskThemeFamilies.All.Select(DeskThemeFamilies.Slug).Distinct().Count());
        Assert.Equal(Enum.GetValues<DeskThemeFamily>().Length, DeskThemeFamilies.All.Count);
    }

    // ---- The chosen accent --------------------------------------------------
    //
    // A different kind of block. A family block redeclares every colour token; an accent block is
    // a compound selector — [data-theme="<slug>-dark|light"][data-accent="<key>"] — that redeclares
    // exactly the four accent tokens Standard's do, and a wash alpha only where its hue departs from
    // the family's; its washes are recomputed from its fill. The gates above find a block by
    // its bare [data-theme] selector, which a compound one never matches, so they are untouched by
    // these; these hold every family's accent blocks to their own shape and to the catalogue's
    // rows, the way the family blocks are held to the token list and to DeskPalettes. They iterate
    // the catalogue, so a family given accents later is checked the day its blocks are written.

    /// <summary>The four tokens an accent block redeclares — all of them, and beside them only its wash alphas.</summary>
    private static readonly string[] AccentTokens = ["--accent", "--accent-hover", "--accent-press", "--accent-ink"];

    /// <summary>What an accent block may add: the strength of its washes, where its hue wants another than its family's.</summary>
    private static readonly string[] AccentAlphas = ["--accent-wash-alpha", "--accent-wash-strong-alpha"];


    /// <summary>Every compound accent block in both stylesheets: its theme, its accent, its tokens.</summary>
    private static List<(string Theme, string Accent, Dictionary<string, string> Tokens)> AccentBlocks()
        => DeskCss.AccentBlocks().ToList();

    /// <summary>One family block as an element wearing it draws: its declarations and the derived tokens, resolved.</summary>
    private static Dictionary<string, string> Resolved(DeskThemeFamily family, bool isDark)
    {
        var theme = DeskThemeFamilies.Attribute(family, isDark);
        return DeskCss.ResolvedThemeBlocks().Single(b => b.Name == theme).Tokens;
    }

    /// <summary>One accent block laid over its family block, resolved.</summary>
    private static Dictionary<string, string> ResolvedAccent(DeskThemeFamily family, string key, bool isDark)
    {
        var theme = DeskThemeFamilies.Attribute(family, isDark);
        return DeskCss.ResolvedAccentBlocks().Single(b => b.Theme == theme && b.Accent == key).Tokens;
    }

    private static Dictionary<string, string> AccentBlock(DeskThemeFamily family, string key, bool isDark)
    {
        var theme = DeskThemeFamilies.Attribute(family, isDark);

        return AccentBlocks().Single(b => b.Theme == theme && b.Accent == key).Tokens;
    }

    /// <summary>Every accent of every family but its own, on each of its desks.</summary>
    public static TheoryData<DeskThemeFamily, string, bool> EveryChosenAccent()
    {
        var accents = new TheoryData<DeskThemeFamily, string, bool>();

        foreach (var info in DeskThemeFamilies.Catalog)
        {
            foreach (var accent in info.Accents.Skip(1))
            {
                accents.Add(info.Family, accent.Key, true);
                accents.Add(info.Family, accent.Key, false);
            }
        }

        return accents;
    }

    /// <summary>
    /// One block per accent per appearance for every family, none for a family's own accent (its
    /// blocks are that accent), and none an accent the catalogue does not list. A block with no row
    /// would be a hue the picker never offers; a row with no block, a swatch that paints nothing.
    /// </summary>
    [Fact]
    public void Every_family_s_accents_are_its_blocks()
    {
        var blocks = AccentBlocks();
        var expected = DeskThemeFamilies.Catalog
            .SelectMany(info => info.Accents.Skip(1).SelectMany(a => new[]
            {
                DeskThemeFamilies.Attribute(info.Family, true) + "/" + a.Key,
                DeskThemeFamilies.Attribute(info.Family, false) + "/" + a.Key
            }))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, blocks.Select(b => b.Theme + "/" + b.Accent).Order(StringComparer.Ordinal).ToArray());

        // No [data-accent] selector anywhere that is not one of these blocks — a bare one would
        // repaint a family the accent does not belong to.
        var everyMention = new[] { Css, ThemesCss }
            .Sum(css => Regex.Matches(Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline), @"\[data-accent").Count);
        Assert.Equal(blocks.Count, everyMention);
    }

    /// <summary>
    /// The shape every accent block keeps, Standard's included: exactly the four accent tokens, and
    /// a wash alpha only where it differs from what the family block already lands on — an override
    /// that restates the family's strength is noise a reader has to check. --on-accent is the
    /// family's, so a pale hue that needs dark ink is a hue its family cannot carry, not a fifth token.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryChosenAccent))]
    public void Every_accent_block_redeclares_the_four_accent_tokens_and_only_its_wash_alphas(DeskThemeFamily family, string key, bool isDark)
    {
        var block = AccentBlock(family, key, isDark);
        var beneath = Resolved(family, isDark);

        Assert.Equal(AccentTokens.Order(StringComparer.Ordinal), block.Keys.Where(k => !AccentAlphas.Contains(k)).Order(StringComparer.Ordinal));
        foreach (var alpha in AccentAlphas.Where(block.ContainsKey))
            Assert.True(block[alpha] != beneath[alpha], $"{DeskThemeFamilies.Attribute(family, isDark)} {key} restates its family's {alpha} {block[alpha]}");
    }

    /// <summary>
    /// The pairing seam, for accents: what MudBlazor and the charts are handed — the fill and its
    /// hover, all that crosses into C# — is the colour the block declares, compared through
    /// MudColor as every other pair is.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryChosenAccent))]
    public void Every_accent_block_mirrors_its_catalogue_row(DeskThemeFamily family, string key, bool isDark)
    {
        var tokens = ResolvedAccent(family, key, isDark);
        var accent = DeskThemeFamilies.Info(family).FindAccent(key)!;
        var where = $"{DeskThemeFamilies.Attribute(family, isDark)} {key}";
        var palette = accent.For(isDark);

        Assert.True(palette is not null, $"{where} has a block but no {(isDark ? "Dark" : "Light")} palette in its row");

        // The ink crosses only where the fill is under 3:1 on the family's panel (AccentPalette.Ink):
        // exactly there, and never anywhere else, so a carried ink is one a chart actually draws.
        var panel = TokenValue(DeskBlock(family, isDark), "--surface-1", where);
        Assert.True(
            (palette.Ink is not null) == (Contrast(palette.Accent, panel) < 3),
            $"{where} {(palette.Ink is null ? "carries no ink but its fill measures under 3:1" : "carries an ink its fill does not need")} on {panel}");

        (string Token, string Value)[] pairs =
        [
            ("--accent", palette.Accent),
            ("--accent-hover", palette.Hover),
            ("--accent-ink", palette.Ink ?? tokens["--accent-ink"])
        ];

        foreach (var (token, value) in pairs)
        {
            Assert.True(
                ColorValue(tokens[token]) == ColorValue(value),
                $"{where} {token} is {tokens[token]} in CSS but {value} in its row");
        }
    }

    /// <summary>
    /// A family's own accent has no block because its family blocks are that accent — so where its
    /// row carries palettes (Standard's Blue does), they have to be what those blocks declare, or
    /// the picker's Blue and MudBlazor's Blue would disagree with the desk choosing it brings back.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryDesk))]
    public void A_family_s_own_accent_is_what_its_blocks_declare(DeskThemeFamily family, bool isDark)
    {
        var own = DeskThemeFamilies.Info(family).OwnAccent.For(isDark);
        if (own is null)
            return;

        var block = Declarations(DeskBlock(family, isDark));

        Assert.Equal(ColorValue(block["--accent"]), ColorValue(own.Accent), ignoreCase: true);
        Assert.Equal(ColorValue(block["--accent-hover"]), ColorValue(own.Hover), ignoreCase: true);
    }

    /// <summary>
    /// The direction rule from DeskAccents' remarks, for every family's accents: on a dark desk hover
    /// lightens and press deepens; on a light desk both deepen; and the washes are the fill's own
    /// channels, so a selected row is tinted by the hue it is selected in. Standard's washes sit at
    /// .15/.24 dark and .12/.20 light; another family keeps the alphas its own blocks use. Read from
    /// the blocks as an element wearing them draws, since the press and the washes live there alone.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryChosenAccent))]
    public void Every_accent_travels_the_way_its_ground_demands(DeskThemeFamily family, string key, bool isDark)
    {
        var a = ResolvedAccent(family, key, isDark);
        var where = $"{DeskThemeFamilies.Attribute(family, isDark)} {key}";

        if (isDark)
            Assert.True(Luminance(a["--accent-hover"]) > Luminance(a["--accent"]), $"{where} dark hover does not lighten");
        else
            Assert.True(Luminance(a["--accent-hover"]) < Luminance(a["--accent"]), $"{where} light hover does not deepen");

        Assert.True(Luminance(a["--accent-press"]) < Luminance(a["--accent"]), $"{where} press does not deepen");

        var fill = Parse(a["--accent"]);
        var wash = Parse(a["--accent-wash"]);
        var strong = Parse(a["--accent-wash-strong"]);

        Assert.Equal((fill.R, fill.G, fill.B), (wash.R, wash.G, wash.B));
        Assert.Equal((fill.R, fill.G, fill.B), (strong.R, strong.G, strong.B));
        Assert.True(strong.A > wash.A, $"{where} strong wash is not stronger than the wash");

        if (family == DeskThemeFamily.Standard)
        {
            Assert.Equal(isDark ? .15 : .12, wash.A, 2);
            Assert.Equal(isDark ? .24 : .20, strong.A, 2);
        }
    }

    /// <summary>
    /// MudBlazor and the charts cannot read the accent block, so the theme and palette a family is
    /// handed carry its chosen accent — and its own accent, or a key it does not offer, is the
    /// family's theme itself, the same instance.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryChosenAccent))]
    public void A_family_s_theme_wears_its_chosen_accent(DeskThemeFamily family, string key, bool isDark)
    {
        var info = DeskThemeFamilies.Info(family);
        var chosen = info.FindAccent(key)!.For(isDark)!;
        var theme = DeskTheme.For(family, key);
        MudBlazor.Palette mud = isDark ? theme.PaletteDark : theme.PaletteLight;
        var desk = DeskThemeFamilies.Palette(family, isDark);

        Assert.Equal(ColorValue(chosen.Accent), mud.Primary.Value, ignoreCase: true);
        Assert.Equal(ColorValue(desk.OnAccent), mud.PrimaryContrastText.Value, ignoreCase: true);
        Assert.Equal(ColorValue(desk.Surface1), mud.Surface.Value, ignoreCase: true);

        var palette = desk.WithAccent(chosen);
        Assert.Equal(chosen.Accent, palette.Accent);
        Assert.Equal(chosen.Hover, palette.AccentHover);
        Assert.Equal(chosen.Ink ?? chosen.Accent, palette.AccentInk);
        Assert.Contains(chosen.Ink ?? chosen.Accent, DeskChartPalette.For(DeskChartFamily.Movement, palette));

        Assert.Same(theme, DeskTheme.For(family, key.ToUpperInvariant()));
        Assert.Same(DeskTheme.For(family), DeskTheme.For(family, info.OwnAccent.Key));
        Assert.Same(DeskTheme.For(family), DeskTheme.For(family, "no-such-accent"));
    }

    /// <summary>The Standard desk's named accents reach only Standard; every other family is untouched by them.</summary>
    [Fact]
    public void The_named_Standard_accents_belong_to_Standard_alone()
    {
        foreach (var accent in DeskAccents.All)
        {
            Assert.Same(DeskTheme.For(DeskThemeFamily.Standard, DeskAccents.Key(accent)), DeskTheme.For(DeskThemeFamily.Standard, accent));
            foreach (var family in DeskThemeFamilies.All.Where(f => f != DeskThemeFamily.Standard))
                Assert.Same(DeskTheme.For(family), DeskTheme.For(family, accent));
        }

        Assert.Same(DeskTheme.Instance, DeskTheme.For(DeskThemeFamily.Standard, DeskAccent.Blue));
    }

    /// <summary>
    /// Every family's list is well formed: at least its own accent, keys lower-case and distinct,
    /// names distinct, and every accent after the first carrying both palettes. Standard's list is
    /// DeskAccents', Blue first.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryFamily))]
    public void Every_family_s_accent_list_is_well_formed(DeskThemeFamily family)
    {
        var accents = DeskThemeFamilies.Accents(family);

        Assert.NotEmpty(accents);
        Assert.All(accents, a => Assert.Matches("^[a-z][a-z0-9-]*$", a.Key));
        Assert.Equal(accents.Count, accents.Select(a => a.Key).Distinct().Count());
        Assert.Equal(accents.Count, accents.Select(a => a.Name).Distinct().Count());
        Assert.All(accents.Skip(1), a =>
        {
            Assert.NotNull(a.Dark);
            Assert.NotNull(a.Light);
        });

        if (family == DeskThemeFamily.Standard)
            Assert.Equal(DeskAccents.All.Select(DeskAccents.Key), accents.Select(a => a.Key));
    }

    [Fact]
    public void Every_accent_has_a_distinct_key_and_label()
    {
        Assert.Equal(Enum.GetValues<DeskAccent>().Length, DeskAccents.All.Count);
        Assert.Equal(DeskAccents.All.Count, DeskAccents.All.Select(DeskAccents.Key).Distinct().Count());
        Assert.Equal(DeskAccents.All.Count, DeskAccents.All.Select(DeskAccents.Label).Distinct().Count());
        Assert.Equal(DeskAccent.Blue, DeskAccents.All[0]);
    }

    // ---- The catalogue -----------------------------------------------------------------

    /// <summary>
    /// The catalogue is the one list: every enum member has exactly one row, every group heading
    /// is a DeskThemeGroups constant with a family under it, and a group's families sit together,
    /// so the picker's reading order is the catalogue's.
    /// </summary>
    [Fact]
    public void The_catalogue_holds_every_family_once_grouped_together()
    {
        Assert.Equal(Enum.GetValues<DeskThemeFamily>().Order(), DeskThemeFamilies.All.Order());
        Assert.Equal(DeskThemeFamily.Standard, DeskThemeFamilies.All[0]);

        var groups = typeof(DeskThemeGroups).GetFields().Select(f => (string)f.GetRawConstantValue()!).ToArray();
        Assert.All(DeskThemeFamilies.Catalog, f => Assert.Contains(f.Group, groups));

        var runs = DeskThemeFamilies.Catalog.Select(f => f.Group)
            .Where((g, i) => i == 0 || DeskThemeFamilies.Catalog[i - 1].Group != g)
            .ToArray();
        Assert.Equal(runs.Length, runs.Distinct().Count());
    }

    // ---- The makes: shape, density, typeface ---------------------------------------------

    /// <summary>Every <c>:root[data-&lt;name&gt;="…"]</c> block in desk.css, value to its declarations.</summary>
    private static Dictionary<string, Dictionary<string, string>> MakeBlocks(string attribute)
        => Regex.Matches(Css, @":root\[data-" + attribute + @"=""([a-z-]+)""\]\s*\{([^{}]*)\}")
            .ToDictionary(m => m.Groups[1].Value, m => Declarations(Regex.Replace(m.Groups[2].Value, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline)));

    /// <summary>The plain :root geometry block every make rescales.</summary>
    private static Dictionary<string, string> Geometry()
    {
        var m = Regex.Match(Css, @"\n:root \{([^{}]*--control-h:[^{}]*)\}");
        Assert.True(m.Success, "desk.css has no plain :root geometry block");
        return Declarations(Regex.Replace(m.Groups[1].Value, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline));
    }

    public static TheoryData<string, string[], string[]> EveryMakeAttribute() => new()
    {
        { "shape", Stampable<DeskShape>(DeskShape.Standard), ["--radius-xs", "--radius-sm", "--radius", "--radius-panel", "--radius-sheet"] },
        {
            "density", Stampable<DeskDensity>(DeskDensity.Standard),
            ["--space-1", "--space-2", "--space-3", "--space-4", "--space-5", "--space-6", "--space-8", "--space-12", "--space-16",
             "--titlebar-h", "--footer-h", "--control-h", "--control-h-sm", "--control-h-lg", "--row-h", "--cell-pad-y", "--cell-inset"]
        },
        { "font", Stampable<DeskTypeface>(DeskTypeface.System), ["--face-ui", "--face-display"] }
    };

    private static string[] Stampable<T>(T byDefault) where T : struct, Enum
        => Enum.GetValues<T>().Where(v => !v.Equals(byDefault)).Select(v => v.ToString().ToLowerInvariant()).ToArray();

    /// <summary>
    /// Every member but the default has its block and every block its member; each block declares
    /// exactly the tokens its make owns — every one, and nothing else — and the geometry block
    /// declares each of them first, so the default is a value and not an absence.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryMakeAttribute))]
    public void Every_make_has_one_block_per_position_declaring_exactly_its_tokens(string attribute, string[] values, string[] tokens)
    {
        var blocks = MakeBlocks(attribute);
        var geometry = Geometry();

        Assert.Equal(values.Order(StringComparer.Ordinal), blocks.Keys.Order(StringComparer.Ordinal));
        Assert.All(tokens, token => Assert.True(geometry.ContainsKey(token), $"the geometry block does not declare {token}"));

        foreach (var (value, declared) in blocks)
            Assert.Equal(tokens.Order(StringComparer.Ordinal), declared.Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>No make's token is redeclared by a theme block: a swatch's own data-theme would reset it inside the swatch.</summary>
    [Theory]
    [MemberData(nameof(EveryDesk))]
    public void No_family_block_declares_a_make_s_token(DeskThemeFamily family, bool isDark)
    {
        var desk = Declarations(DeskBlock(family, isDark));
        var owned = EveryMakeAttribute().SelectMany(row => (string[])row[2]).Append("--backdrop-tint");

        Assert.All(owned, token => Assert.False(desk.ContainsKey(token), $"{DeskThemeFamilies.Attribute(family, isDark)} declares {token}"));
    }

    /// <summary>Shape keeps the ladder in order in every position, so an inner corner stays inside its outer one.</summary>
    [Fact]
    public void Every_shape_keeps_the_radius_ladder_in_order()
    {
        string[] ladder = ["--radius-xs", "--radius-sm", "--radius", "--radius-panel", "--radius-sheet"];
        var shapes = MakeBlocks("shape").Values.Append(Geometry());

        foreach (var shape in shapes)
        {
            var px = ladder.Select(t => double.Parse(shape[t].Replace("px", ""), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Assert.Equal(px.Order(), px);
        }
    }

    /// <summary>
    /// Compact takes out air and keeps targets pressable: the toolbar control, the small key and the
    /// cell padding are at least Standard's, and the heights it does bring down — the large key, a
    /// settings row, the title bar — stay at 28px or more. Roomy grows them.
    /// </summary>
    [Fact]
    public void Compact_keeps_every_target_and_Roomy_grows_them()
    {
        string[] targets = ["--control-h", "--control-h-sm", "--cell-pad-y"];
        var standard = Geometry();
        var blocks = MakeBlocks("density");

        foreach (var token in targets)
        {
            Assert.True(Px(blocks["compact"][token]) >= Px(standard[token]), $"Compact shrinks {token}");
            Assert.True(Px(blocks["roomy"][token]) > Px(standard[token]), $"Roomy does not grow {token}");
        }

        foreach (var token in new[] { "--control-h", "--control-h-lg", "--row-h", "--titlebar-h" })
            Assert.True(Px(blocks["compact"][token]) >= 28, $"Compact takes {token} under 28px");
        Assert.True(Px(blocks["compact"]["--titlebar-h"]) >= Px(blocks["compact"]["--control-h"]));
    }

    private static double Px(string v) => double.Parse(v.Replace("px", ""), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Every density's geometry: the plain :root block, then each block's values over it.</summary>
    private static IEnumerable<(string Density, Dictionary<string, string> Tokens)> EveryDensity()
    {
        var standard = Geometry();
        yield return ("standard", standard);

        foreach (var (density, block) in MakeBlocks("density"))
            yield return (density, new Dictionary<string, string>(standard.Concat(block).GroupBy(p => p.Key).Select(g => g.Last())));
    }

    /// <summary>The declarations of the first plain rule for <paramref name="selector"/> in desk.css.</summary>
    private static Dictionary<string, string> Rule(string selector)
    {
        var m = Regex.Match(Css, @"\n" + Regex.Escape(selector) + @"\s*\{([^{}]*)\}");
        Assert.True(m.Success, $"desk.css has no {selector} rule");
        return Regex.Matches(Regex.Replace(m.Groups[1].Value, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline), @"([a-z-]+)\s*:\s*([^;]+);")
            .ToDictionary(p => p.Groups[1].Value, p => p.Groups[2].Value.Trim());
    }

    /// <summary>
    /// A window's body keeps the 14px it had before the spacing became tokens, at Standard, and
    /// moves with Density: the token move had made it --space-4, 16px.
    /// </summary>
    [Fact]
    public void A_window_body_is_14px_at_Standard_and_follows_density()
    {
        Assert.Equal("var(--win-pad)", Rule(".win__body")["padding"]);

        var pads = EveryDensity().ToDictionary(d => d.Density, d => Px(d.Tokens["--space-4"]) - Px(d.Tokens["--space-1"]) / 2);

        Assert.Equal(14, pads["standard"]);
        Assert.True(pads["compact"] < 14 && pads["roomy"] > 14, "the window body's padding does not follow density");
    }

    /// <summary>
    /// A notice keeps a panel's corner and never becomes a capsule: its radius is held under half
    /// its height (a title bar) in every shape at every density. Round's 18px was more than half
    /// a 34px notice. Standard's 12px is untouched.
    /// </summary>
    [Fact]
    public void A_notice_is_never_rounded_into_a_capsule()
    {
        Assert.Equal("min(var(--radius-panel), calc(var(--titlebar-h) * .375))", Rule(".toast")["border-radius"]);

        var shapes = MakeBlocks("shape").Values.Append(Geometry()).Select(s => Px(s["--radius-panel"])).ToArray();
        foreach (var (density, tokens) in EveryDensity())
        {
            var height = Px(tokens["--titlebar-h"]);
            foreach (var panel in shapes)
                Assert.True(Math.Min(panel, height * .375) < height / 2, $"a {density} notice is a capsule at a {panel}px panel radius");
        }

        Assert.Equal(12, Math.Min(Px(Geometry()["--radius-panel"]), Px(Geometry()["--titlebar-h"]) * .375));
    }

    /// <summary>
    /// The Activities slot is never narrower than its key alone, "Activities · 12" — fifteen
    /// characters at the key's footnote size and its two paddings — in the widest face, Mono, whose
    /// characters are about .6em, at the largest Text size. 128px alone fell short there.
    /// </summary>
    [Fact]
    public void The_activities_slot_holds_its_key_in_every_face_and_text_size()
    {
        const string floor = "max(128px, calc(var(--text-footnote) * 9.6 + 2 * var(--space-2)))";
        Assert.Equal(floor, Rule(".toasts")["min-width"]);
        Assert.Equal("Activities · 12", XLines.Desk.Primitives.DeskToastStack.HeadLabel(12));

        foreach (var (density, tokens) in EveryDensity())
        {
            var padding = 2 * Px(tokens["--space-2"]);
            foreach (var scale in new[] { 1.0, 1.4 })
            {
                var footnote = Math.Max(10, 11 * scale);
                var key = 15 * .6 * footnote + padding;
                Assert.True(Math.Max(128, footnote * 9.6 + padding) >= key, $"the {density} slot is narrower than its key at {scale:P0}");
            }
        }
    }

    /// <summary>
    /// A grid's placeholder row stands as tall as the row that replaces it — --cell-pad-y either side
    /// of the 20px line box mud-bridge.css sets, plus the hairline — at every density. It was a
    /// literal 29px, 8px short of a Roomy row.
    /// </summary>
    [Fact]
    public void A_skeleton_row_is_as_tall_as_a_real_row_at_every_density()
    {
        var bridge = DeskCss.Raw("mud-bridge.css");
        var body = Regex.Match(bridge, @"\.deskgrid \.mud-table-body \.mud-table-cell \{([^}]*)\}").Groups[1].Value;
        Assert.Contains("padding: var(--cell-pad-y) var(--deskgrid-inset);", body);
        Assert.Contains("line-height: 20px;", body);
        Assert.Contains("border-bottom: 1px solid", body);

        Assert.Equal("calc(20px + 2 * var(--cell-pad-y) + 1px)", Rule(".deskgrid__skeleton-row")["height"]);

        var standard = Px(Geometry()["--cell-pad-y"]) * 2 + 21;
        Assert.Equal(29, standard);
    }

    /// <summary>
    /// Every typeface is a local stack: no font is fetched for one — the only @font-face in the
    /// Desk's sheets is the bundled Inter, which System alone names — and each ends on a generic.
    /// </summary>
    [Fact]
    public void Every_typeface_is_local_and_ends_on_a_generic_family()
    {
        foreach (var name in DeskConventions.DeskSheetNames)
        {
            var sheet = DeskCss.Raw(name);
            foreach (Match face in Regex.Matches(sheet, @"@font-face\s*\{([^}]*)\}"))
                Assert.Contains("'Inter var'", face.Groups[1].Value);
            Assert.DoesNotMatch(@"@import|fonts\.googleapis|https?://", Regex.Replace(sheet, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline));
        }

        foreach (var (value, block) in MakeBlocks("font"))
        {
            Assert.Matches(@"(serif|sans-serif|monospace)$", block["--face-ui"]);
            Assert.DoesNotContain("Inter", block["--face-ui"]);
        }
    }

    /// <summary>The backdrop's values are DeskBackdrop's members but Dots, the default, each with a rule on the ground.</summary>
    [Fact]
    public void Every_backdrop_but_the_default_draws_the_ground_and_names_no_family()
    {
        var rules = Regex.Matches(Css, @":root\[data-backdrop=""([a-z]+)""\] \.desk \{([^}]*)\}")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

        Assert.Equal(Stampable<DeskBackdrop>(DeskBackdrop.Dots).Order(StringComparer.Ordinal), rules.Keys.Order(StringComparer.Ordinal));
        Assert.All(rules.Values, body => Assert.Contains("background-image", body));
        Assert.DoesNotMatch(@"\[data-theme[^\]]*\]\[data-backdrop|\[data-backdrop[^\]]*\]\[data-theme", Css);
        Assert.Contains("color-mix(in srgb, var(--accent) var(--backdrop-tint), var(--surface-0))", rules["gradient"]);
    }
    /// <summary>Normalizes a colour literal the same way MudColor does, for apples-to-apples comparison.</summary>
    private static string ColorValue(string cssColor) => new MudColor(cssColor).Value;

    private static string TokenValue(string token) => TokenValue(DarkBlock, token, "the dark block");

    private static string LightTokenValue(string token) => TokenValue(LightBlock, token, "the light block");

    private static string TokenValue(string block, string token, string where)
    {
        var match = Regex.Match(block, Regex.Escape(token) + @"\s*:\s*([^;]+);");

        Assert.True(match.Success, $"{token} is not declared in {where} of desk.css");

        return match.Groups[1].Value.Trim();
    }

    private static string Block(string selectorPattern) => DeskCss.Block(Css, selectorPattern);

    private static string Block(string css, string selectorPattern) => DeskCss.Block(css, selectorPattern);

    private static Dictionary<string, string> Declarations(string block) => DeskCss.Declarations(block);

    /// <summary>
    /// Whether a token's value names a colour, and therefore whether a second theme owes it
    /// a redefinition. Shadows count: they are ink with a geometry attached, and the dark
    /// desk's alphas exist to be seen against near-black.
    /// </summary>
    private static bool IsColour(string value)
        => value.Contains('#', StringComparison.Ordinal)
           || value.Contains("rgb", StringComparison.OrdinalIgnoreCase);
}