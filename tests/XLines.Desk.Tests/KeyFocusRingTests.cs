using System.Text.RegularExpressions;
using XLines.Desk.Tests.Support;

namespace XLines.Desk.Tests;

/// <summary>
/// The finish never costs the focus ring. The keys' hover and press rules weigh more than
/// <c>:focus-visible</c> alone, so a focused key that was pointed at, or held down with Space,
/// used to lose its ring; each such moment has its own rule carrying the ring with its finish,
/// later in the sheet and at least as heavy. And asked for more contrast, every family's keys go
/// flat, as High contrast's are.
/// </summary>
public class KeyFocusRingTests
{
    private static string Css(string file)
        => File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "css", file)).Replace("\r\n", "\n");

    private static void AssertAfter(string css, string earlier, string later)
    {
        var a = css.IndexOf(earlier, StringComparison.Ordinal);
        var b = css.IndexOf(later, StringComparison.Ordinal);
        Assert.True(a >= 0, $"not found: {earlier}");
        Assert.True(b >= 0, $"not found: {later}");
        Assert.True(b > a, $"{later} must come after {earlier}");
    }

    [Fact]
    public void A_focused_key_pointed_at_keeps_its_ring()
    {
        var css = Css("desk.css");

        Assert.Contains(".desk-btn:focus-visible:hover:not(:disabled) { box-shadow: var(--focus-ring); }", css);
        Assert.Matches(@"\.desk-btn--plain:focus-visible:hover:not\(:disabled\)\s*\{\s*box-shadow:[^;]*var\(--focus-ring\);", css);
        Assert.Matches(@"\.desk-btn--filled:focus-visible:hover:not\(:disabled\)\s*\{\s*box-shadow:\s*inset 0 1px 0 var\(--key-highlight\), var\(--focus-ring\);", css);

        // Each outlasts the rule it would otherwise lose to.
        AssertAfter(css, ".desk-btn--plain:hover:not(:disabled) {", ".desk-btn--plain:focus-visible:hover:not(:disabled)");
    }

    [Fact]
    public void A_key_held_from_the_keyboard_keeps_its_ring()
    {
        var css = Css("desk.css");
        Assert.Matches(@"\.desk-btn:focus-visible:active:not\(:disabled\),\s*\.btn:focus-visible:active:not\(:disabled\)\s*\{\s*box-shadow:\s*inset 0 1px 2px var\(--key-press\), var\(--focus-ring\);", css);
        AssertAfter(css, ".desk-btn:active:not(:disabled),", ".desk-btn:focus-visible:active:not(:disabled),");

        var controls = Css("controls.css");
        Assert.Contains(".popup__key:focus-visible:active:not(:disabled) { box-shadow: inset 0 1px 2px var(--key-press), var(--focus-ring); }", controls);
        AssertAfter(controls, ".popup__key:active:not(:disabled) {", ".popup__key:focus-visible:active:not(:disabled)");
    }

    [Fact]
    public void More_contrast_flattens_every_familys_keys()
    {
        var css = Css("desk.css");
        var block = Regex.Match(css, @"@media \(prefers-contrast: more\) \{\s*:root,\s*\[data-theme\] \{(?<b>[^}]*)\}").Groups["b"].Value;

        Assert.Contains("--key-highlight-alpha: 0;", block);
        Assert.Contains("--key-gloss-alpha: 0;", block);

        // After the appearance rules that set the alphas, at the same weight, so it wins.
        AssertAfter(css, "--key-gloss: rgb(255 255 255 / var(--key-gloss-alpha));", "@media (prefers-contrast: more) {\n    :root,\n    [data-theme] {");
    }
}
