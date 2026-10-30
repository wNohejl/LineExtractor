using Bunit;
using LineOps.Desk.Primitives;

namespace LineOps.Desk.Tests;

/// <summary>
/// The checkbox: one setting that is on or off, said by the shape of a tick rather than by a
/// key changing colour. The native input stays in the tree, so it is announced as a checkbox,
/// toggles with Space and is named by the label wrapped round it; the drawn box only paints.
/// </summary>
public class DeskCheckboxTests : DeskTestContext
{
    private IRenderedComponent<DeskCheckbox> Box(bool on = false, Action<bool>? changed = null, bool disabled = false)
        => RenderComponent<DeskCheckbox>(p => p
            .Add(x => x.Label, "Only open items")
            .Add(x => x.Checked, on)
            .Add(x => x.Disabled, disabled)
            .Add(x => x.CheckedChanged, (bool b) => changed?.Invoke(b)));

    [Fact]
    public void A_native_checkbox_is_named_by_the_label_round_it()
    {
        var label = Box().Find("label.checkbox");

        Assert.NotNull(label.QuerySelector("input[type=checkbox]"));
        Assert.Equal("Only open items", label.QuerySelector(".checkbox__label")?.TextContent);
    }

    [Fact]
    public void The_drawn_box_is_hidden_from_a_screen_reader()
    {
        Assert.Equal("true", Box().Find(".checkbox__box").GetAttribute("aria-hidden"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Checked_reaches_the_input(bool on)
    {
        Assert.Equal(on, Box(on).Find("input").HasAttribute("checked"));
    }

    [Fact]
    public void A_change_reports_the_new_state()
    {
        var seen = new List<bool>();

        var cut = Box(changed: seen.Add);
        cut.Find("input").Change(true);
        cut.Find("input").Change(false);

        Assert.Equal([true, false], seen);
    }

    [Fact]
    public void Disabled_disables_the_input()
    {
        Assert.True(Box(disabled: true).Find("input").HasAttribute("disabled"));
        Assert.False(Box().Find("input").HasAttribute("disabled"));
    }

    [Fact]
    public void A_label_with_markup_takes_the_place_of_the_string()
    {
        var cut = RenderComponent<DeskCheckbox>(p => p
            .Add(x => x.Label, "unused")
            .AddChildContent("<b>Bold</b> label"));

        var label = cut.Find(".checkbox__label");

        Assert.Equal("Bold label", label.TextContent);
        Assert.NotNull(label.QuerySelector("b"));
    }

    [Fact]
    public void Title_carries_the_longer_wording()
    {
        var cut = RenderComponent<DeskCheckbox>(p => p
            .Add(x => x.Label, "Short")
            .Add(x => x.Title, "The longer wording a short label leaves out"));

        Assert.Equal("The longer wording a short label leaves out", cut.Find("label.checkbox").GetAttribute("title"));
    }

    /// <summary>
    /// Class and style are the wrapper's, so the row lays out; anything else is the input's,
    /// so an <c>aria-describedby</c> or a <c>data-</c> hook lands on the control it describes.
    /// </summary>
    [Fact]
    public void Class_and_style_reach_the_wrapper_and_the_rest_reach_the_input()
    {
        var cut = RenderComponent<DeskCheckbox>(p => p
            .Add(x => x.Label, "Item")
            .Add(x => x.Class, "extra")
            .AddUnmatched("style", "margin-left:auto")
            .AddUnmatched("data-test", "c1"));

        var label = cut.Find("label.checkbox");

        Assert.Contains("extra", label.ClassList);
        Assert.Equal("margin-left:auto", label.GetAttribute("style"));
        Assert.Equal("c1", cut.Find("input").GetAttribute("data-test"));
        Assert.Null(label.GetAttribute("data-test"));
    }
}
