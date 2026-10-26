using System.Globalization;
using System.Text.RegularExpressions;

namespace LineOps.Web.Tests;

/// <summary>
/// Contrast, measured from the stylesheets rather than trusted from a table (HIG live audit,
/// 25 October: Top findings 1, 4, 5, 6 and 14).
///
/// <para>
/// A state colour is a fill first. Apple's light-mode green, orange and red are tuned to be
/// seen as a dot or a wash, and as text on white they measure 2.2 to 3.6:1 — so every desk
/// carries a second, darker (or on a dark desk, lighter) <c>-ink</c> for the same state, and
/// text reads that. The accent is the same story. These tests parse every
/// <c>[data-theme="…"]</c> block in lineops.css and themes.css and hold each ink to WCAG's
/// 4.5:1 over that block's own panel, so an eleventh desk is checked the day it is written.
/// </para>
///
/// <para>
/// The calculator is written out here rather than borrowed from MudColor on purpose: this file
/// is the one a reviewer opens to check a number, and it should read as the WCAG 2.x formula
/// and nothing else.
/// </para>
/// </summary>
public class ThemeContrastTests
{
    private static readonly string Css = Strip(Read("src/LineOps.Desk/wwwroot/css/lineops.css"));
    private static readonly string ThemesCss = Strip(Read("src/LineOps.Desk/wwwroot/css/themes.css"));

    private static readonly string[] StateInks = ["--state-positive-ink", "--state-warning-ink", "--state-negative-ink"];

    // ---- Task 6: inks ------------------------------------------------------------------

    [Fact]
    public void Every_theme_block_is_found()
    {
        // Five families, a dark and a light each. Fewer means the parser lost a block, and a
        // contrast suite that quietly checks eight desks is worse than none.
        var names = ThemeBlocks().Select(b => b.Name).ToArray();

        Assert.Equal(10, names.Length);
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

    // ---- Parsing ---------------------------------------------------------------------------

    /// <summary>
    /// Every top-level <c>[data-theme="…"]</c> block in both files, name to declarations. Blocks
    /// inside an @media or @supports are overrides of a desk, not desks, and are skipped.
    /// </summary>
    private static IEnumerable<(string Name, Dictionary<string, string> Tokens)> ThemeBlocks()
    {
        foreach (var css in new[] { Css, ThemesCss })
        {
            var topLevel = Regex.Replace(css, @"@(media|supports)[^{]*" + Balanced, string.Empty);

            foreach (Match m in Regex.Matches(topLevel, @"\[data-theme=""([a-z-]+)""\]\s*\{([^{}]*)\}"))
                yield return (m.Groups[1].Value, Declarations(m.Groups[2].Value));
        }
    }

    /// <summary>A brace-balanced group: <c>{ … { … } … }</c>.</summary>
    private const string Balanced = @"\{(?>[^{}]+|\{(?<d>)|\}(?<-d>))*(?(d)(?!))\}";

    private static string MediaBody(string css, string opener)
    {
        var m = Regex.Match(css, opener + @"[^{]*(" + Balanced + ")");

        Assert.True(m.Success, $"no block opening {opener}");

        return m.Groups[1].Value[1..^1];
    }

    private static Dictionary<string, string> BlockIn(string css, string theme)
    {
        var m = Regex.Match(css, @"\[data-theme=""" + theme + @"""\]\s*\{([^{}]*)\}");

        Assert.True(m.Success, $"no {theme} block");

        return Declarations(m.Groups[1].Value);
    }

    private static Dictionary<string, string> Declarations(string body)
    {
        var tokens = new Dictionary<string, string>();

        foreach (Match d in Regex.Matches(body, @"(--[a-z0-9-]+)\s*:\s*([^;]+);"))
            tokens[d.Groups[1].Value] = d.Groups[2].Value.Trim();

        return tokens;
    }

    private static string? Require(Dictionary<string, string> tokens, string token, string theme, List<string> failures)
    {
        if (tokens.TryGetValue(token, out var value))
            return value;

        failures.Add($"{theme} does not declare {token}");
        return null;
    }

    // ---- WCAG 2.x ----------------------------------------------------------------------

    /// <summary>
    /// Contrast of a possibly translucent ink over an opaque panel. Compositing happens in
    /// gamma-encoded sRGB, as a browser does, and linearisation comes after.
    /// </summary>
    private static double Contrast(string ink, string panel)
    {
        var (br, bg, bb, _) = Parse(panel);
        var (r, g, b, a) = Parse(ink);

        var cr = r * a + br * (1 - a);
        var cg = g * a + bg * (1 - a);
        var cb = b * a + bb * (1 - a);

        var fore = Luminance(cr, cg, cb);
        var back = Luminance(br, bg, bb);

        return (Math.Max(fore, back) + 0.05) / (Math.Min(fore, back) + 0.05);
    }

    private static double Luminance(double r, double g, double b)
        => 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);

    private static double Linear(double channel)
    {
        var c = channel / 255d;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static (double R, double G, double B, double A) Parse(string colour)
    {
        colour = colour.Trim();

        if (colour.StartsWith('#'))
        {
            var hex = colour[1..];
            Assert.True(hex.Length == 6, $"expected #RRGGBB, got {colour}");
            return (Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..6], 16), 1);
        }

        var rgba = Regex.Match(colour, @"^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)\s*)?\)$");
        Assert.True(rgba.Success, $"not a colour this calculator reads: {colour}");

        double Channel(int i) => double.Parse(rgba.Groups[i].Value, CultureInfo.InvariantCulture);

        return (Channel(1), Channel(2), Channel(3), rgba.Groups[4].Success ? Channel(4) : 1);
    }

    // ---- Files -----------------------------------------------------------------------------

    private static string Strip(string css) => Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), relative));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LineOps.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
