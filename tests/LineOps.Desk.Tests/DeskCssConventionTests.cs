using System.Text.RegularExpressions;
using LineOps.Desk.Tests.Support;

namespace LineOps.Desk.Tests;

/// <summary>
/// The Desk stylesheet's house rules, read from the source: a colour is a token, a form has one
/// edge, the toolbar folds by one rule, and a group carries its hairline where it is defined.
/// Each of these drifted once — a literal scrim, a boxed form around a bordered group, a second
/// container query that lost the cascade, a border bolted on five hundred lines later. The same
/// rules for an application's sheet are <see cref="DeskConventions"/>' to check; the app runs them.
/// </summary>
public class DeskCssConventionTests
{
    private static string Css(string name) =>
        File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "wwwroot", "css", name));

    private static string WithoutComments(string css) => DeskConventions.WithoutComments(css);

    private const string LiteralColour = @"#[0-9a-fA-F]{3,8}\b|rgba?\(";

    [Fact]
    public void No_literal_colour_outside_the_token_blocks()
    {
        // The token blocks end at the Scale banner that follows the last theme block. The word
        // also appears in a comment inside the type tokens, so the anchor is the banner itself.
        var css = Css("desk.css");
        var scale = Regex.Match(css, @"/\*\s*=+\s*Scale\.");
        Assert.True(scale.Success, "The Scale banner after the theme blocks was not found.");
        Assert.DoesNotMatch(LiteralColour, WithoutComments(css[scale.Index..]));

        // controls.css has no token block either: a component spends the tokens desk.css declares.
        Assert.DoesNotMatch(LiteralColour, WithoutComments(Css("controls.css")));

        var reconnect = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "Layout", "ReconnectModal.razor.css"));
        Assert.DoesNotMatch(LiteralColour + @"|\bwhite\b", WithoutComments(reconnect));
    }

    /// <summary>
    /// Every duration and delay a transition or an animation spends is a <c>--dur-*</c> token (or
    /// a multiple of one), so motion keeps one tempo. Literal durations drifted under the 150ms
    /// floor — .1s, .12s, .16s — each one a hand-tuned exception nobody could see beside the rest.
    /// </summary>
    [Fact]
    public void No_literal_duration_outside_the_token_blocks()
    {
        // The one allowed literal: the reduced-motion block's near-zero, which is not a tempo but
        // the absence of one, and must out-weigh any component (hence its !important).
        string[] allowed = [".01ms !important"];

        var desk = Css("desk.css");
        var scale = Regex.Match(desk, @"/\*\s*=+\s*Scale\.");
        Assert.True(scale.Success, "The Scale banner after the theme blocks was not found.");

        var sheets = new Dictionary<string, string>
        {
            ["desk.css"] = desk[scale.Index..],
            ["controls.css"] = Css("controls.css"),
            ["mud-bridge.css"] = Css("mud-bridge.css"),
            ["ReconnectModal.razor.css"] = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "Layout", "ReconnectModal.razor.css")),
        };

        var literals = new List<string>();
        foreach (var (name, css) in sheets)
        {
            foreach (Match declaration in Regex.Matches(WithoutComments(css), @"(?<![\w-])(?:transition|animation)[\w-]*\s*:\s*([^;{}]*)"))
            {
                var value = declaration.Groups[1].Value.Trim();
                if (allowed.Contains(value))
                    continue;
                if (Regex.IsMatch(value, @"(?<![\w.-])\d*\.?\d+m?s\b"))
                    literals.Add($"{name}: {declaration.Value.Trim()}");
            }
        }

        Assert.True(literals.Count == 0, "Literal durations:\n" + string.Join("\n", literals));
    }

    [Fact]
    public void A_form_is_not_boxed()
    {
        var pform = Regex.Match(Css("desk.css"), @"\.pform\s*\{[^}]*\}").Value;
        Assert.NotEmpty(pform);
        Assert.DoesNotContain("border:", pform);
        Assert.DoesNotContain("background:", pform);
    }

    [Fact]
    public void The_toolbar_folds_by_one_rule()
    {
        Assert.DoesNotContain("@container ptoolbar (max-width: 520px)", Css("desk.css"));
        Assert.DoesNotContain("@container ptoolbar (max-width: 520px)", Css("controls.css"));
    }

    [Fact]
    public void A_group_carries_its_hairline_where_it_is_defined()
    {
        var css = Css("desk.css");
        var group = Regex.Match(css, @"\.wgroup\s*\{[^}]*\}").Value; // the first .wgroup block
        Assert.Contains("border: 1px solid var(--separator-strong)", group);
        Assert.Single(Regex.Matches(css, @"^\.wgroup\s*\{", RegexOptions.Multiline));
    }

    /// <summary>The body of every <c>@media</c> block whose condition matches, braces balanced.</summary>
    private static List<string> MediaBodies(string css, string condition)
    {
        var bodies = new List<string>();

        foreach (Match opener in Regex.Matches(css, @"@media\s*\(" + condition + @"\)\s*\{"))
        {
            var depth = 1;
            var i = opener.Index + opener.Length;
            for (; i < css.Length && depth > 0; i++)
                depth += css[i] == '{' ? 1 : css[i] == '}' ? -1 : 0;

            bodies.Add(css[(opener.Index + opener.Length)..(i - 1)]);
        }

        return bodies;
    }

    /// <summary>
    /// A Windows contrast theme drops box-shadow and author colours, which is where the desk keeps
    /// its focus ring and its selection. One block answers it, in desk.css: the ring comes back
    /// as an outline, selection as the Highlight pair, and only the swatches — whose colour is
    /// the information — opt out of forcing. System colour keywords are not literals, so the
    /// literal-colour rule above has nothing to object to; this holds the block to its shape.
    /// </summary>
    [Fact]
    public void One_forced_colours_block_restores_focus_and_selection()
    {
        var desk = WithoutComments(Css("desk.css"));
        var blocks = MediaBodies(desk, @"forced-colors:\s*active");

        var block = Assert.Single(blocks);
        foreach (var other in new[] { "controls.css", "mud-bridge.css", "themes.css" })
            Assert.Empty(MediaBodies(WithoutComments(Css(other)), @"forced-colors:\s*active"));

        Assert.Matches(@":focus-visible[^{]*\{\s*outline:\s*2px solid Highlight;\s*outline-offset:\s*2px;", block);
        Assert.Contains("background-color: Highlight;", block);
        Assert.Contains("color: HighlightText;", block);
        Assert.Contains("GrayText", block);
        Assert.Contains("LinkText", block);
        Assert.Contains("--material-blur: none", block);

        // Restored by weight, never by !important, and keyed on no theme: a contrast theme is the
        // machine's palette, whichever family the desk was wearing.
        Assert.DoesNotContain("!important", block);
        Assert.DoesNotContain("[data-theme", block);
    }

    /// <summary>
    /// The sheets' comments ship with the package, so their examples are generic: a truncated
    /// name is "Northwind", not a sportsbook's, and a moving figure is not a price (review,
    /// 2026-11-21). DeskVocabularyTests holds the domain's own words; this holds the names and
    /// nouns that list does not reach.
    /// </summary>
    [Fact]
    public void No_sheet_comment_names_a_sportsbook_or_a_price()
    {
        foreach (var name in DeskConventions.DeskSheetNames)
        {
            foreach (Match comment in Regex.Matches(Css(name), @"/\*.*?\*/", RegexOptions.Singleline))
                Assert.DoesNotMatch(@"(?i)betrivers|draftkings|fanduel|betmgm|caesars|pinnacle|sportsbook|\bprices?\b", comment.Value);
        }
    }

    /// <summary>
    /// A switch's native input is invisible, so greying the input greys nothing anyone sees: under
    /// a contrast theme a disabled switch's drawn parts — track edge, thumb, words — take GrayText
    /// themselves, and a disabled switch that is on gives up the Highlight pair. At rest the track
    /// is ButtonFace, which the thumb's ButtonText is drawn against (review, 2026-11-21).
    /// </summary>
    [Fact]
    public void A_disabled_switch_is_GrayText_under_forced_colours()
    {
        var block = Assert.Single(MediaBodies(WithoutComments(Css("desk.css")), @"forced-colors:\s*active"));

        Assert.Matches(@":root \.toggle__track \{\s*background-color:\s*ButtonFace;", block);
        Assert.Matches(@":root \.toggle__thumb \{\s*background-color:\s*ButtonText;", block);
        Assert.Matches(@":root \.toggle__input:disabled \+ \.toggle__track \{\s*outline-color:\s*GrayText;", block);
        Assert.Matches(@":root \.toggle__input:disabled \+ \.toggle__track \.toggle__thumb \{\s*background-color:\s*GrayText;", block);
        Assert.Matches(@":root \.toggle__input:disabled ~ \.toggle__label \{\s*color:\s*GrayText;", block);
        Assert.Matches(@":root \.toggle__input:disabled:checked \+ \.toggle__track \{\s*background-color:\s*GrayText;", block);

        // The disabled rules come after the checked ones they answer, at the same weight.
        Assert.True(
            block.IndexOf(":root .toggle__input:disabled + .toggle__track .toggle__thumb", StringComparison.Ordinal)
                > block.IndexOf(":root .toggle__input:checked + .toggle__track .toggle__thumb", StringComparison.Ordinal),
            "the disabled thumb rule must follow the checked one");
    }

    /// <summary>
    /// Opting out of forced colours is for the two pickers whose colour is the choice, and for
    /// nothing else in any of the Desk's sheets.
    /// </summary>
    [Fact]
    public void Only_the_swatches_keep_their_colours_under_forced_colours()
    {
        foreach (var name in DeskConventions.DeskSheetNames)
        {
            foreach (Match rule in Regex.Matches(WithoutComments(Css(name)), @"([^{}]+)\{[^{}]*forced-color-adjust:\s*none"))
            {
                var selectors = rule.Groups[1].Value.Split(',').Select(s => s.Trim()).ToArray();
                Assert.All(selectors, s => Assert.Matches(@"^:root \.(swatch|accent-swatch)$", s));
            }
        }
    }

    /// <summary>
    /// The theme switch's cross-fade runs on the view-transition pseudo-elements, which the
    /// reduced-motion block's <c>*</c> does not match — so that block names them, and the switch is
    /// a cut for an operator who asked for no motion even if a script forgot to ask.
    /// </summary>
    [Fact]
    public void The_reduced_motion_block_stills_the_view_transition()
    {
        var important = MediaBodies(WithoutComments(Css("desk.css")), @"prefers-reduced-motion:\s*reduce")
            .Single(b => b.Contains("!important", StringComparison.Ordinal));

        foreach (var part in new[] { "group", "old", "new" })
            Assert.Contains($"::view-transition-{part}(*)", important);
        Assert.Matches(@"::view-transition-new\(\*\)\s*\{\s*animation:\s*none !important;", important);
    }

    /// <summary>
    /// The cross-fade is the root's, on the desk's own tempo, and nothing is lifted out of it to
    /// travel on its own: no element on the desk carries a view-transition-name.
    /// </summary>
    [Fact]
    public void The_theme_switch_cross_fades_on_the_desk_s_tempo()
    {
        var desk = WithoutComments(Css("desk.css"));

        Assert.Matches(@"::view-transition-old\(root\),\s*::view-transition-new\(root\)\s*\{\s*animation-duration:\s*var\(--dur-base\);\s*animation-timing-function:\s*var\(--ease-standard\);", desk);
        foreach (var name in DeskConventions.DeskSheetNames)
            Assert.DoesNotContain("view-transition-name", WithoutComments(Css(name)));
    }

    [Fact]
    public void Selected_text_and_the_caret_spend_the_accent()
    {
        var desk = WithoutComments(Css("desk.css"));

        Assert.Matches(@"::selection\s*\{\s*background:\s*var\(--accent-wash-strong\);\s*color:\s*var\(--text-primary\);", desk);
        Assert.Matches(@"caret-color:\s*var\(--accent-ink\)", desk);
    }

    /// <summary>
    /// The caret and an open key's ring are strokes, so they are drawn in the accent's ink, as the
    /// focus ring is: --accent is a fill, tuned for the label on it, and on the pale families it is
    /// too faint to make a line (review, 2026-11-21). No sheet sets the caret in the bare fill,
    /// and every open-key ring — the popup key and the window tab's — spends the ink.
    /// </summary>
    [Fact]
    public void The_caret_and_an_open_key_s_ring_spend_the_accent_ink()
    {
        foreach (var name in DeskConventions.DeskSheetNames)
            Assert.DoesNotMatch(@"caret-color:\s*var\(--accent\)", WithoutComments(Css(name)));

        Assert.Matches(@"\.popup__key\[aria-expanded=""true""\]\s*\{\s*box-shadow:\s*inset 0 0 0 1\.5px var\(--accent-ink\);", WithoutComments(Css("controls.css")));
        Assert.Matches(@"\.wtabs__key\[aria-expanded=""true""\]\s*\{[^}]*box-shadow:\s*inset 0 0 0 1\.5px var\(--accent-ink\);", WithoutComments(Css("desk.css")));

        foreach (var name in DeskConventions.DeskSheetNames)
            Assert.DoesNotMatch(@"\[aria-expanded=""true""\]\s*\{[^}]*inset[^;}]*var\(--accent\)", WithoutComments(Css(name)));
    }

    /// <summary>
    /// The package carries the Desk's sheets inside it for the ownership check, and an application
    /// trusts that check only if what it carries is what the Desk ships.
    /// </summary>
    [Fact]
    public void The_testing_package_carries_the_desk_s_sheets_as_they_are()
    {
        var shipped = Directory.EnumerateFiles(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "wwwroot", "css"), "*.css")
            .Select(Path.GetFileName)
            .Order()
            .ToArray();

        Assert.Equal(shipped, DeskConventions.DeskSheetNames.Order().ToArray());
        foreach (var name in DeskConventions.DeskSheetNames)
            Assert.Equal(Css(name), DeskConventions.DeskSheet(name));
    }
}
