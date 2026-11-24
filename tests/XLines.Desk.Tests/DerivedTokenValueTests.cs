using System.Text.RegularExpressions;
using XLines.Desk.Tests.Support;

namespace XLines.Desk.Tests;

/// <summary>
/// The derived tokens (desk.css, "Derived tokens") held from outside the resolver that computes
/// them. DeskThemeTests and ThemeContrastTests read a wash, its fill and its alpha through
/// <see cref="DeskCss"/> alike, so a resolver that computed every wash wrongly in the same way would
/// pass them; these hold a spread of blocks to the values the browser drew before the derivation
/// (each block's literal washes, as they were written out), the mix itself to hand-worked sums, and
/// the rules the resolver does not walk — source order, the relative-colour group and the media
/// blocks — by reading them.
/// </summary>
public class DerivedTokenValueTests
{
    /// <summary>The focus ring as desk.css declares it, on :root.</summary>
    private static string FocusRing => DeskCss.BlockIn(DeskCss.Desk, "apple-dark")["--focus-ring"];

    private static void AssertSame(Dictionary<string, string> expected, Dictionary<string, string> actual)
        => Assert.Equal(expected.OrderBy(p => p.Key, StringComparer.Ordinal), actual.OrderBy(p => p.Key, StringComparer.Ordinal));

    // ---- Resolved values, written out ----------------------------------------------------

    /// <summary>
    /// Each value as the block declared it literally before the washes were derived. Nord dark's
    /// accent wash and strong wash sit at .16/.22 where the dark default is .15/.24; Night Owl's
    /// purple restates the default its family departs from; Midnight's mono and Synthwave's chrome
    /// carry an alpha of their own; High contrast's keys are flat. A key's line and sheen are the
    /// family's sheen by night and white by day.
    /// </summary>
    public static TheoryData<string, string?, string, string> Expected() => new()
    {
        { "apple-dark", null, "--accent-wash", "rgba(8, 111, 215, .15)" },
        { "apple-dark", null, "--accent-wash-strong", "rgba(8, 111, 215, .24)" },
        { "apple-light", null, "--accent-wash", "rgba(0, 113, 235, .12)" },
        { "apple-light", null, "--accent-wash-strong", "rgba(0, 113, 235, .20)" },
        { "nord-dark", null, "--accent-wash", "rgba(136, 192, 208, .16)" },
        { "nord-dark", null, "--accent-wash-strong", "rgba(136, 192, 208, .22)" },
        { "nord-dark", null, "--state-negative-wash", "rgba(207, 136, 143, .18)" },
        { "solarized-light", null, "--accent-wash", "rgba(33, 122, 185, .14)" },
        { "solarized-light", null, "--accent-wash-strong", "rgba(33, 122, 185, .22)" },
        { "solarized-light", null, "--state-negative-wash", "rgba(220, 50, 47, .13)" },
        { "contrast-dark", null, "--accent-wash", "rgba(77, 184, 255, .22)" },
        { "contrast-dark", null, "--accent-wash-strong", "rgba(77, 184, 255, .34)" },
        { "contrast-dark", null, "--state-negative-wash", "rgba(255, 92, 92, .20)" },
        { "contrast-light", null, "--accent-wash", "rgba(0, 80, 200, .14)" },
        { "contrast-light", null, "--accent-wash-strong", "rgba(0, 80, 200, .24)" },
        { "contrast-light", null, "--state-negative-wash", "rgba(200, 0, 30, .12)" },
        { "catppuccin-light", null, "--state-positive-wash", "rgba(64, 160, 43, .14)" },
        { "rose-pine-light", null, "--state-warning-wash", "rgba(234, 157, 52, .16)" },
        { "paper-light", null, "--accent-wash", "rgba(42, 74, 128, .11)" },
        { "paper-light", null, "--accent-wash-strong", "rgba(42, 74, 128, .19)" },
        { "paper-light", null, "--state-positive-wash", "rgba(78, 122, 51, .13)" },
        { "midnight-light", null, "--accent-wash", "rgba(0, 64, 255, .10)" },
        { "midnight-light", null, "--accent-wash-strong", "rgba(0, 64, 255, .18)" },
        { "midnight-light", null, "--state-negative-wash", "rgba(224, 0, 30, .10)" },
        { "synthwave-dark", null, "--accent-wash", "rgba(255, 61, 193, .16)" },
        { "synthwave-dark", null, "--accent-wash-strong", "rgba(255, 61, 193, .26)" },
        { "synthwave-dark", null, "--state-negative-wash", "rgba(255, 90, 60, .18)" },
        { "zenburn-light", null, "--state-warning-wash", "rgba(223, 175, 143, .16)" },

        { "nord-dark", "teal", "--accent-wash", "rgba(143, 188, 187, .16)" },
        { "nord-dark", "teal", "--accent-wash-strong", "rgba(143, 188, 187, .22)" },
        { "midnight-dark", "mono", "--accent-wash", "rgba(237, 237, 237, .12)" },
        { "midnight-dark", "mono", "--accent-wash-strong", "rgba(237, 237, 237, .20)" },
        { "midnight-light", "mono", "--accent-wash", "rgba(28, 28, 28, .08)" },
        { "midnight-light", "mono", "--accent-wash-strong", "rgba(28, 28, 28, .14)" },
        { "synthwave-dark", "chrome", "--accent-wash", "rgba(201, 193, 234, .13)" },
        { "synthwave-dark", "chrome", "--accent-wash-strong", "rgba(201, 193, 234, .22)" },
        { "night-owl-dark", "purple", "--accent-wash", "rgba(199, 146, 234, .15)" },
        { "night-owl-dark", "purple", "--accent-wash-strong", "rgba(199, 146, 234, .24)" },

        { "nord-dark", null, "--key-highlight", "rgba(236, 239, 244, .16)" },
        { "nord-dark", null, "--key-gloss", "rgba(236, 239, 244, .09)" },
        { "synthwave-dark", null, "--key-highlight", "rgba(255, 61, 193, .16)" },
        { "synthwave-dark", null, "--key-gloss", "rgba(255, 61, 193, .09)" },
        { "solarized-light", null, "--key-highlight", "rgba(255, 255, 255, .55)" },
        { "solarized-light", null, "--key-gloss", "rgba(255, 255, 255, .18)" },
        { "contrast-dark", null, "--key-highlight", "rgba(255, 255, 255, 0)" },
        { "contrast-dark", null, "--key-gloss", "rgba(255, 255, 255, 0)" },
        { "contrast-light", null, "--key-highlight", "rgba(0, 0, 0, 0)" },
        { "contrast-light", null, "--key-gloss", "rgba(0, 0, 0, 0)" }
    };

    /// <summary>The resolved token is the colour written out, channel for channel and alpha for alpha; transparent is transparent whatever its channels.</summary>
    [Theory]
    [MemberData(nameof(Expected))]
    public void A_derived_token_resolves_to_the_value_the_block_once_declared(string theme, string? accent, string token, string expected)
    {
        var tokens = accent is null
            ? DeskCss.ResolvedThemeBlocks().Single(b => b.Name == theme).Tokens
            : DeskCss.ResolvedAccentBlocks().Single(b => b.Theme == theme && b.Accent == accent).Tokens;

        var (er, eg, eb, ea) = Wcag.Parse(expected);
        var (r, g, b, a) = Wcag.Parse(tokens[token]);

        Assert.True(Math.Abs(a - ea) < 1e-9, $"{theme} {accent} {token} resolves to {tokens[token]}, not {expected}");
        if (ea > 0)
            Assert.True((r, g, b) == (er, eg, eb), $"{theme} {accent} {token} resolves to {tokens[token]}, not {expected}");
    }

    /// <summary>The focus ring holds the strong wash, so it is the colour the browser draws the ring's 3px halo in.</summary>
    [Fact]
    public void The_focus_ring_resolves_to_its_wash_and_its_ink()
    {
        // Declared on :root alone (with the Standard dark block), so on <html> it resolves against
        // whichever block <html> carries.
        var nord = DeskCss.ResolvedThemeBlocks().Single(b => b.Name == "nord-dark").Tokens;
        var ring = DeskCss.ResolveValue(FocusRing, nord, 0);

        Assert.Equal($"0 0 0 3px rgba(136, 192, 208, 0.22), 0 0 0 1px {nord["--accent-ink"]}", ring);
    }

    // ---- The mix, worked by hand ---------------------------------------------------------

    /// <summary>
    /// color-mix in sRGB mixes premultiplied: an opaque colour with transparent keeps its channels
    /// and takes its share as its alpha; a translucent one multiplies its alpha by its share (the
    /// reason the dark key's finish is not a mix); two shares under 100% scale the alpha by their sum.
    /// </summary>
    [Theory]
    [InlineData("#FF0000", "15%", "transparent", "", "rgba(255, 0, 0, 0.15)")]
    [InlineData("#88C0D0", "calc(.16 * 100%)", "transparent", "", "rgba(136, 192, 208, 0.16)")]
    [InlineData("#88C0D0", "calc(0 * 100%)", "transparent", "", "rgba(0, 0, 0, 0)")]
    [InlineData("rgba(236, 239, 244, .16)", "calc(.16 * 100%)", "transparent", "", "rgba(236, 239, 244, 0.0256)")]
    [InlineData("rgba(236, 239, 244, .16)", "100%", "transparent", "", "rgba(236, 239, 244, 0.16)")]
    [InlineData("#000000", "", "#FFFFFF", "", "rgba(128, 128, 128, 1)")]
    [InlineData("#FF0000", "30%", "#0000FF", "30%", "rgba(128, 0, 128, 0.6)")]
    [InlineData("rgba(255, 0, 0, .5)", "50%", "#0000FF", "", "rgba(85, 0, 170, 0.75)")]
    public void A_mix_is_premultiplied(string one, string p1, string two, string p2, string expected)
        => Assert.Equal(expected, DeskCss.Mix(one, p1, two, p2));

    [Fact]
    public void A_mix_is_resolved_where_it_is_written()
    {
        var declared = new Dictionary<string, string> { ["--fill"] = "#30D158", ["--fill-alpha"] = ".15" };

        Assert.Equal("rgba(48, 209, 88, 0.15)",
            DeskCss.ResolveValue("color-mix(in srgb, var(--fill) calc(var(--fill-alpha) * 100%), transparent)", declared, 0));
    }

    // ---- What an older browser draws -------------------------------------------------------

    /// <summary>
    /// Outside the relative-colour group no derived token is written in relative colour, so a
    /// browser that cannot parse it still draws every wash and the focus ring that holds one.
    /// </summary>
    [Fact]
    public void Outside_the_relative_colour_group_nothing_is_derived_by_relative_colour()
    {
        foreach (var rule in new[] { DeskCss.DerivedRule, DeskCss.DarkFallback, DeskCss.LightDefaults })
            Assert.All(rule, d => Assert.DoesNotContain("rgb(from", d.Value, StringComparison.Ordinal));

        Assert.Equal("color-mix(in srgb, var(--accent) calc(var(--accent-wash-alpha) * 100%), transparent)", DeskCss.DerivedRule["--accent-wash"]);
        Assert.Equal("color-mix(in srgb, var(--accent) calc(var(--accent-wash-strong-alpha) * 100%), transparent)", DeskCss.DerivedRule["--accent-wash-strong"]);
        foreach (var state in new[] { "positive", "negative", "warning" })
            Assert.Equal($"color-mix(in srgb, var(--state-{state}) calc(var(--state-{state}-wash-alpha) * 100%), transparent)", DeskCss.DerivedRule[$"--state-{state}-wash"]);
        Assert.Equal("color-mix(in srgb, var(--sheen) calc(var(--key-highlight-alpha) * 100%), transparent)", DeskCss.DarkFallback["--key-highlight"]);
        Assert.Equal("color-mix(in srgb, var(--sheen) calc(var(--key-gloss-alpha) * 100%), transparent)", DeskCss.DarkFallback["--key-gloss"]);
    }

    /// <summary>
    /// Only the dark key's finish is in the group, because only its source is translucent: there
    /// relative colour replaces the sheen's alpha, which a mix would multiply.
    /// </summary>
    [Fact]
    public void The_relative_colour_group_holds_the_dark_key_s_finish_alone()
    {
        Assert.Equal(new[] { "--key-gloss", "--key-highlight" }, DeskCss.RelativeColourDark.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("rgb(from var(--sheen) r g b / var(--key-highlight-alpha))", DeskCss.RelativeColourDark["--key-highlight"]);
        Assert.Equal("rgb(from var(--sheen) r g b / var(--key-gloss-alpha))", DeskCss.RelativeColourDark["--key-gloss"]);
        Assert.Single(Regex.Matches(DeskCss.Desk, DeskCss.RelativeColourSupports));
    }

    /// <summary>
    /// Every fill a wash is mixed from is opaque in every block, which is what makes the mix exact;
    /// and an older browser's cascade — the fallback dark rule, no group — resolves every wash and
    /// the focus ring to a colour, the dark key's finish to the sheen at the product of the alphas.
    /// </summary>
    [Fact]
    public void Without_relative_colour_every_wash_and_ring_still_resolves()
    {
        var failures = new List<string>();
        var families = DeskCss.ThemeBlocks().ToDictionary(b => b.Name, b => b.Tokens);
        var blocks = families.Select(f => (Where: f.Key, Theme: f.Key, Accent: (Dictionary<string, string>?)null))
            .Concat(DeskCss.AccentBlocks().Select(a => (Where: $"{a.Theme} {a.Accent}", a.Theme, Accent: (Dictionary<string, string>?)a.Tokens)));

        foreach (var (where, theme, accent) in blocks)
        {
            var dark = theme.EndsWith("-dark", StringComparison.Ordinal);
            var declared = new Dictionary<string, string>();
            foreach (var layer in new[] { DeskCss.DerivedRule, dark ? DeskCss.DarkFallback : DeskCss.LightDefaults, families[theme], accent ?? [] })
                foreach (var (token, value) in layer)
                    declared[token] = value;

            foreach (var fill in new[] { "--accent", "--state-positive", "--state-negative", "--state-warning" })
            {
                if (Wcag.Parse(DeskCss.ResolveValue(declared[fill], declared, 0)).A != 1)
                    failures.Add($"{where} {fill} is translucent, so its wash would not be exact");
            }

            foreach (var token in new[] { "--accent-wash", "--accent-wash-strong", "--state-positive-wash", "--state-negative-wash", "--state-warning-wash", "--key-highlight", "--key-gloss" })
            {
                var resolved = DeskCss.ResolveValue(declared[token], declared, 0);
                if (!Regex.IsMatch(resolved, @"^rgba\(\d+, \d+, \d+, [\d.]+\)$"))
                    failures.Add($"{where} {token} does not resolve without relative colour: {resolved}");
            }

            if (!Regex.IsMatch(DeskCss.ResolveValue(FocusRing, declared, 0), @"^0 0 0 3px rgba\([^)]*\), 0 0 0 1px #[0-9A-Fa-f]{6}$"))
                failures.Add($"{where} --focus-ring does not resolve without relative colour");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

        var nord = families["nord-dark"];
        var old = new Dictionary<string, string>();
        foreach (var layer in new[] { DeskCss.DerivedRule, DeskCss.DarkFallback, nord })
            foreach (var (token, value) in layer)
                old[token] = value;
        Assert.Equal("rgba(236, 239, 244, 0.0256)", DeskCss.ResolveValue(old["--key-highlight"], old, 0));
    }

    // ---- Source order -----------------------------------------------------------------------

    /// <summary>
    /// The resolver lays a family block over the alpha defaults; the browser does so only for a
    /// block that comes after them. The Standard blocks come before (they open desk.css), at the
    /// same weight, so an alpha written in one would pass every resolved test and lose to the
    /// default on screen. Nothing before the derived rules may declare an alpha, in a block or a
    /// media group. And the sheets load desk, controls, themes, so every themes.css block follows.
    /// </summary>
    [Fact]
    public void No_alpha_is_declared_before_the_defaults_it_would_lose_to()
    {
        var derived = Regex.Match(DeskCss.Desk, @"(?m)^:root,\s*\[data-theme\]\s*\{");
        Assert.True(derived.Success, "desk.css has no derived rule");

        var before = DeskCss.Desk[..derived.Index];
        Assert.Contains("[data-theme=\"apple-dark\"]", before, StringComparison.Ordinal);
        Assert.Contains("[data-theme=\"apple-light\"]", before, StringComparison.Ordinal);

        var early = Regex.Matches(before, @"(--[a-z0-9-]+-alpha)\s*:").Select(m => m.Groups[1].Value).ToList();
        Assert.True(early.Count == 0, $"declared before the defaults, so the defaults win: {string.Join(", ", early)}");

        var head = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "Layout", "DeskHead.razor"));
        var desk = head.IndexOf("DeskAssets.Css(\"desk.css\")", StringComparison.Ordinal);
        var controls = head.IndexOf("DeskAssets.Css(\"controls.css\")", StringComparison.Ordinal);
        var themes = head.IndexOf("DeskAssets.Css(\"themes.css\")", StringComparison.Ordinal);
        Assert.True(desk >= 0 && desk < controls && controls < themes, "DeskHead loads desk.css, controls.css, themes.css in that order");
        Assert.DoesNotMatch(@"--[a-z0-9-]+-alpha\s*:", DeskCss.Controls);
    }

    /// <summary>
    /// The derived rules' order is load-bearing: the dark defaults, the relative-colour group that
    /// refines them, then the light rule — <html> matches :root in both dark rules, and on a light
    /// desk the light rule must come last — then Increase Contrast, which outlasts all three.
    /// </summary>
    [Fact]
    public void The_derived_rules_stand_in_their_order()
    {
        var css = DeskCss.Desk;
        int At(string pattern)
        {
            var m = Regex.Match(css, pattern);
            Assert.True(m.Success, $"desk.css has no {pattern}");
            return m.Index;
        }

        var derived = At(@"(?m)^:root,\s*\[data-theme\]\s*\{");
        var dark = At(@"(?m)^:root,\s*\[data-theme\$=""-dark""\]\s*\{");
        var group = At(DeskCss.RelativeColourSupports);
        var light = At(@"(?m)^\[data-theme\$=""-light""\]\s*\{");
        var contrast = At(@"@media \(prefers-contrast: more\) \{\s*:root,\s*\[data-theme\] \{");

        Assert.True(derived < dark && dark < group && group < light && light < contrast,
            $"derived {derived}, dark {dark}, group {group}, light {light}, more contrast {contrast}");
    }

    // ---- The media blocks the resolver does not walk -----------------------------------------

    /// <summary>
    /// Asked for more contrast, the key's line and sheen go to an alpha of 0 on every block, and
    /// nothing else derived moves: no wash, no blur. Laid over every resolved block, as a browser
    /// with the query on does, every key's finish is transparent.
    /// </summary>
    [Fact]
    public void More_contrast_sets_the_key_alphas_to_zero_and_nothing_else_derived()
    {
        var group = Regex.Match(DeskCss.Desk, @"@media \(prefers-contrast: more\) \{\s*:root,\s*\[data-theme\] \{(?<b>[^}]*)\}");
        Assert.True(group.Success, "desk.css has no Increase Contrast rule on :root, [data-theme]");
        var set = DeskCss.Declarations(group.Groups["b"].Value);

        AssertSame(new Dictionary<string, string> { ["--key-highlight-alpha"] = "0", ["--key-gloss-alpha"] = "0" }, set);

        var families = DeskCss.ThemeBlocks().ToDictionary(b => b.Name, b => b.Tokens);
        foreach (var (name, tokens) in families)
        {
            var under = new Dictionary<string, string>(tokens);
            foreach (var (token, value) in set)
                under[token] = value;

            var resolved = DeskCss.Resolve(name, under, null);
            Assert.Equal(0d, Wcag.Parse(resolved["--key-highlight"]).A);
            Assert.Equal(0d, Wcag.Parse(resolved["--key-gloss"]).A);
        }

        // The other Increase Contrast group, before the derived rules, sets Standard's hairline and
        // secondary ink and no derived token or alpha.
        var standard = DeskCss.MediaBody(DeskCss.Desk, @"@media\s*\(prefers-contrast:\s*more\)");
        Assert.DoesNotMatch(@"--[a-z0-9-]+-(alpha|wash[a-z-]*)\s*:|--key-|--material-blur", standard);
    }

    /// <summary>
    /// A Windows contrast theme drops the blur and the translucency: under forced colours
    /// <c>html:root</c> — heavier than every block — sets the four materials to Canvas and the blur
    /// to none, and sets no wash, since the browser paints selection in its own pair there.
    /// </summary>
    [Fact]
    public void Forced_colours_set_the_blur_to_none_and_the_materials_to_canvas()
    {
        var forced = DeskCss.MediaBody(DeskCss.Desk, @"@media\s*\(forced-colors:\s*active\)");
        var root = Regex.Match(forced, @"html:root\s*\{([^{}]*)\}");
        Assert.True(root.Success, "the forced-colours block has no html:root rule");

        AssertSame(new Dictionary<string, string>
        {
            ["--material-ultrathin"] = "Canvas",
            ["--material-thin"] = "Canvas",
            ["--material-regular"] = "Canvas",
            ["--material-thick"] = "Canvas",
            ["--material-blur"] = "none"
        }, DeskCss.Declarations(root.Groups[1].Value));

        Assert.DoesNotMatch(@"--[a-z-]*wash[a-z-]*\s*:|--key-(highlight|gloss)[a-z-]*\s*:", forced);
    }

    /// <summary>
    /// Reduce Transparency, by either route: the media query turns every material surface opaque and
    /// drops its backdrop filter, and the attribute theme.js stamps, at :root's weight plus its own,
    /// sets the material tokens to the opaque tier and the blur to none. Neither touches a wash.
    /// </summary>
    [Fact]
    public void Reduced_transparency_sets_the_blur_and_materials_and_no_wash()
    {
        var media = DeskCss.MediaBody(DeskCss.Desk, @"@media\s*\(prefers-reduced-transparency:\s*reduce\)");
        Assert.Matches(@"backdrop-filter:\s*none;", media);
        Assert.DoesNotMatch(@"--[a-z0-9-]+\s*:", media);

        var attribute = DeskCss.Declarations(DeskCss.Block(DeskCss.TopLevel(DeskCss.Desk), @"(?m)^:root\[data-reduced-transparency=""true""\]"));
        AssertSame(new Dictionary<string, string>
        {
            ["--material-ultrathin"] = "var(--surface-2)",
            ["--material-thin"] = "var(--surface-2)",
            ["--material-regular"] = "var(--surface-2)",
            ["--material-thick"] = "var(--surface-2)",
            ["--material-blur"] = "none"
        }, attribute);
    }
}
