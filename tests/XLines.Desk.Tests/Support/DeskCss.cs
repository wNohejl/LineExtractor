using System.Text.RegularExpressions;

namespace XLines.Desk.Tests.Support;

/// <summary>
/// The Desk's stylesheets as the theming tests read them: the sheets with their comments taken
/// out, the token blocks found in them, and each block's declarations.
///
/// <para>
/// A family block is a top-level <c>[data-theme="…"]</c> rule in desk.css (Standard) or
/// themes.css (the rest); a block inside an <c>@media</c> or <c>@supports</c> overrides a desk
/// and is not one. An accent block is the compound <c>[data-theme="…"][data-accent="…"]</c>,
/// which the family pattern never matches, because a compound selector is not followed by a
/// brace straight after its first attribute. Comments come out first: the blocks explain
/// themselves at length, and prose naming a token is not a declaration of it.
/// </para>
/// </summary>
public static class DeskCss
{
    private static readonly Lazy<string> DeskSheet = new(() => Stripped("desk.css"));
    private static readonly Lazy<string> ThemesSheet = new(() => Stripped("themes.css"));
    private static readonly Lazy<string> ControlsSheet = new(() => Stripped("controls.css"));

    /// <summary>desk.css, comments stripped: the Standard blocks and every rule.</summary>
    public static string Desk => DeskSheet.Value;

    /// <summary>themes.css, comments stripped: every other family and every chosen accent.</summary>
    public static string Themes => ThemesSheet.Value;

    /// <summary>controls.css, comments stripped.</summary>
    public static string Controls => ControlsSheet.Value;

    /// <summary>A brace-balanced group: <c>{ … { … } … }</c>.</summary>
    public const string Balanced = @"\{(?>[^{}]+|\{(?<d>)|\}(?<-d>))*(?(d)(?!))\}";

    /// <summary>One of the Desk's sheets as it ships, comments and all.</summary>
    public static string Raw(string name)
        => File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "css", name));

    /// <summary>One of the Desk's sheets with its comments taken out.</summary>
    public static string Stripped(string name) => Strip(Raw(name));

    public static string Strip(string css) => Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    /// <summary>Every custom property a rule body declares, name to value; a later declaration wins.</summary>
    public static Dictionary<string, string> Declarations(string body)
    {
        var tokens = new Dictionary<string, string>();

        foreach (Match d in Regex.Matches(body, @"(--[a-z0-9-]+)\s*:\s*([^;]+);"))
            tokens[d.Groups[1].Value] = d.Groups[2].Value.Trim();

        return tokens;
    }

    /// <summary>Every top-level family block in desk.css then themes.css, name to what it declares.</summary>
    public static IEnumerable<(string Name, Dictionary<string, string> Tokens)> ThemeBlocks()
    {
        foreach (var css in new[] { Desk, Themes })
        {
            foreach (Match m in Regex.Matches(TopLevel(css), @"\[data-theme=""([a-z-]+)""\]\s*\{([^{}]*)\}"))
                yield return (m.Groups[1].Value, Declarations(m.Groups[2].Value));
        }
    }

    /// <summary>Every accent block in both files: the family block it refines, the accent it names, what it declares.</summary>
    public static IEnumerable<(string Theme, string Accent, Dictionary<string, string> Tokens)> AccentBlocks()
    {
        foreach (var css in new[] { Desk, Themes })
        {
            foreach (Match m in Regex.Matches(css, @"\[data-theme=""([a-z-]+)""\]\[data-accent=""([a-z-]+)""\]\s*\{([^{}]*)\}"))
                yield return (m.Groups[1].Value, m.Groups[2].Value, Declarations(m.Groups[3].Value));
        }
    }

    // ---- The derived tokens ---------------------------------------------------------------
    //
    // Some colour tokens are not declared per block: desk.css's "Derived tokens" computes them on
    // :root and every [data-theme] from what a block does declare (the washes from their fills, a
    // key's line and sheen from the family's light, at alphas defaulted by appearance). What a
    // block declares is one question — the exact-set tests ask it of ThemeBlocks and AccentBlocks.
    // What an element wearing the block draws with is another, and every contrast test asks that
    // one, of the resolved blocks below: the cascade an element carrying the block's data-theme
    // (and data-accent) meets, with every var() substituted and every color-mix and relative
    // colour evaluated as the browser would, so a measured wash is the colour on screen and not a
    // literal that no longer exists. The cascade is a current browser's: the relative-colour group
    // in desk.css applies. It is not a media engine, and it lays a family block over the defaults
    // whatever its place in the sheet; DerivedTokenValueTests holds the rules that keep both true
    // (no alpha before the defaults, the derived rules in their order) and reads the media groups.

    /// <summary>The rule on <c>:root, [data-theme]</c> that derives the washes and declares the blur.</summary>
    public static Dictionary<string, string> DerivedRule => TopLevelRule(@":root,\s*\[data-theme\]");

    /// <summary>
    /// The alpha defaults, and the key's line and sheen, for a dark block (and for <c>:root</c>), as
    /// a current browser lands on them: the top-level rule with the relative-colour rule laid over it.
    /// </summary>
    public static Dictionary<string, string> DarkDefaults
    {
        get
        {
            var defaults = DarkFallback;

            foreach (var (token, value) in RelativeColourDark)
                defaults[token] = value;

            return defaults;
        }
    }

    /// <summary>The dark rule as it stands outside any group: what a browser without relative colour draws with.</summary>
    public static Dictionary<string, string> DarkFallback => TopLevelRule(@":root,\s*\[data-theme\$=""-dark""\]");

    /// <summary>The opener of the group that holds what only a browser reading relative colour applies.</summary>
    public const string RelativeColourSupports = @"@supports\s*\(color:\s*rgb\(from white r g b\)\)";

    /// <summary>The dark rule inside <see cref="RelativeColourSupports"/>: the key's exact finish.</summary>
    public static Dictionary<string, string> RelativeColourDark
    {
        get
        {
            var m = Regex.Match(MediaBody(Desk, RelativeColourSupports), @":root,\s*\[data-theme\$=""-dark""\]\s*\{([^{}]*)\}");
            Assert.True(m.Success, "desk.css's relative-colour group holds no dark rule");
            return Declarations(m.Groups[1].Value);
        }
    }

    /// <summary>The same for a light block.</summary>
    public static Dictionary<string, string> LightDefaults => TopLevelRule(@"\[data-theme\$=""-light""\]");

    /// <summary>The tokens computed once rather than declared per block: everything the three rules declare but the alphas.</summary>
    public static IReadOnlyList<string> DerivedTokens => DerivedRule.Keys
        .Concat(DarkDefaults.Keys).Concat(LightDefaults.Keys)
        .Where(token => !IsAlpha(token))
        .Distinct()
        .Order(StringComparer.Ordinal)
        .ToList();

    /// <summary>The alphas a block may override, which both appearances default.</summary>
    public static IReadOnlyList<string> AlphaTokens => DarkDefaults.Keys.Where(IsAlpha).Order(StringComparer.Ordinal).ToList();

    public static bool IsAlpha(string token) => token.EndsWith("-alpha", StringComparison.Ordinal);

    /// <summary>Every family block, resolved: what an element carrying its data-theme draws with.</summary>
    public static IEnumerable<(string Name, Dictionary<string, string> Tokens)> ResolvedThemeBlocks()
    {
        var families = ThemeBlocks().ToList();

        foreach (var (name, tokens) in families)
            yield return (name, Resolve(name, tokens, null));
    }

    /// <summary>
    /// Every accent block, resolved over its family block: the whole token set an element carrying
    /// both attributes draws with, not only the tokens the accent block names.
    /// </summary>
    public static IEnumerable<(string Theme, string Accent, Dictionary<string, string> Tokens)> ResolvedAccentBlocks()
    {
        var families = ThemeBlocks().ToDictionary(b => b.Name, b => b.Tokens);

        foreach (var (theme, accent, tokens) in AccentBlocks())
        {
            Assert.True(families.ContainsKey(theme), $"{theme} {accent} names a desk with no block of its own");
            yield return (theme, accent, Resolve(theme, families[theme], tokens));
        }
    }

    /// <summary>
    /// The cascade for one element, lowest first — the derived rule, its appearance's defaults, the
    /// family block, the accent block — each a later or heavier rule than the one before, then every
    /// value resolved.
    /// </summary>
    public static Dictionary<string, string> Resolve(string theme, Dictionary<string, string> family, Dictionary<string, string>? accent)
    {
        var declared = new Dictionary<string, string>();
        var appearance = theme.EndsWith("-light", StringComparison.Ordinal) ? LightDefaults : DarkDefaults;

        foreach (var layer in new[] { DerivedRule, appearance, family, accent ?? [] })
        {
            foreach (var (token, value) in layer)
                declared[token] = value;
        }

        return declared.ToDictionary(p => p.Key, p => ResolveValue(p.Value, declared, 0));
    }

    /// <summary>
    /// One value with its <c>var()</c>s substituted from <paramref name="declared"/>, and every
    /// <c>rgb(from &lt;colour&gt; r g b / &lt;alpha&gt;)</c>, <c>rgb(r g b / a)</c> and
    /// <c>color-mix(in srgb, …)</c> it then holds written out as the <c>rgba(r, g, b, a)</c> the
    /// calculator reads.
    /// </summary>
    public static string ResolveValue(string value, Dictionary<string, string> declared, int depth)
    {
        Assert.True(depth < 16, $"var() cycle resolving {value}");

        // A var() the cascade here does not hold (the type ramp's --text-scale, declared with the
        // geometry) is left as written: it is not a colour, and nothing here measures it.
        value = Regex.Replace(value, @"var\((--[a-z0-9-]+)\)", m =>
            declared.TryGetValue(m.Groups[1].Value, out var inner) ? ResolveValue(inner, declared, depth + 1) : m.Value);

        // Relative colour keeps the channels and replaces the alpha, whatever alpha it was handed.
        value = Regex.Replace(value, @"rgb\(from\s+(#[0-9A-Fa-f]{6}|rgba?\([^()]*\))\s+r\s+g\s+b\s*/\s*([\d.]+)\)", m =>
        {
            var (r, g, b, _) = Wcag.Parse(m.Groups[1].Value);
            return $"rgba({r:0}, {g:0}, {b:0}, {m.Groups[2].Value})";
        });

        value = Regex.Replace(value, @"rgb\((\d+)\s+(\d+)\s+(\d+)\s*/\s*([\d.]+)\)", "rgba($1, $2, $3, $4)");

        // Innermost first, until none is left: a mix can hold a mix.
        while (MixPattern.IsMatch(value))
            value = MixPattern.Replace(value, m => Mix(m.Groups["one"].Value, m.Groups["p1"].Value, m.Groups["two"].Value, m.Groups["p2"].Value));

        return value;
    }

    private const string MixColour = @"#[0-9A-Fa-f]{6}|rgba?\([^()]*\)|transparent";
    private const string MixShare = @"[\d.]+%|calc\(\s*[\d.]+\s*\*\s*100%\s*\)";

    private static readonly Regex MixPattern = new(
        $@"color-mix\(\s*in srgb\s*,\s*(?<one>{MixColour})(?:\s+(?<p1>{MixShare}))?\s*,\s*(?<two>{MixColour})(?:\s+(?<p2>{MixShare}))?\s*\)");

    /// <summary>
    /// <c>color-mix(in srgb, one p1, two p2)</c> as CSS Color 5 computes it: an omitted share is what
    /// the other leaves of 100%, both omitted are half each; two shares summing under 100% scale the
    /// result's alpha by that sum and are then normalised. The mix is of premultiplied colours — each
    /// channel weighted by its own alpha and share — divided back by the mixed alpha, so a colour
    /// mixed with transparent keeps its channels and multiplies its alpha by its share: an opaque
    /// fill at 15% is that fill at .15, and a fill already at .16 comes out at .024.
    /// </summary>
    public static string Mix(string one, string p1, string two, string p2)
    {
        var a = Colour(one);
        var b = Colour(two);
        double? s1 = Share(p1), s2 = Share(p2);
        var scale = 1d;

        if (s1 is null && s2 is null)
            (s1, s2) = (.5, .5);
        else if (s2 is null)
            s2 = 1 - s1!.Value;
        else if (s1 is null)
            s1 = 1 - s2.Value;
        else
        {
            var sum = s1.Value + s2.Value;
            Assert.True(sum > 0, $"color-mix with shares summing to zero: {one} {p1}, {two} {p2}");
            if (sum < 1)
                scale = sum;
            (s1, s2) = (s1.Value / sum, s2.Value / sum);
        }

        var alpha = a.A * s1.Value + b.A * s2.Value;
        if (alpha == 0)
            return "rgba(0, 0, 0, 0)";

        double Channel(double x, double y) => Math.Round((x * a.A * s1.Value + y * b.A * s2.Value) / alpha);

        var mixed = (alpha * scale).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        return $"rgba({Channel(a.R, b.R):0}, {Channel(a.G, b.G):0}, {Channel(a.B, b.B):0}, {mixed})";

        static (double R, double G, double B, double A) Colour(string colour)
            => colour == "transparent" ? (0, 0, 0, 0) : Wcag.Parse(colour);

        // calc(x * 100%) is the share x itself, read as written rather than through a percentage
        // and back, so a share of .15 is .15 and not 15% / 100.
        static double? Share(string share)
        {
            if (share.Length == 0)
                return null;

            var calc = Regex.Match(share, @"^calc\(\s*([\d.]+)\s*\*\s*100%\s*\)$");
            var number = calc.Success ? calc.Groups[1].Value : share.TrimEnd('%');
            var parsed = double.Parse(number, System.Globalization.CultureInfo.InvariantCulture);

            return calc.Success ? parsed : parsed / 100;
        }
    }

    private static Dictionary<string, string> TopLevelRule(string selectorPattern)
    {
        var m = Regex.Match(TopLevel(Desk), @"(?m)^" + selectorPattern + @"\s*\{([^{}]*)\}");
        Assert.True(m.Success, $"desk.css has no top-level rule {selectorPattern}");
        return Declarations(m.Groups[1].Value);
    }

    /// <summary>A sheet with its <c>@media</c> and <c>@supports</c> groups taken out.</summary>
    public static string TopLevel(string css) => Regex.Replace(css, @"@(media|supports)[^{]*" + Balanced, string.Empty);

    /// <summary>The body of the first group opening with <paramref name="opener"/>, braces balanced.</summary>
    public static string MediaBody(string css, string opener)
    {
        var m = Regex.Match(css, opener + @"[^{]*(" + Balanced + ")");

        Assert.True(m.Success, $"no block opening {opener}");

        return m.Groups[1].Value[1..^1];
    }

    /// <summary>
    /// The body of the first rule whose selector ends in <paramref name="selectorPattern"/>, in a
    /// stripped sheet. A block that has lost its tokens fails here rather than quietly
    /// under-checking everywhere it is read.
    /// </summary>
    public static string Block(string css, string selectorPattern)
    {
        var match = Regex.Match(css, selectorPattern + @"\s*\{([^{}]*)\}");

        Assert.True(match.Success, $"no token block matched {selectorPattern}");
        Assert.Contains("--", match.Groups[1].Value, StringComparison.Ordinal);

        return match.Groups[1].Value;
    }

    /// <summary>The declarations of the first <c>[data-theme="<paramref name="theme"/>"]</c> rule in <paramref name="css"/>.</summary>
    public static Dictionary<string, string> BlockIn(string css, string theme)
        => Declarations(Block(css, @"\[data-theme=""" + Regex.Escape(theme) + @"""\]"));

    /// <summary>A value as one declaration writes it, failing with where it was looked for.</summary>
    public static string Require(Dictionary<string, string> tokens, string token, string where)
    {
        Assert.True(tokens.TryGetValue(token, out var value), $"{token} is not declared in {where}");
        return value!;
    }

    /// <summary>The value, or a failure recorded against <paramref name="where"/> and null, for suites that report every miss at once.</summary>
    public static string? Require(Dictionary<string, string> tokens, string token, string where, List<string> failures)
    {
        if (tokens.TryGetValue(token, out var value))
            return value;

        failures.Add($"{where} does not declare {token}");
        return null;
    }
}
