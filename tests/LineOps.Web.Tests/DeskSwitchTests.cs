using Bunit;
using LineOps.Web.Components.Desk;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace LineOps.Web.Tests;

/// <summary>
/// The gate switch: one visible choice among a few, with radiogroup semantics. Arrows move
/// the cap, Tab leaves the group, and fewer than two positions is not a choice at all.
/// </summary>
public class DeskSwitchTests : DeskTestContext
{
    private static readonly IReadOnlyList<DeskSwitchOption<int>> Lookback =
    [
        new(14, "14d", "Last 14 days"),
        new(30, "30d", "Last 30 days"),
        new(90, "90d", "Last 90 days")
    ];

    private IRenderedComponent<DeskSwitch<int>> Gate(
        int value,
        EventCallback<int> changed = default,
        IReadOnlyList<DeskSwitchOption<int>>? options = null)
        => RenderComponent<DeskSwitch<int>>(p => p
            .Add(x => x.Options, options ?? Lookback)
            .Add(x => x.Label, "Lookback window")
            .Add(x => x.Value, value)
            .Add(x => x.ValueChanged, changed));

    [Fact]
    public void Renders_a_radiogroup_named_by_its_label()
    {
        var group = Gate(30).Find("[role=radiogroup]");

        Assert.Equal("Lookback window", group.GetAttribute("aria-label"));
        Assert.Contains("gate", group.GetAttribute("class"));
    }

    [Fact]
    public void Every_option_is_a_radio_carrying_its_label_and_title()
    {
        var radios = Gate(30).FindAll("[role=radio]");

        Assert.Equal(3, radios.Count);
        Assert.Equal(["14d", "30d", "90d"], radios.Select(r => r.TextContent.Trim()));
        Assert.Equal("Last 14 days", radios[0].GetAttribute("title"));
    }

    [Fact]
    public void Options_are_plain_buttons_so_a_gate_never_submits_a_form()
    {
        Assert.All(Gate(30).FindAll("[role=radio]"), r => Assert.Equal("button", r.GetAttribute("type")));
    }

    [Fact]
    public void Only_the_selected_position_is_checked()
    {
        var radios = Gate(30).FindAll("[role=radio]");

        Assert.Equal(["false", "true", "false"], radios.Select(r => r.GetAttribute("aria-checked")));
        Assert.Contains("gate__opt--on", radios[1].GetAttribute("class"));
    }

    /// <summary>The group is one Tab stop; arrows do the travelling inside it.</summary>
    [Fact]
    public void Roving_tabindex_puts_the_one_tab_stop_on_the_selection()
    {
        var radios = Gate(90).FindAll("[role=radio]");

        Assert.Equal(["-1", "-1", "0"], radios.Select(r => r.GetAttribute("tabindex")));
    }

    /// <summary>
    /// A custom value beside the gate leaves every position up. The cap is simply absent
    /// rather than lying about one — but the group must still be reachable by Tab.
    /// </summary>
    [Fact]
    public void An_unmatched_value_raises_the_cap_and_still_leaves_a_tab_stop()
    {
        var cut = Gate(7);

        Assert.Empty(cut.FindAll(".gate__cap"));
        Assert.All(cut.FindAll("[role=radio]"), r => Assert.Equal("false", r.GetAttribute("aria-checked")));
        Assert.Equal("0", cut.FindAll("[role=radio]")[0].GetAttribute("tabindex"));
    }

    [Fact]
    public void A_matched_value_shows_the_cap()
    {
        Assert.Single(Gate(30).FindAll(".gate__cap"));
    }

    [Fact]
    public void Fewer_than_two_positions_is_not_a_choice_and_renders_nothing()
    {
        var cut = Gate(14, options: [new(14, "14d")]);

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void Picking_another_position_reports_its_value()
    {
        var picked = new List<int>();

        var cut = Gate(30, EventCallback.Factory.Create<int>(this, picked.Add));

        cut.FindAll("[role=radio]")[2].Click();

        Assert.Equal([90], picked);
    }

    [Fact]
    public void Picking_the_position_already_selected_says_nothing()
    {
        var picked = new List<int>();

        var cut = Gate(30, EventCallback.Factory.Create<int>(this, picked.Add));

        cut.FindAll("[role=radio]")[1].Click();

        Assert.Empty(picked);
    }

    [Theory]
    [InlineData("ArrowRight", 90)]
    [InlineData("ArrowDown", 90)]
    [InlineData("ArrowLeft", 14)]
    [InlineData("ArrowUp", 14)]
    [InlineData("Home", 14)]
    [InlineData("End", 90)]
    public void Arrows_move_the_cap(string key, int expected)
    {
        var picked = new List<int>();

        var cut = Gate(30, EventCallback.Factory.Create<int>(this, picked.Add));

        cut.Find("[role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Equal([expected], picked);
    }

    /// <summary>
    /// A track with two ends you can feel, unlike the tab bar beside it — travelling past
    /// the last position stays there rather than wrapping round to the first.
    /// </summary>
    [Fact]
    public void The_track_has_ends_and_does_not_wrap()
    {
        var picked = new List<int>();

        var atEnd = Gate(90, EventCallback.Factory.Create<int>(this, picked.Add));
        atEnd.Find("[role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        var atStart = Gate(14, EventCallback.Factory.Create<int>(this, picked.Add));
        atStart.Find("[role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });

        Assert.Empty(picked);
    }

    [Fact]
    public void An_unhandled_key_is_left_alone()
    {
        var picked = new List<int>();

        var cut = Gate(30, EventCallback.Factory.Create<int>(this, picked.Add));

        cut.Find("[role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = "a" });

        Assert.Empty(picked);
    }

    [Theory]
    [InlineData(DeskKeySize.Small, "gate--sm")]
    [InlineData(DeskKeySize.Large, "gate--lg")]
    public void Size_maps_to_its_class(DeskKeySize size, string expected)
    {
        var cut = RenderComponent<DeskSwitch<int>>(p => p
            .Add(x => x.Options, Lookback)
            .Add(x => x.Label, "Lookback window")
            .Add(x => x.Value, 30)
            .Add(x => x.Size, size));

        Assert.Contains(expected, cut.Find("[role=radiogroup]").GetAttribute("class"));
    }

    [Fact]
    public void Mono_asks_for_tabular_figures()
    {
        var cut = RenderComponent<DeskSwitch<int>>(p => p
            .Add(x => x.Options, Lookback)
            .Add(x => x.Label, "Lookback window")
            .Add(x => x.Value, 30)
            .Add(x => x.Mono, true));

        Assert.Contains("gate--num", cut.Find("[role=radiogroup]").GetAttribute("class"));
    }

    /// <summary>
    /// The layout rides on --n and --i, so an incoming style must not replace them outright —
    /// that would drop the custom properties and collapse the row into a stack.
    /// </summary>
    [Fact]
    public void A_callers_style_is_appended_so_the_layout_properties_survive()
    {
        var cut = RenderComponent<DeskSwitch<int>>(p => p
            .Add(x => x.Options, Lookback)
            .Add(x => x.Label, "Lookback window")
            .Add(x => x.Value, 30)
            .AddUnmatched("style", "margin-left:auto"));

        var style = cut.Find("[role=radiogroup]").GetAttribute("style") ?? string.Empty;

        Assert.Equal("--n:3; --i:1; margin-left:auto", style);
    }

    [Fact]
    public void The_layout_properties_report_the_count_and_the_selection()
    {
        Assert.Equal("--n:3; --i:2", Gate(90).Find("[role=radiogroup]").GetAttribute("style"));
    }

    [Fact]
    public void Unmatched_attributes_reach_the_group_but_class_and_style_do_not_splat_twice()
    {
        var cut = RenderComponent<DeskSwitch<int>>(p => p
            .Add(x => x.Options, Lookback)
            .Add(x => x.Label, "Lookback window")
            .Add(x => x.Value, 30)
            .Add(x => x.Class, "panel__gate")
            .AddUnmatched("data-testid", "lookback"));

        var group = cut.Find("[role=radiogroup]");

        Assert.Equal("lookback", group.GetAttribute("data-testid"));
        Assert.Contains("panel__gate", group.GetAttribute("class"));
    }
}
