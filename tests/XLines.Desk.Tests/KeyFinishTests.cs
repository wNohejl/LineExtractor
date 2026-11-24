using System.Text.RegularExpressions;
using XLines.Desk.Tests.Support;

namespace XLines.Desk.Tests;

/// <summary>
/// The keys' finish (desk.css, "Finish", 21 November 2026): a top line and a sheen faded out above
/// the label on Filled, Tinted, a pointed-at Plain key and the gray pop-up key; an inner shade
/// while held; nothing on a disabled key, under forced colours or in the High contrast family.
/// The shade is declared by every block and the line and sheen are derived once (DeskThemeTests
/// holds both); these hold what the recipe does with them.
/// </summary>
public class KeyFinishTests
{
    private static string Css(string file)
        => File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "css", file)).Replace("\r\n", "\n");

    [Fact]
    public void Filled_and_Tinted_wear_the_sheen_and_the_top_line()
    {
        var css = Css("desk.css");

        Assert.Contains(".desk-btn--filled,\n.desk-btn--tinted {\n    background-image: linear-gradient(to bottom, var(--key-gloss), transparent 38%);", css);
        Assert.Contains(".desk-btn--filled { box-shadow: inset 0 1px 0 var(--key-highlight), var(--shadow-1); }", css);
        Assert.Contains(".desk-btn--tinted { box-shadow: inset 0 1px 0 color-mix(in srgb, var(--key-highlight) 60%, transparent); }", css);
        Assert.Contains(".desk-btn--filled:focus-visible { box-shadow: inset 0 1px 0 var(--key-highlight), var(--focus-ring); }", css);
    }

    [Fact]
    public void A_held_key_loses_the_sheen_and_takes_the_inner_shade()
    {
        var css = Css("desk.css");
        Assert.Matches(@"\.desk-btn:active:not\(:disabled\),\s*\.btn:active:not\(:disabled\)\s*\{\s*background-image:\s*none;\s*box-shadow:\s*inset 0 1px 2px var\(--key-press\);", css);
        Assert.Matches(@"\.desk-btn:disabled, \.desk-btn\[disabled\]\s*\{\s*background-image:\s*none;\s*box-shadow:\s*none;", css);

        var controls = Css("controls.css");
        Assert.Matches(@"\.popup__key:not\(:disabled\)\s*\{\s*background-image:\s*linear-gradient\(to bottom, var\(--key-gloss\), transparent 38%\)", controls);
        Assert.Matches(@"\.popup__key:active:not\(:disabled\)\s*\{[^}]*background-image:\s*none;[^}]*var\(--key-press\)", controls);
    }

    [Fact]
    public void Plain_keys_are_still_nothing_at_rest()
    {
        var css = Css("desk.css");
        var rest = Regex.Match(css, @"(?m)^\.desk-btn--plain \{[^}]*\}").Value;
        Assert.DoesNotContain("box-shadow", rest);
        Assert.DoesNotContain("background-image", rest);
        Assert.Contains(".desk-btn--plain:hover:not(:disabled) {\n    box-shadow: inset 0 1px 0 color-mix(in srgb, var(--key-highlight) 50%, transparent);", css);
    }

    /// <summary>
    /// High contrast's keys are flat: its blocks set the line's and the sheen's alpha to 0, so the
    /// derived tokens resolve to nothing however the rule computes them.
    /// </summary>
    [Fact]
    public void High_contrast_and_forced_colours_draw_no_finish()
    {
        var resolved = DeskCss.ResolvedThemeBlocks().ToDictionary(b => b.Name, b => b.Tokens);

        foreach (var block in new[] { "contrast-dark", "contrast-light" })
        {
            Assert.Equal("0", DeskCss.BlockIn(DeskCss.Themes, block)["--key-highlight-alpha"]);
            Assert.Equal("0", DeskCss.BlockIn(DeskCss.Themes, block)["--key-gloss-alpha"]);
            Assert.Matches(@"^rgba\([^)]*,\s*0\)$", resolved[block]["--key-highlight"]);
            Assert.Matches(@"^rgba\([^)]*,\s*0\)$", resolved[block]["--key-gloss"]);
        }

        Assert.Contains(":root :is(.desk-btn, .popup__key, .wtabs__key, .wtabs__tab) { background-image: none; }", Css("desk.css"));
    }

    /// <summary>
    /// The shade a held key takes is the family's own shadow ink, which no other token carries, so
    /// every family block declares it once. The line and the sheen are derived — the family's sheen
    /// by night, white by day — and declared only in desk.css's two appearance rules.
    /// </summary>
    [Fact]
    public void Every_family_declares_the_shade_once_and_derives_the_line_and_sheen()
    {
        var families = DeskCss.ThemeBlocks().ToList();
        Assert.Equal(XLines.Desk.Theming.DeskThemeFamilies.All.Count * 2, families.Count);

        foreach (var (name, tokens) in families)
        {
            Assert.True(tokens.ContainsKey("--key-press"), $"{name} declares no --key-press");
            Assert.False(tokens.ContainsKey("--key-highlight") || tokens.ContainsKey("--key-gloss"), $"{name} writes out a derived key token");
        }

        foreach (var block in DeskCss.Themes.Split("\n}"))
            Assert.True(Regex.Matches(block, @"--key-press:").Count <= 1, "a block declares --key-press twice");

        Assert.Equal("rgb(from var(--sheen) r g b / var(--key-highlight-alpha))", DeskCss.DarkDefaults["--key-highlight"]);
        Assert.Equal("rgb(from var(--sheen) r g b / var(--key-gloss-alpha))", DeskCss.DarkDefaults["--key-gloss"]);
        Assert.Equal("rgb(255 255 255 / var(--key-highlight-alpha))", DeskCss.LightDefaults["--key-highlight"]);
        Assert.Equal("rgb(255 255 255 / var(--key-gloss-alpha))", DeskCss.LightDefaults["--key-gloss"]);
    }
}
