using System.Text.RegularExpressions;
using XLines.Desk.Tests.Support;
using static XLines.Desk.Tests.Support.DeskCss;
using static XLines.Desk.Tests.Support.Wcag;

namespace XLines.Desk.Tests;

/// <summary>
/// Controls whose essential graphic is not text, held to WCAG 1.4.11's 3:1 on every desk
/// (contrast audit, 20 November, recommendations 1 and 2).
///
/// <para>
/// A switch has one state signal, where its knob stands, so the knob has to be seen against the
/// track it stands on, off and on. A focus ring's 1px stroke is what says where the keyboard is,
/// on the panel and on the raised tier alike. Both are measured from the stylesheets: the tokens
/// the rules spend are read out of controls.css and desk.css, so a rule that moves to another
/// token is measured as it now draws, not as this file remembers it.
/// </para>
///
/// <para>
/// The blocks are found by <see cref="DeskCss"/> and measured by <see cref="Wcag"/>, as every
/// theming test's are.
/// </para>
/// </summary>
public class ControlContrastTests
{

    // ---- The switch ----------------------------------------------------------------------

    /// <summary>What the rules draw: the knob's fill, its rim off and on, and the two tracks.</summary>
    private static (string Fill, string RimOff, string RimOn, string TrackOff, string TrackOn) Toggle()
    {
        var thumb = Rule(@"\.toggle__thumb");
        var thumbOn = Rule(@"\.toggle__input:checked \+ \.toggle__track \.toggle__thumb");
        var track = Rule(@"\.toggle__track");
        var trackOn = Rule(@"\.toggle__input:checked \+ \.toggle__track");

        return (
            Token(thumb, @"background\s*:\s*var\((--[a-z0-9-]+)\)"),
            Token(thumb, @"inset 0 0 0 1px var\((--[a-z0-9-]+)\)"),
            Token(thumbOn, @"inset 0 0 0 1px var\((--[a-z0-9-]+)\)"),
            Token(track, @"background\s*:\s*var\((--[a-z0-9-]+)\)"),
            Token(trackOn, @"background\s*:\s*var\((--[a-z0-9-]+)\)"));
    }

    [Fact]
    public void The_knob_is_its_own_token_and_never_the_ink_for_a_fill()
    {
        // --on-accent is dark on every pale-accent dark desk; as the knob's fill it was a dark
        // disc on a dark track.
        Assert.Equal("--knob", Toggle().Fill);
    }

    /// <summary>
    /// The knob against the resting track and against the accent, in every block. The knob is
    /// read by whichever of its two edges meets the track at the higher contrast — the fill, or
    /// the 1px rim drawn inside it (translucent rims composited over the fill first) — because
    /// either one draws the disc's outline.
    /// </summary>
    [Fact]
    public void The_knob_reads_against_both_tracks_on_every_desk()
    {
        var (fill, rimOff, rimOn, trackOff, trackOn) = Toggle();
        var blocks = ResolvedThemeBlocks().ToList();
        var failures = new List<string>();

        Assert.Equal(XLines.Desk.Theming.DeskThemeFamilies.All.Count * 2, blocks.Count);

        foreach (var (name, t) in blocks)
        {
            Check(name, "off", t[fill], t[rimOff], t[trackOff]);
            Check(name, "on", t[fill], t[rimOn], t[trackOn]);
        }

        // Every family's chosen accents bring their own track; the knob and rim are that family's block's.
        var families = blocks.ToDictionary(b => b.Name, b => b.Tokens);
        foreach (var (theme, accent, tokens) in ResolvedAccentBlocks())
        {
            var t = families[theme];
            Check($"{theme} {accent}", "on", t[fill], tokens.GetValueOrDefault(rimOn, t[rimOn]), tokens.GetValueOrDefault(trackOn, t[trackOn]));
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

        void Check(string where, string state, string knob, string rim, string track)
        {
            var byFill = Contrast(knob, track);
            var byRim = Contrast(Composite(rim, knob), track);
            if (Math.Max(byFill, byRim) < 3)
                failures.Add($"{where} {state}: knob {knob} measures {byFill:F2}:1 and its rim {byRim:F2}:1 against the track {track}");
        }
    }

    // ---- The focus ring ------------------------------------------------------------------

    /// <summary>The colour of the ring's 1px stroke, as the root block writes it.</summary>
    private static string Stroke(string ring)
    {
        var declared = Regex.Match(DeskCss.Desk, Regex.Escape(ring) + @"\s*:\s*([^;]+);");
        Assert.True(declared.Success, $"{ring} is not declared");

        var stroke = Regex.Match(declared.Groups[1].Value, @"0 0 0 1px var\((--[a-z0-9-]+)\)");
        Assert.True(stroke.Success, $"{ring} has no 1px stroke: {declared.Groups[1].Value}");

        return stroke.Groups[1].Value;
    }

    [Theory]
    [InlineData("--focus-ring")]
    [InlineData("--focus-ring-inset")]
    public void The_focus_stroke_is_the_accent_ink(string ring)
        => Assert.Equal("--accent-ink", Stroke(ring));

    /// <summary>
    /// The stroke over the panel and over the raised tier — title bars, tiles and resting controls
    /// are --surface-2 — in every block and every chosen accent. The accent fill measured 2.63:1
    /// (Solarized dark) and 2.83:1 (Standard dark) there.
    /// </summary>
    [Fact]
    public void The_focus_stroke_reads_on_the_panel_and_the_raised_tier_on_every_desk()
    {
        var token = Stroke("--focus-ring");
        var blocks = ResolvedThemeBlocks().ToList();
        var failures = new List<string>();

        foreach (var (name, t) in blocks)
            Check(name, t[token], t);

        var standard = blocks.ToDictionary(b => b.Name, b => b.Tokens);
        foreach (var (theme, accent, tokens) in ResolvedAccentBlocks())
            Check($"{theme} {accent}", tokens.GetValueOrDefault(token, standard[theme][token]), standard[theme]);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

        void Check(string where, string stroke, Dictionary<string, string> t)
        {
            foreach (var surface in new[] { "--surface-1", "--surface-2" })
            {
                var ratio = Contrast(stroke, t[surface]);
                if (ratio < 3)
                    failures.Add($"{where} focus stroke {stroke} measures {ratio:F2}:1 over {surface} {t[surface]}");
            }
        }
    }

    // ---- Parsing -------------------------------------------------------------------------

    /// <summary>
    /// The rule whose whole selector is <paramref name="selector"/>: written at the start of a
    /// line, not indented into an @media, not the tail of a compound (".toggle__input:checked +
    /// .toggle__track {" is not ".toggle__track {"), and not the last member of a list. The first
    /// match used to be taken wherever it stood, so a compound rule written above the bare one
    /// would silently have been measured in its place.
    /// </summary>
    private static string Rule(string selector) => Rule(DeskCss.Controls, selector);

    private static string Rule(string css, string selector)
    {
        var m = Regex.Match(css, @"(?<!,\s*)^" + selector + @"\s*\{([^{}]*)\}", RegexOptions.Multiline);
        Assert.True(m.Success, $"controls.css has no rule for {selector}");
        return m.Groups[1].Value;
    }

    /// <summary>
    /// A disabled switch dims its track and its words, never its input: the input is the invisible
    /// sheet over the row, at opacity 0, and an opacity of its own would raise the browser's tick
    /// box into view across the row. It takes only the cursor.
    /// </summary>
    [Fact]
    public void A_disabled_switch_dims_what_is_drawn_and_not_the_invisible_input()
    {
        Assert.Matches(@"opacity:\s*0;", Rule(@"\.toggle__input"));

        var input = Rule(@"\.toggle__input:disabled");
        Assert.Contains("cursor: not-allowed", input);
        Assert.DoesNotContain("opacity", input);

        Assert.Matches(
            @"(?m)^\.toggle__input:disabled \+ \.toggle__track,\s*\.toggle__input:disabled ~ \.toggle__label\s*\{[^}]*opacity:\s*\.40;",
            DeskCss.Controls);
    }

    [Fact]
    public void A_rule_is_found_by_its_whole_selector_whatever_stands_above_it()
    {
        const string css = """
            .toggle__input:checked + .toggle__track { background: var(--accent); }
            .toggle__label,
            .toggle__track { opacity: .4; }
            @media (prefers-reduced-motion: reduce) {
                .toggle__track { transition: none; }
            }
            .toggle__track { background: var(--surface-3); }
            """;

        Assert.Equal(" background: var(--surface-3); ", Rule(css, @"\.toggle__track"));
        Assert.Equal(" background: var(--accent); ", Rule(css, @"\.toggle__input:checked \+ \.toggle__track"));
    }

    private static string Token(string body, string pattern)
    {
        var m = Regex.Match(body, pattern);
        Assert.True(m.Success, $"no match for {pattern} in: {body.Trim()}");
        return m.Groups[1].Value;
    }
}
