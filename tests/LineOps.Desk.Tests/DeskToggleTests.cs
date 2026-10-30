using Bunit;
using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components;

namespace LineOps.Desk.Tests;

/// <summary>
/// The toggle: one setting that takes effect the moment it flips. It is a native checkbox that
/// announces itself as a switch, so Space flips it, a press on its words flips it, and a reader
/// hears "switch, on" rather than "checkbox, checked".
/// </summary>
public class DeskToggleTests : DeskTestContext
{
    private IRenderedComponent<DeskToggle> Toggle(
        bool on,
        EventCallback<bool> changed = default,
        bool disabled = false)
        => RenderComponent<DeskToggle>(p => p
            .Add(x => x.Label, "Show hidden items")
            .Add(x => x.Checked, on)
            .Add(x => x.Disabled, disabled)
            .Add(x => x.CheckedChanged, changed));

    [Fact]
    public void Announces_as_a_switch_and_flips_on_change()
    {
        var flipped = new List<bool>();

        var off = Toggle(false, EventCallback.Factory.Create<bool>(this, flipped.Add));
        var input = off.Find("input");

        // A native checkbox, so Space and a press are the browser's own; the role says what it is.
        Assert.Equal("checkbox", input.GetAttribute("type"));
        Assert.Equal("switch", input.GetAttribute("role"));
        Assert.Equal("false", input.GetAttribute("aria-checked"));

        input.Change(true);

        Assert.Equal([true], flipped);
        Assert.Equal("true", Toggle(true).Find("input").GetAttribute("aria-checked"));
    }

    /// <summary>
    /// The input sits inside the label, so the words name it for a reader and a press anywhere
    /// on them flips it: there is no id to wire and none to collide.
    /// </summary>
    [Fact]
    public void Is_named_by_its_label()
    {
        var label = Toggle(false).Find("label.toggle");

        Assert.NotNull(label.QuerySelector("input[role=switch]"));
        Assert.Equal("Show hidden items", label.QuerySelector(".toggle__label")!.TextContent.Trim());
    }

    /// <summary>The track only paints; a reader is told the state once, by the input.</summary>
    [Fact]
    public void The_drawn_track_is_hidden_from_a_reader()
    {
        Assert.Equal("true", Toggle(false).Find(".toggle__track").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void Disabled_does_not_flip()
    {
        var flipped = new List<bool>();

        var cut = Toggle(false, EventCallback.Factory.Create<bool>(this, flipped.Add), disabled: true);
        var input = cut.Find("input");

        Assert.True(input.HasAttribute("disabled"));

        input.Change(true);

        Assert.Empty(flipped);
    }

    [Fact]
    public void A_title_and_extra_attributes_reach_their_elements()
    {
        var cut = RenderComponent<DeskToggle>(p => p
            .Add(x => x.Label, "Show hidden items")
            .Add(x => x.Title, "Lists the items that are usually left out")
            .Add(x => x.Class, "panel__toggle")
            .AddUnmatched("data-testid", "hidden-items"));

        var label = cut.Find("label");

        Assert.Equal("Lists the items that are usually left out", label.GetAttribute("title"));
        Assert.Contains("panel__toggle", label.GetAttribute("class"));
        Assert.Equal("hidden-items", cut.Find("input").GetAttribute("data-testid"));
    }
}
