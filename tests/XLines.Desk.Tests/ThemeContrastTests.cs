using System.Globalization;
using System.Text.RegularExpressions;
using XLines.Desk.Tests.Support;
using static XLines.Desk.Tests.Support.DeskCss;
using static XLines.Desk.Tests.Support.Wcag;

namespace XLines.Desk.Tests;

/// <summary>
/// Contrast, measured from the stylesheets rather than trusted from a table (HIG live audit,
/// 25 October: Top findings 1, 4, 5, 6 and 14).
///
/// <para>
/// A state colour is a fill first. Apple's light-mode green, orange and red are tuned to be
/// seen as a dot or a wash, and as text on white they measure 2.2 to 3.6:1 — so every desk
/// carries a second, darker (or on a dark desk, lighter) <c>-ink</c> for the same state, and
/// text reads that. The accent is the same story. These tests parse every
/// <c>[data-theme="…"]</c> block in desk.css and themes.css and hold each ink to WCAG's
/// 4.5:1 over that block's own panel, so an eleventh desk is checked the day it is written.
/// </para>
///
/// <para>
/// The calculator is <see cref="Wcag"/>, written out rather than borrowed from MudColor, and the
/// blocks are found by <see cref="DeskCss"/>; every theming test reads both, so a number checked
/// here is the number checked everywhere.
/// </para>
/// </summary>
public class ThemeContrastTests
{
    private static string Css => DeskCss.Desk;

    /// <summary>Every family block as an element wearing it draws, the derived washes and finish included.</summary>
    private static IEnumerable<(string Name, Dictionary<string, string> Tokens)> ThemeBlocks() => ResolvedThemeBlocks();

    /// <summary>Every accent block laid over its family block and resolved, so its washes are the ones recomputed from its fill.</summary>
    private static IEnumerable<(string Theme, string Accent, Dictionary<string, string> Tokens)> AccentBlocks() => ResolvedAccentBlocks();

    private static readonly string[] StateInks = ["--state-positive-ink", "--state-warning-ink", "--state-negative-ink"];

    // ---- Task 6: inks ------------------------------------------------------------------

    [Fact]
    public void Every_theme_block_is_found()
    {
        // Every family in the catalogue, a dark and a light each. Fewer means the parser lost a
        // block, and a contrast suite that quietly checks fewer desks than there are is worse than none.
        var names = ThemeBlocks().Select(b => b.Name).ToArray();

        Assert.Equal(XLines.Desk.Theming.DeskThemeFamilies.All.Count * 2, names.Length);
        Assert.Contains("apple-dark", names);
        Assert.Contains("apple-light", names);
        Assert.Contains("contrast-light", names);
    }

    [Fact]
    public void State_ink_tokens_pass_contrast_in_every_theme()
    {
        var failures = new List<string>();

        foreach (var (name, tokens) in ThemeBlocks())
        {
            var panel = Require(tokens, "--surface-1", name, failures);
            if (panel is null)
                continue;

            foreach (var ink in StateInks)
            {
                var value = Require(tokens, ink, name, failures);
                if (value is null)
                    continue;

                var ratio = Contrast(value, panel);
                if (ratio < 4.5)
                    failures.Add($"{name} {ink} {value} measures {ratio:F2}:1 over {panel}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Accent_ink_passes_in_every_theme()
    {
        var failures = new List<string>();

        foreach (var (name, tokens) in ThemeBlocks())
        {
            var panel = Require(tokens, "--surface-1", name, failures);
            var ink = Require(tokens, "--accent-ink", name, failures);
            if (panel is null || ink is null)
                continue;

            var ratio = Contrast(ink, panel);
            if (ratio < 4.5)
                failures.Add($"{name} --accent-ink {ink} measures {ratio:F2}:1 over {panel}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// A Filled key is a label on the accent, at rest, under the pointer and while pressed, so
    /// --on-accent is held to 4.5:1 over --accent, --accent-hover and --accent-press in every
    /// block, the chosen accents included (they inherit the Standard block's --on-accent). Apple's
    /// own blue measured 4.02:1 under white on the light desk and 3.65:1 on the dark one (HIG
    /// audit, 17 November); the fills were deepened rather than the label changed.
    /// </summary>
    [Fact]
    public void A_filled_key_label_passes_over_every_accent_fill()
    {
        var failures = new List<string>();
        string[] fills = ["--accent", "--accent-hover", "--accent-press"];
        var labels = new Dictionary<string, string>();

        foreach (var (name, tokens) in ThemeBlocks())
        {
            var label = Require(tokens, "--on-accent", name, failures);
            if (label is null)
                continue;
            labels[name] = label;

            foreach (var fill in fills)
            {
                var value = Require(tokens, fill, name, failures);
                if (value is null)
                    continue;

                var ratio = Contrast(label, value);
                if (ratio < 4.5)
                    failures.Add($"{name} --on-accent {label} measures {ratio:F2}:1 over {fill} {value}");
            }
        }

        foreach (var (theme, accent, tokens) in AccentBlocks())
        {
            if (!labels.TryGetValue(theme, out var label))
                continue;

            foreach (var fill in fills)
            {
                if (!tokens.TryGetValue(fill, out var value))
                    continue;

                var ratio = Contrast(label, value);
                if (ratio < 4.5)
                    failures.Add($"{theme} {accent} --on-accent {label} measures {ratio:F2}:1 over {fill} {value}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// An ink is read on the raised surface as often as on the panel: a metric tile, a title bar,
    /// a raised row and a resting control are all --surface-2. The light desk's first inks were
    /// tuned to 4.5:1 on white and measured 3.6:1 on the tiles (HIG audit, 17 November), so every
    /// ink — the three states and the accent, the chosen accents included — is held over both.
    /// </summary>
    [Fact]
    public void Every_ink_also_passes_over_the_raised_surface()
    {
        var failures = new List<string>();
        var raised = new Dictionary<string, string>();

        foreach (var (name, tokens) in ThemeBlocks())
        {
            var surface = Require(tokens, "--surface-2", name, failures);
            if (surface is null)
                continue;
            raised[name] = surface;

            foreach (var ink in StateInks.Append("--accent-ink"))
            {
                var value = Require(tokens, ink, name, failures);
                if (value is null)
                    continue;

                var ratio = Contrast(value, surface);
                if (ratio < 4.5)
                    failures.Add($"{name} {ink} {value} measures {ratio:F2}:1 over {surface}");
            }
        }

        foreach (var (theme, accent, tokens) in AccentBlocks())
        {
            if (!tokens.TryGetValue("--accent-ink", out var ink) || !raised.TryGetValue(theme, out var surface))
                continue;

            var ratio = Contrast(ink, surface);
            if (ratio < 4.5)
                failures.Add($"{theme} {accent} --accent-ink {ink} measures {ratio:F2}:1 over {surface}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Every family's chosen accents carry inks of their own, and each is held to the same 4.5:1
    /// over the panel of the family it sits on — that block's --surface-1, since an accent block
    /// redeclares the accent and nothing under it. The pale hues are why this exists: Standard's
    /// teal press measures 4.09:1 on white, indigo's dark hover 4.36:1 over the dark panel, and
    /// each ink is deepened or lifted a step past that. Every other check in this file that reads
    /// AccentBlocks holds them by the family block they name, so a family's accents get the
    /// whole suite the day they are written.
    /// </summary>
    [Fact]
    public void Every_chosen_accent_ink_passes_on_its_family_s_desk()
    {
        var panels = ThemeBlocks().ToDictionary(b => b.Name, b => b.Tokens["--surface-1"]);
        var blocks = AccentBlocks().ToList();
        var failures = new List<string>();

        // One block per accent after each family's own, a dark and a light each. Fewer means the
        // parser lost one.
        Assert.Equal(XLines.Desk.Theming.DeskThemeFamilies.Catalog.Sum(f => f.Accents.Count - 1) * 2, blocks.Count);

        foreach (var (theme, accent, tokens) in blocks)
        {
            if (!panels.TryGetValue(theme, out var panel))
            {
                failures.Add($"{theme} {accent} names a desk with no block of its own");
                continue;
            }

            var ink = Require(tokens, "--accent-ink", $"{theme} {accent}", failures);
            if (ink is null)
                continue;

            var ratio = Contrast(ink, panel);
            if (ratio < 4.5)
                failures.Add($"{theme} {accent} --accent-ink {ink} measures {ratio:F2}:1 over {panel}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The Gradient backdrop mixes the accent into the ground at --backdrop-tint, and the empty
    /// desk's start screen sets primary and secondary text straight on it. Both are held to 4.5:1
    /// over the brightest point of the glow on every desk, in the family's own accent and in every
    /// accent it offers. The Grid's lines are --surface-2 and the Dots --surface-3, which
    /// <see cref="Secondary_text_reads_on_every_surface_it_is_set_on"/> already holds secondary to.
    /// </summary>
    [Fact]
    public void Text_on_the_empty_desk_reads_over_the_gradient_backdrop()
    {
        var tint = Regex.Match(Css, @"--backdrop-tint:\s*([0-9.]+)%;");
        Assert.True(tint.Success, "desk.css declares no --backdrop-tint");
        var t = double.Parse(tint.Groups[1].Value, CultureInfo.InvariantCulture) / 100d;
        Assert.InRange(t, 0.01, 0.25);

        var failures = new List<string>();
        var desks = ThemeBlocks().ToDictionary(b => b.Name, b => b.Tokens);

        var cases = desks.Select(d => (Where: d.Key, Desk: d.Value, Accent: d.Value["--accent"]))
            .Concat(AccentBlocks()
                .Where(b => desks.ContainsKey(b.Theme))
                .Select(b => (Where: $"{b.Theme} {b.Accent}", Desk: desks[b.Theme], Accent: b.Tokens["--accent"])));

        foreach (var (where, desk, accent) in cases)
        {
            var (ar, ag, ab, _) = Parse(accent);
            var (gr, gg, gb, _) = Parse(desk["--surface-0"]);
            var glow = string.Format(CultureInfo.InvariantCulture, "rgb({0:F0}, {1:F0}, {2:F0})",
                ar * t + gr * (1 - t), ag * t + gg * (1 - t), ab * t + gb * (1 - t));

            foreach (var ink in new[] { "--text-primary", "--text-secondary" })
            {
                var ratio = Contrast(desk[ink], glow);
                if (ratio < 4.5)
                    failures.Add($"{where} {ink} measures {ratio:F2}:1 over the gradient's glow {glow}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- The theming audit, 20 November: recommendations 3, 4 and 5 ---------------------

    /// <summary>
    /// Secondary text is not only set on the panel. It sits on --surface-0 (the gate's track, the
    /// window strip's channel), --surface-2 (title bars, tiles, the sticky table header, hovered
    /// rows) and --surface-3 (the neutral tag, the badge at caption size, MudChip's neutral
    /// capsule). Over --surface-3 nine blocks measured 3.75 to 4.47:1 (contrast audit, T1), so the
    /// tier is held to 4.5:1 on all four. The two steep dark ramps, Catppuccin and Gruvbox, were
    /// mended by easing --surface-3 halfway back toward --surface-2 rather than by lifting
    /// secondary to .77, which would have pushed it to 7:1 on the panel and toward primary.
    /// </summary>
    [Fact]
    public void Secondary_text_reads_on_every_surface_it_is_set_on()
    {
        var failures = new List<string>();
        string[] surfaces = ["--surface-0", "--surface-1", "--surface-2", "--surface-3"];

        foreach (var (name, tokens) in ThemeBlocks())
        {
            var secondary = Require(tokens, "--text-secondary", name, failures);
            if (secondary is null)
                continue;

            foreach (var surface in surfaces)
            {
                var ground = Require(tokens, surface, name, failures);
                if (ground is null)
                    continue;

                var ratio = Contrast(secondary, ground);
                if (ratio < 4.5)
                    failures.Add($"{name} --text-secondary {secondary} measures {ratio:F2}:1 over {surface} {ground}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The destructive Filled key is --on-accent on --state-negative, and --state-negative-hover
    /// under the pointer. A press keeps the hover fill (the destructive hover rule outranks the
    /// Filled key's :active one), and there is no separate press token, so the two fills are the
    /// whole of it. Standard dark measured 3.41:1 at rest and 2.82:1 on hover (contrast audit, T2).
    /// The hover travels away from its label — a red under white deepens, a red under dark ink
    /// lifts — because that is the only way the label keeps 4.5:1 on both fills, and it moves at
    /// least <see cref="HoverStep"/> in luminance, so the pointer is seen: Standard dark and
    /// Solarized dark once lightened by a hair, 1.02:1, a hover nobody could see (review,
    /// 2026-11-21). The fill, which is also the negative state's dot, stays a 3:1 graphic on the
    /// panel.
    /// </summary>
    private const double HoverStep = 1.1;

    [Fact]
    public void A_destructive_filled_key_label_passes_over_the_negative_fill()
    {
        var failures = new List<string>();
        var measured = 0;

        foreach (var (name, tokens) in ThemeBlocks())
        {
            var label = Require(tokens, "--on-accent", name, failures);
            var rest = Require(tokens, "--state-negative", name, failures);
            var hover = Require(tokens, "--state-negative-hover", name, failures);
            var panel = Require(tokens, "--surface-1", name, failures);
            if (label is null || rest is null || hover is null || panel is null)
                continue;

            foreach (var (token, fill) in new (string Token, string Fill)[] { ("--state-negative", rest), ("--state-negative-hover", hover) })
            {
                var ratio = Contrast(label, fill);
                if (ratio < 4.5)
                    failures.Add($"{name} --on-accent {label} measures {ratio:F2}:1 over {token} {fill}");
            }

            var lighter = Luminance(hover) > Luminance(rest);
            var labelLighter = Luminance(label) > Luminance(rest);
            if (lighter == labelLighter)
                failures.Add($"{name} --state-negative-hover {hover} travels toward its label {label} from {rest}");

            var step = Contrast(hover, rest);
            if (step < HoverStep)
                failures.Add($"{name} --state-negative-hover {hover} is {step:F3}:1 from {rest}, under the {HoverStep}:1 a hover needs to be seen");

            var graphic = Contrast(rest, panel);
            if (graphic < 3)
                failures.Add($"{name} --state-negative {rest} measures {graphic:F2}:1 over {panel}, under 3:1 as a graphic");
            measured++;
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.Equal(XLines.Desk.Theming.DeskThemeFamilies.All.Count * 2, measured);
    }

    /// <summary>
    /// A state's wash is its fill, thinned: the same RGB at the block's own alpha. When a fill
    /// moves — deepened or lifted until its label reads — the wash moves with it, or a tag and a
    /// selected destructive row are tinted with a red the desk no longer shows. Nord dark, Nord
    /// light and Gruvbox dark kept the old red in their wash after the fill moved (review,
    /// 2026-11-21); Every_ink_passes_over_its_own_wash measures the result.
    /// </summary>
    [Fact]
    public void Every_state_wash_is_its_own_fill_thinned()
    {
        var failures = new List<string>();

        foreach (var (name, tokens) in ThemeBlocks())
        {
            foreach (var state in new[] { "positive", "warning", "negative" })
            {
                var fill = Require(tokens, $"--state-{state}", name, failures);
                var wash = Require(tokens, $"--state-{state}-wash", name, failures);
                if (fill is null || wash is null)
                    continue;

                var (fr, fg, fb, _) = Parse(fill);
                var (wr, wg, wb, wa) = Parse(wash);
                if ((fr, fg, fb) != (wr, wg, wb) || wa >= 1)
                    failures.Add($"{name} --state-{state}-wash {wash} is not --state-{state} {fill} thinned");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// An ink is read on its own wash as often as on a bare surface: a tag is a state ink on its
    /// wash over the panel, the current window key is --accent-ink on --accent-wash inside the
    /// --surface-0 channel, a selected row or a Tinted key is the same over the panel, and a
    /// Tinted key under the pointer is --accent-ink on --accent-wash-strong. Twenty-two cells fell
    /// short (contrast audit, T10), the worst a Tinted key's hover at 3.64:1 on Dracula dark. The
    /// wash is composited onto its surface in gamma-encoded sRGB first, the ink onto that, and
    /// only then is anything linearised. The chosen accents are held on the Standard surfaces.
    /// </summary>
    [Fact]
    public void Every_ink_passes_over_its_own_wash()
    {
        var failures = new List<string>();
        var grounds = new Dictionary<string, (string Panel, string Void)>();

        foreach (var (name, tokens) in ThemeBlocks())
        {
            var panel = Require(tokens, "--surface-1", name, failures);
            var ground = Require(tokens, "--surface-0", name, failures);
            if (panel is null || ground is null)
                continue;
            grounds[name] = (panel, ground);

            foreach (var state in new[] { "positive", "warning", "negative" })
            {
                var ink = Require(tokens, $"--state-{state}-ink", name, failures);
                var wash = Require(tokens, $"--state-{state}-wash", name, failures);
                if (ink is not null && wash is not null)
                    CheckOnWash(name, $"--state-{state}-ink", ink, $"--state-{state}-wash", wash, "--surface-1", panel, failures);
            }

            CheckAccentInk(name, tokens, panel, ground, failures);
        }

        foreach (var (theme, accent, tokens) in AccentBlocks())
        {
            if (grounds.TryGetValue(theme, out var g))
                CheckAccentInk($"{theme} {accent}", tokens, g.Panel, g.Void, failures);
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static void CheckAccentInk(string where, Dictionary<string, string> tokens, string panel, string ground, List<string> failures)
    {
        var ink = Require(tokens, "--accent-ink", where, failures);
        var wash = Require(tokens, "--accent-wash", where, failures);
        var strong = Require(tokens, "--accent-wash-strong", where, failures);
        if (ink is null || wash is null || strong is null)
            return;

        CheckOnWash(where, "--accent-ink", ink, "--accent-wash", wash, "--surface-1", panel, failures);
        CheckOnWash(where, "--accent-ink", ink, "--accent-wash", wash, "--surface-0", ground, failures);
        CheckOnWash(where, "--accent-ink", ink, "--accent-wash-strong", strong, "--surface-1", panel, failures);
    }

    private static void CheckOnWash(string where, string inkToken, string ink, string washToken, string wash, string surfaceToken, string surface, List<string> failures)
    {
        var ratio = Contrast(ink, wash, surface);
        if (ratio < 4.5)
            failures.Add($"{where} {inkToken} {ink} measures {ratio:F2}:1 over {washToken} {wash} on {surfaceToken} {surface}");
    }

    /// <summary>
    /// The ink exists so text can read the state; a text rule still on the fill token is the
    /// defect the ink was added to fix. Fills, dots, borders and washes stay on the fill.
    /// </summary>
    [Fact]
    public void No_text_rule_reads_a_state_fill_or_the_accent_fill()
    {
        var offenders = Regex.Matches(Css, @"(?<![-\w])color\s*:\s*var\(--(state-(?:positive|negative|warning)|accent)\)")
            .Select(m => m.Value)
            .ToArray();

        Assert.True(offenders.Length == 0, $"text on a fill token: {offenders.Length} rule(s), e.g. {offenders.FirstOrDefault()}");
    }

    [Theory]
    [InlineData(".launcher__desc")]
    [InlineData(".metric__note")]
    [InlineData(".grid .dim")]
    [InlineData(".price--closing .price__sel")]
    [InlineData(".snippet__note")]
    [InlineData(".hdr__sub")]
    [InlineData(".bar__key")]
    [InlineData(".menu__heading")]
    public void Text_people_must_read_is_never_tertiary(string selector)
    {
        foreach (Match rule in Regex.Matches(Css, Regex.Escape(selector) + @"\s*\{([^}]*)\}"))
            Assert.DoesNotContain("--text-tertiary", rule.Groups[1].Value, StringComparison.Ordinal);
    }

    // ---- Task 7: the OS settings, the ring, the floor, the spring -----------------------

    /// <summary>
    /// Increase Contrast on the Standard desk (the operator chose Standard, so ThemeService does
    /// not swap in the contrast family): hairlines and secondary text both step up.
    /// </summary>
    [Fact]
    public void Increase_contrast_raises_the_standard_desks_hairline_and_secondary_text()
    {
        var media = MediaBody(Css, @"@media\s*\(prefers-contrast:\s*more\)");

        var dark = BlockIn(media, "apple-dark");
        var light = BlockIn(media, "apple-light");

        Assert.Equal("rgba(255, 255, 255, .24)", dark["--separator"]);
        Assert.Equal("rgba(255, 255, 255, .70)", dark["--text-secondary"]);
        Assert.Equal("rgba(0, 0, 0, .24)", light["--separator"]);
        Assert.Equal("rgba(0, 0, 0, .75)", light["--text-secondary"]);
    }

    /// <summary>
    /// Reduce Transparency, whether the browser reports it or theme.js stamps it, gets exactly
    /// the opaque surfaces the no-backdrop-filter path already defines — no fewer.
    /// </summary>
    [Fact]
    public void Reduced_transparency_makes_every_fallback_material_opaque()
    {
        // The material fallback is the no-blur block that holds the header; a component may
        // carry a small one of its own (the book picker does), which is not this.
        var supports = Regex.Matches(Css, @"@supports\s+not\s+\(\(backdrop-filter:\s*blur\(1px\)\)[^{]*(" + Balanced + ")")
            .Select(m => m.Groups[1].Value[1..^1])
            .Single(body => body.Contains(".hdr", StringComparison.Ordinal));
        var selectors = Regex.Matches(supports, @"([^{}]+)\{")
            .SelectMany(m => m.Groups[1].Value.Split(','))
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();

        Assert.NotEmpty(selectors);

        var reduced = MediaBody(Css, @"@media\s*\(prefers-reduced-transparency:\s*reduce\)");

        foreach (var selector in selectors)
        {
            Assert.Contains(selector, reduced, StringComparison.Ordinal);
            Assert.Contains("[data-reduced-transparency=\"true\"] " + selector, Css, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Focus_is_one_ring_outside_the_wager_block()
    {
        // Comments are stripped, so the block is found by its first rule.
        var wager = Css.IndexOf(".wsections {", StringComparison.Ordinal);
        Assert.True(wager > 0, "the wager block was not found");
        var head = Css[..wager];

        Assert.DoesNotMatch(@"outline:\s*2px", head);
    }

    [Fact]
    public void The_spring_is_kept_for_dialog_and_sheet_entrances()
    {
        // Every use of the spring, by the rule it sits in.
        var users = Regex.Matches(Css, @"([^{}]+)\{[^{}]*var\(--ease-spring\)")
            .Select(m => m.Groups[1].Value.Trim())
            .ToArray();

        Assert.NotEmpty(users);
        Assert.All(users, selector => Assert.Matches(@"deskdialog|desk-sheet", selector));
    }

    [Fact]
    public void Metric_tiles_do_not_lift_on_hover()
        => Assert.DoesNotMatch(@"\.metric[\w-]*:hover\s*\{[^}]*translateY", Css);

    [Theory]
    [InlineData("--text-footnote", 11)]
    [InlineData("--text-caption", 10)]
    public void Small_type_has_a_ten_pixel_floor_at_every_scale(string rung, int size)
    {
        var declared = Regex.Match(Css, Regex.Escape(rung) + @"\s*:\s*([^;]+);");

        Assert.True(declared.Success, $"{rung} is not declared");
        Assert.Equal(
            $"max(calc(10px / var(--ui-scale)), calc({size}px * var(--text-scale)))",
            declared.Groups[1].Value.Trim());
    }

    /// <summary>
    /// A scrim subtracts attention, and on a light desk that means going darker (DeskPalette.Overlay).
    /// Every light desk's scrim darkens white by at least 1.5:1; the lightest of the dark-ink scrims
    /// measures about 1.55. Primer light's was its own pale grey at .40, 1.18:1 — barely a scrim.
    /// </summary>
    [Fact]
    public void Every_light_desk_s_scrim_darkens_white()
    {
        var failures = new List<string>();

        foreach (var family in XLines.Desk.Theming.DeskThemeFamilies.All)
        {
            var overlay = XLines.Desk.Theming.DeskThemeFamilies.Palette(family, false).Overlay;
            var ratio = Contrast(overlay, "#FFFFFF");

            if (ratio < 1.5)
                failures.Add($"{XLines.Desk.Theming.DeskThemeFamilies.Attribute(family, false)} scrim {overlay} darkens white only {ratio:F2}:1");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// A warning is not the accent: on every desk the warning fill stands at least .06 from the
    /// family's own accent, and its ink .05 from the accent's ink, as distances in OKLab — so a
    /// warning tag never reads as an accent-tinted key. Ayu light's orange stood .048 from its gold
    /// and its ink .032 from the gold's ink. (The same floor against the primary text is not held:
    /// Everforest's published yellow ink sits .05 from its fg, as Zenburn's did before it took
    /// orange, and a floor that passed it would not have caught Zenburn.)
    /// </summary>
    [Fact]
    public void A_warning_stands_apart_from_its_desk_s_accent()
    {
        var failures = new List<string>();

        foreach (var (name, tokens) in ThemeBlocks())
        {
            var panel = Require(tokens, "--surface-1", name, failures);
            if (panel is null)
                continue;

            foreach (var (warning, accent, floor) in new[] { ("--state-warning", "--accent", .06), ("--state-warning-ink", "--accent-ink", .05) })
            {
                var w = Require(tokens, warning, name, failures);
                var a = Require(tokens, accent, name, failures);
                if (w is null || a is null)
                    continue;

                var distance = OkDistance(w, a, panel);
                if (distance < floor)
                    failures.Add($"{name} {warning} {w} stands {distance:F3} from {accent} {a}, under {floor}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
