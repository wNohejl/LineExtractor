using XLines.Desk.Primitives;
using MudBlazor;

namespace XLines.Desk.Tests;

/// <summary>
/// A chart's accessible name is the one sentence somebody who cannot see the lines gets
/// instead of them. Apple's Charts page asks for an overview with axes and bounds, not a
/// sentence of mechanism, so the description is written from the data.
/// </summary>
public class DeskChartTests : DeskTestContext
{
    /// <summary>
    /// What is plotted, how many series over how many points, the span of the axis, and
    /// each series by name with the range it covers. The names are the legend's, so on the
    /// odds chart the books are spoken as well as coloured.
    /// </summary>
    [Fact]
    public void Describe_names_range_and_series()
    {
        var description = DeskChart.Describe(
            "Handicap over time",
            ["Oct 20", "Oct 21", "Oct 22"],
            [
                new ChartSeries<double> { Name = "Steelers · DK", Data = new double[] { -1.5, -2, -2.5 } },
                new ChartSeries<double> { Name = "Steelers · FD", Data = new double[] { -1.5, -1.5, -1.5 } }
            ]);

        Assert.Equal(
            "Handicap over time. 2 series over 3 points from Oct 20 to Oct 22; "
            + "Steelers · DK: -2.5–-1.5; Steelers · FD: -1.5.",
            description);
    }

    /// <summary>
    /// A dense time series labels every nth tick and leaves the rest blank, so the last
    /// label is often empty. The bound is the last label that says something — "to" followed
    /// by nothing is not a bound.
    /// </summary>
    [Fact]
    public void Describe_reads_the_span_from_labels_that_say_something()
    {
        var description = DeskChart.Describe(
            "Price over time",
            ["20 09:00", "", "20 15:00", ""],
            [new ChartSeries<double> { Name = "Home · Pinnacle", Data = new double[] { 52, 53, 54, 55 } }]);

        Assert.StartsWith("Price over time. 1 series over 4 points from 20 09:00 to 20 15:00;", description);
    }

    /// <summary>The convention reaches the plot itself, as its accessible name.</summary>
    [Fact]
    public void The_description_is_the_plots_accessible_name()
    {
        string[] labels = ["a", "b"];
        List<ChartSeries<double>> series = [new() { Name = "Bankroll", Data = new double[] { 100, 120 } }];

        var cut = RenderComponent<DeskChart>(p => p
            .Add(x => x.Series, series)
            .Add(x => x.Labels, labels)
            .Add(x => x.Description, DeskChart.Describe("Bankroll over settled bets", labels, series)));

        Assert.Equal(
            "Bankroll over settled bets. 1 series over 2 points from a to b; Bankroll: 100–120.",
            cut.Find(".deskchart__plot").GetAttribute("aria-label"));
    }

    /// <summary>
    /// An svg's height attribute takes a length. "auto" (a plot sized from its width by the
    /// host's sheet) was written there as is, and the browser logged an error on every render.
    /// </summary>
    [Fact]
    public void An_auto_height_is_left_to_the_sheet_not_written_on_the_svg()
    {
        string[] labels = ["a", "b"];
        List<ChartSeries<double>> series = [new() { Name = "Total", Data = new double[] { 100, 120 } }];

        var cut = RenderComponent<DeskChart>(p => p
            .Add(x => x.Series, series)
            .Add(x => x.Labels, labels)
            .Add(x => x.Height, "auto")
            .Add(x => x.Description, "Total over two points"));

        Assert.All(cut.FindAll("svg"), svg => Assert.NotEqual("auto", svg.GetAttribute("height")));
        Assert.NotEmpty(cut.FindAll("svg"));
    }

    /// <summary>
    /// A value axis is a handful of round figures, not a ruler. MudBlazor's default allows
    /// twenty ticks, and the Performance window's cumulative profit drew twenty labels from
    /// -320 to 5,760 in a 200px plot, each overlapping the next (HIG audit, 17 November;
    /// HIG Charting data: few gridlines, labelled sparingly).
    /// </summary>
    [Theory]
    [InlineData(DeskChartKind.Line)]
    [InlineData(DeskChartKind.Bar)]
    public void A_value_axis_carries_at_most_six_labels(DeskChartKind kind)
    {
        var labels = Enumerable.Range(1, 30).Select(i => $"d{i}").ToArray();
        List<ChartSeries<double>> series =
            [new() { Name = "Profit", Data = Enumerable.Range(0, 30).Select(i => i * 200.0 - 300).ToArray() }];

        var cut = RenderComponent<DeskChart>(p => p
            .Add(x => x.Series, series)
            .Add(x => x.Labels, labels)
            .Add(x => x.Kind, kind)
            .Add(x => x.Description, "Profit"));

        var figures = cut.FindAll("svg text")
            .Count(t => double.TryParse(t.TextContent, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.CurrentCulture, out _));

        Assert.InRange(figures, 2, 6);
    }

    /// <summary>
    /// The step is a round figure taken from the data's own range — 1, 2 or 5 times a power of
    /// ten — rather than MudBlazor's twenty doubled until it fits, which labelled a profit
    /// curve topping out near 5,800 at -2,560, 0, 2,560, 5,120 and 7,680.
    /// </summary>
    [Theory]
    [InlineData(-300, 5760, 2000)]
    [InlineData(0, 120, 50)]
    [InlineData(-14, 12, 10)]
    [InlineData(0, 3, 1)]
    public void The_value_step_is_a_round_figure_from_the_range(double min, double max, int step)
    {
        Assert.Equal(step, DeskChart.RoundStep(min, max));
    }

    /// <summary>
    /// What <c>.deskchart--alternate</c> in mud-bridge.css stands on. MudBlazor 9.7 has no
    /// per-series dash — <c>ChartSeries</c> is a name and data, and its per-series line
    /// overrides reach interpolation, fill and opacity only — so the dashed second channel
    /// is applied by position: every series is a sibling <c>path.mud-chart-line</c> in one
    /// group, in series order, and so is every legend item. If an upgrade wraps each series
    /// in its own group, <c>:nth-of-type(even)</c> silently stops matching; this says so.
    /// </summary>
    [Fact]
    public void Series_lines_and_legend_items_are_siblings_in_series_order()
    {
        var cut = RenderComponent<DeskChart>(p => p
            .Add(x => x.Series, new List<ChartSeries<double>>
            {
                new() { Name = "Home · Pinnacle", Data = new double[] { 1, 2, 3 } },
                new() { Name = "Home · Circa", Data = new double[] { 3, 2, 1 } },
                new() { Name = "Away · Pinnacle", Data = new double[] { 2, 2, 2 } }
            })
            .Add(x => x.Labels, new[] { "a", "b", "c" })
            .Add(x => x.Class, "deskchart--alternate")
            .Add(x => x.Description, "d"));

        Assert.Contains("deskchart--alternate", cut.Find("figure").ClassName);

        var lines = cut.FindAll(".mud-charts-line-series > path.mud-chart-line");
        Assert.Equal(3, lines.Count);
        Assert.All(lines, l => Assert.Same(lines[0].ParentElement, l.ParentElement));

        var legend = cut.FindAll(".mud-chart-legend > .mud-chart-legend-item");
        Assert.Equal(["Home · Pinnacle", "Home · Circa", "Away · Pinnacle"], legend.Select(i => i.TextContent.Trim()));
    }
}
