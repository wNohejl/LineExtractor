using Bunit;
using LineOps.Desk.Primitives;

namespace LineOps.Desk.Tests;

/// <summary>
/// The progress bar: how far along a named piece of work is. Determinate is the default and
/// reports its value to a screen reader; indeterminate reports none rather than a made-up one.
/// The label names the work and doubles as the bar's accessible name.
/// </summary>
public class DeskProgressTests : DeskTestContext
{
    private IRenderedComponent<DeskProgress> Bar(double value = 30, double max = 120, bool indeterminate = false, string? label = "Copying 120 files")
        => RenderComponent<DeskProgress>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.Max, max)
            .Add(x => x.Indeterminate, indeterminate)
            .Add(x => x.Label, label));

    [Fact]
    public void A_determinate_bar_reports_its_value_and_bounds()
    {
        var bar = Bar().Find("[role=progressbar]");

        Assert.Equal("0", bar.GetAttribute("aria-valuemin"));
        Assert.Equal("120", bar.GetAttribute("aria-valuemax"));
        Assert.Equal("30", bar.GetAttribute("aria-valuenow"));
        Assert.Equal("width:25%", Bar().Find(".prog__fill").GetAttribute("style"));
    }

    [Fact]
    public void An_indeterminate_bar_reports_no_value()
    {
        var cut = Bar(indeterminate: true);
        var bar = cut.Find("[role=progressbar]");

        Assert.Null(bar.GetAttribute("aria-valuenow"));
        Assert.Null(bar.GetAttribute("aria-valuemax"));
        Assert.Null(bar.GetAttribute("aria-valuemin"));
        Assert.Contains("prog--indeterminate", cut.Find(".prog").ClassList);
        Assert.Empty(cut.FindAll(".prog__pct"));
    }

    [Fact]
    public void The_label_names_the_bar_and_the_work()
    {
        var cut = Bar();

        Assert.Equal("Copying 120 files", cut.Find("[role=progressbar]").GetAttribute("aria-label"));
        Assert.Contains("Copying 120 files", cut.Find(".prog__label").TextContent);
    }

    [Fact]
    public void The_percentage_sits_beside_the_label_unless_asked_not_to()
    {
        Assert.Equal("25%", Bar().Find(".prog__pct").TextContent);

        var quiet = RenderComponent<DeskProgress>(p => p
            .Add(x => x.Value, 30)
            .Add(x => x.Label, "Copying files")
            .Add(x => x.ShowValue, false));

        Assert.Empty(quiet.FindAll(".prog__pct"));
    }

    [Fact]
    public void A_value_past_the_end_is_held_at_the_end()
    {
        var cut = Bar(value: 500, max: 100);

        Assert.Equal("100", cut.Find("[role=progressbar]").GetAttribute("aria-valuenow"));
        Assert.Equal("width:100%", cut.Find(".prog__fill").GetAttribute("style"));
    }

    [Fact]
    public void No_label_renders_no_label_line()
    {
        Assert.Empty(Bar(label: null).FindAll(".prog__label"));
    }

    [Fact]
    public void Class_and_style_add_to_the_bar_s_own_and_the_rest_pass_through()
    {
        var cut = RenderComponent<DeskProgress>(p => p
            .Add(x => x.Value, 10)
            .Add(x => x.Class, "extra")
            .Add(x => x.Style, "margin-top:4px")
            .AddUnmatched("data-test", "p1"));

        var root = cut.Find(".prog");

        Assert.Contains("prog--determinate", root.ClassList);
        Assert.Contains("extra", root.ClassList);
        Assert.Equal("margin-top:4px", root.GetAttribute("style"));
        Assert.Equal("p1", root.GetAttribute("data-test"));
    }
}
