using Bunit;
using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components;

namespace LineOps.Desk.Tests;

/// <summary>
/// One number on the desk: the figure leads, the label explains it, the note keeps it from
/// being read wrong. The state is named, not coloured, and a tile with somewhere to go is a real
/// button — reachable by keyboard and announced as pressable — rather than a div that happens to
/// take clicks.
/// </summary>
public class MetricTests : DeskTestContext
{
    [Fact]
    public void Renders_the_value_the_label_and_the_note_in_that_order()
    {
        var cut = RenderComponent<Metric>(p => p
            .Add(x => x.Label, "Open items")
            .Add(x => x.Value, "12")
            .Add(x => x.Note, "of 14 wanted"));

        var tile = cut.Find(".metric");

        Assert.Equal(["metric__value", "metric__label", "metric__note"], tile.Children.Select(c => c.ClassList[0]));
        Assert.Equal("12", tile.QuerySelector(".metric__value")?.TextContent);
        Assert.Equal("Open items", tile.QuerySelector(".metric__label")?.TextContent);
        Assert.Equal("of 14 wanted", tile.QuerySelector(".metric__note")?.TextContent);
    }

    [Fact]
    public void An_empty_note_is_left_out()
    {
        var cut = RenderComponent<Metric>(p => p.Add(x => x.Label, "Open items").Add(x => x.Value, "12"));

        Assert.Empty(cut.FindAll(".metric__note"));
    }

    [Fact]
    public void Markup_in_place_of_the_value_wins()
    {
        var cut = RenderComponent<Metric>(p => p
            .Add(x => x.Label, "Done of total")
            .Add(x => x.Value, "ignored")
            .AddChildContent("<span>3</span> / <span>7</span>"));

        Assert.Equal("3 / 7", cut.Find(".metric__value").TextContent);
    }

    [Fact]
    public void Numeric_sets_the_figure_in_tabular_digits()
    {
        var cut = RenderComponent<Metric>(p => p.Add(x => x.Label, "Count").Add(x => x.Value, "12").Add(x => x.Numeric, true));

        Assert.Contains("num", cut.Find(".metric__value").ClassList);
        Assert.DoesNotContain("num", RenderComponent<Metric>(p => p.Add(x => x.Label, "Count")).Find(".metric__value").ClassList);
    }

    [Theory]
    [InlineData(DeskState.Positive, "metric--good")]
    [InlineData(DeskState.Negative, "metric--bad")]
    [InlineData(DeskState.Warning, "metric--warn")]
    [InlineData(DeskState.Info, "metric--info")]
    public void The_state_maps_to_its_class(DeskState state, string expected)
    {
        Assert.Contains(expected, RenderComponent<Metric>(p => p.Add(x => x.Label, "Count").Add(x => x.State, state)).Find(".metric").ClassList);
    }

    [Fact]
    public void A_tile_with_nowhere_to_go_is_not_a_button()
    {
        var cut = RenderComponent<Metric>(p => p.Add(x => x.Label, "Count").Add(x => x.Value, "3"));

        Assert.Empty(cut.FindAll("button"));
        Assert.Equal("div", cut.Find(".metric").TagName.ToLowerInvariant());
    }

    [Fact]
    public void A_tile_with_somewhere_to_go_is_a_button_that_says_where_and_goes()
    {
        var pressed = 0;

        var cut = RenderComponent<Metric>(p => p
            .Add(x => x.Label, "Late items")
            .Add(x => x.Value, "3")
            .Add(x => x.ActionHint, "Open the late items")
            .Add(x => x.OnClick, EventCallback.Factory.Create(this, () => pressed++)));

        var button = cut.Find("button.metric");

        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal("Open the late items", button.GetAttribute("title"));
        Assert.Contains("metric--action", button.ClassList);
        Assert.Equal("true", button.QuerySelector(".metric__go")?.GetAttribute("aria-hidden"));

        button.Click();

        Assert.Equal(1, pressed);
    }

    /// <summary>A caller's class is appended after the state modifier, never in place of it; anything else passes through.</summary>
    [Fact]
    public void A_callers_class_cannot_erase_the_state_and_the_rest_pass_through()
    {
        var cut = RenderComponent<Metric>(p => p
            .Add(x => x.Label, "Count")
            .Add(x => x.State, DeskState.Negative)
            .Add(x => x.Class, "named")
            .Add(x => x.Style, "min-width:8rem")
            .AddUnmatched("data-test", "m1"));

        var tile = cut.Find(".metric");

        Assert.Equal(["metric", "metric--bad", "named"], tile.ClassList);
        Assert.Equal("min-width:8rem", tile.GetAttribute("style"));
        Assert.Equal("m1", tile.GetAttribute("data-test"));
    }
}
