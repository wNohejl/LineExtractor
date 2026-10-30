using System.Globalization;
using System.Text.RegularExpressions;
using LineOps.Desk.Primitives;

namespace LineOps.Web.Tests;

/// <summary>
/// The sparkline is a shape and a sentence. The shape is held to the box it is drawn in —
/// its own lowest value on the bottom inset, its highest on the top, the dot on the last
/// point — and the sentence is the one a screen reader hears instead of it. A series too
/// short to be a trend draws a dash, not a line.
/// </summary>
public class DeskSparklineTests : DeskTestContext
{
    private IRenderedComponent<DeskSparkline> Render(
        IReadOnlyList<double> values, IReadOnlyList<double>? positions = null, bool step = false, int width = 96)
        => RenderComponent<DeskSparkline>(p => p
            .Add(x => x.Values, values)
            .Add(x => x.Positions, positions)
            .Add(x => x.Step, step)
            .Add(x => x.Width, width)
            .Add(x => x.Label, "Price -110 to -120, 2 moves"));

    /// <summary>
    /// Scaled to its own range inside a 3px inset: 1 is the bottom (17), 3 the top (3), and 2
    /// halfway, spaced evenly across 3 to 93 when no positions are given.
    /// </summary>
    [Fact]
    public void Draws_the_series_scaled_to_its_own_range()
    {
        var cut = Render([1, 3, 2]);

        Assert.Equal("3,17 48,3 93,10", cut.Find("polyline.sparkline__line").GetAttribute("points"));
    }

    [Fact]
    public void The_dot_sits_on_the_last_point()
    {
        var cut = Render([1, 3, 2]);
        var dot = cut.Find("circle.sparkline__dot");

        Assert.Equal("93", dot.GetAttribute("cx"));
        Assert.Equal("10", dot.GetAttribute("cy"));
    }

    /// <summary>A series written on change holds its value until the next one; a diagonal would invent prices.</summary>
    [Fact]
    public void Step_holds_each_value_until_the_next()
    {
        var cut = Render([1, 3], positions: [0, 1], step: true);

        Assert.Equal("3,17 93,17 93,3", cut.Find("polyline").GetAttribute("points"));
    }

    /// <summary>Positions are a shared time axis: a value at half the span is drawn at half the width.</summary>
    [Fact]
    public void Positions_place_each_value_along_the_width()
    {
        var cut = Render([1, 3, 3], positions: [0, 0.25, 1]);

        Assert.Equal("3,17 25.5,3 93,3", cut.Find("polyline").GetAttribute("points"));
    }

    [Fact]
    public void Positions_that_do_not_match_the_values_are_ignored()
    {
        var cut = Render([1, 3, 2], positions: [0, 1]);

        Assert.Equal("3,17 48,3 93,10", cut.Find("polyline").GetAttribute("points"));
    }

    /// <summary>No range to scale to, so the line runs level through the middle rather than along the top.</summary>
    [Fact]
    public void A_series_that_never_changed_is_level_through_the_middle()
    {
        var cut = Render([-110, -110]);

        Assert.Equal("3,10 93,10", cut.Find("polyline").GetAttribute("points"));
    }

    /// <summary>One reading is not a trend: no plot, a dash, and the same accessible name.</summary>
    [Fact]
    public void Fewer_than_two_values_draws_a_dash()
    {
        var cut = Render([-110]);

        Assert.Empty(cut.FindAll("svg"));

        var dash = cut.Find(".sparkline");
        Assert.Contains("sparkline--none", dash.ClassName);
        Assert.Equal("—", dash.TextContent.Trim());
        Assert.Equal("img", dash.GetAttribute("role"));
        Assert.Equal("Price -110 to -120, 2 moves", dash.GetAttribute("aria-label"));
    }

    /// <summary>
    /// One object with one name: the wrapper is the image and carries the sentence as its
    /// accessible name and its tooltip; the SVG inside is hidden so nobody hears its paths.
    /// </summary>
    [Fact]
    public void The_label_is_the_accessible_name_and_the_plot_is_hidden()
    {
        var cut = Render([1, 2]);
        var wrapper = cut.Find(".sparkline");

        Assert.Equal("img", wrapper.GetAttribute("role"));
        Assert.Equal("Price -110 to -120, 2 moves", wrapper.GetAttribute("aria-label"));
        Assert.Equal("Price -110 to -120, 2 moves", wrapper.GetAttribute("title"));
        Assert.Equal("true", cut.Find("svg").GetAttribute("aria-hidden"));
    }

    /// <summary>SVG reads a full stop as the decimal point whatever culture the desk runs under.</summary>
    [Fact]
    public void Points_are_written_in_the_invariant_culture()
    {
        var before = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var cut = Render([1, 3, 2], width: 97);

            Assert.Equal("3,17 48.5,3 94,10", cut.Find("polyline").GetAttribute("points"));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    /// <summary>
    /// No colour of its own: the stroke and the dot come from the stylesheet, on the text inks,
    /// so a rise is not drawn as good or a fall as bad, and the plot follows the desk's theme.
    /// </summary>
    [Fact]
    public void Ink_comes_from_the_text_tokens_and_never_a_state()
    {
        var cut = Render([1, 3, 2]);

        Assert.Null(cut.Find("polyline").GetAttribute("stroke"));
        Assert.Null(cut.Find("circle").GetAttribute("fill"));

        var css = Regex.Replace(
            File.ReadAllText(Path.Combine(RepoRoot(), "src/LineOps.Desk/wwwroot/css/desk.css")),
            @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        var line = Rule(css, ".sparkline__line");
        var dot = Rule(css, ".sparkline__dot");

        Assert.Contains("stroke: var(--text-secondary)", line, StringComparison.Ordinal);
        Assert.Contains("fill: var(--text-primary)", dot, StringComparison.Ordinal);
        Assert.DoesNotContain("--state-", line + dot, StringComparison.Ordinal);
    }

    private static string Rule(string css, string selector)
    {
        var match = Regex.Match(css, Regex.Escape(selector) + @"\s*\{([^}]*)\}");

        Assert.True(match.Success, $"no {selector} rule in desk.css");
        return match.Groups[1].Value;
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LineOps.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("LineOps.slnx not found above the test output.");
    }
}
