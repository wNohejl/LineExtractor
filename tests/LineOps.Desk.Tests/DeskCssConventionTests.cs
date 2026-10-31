using System.Text.RegularExpressions;
using LineOps.Desk.Tests.Support;

namespace LineOps.Desk.Tests;

/// <summary>
/// The Desk stylesheet's house rules, read from the source: a colour is a token, a form has one
/// edge, the toolbar folds by one rule, and a group carries its hairline where it is defined.
/// Each of these drifted once — a literal scrim, a boxed form around a bordered group, a second
/// container query that lost the cascade, a border bolted on five hundred lines later.
/// </summary>
public class DeskCssConventionTests
{
    private static string Css(string name) =>
        File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "wwwroot", "css", name));

    private static string AppCss() =>
        File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "wwwroot", "css", "lineops.css"));

    // A comment may name the colour it describes; only a rule can paint one.
    private static string WithoutComments(string css) => Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

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

        // This app's sheet declares no token block at all: every colour in it is the Desk's.
        Assert.DoesNotMatch(LiteralColour, WithoutComments(AppCss()));

        // controls.css has no token block either: a component spends the tokens desk.css declares.
        Assert.DoesNotMatch(LiteralColour, WithoutComments(Css("controls.css")));

        var reconnect = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "Layout", "ReconnectModal.razor.css"));
        Assert.DoesNotMatch(LiteralColour + @"|\bwhite\b", WithoutComments(reconnect));
    }

    // Rules that draw a target smaller than the floor on purpose, by selector exactly as the
    // sheet writes it, each with its reason. Empty: nothing on the desk is small on purpose yet.
    private static readonly Dictionary<string, string> SmallOnPurpose = new();

    [Fact]
    public void Targets_are_at_least_28px_in_the_app_sheet()
    {
        // A trigger, a segment or a takeable cell is something the pointer has to land on: 28px
        // standing alone, 24px for a segment inside a gate, so nothing under 24px at all. Only the
        // target itself is read, the last compound of each selector; an icon or a capsule inside
        // one (".book-picker__trigger svg") is decoration, not the target.
        var small = new List<string>();
        foreach (Match rule in Regex.Matches(WithoutComments(AppCss()), @"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}"))
        {
            var selector = rule.Groups["sel"].Value.Trim();
            if (SmallOnPurpose.ContainsKey(selector)) continue;
            var targets = selector.Split(',')
                .Select(part => Regex.Split(part.Trim(), @"\s*[\s>+~]\s*").Last())
                .Any(last => Regex.IsMatch(last, @"__trigger|__opt|--take"));
            if (!targets) continue;
            foreach (Match height in Regex.Matches(rule.Groups["body"].Value, @"(?<![-\w])(?:min-)?height:\s*(?<px>\d+(?:\.\d+)?)px"))
            {
                if (double.Parse(height.Groups["px"].Value, System.Globalization.CultureInfo.InvariantCulture) < 24)
                    small.Add($"{selector} {{ {height.Value} }}");
            }
        }
        Assert.Empty(small);
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
        Assert.DoesNotContain("@container ptoolbar (max-width: 520px)", AppCss());
    }

    [Fact]
    public void A_group_carries_its_hairline_where_it_is_defined()
    {
        var css = Css("desk.css");
        var group = Regex.Match(css, @"\.wgroup\s*\{[^}]*\}").Value; // the first .wgroup block
        Assert.Contains("border: 1px solid var(--separator-strong)", group);
        Assert.Single(Regex.Matches(css, @"^\.wgroup\s*\{", RegexOptions.Multiline));
    }
}
