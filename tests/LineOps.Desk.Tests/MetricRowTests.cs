using Bunit;
using LineOps.Desk.Primitives;

namespace LineOps.Desk.Tests;

/// <summary>
/// The shelf a row of metrics sits on: nothing but the grid, so a panel never re-declares the
/// container by hand. Its own class survives whatever the caller passes, and the tiles inside
/// keep their own behaviour — a tile with somewhere to go is still a button on the shelf.
/// </summary>
public class MetricRowTests : DeskTestContext
{
    [Fact]
    public void Renders_its_tiles_inside_the_grid()
    {
        var cut = RenderComponent<MetricRow>(p => p
            .AddChildContent<Metric>(m => m.Add(x => x.Label, "Open").Add(x => x.Value, "12"))
            .AddChildContent<Metric>(m => m.Add(x => x.Label, "Closed").Add(x => x.Value, "4")));

        var row = cut.Find("div.metrics");

        Assert.Equal(2, row.QuerySelectorAll(".metric").Length);
        Assert.Equal(["12", "4"], row.QuerySelectorAll(".metric__value").Select(v => v.TextContent));
    }

    [Fact]
    public void A_tile_on_the_shelf_still_goes_where_it_says()
    {
        var pressed = 0;

        var cut = RenderComponent<MetricRow>(p => p
            .AddChildContent<Metric>(m => m
                .Add(x => x.Label, "Late")
                .Add(x => x.Value, "3")
                .Add(x => x.OnClick, () => pressed++)));

        cut.Find("button.metric").Click();

        Assert.Equal(1, pressed);
    }

    [Fact]
    public void A_bare_row_carries_only_its_own_class()
    {
        var row = RenderComponent<MetricRow>().Find("div");

        Assert.Equal("metrics", row.GetAttribute("class"));
        Assert.Null(row.GetAttribute("style"));
    }

    [Fact]
    public void A_callers_class_cannot_replace_the_grid_and_the_rest_pass_through()
    {
        var row = RenderComponent<MetricRow>(p => p
            .Add(x => x.Class, "named")
            .Add(x => x.Style, "margin-top:8px")
            .AddUnmatched("data-test", "mr1")).Find("div");

        Assert.Equal(["metrics", "named"], row.ClassList);
        Assert.Equal("margin-top:8px", row.GetAttribute("style"));
        Assert.Equal("mr1", row.GetAttribute("data-test"));
    }
}
