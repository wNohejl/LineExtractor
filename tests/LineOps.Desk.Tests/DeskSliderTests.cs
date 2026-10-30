using Bunit;
using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components;

namespace LineOps.Desk.Tests;

/// <summary>
/// The slider: a value along a continuous range, dragged rather than typed. The native range
/// input stays in the tree, so the keyboard and the reader get the browser's own behaviour;
/// the readout beside it is for the eye and says the same thing the reader hears.
/// </summary>
public class DeskSliderTests : DeskTestContext
{
    private IRenderedComponent<DeskSlider> Slider(
        double value,
        EventCallback<double> changed = default,
        Func<double, string>? format = null,
        Action<ComponentParameterCollectionBuilder<DeskSlider>>? more = null)
        => RenderComponent<DeskSlider>(p =>
        {
            p.Add(x => x.Label, "Share of the row")
             .Add(x => x.Min, 20)
             .Add(x => x.Max, 80)
             .Add(x => x.Step, 5)
             .Add(x => x.Value, value)
             .Add(x => x.ValueChanged, changed)
             .Add(x => x.Format, format);
            more?.Invoke(p);
        });

    [Fact]
    public void Is_a_native_range_named_by_its_label()
    {
        var input = Slider(45).Find(".slider input");

        Assert.Equal("range", input.GetAttribute("type"));
        Assert.Contains("slider__input", input.GetAttribute("class"));
        Assert.Equal("Share of the row", input.GetAttribute("aria-label"));
    }

    [Fact]
    public void Is_named_by_its_label()
    {
        Assert.Equal("Share of the row", Slider(45).Find("input[type=range]").GetAttribute("aria-label"));
    }

    /// <summary>
    /// The reader hears the formatted value, not a bare number, and the readout the eye sees is the
    /// same text. The readout is hidden from the reader so the value is not announced twice.
    /// </summary>
    [Fact]
    public void Reports_its_value_to_a_reader()
    {
        var cut = Slider(45, format: v => $"{v:0}%");

        var input = cut.Find("input[type=range]");
        Assert.Equal("45", input.GetAttribute("value"));
        Assert.Equal("45%", input.GetAttribute("aria-valuetext"));

        var readout = cut.Find("output.slider__value");
        Assert.Equal("45%", readout.TextContent.Trim());
        Assert.Equal("true", readout.GetAttribute("aria-hidden"));
    }

    [Fact]
    public void Without_a_format_the_value_speaks_for_itself()
    {
        var cut = Slider(42.5);

        Assert.Equal("42.5", cut.Find("input[type=range]").GetAttribute("aria-valuetext"));
        Assert.Equal("42.5", cut.Find(".slider__value").TextContent.Trim());
    }

    [Fact]
    public void Bounds_and_step_reach_the_input()
    {
        var input = Slider(45).Find("input[type=range]");

        Assert.Equal("20", input.GetAttribute("min"));
        Assert.Equal("80", input.GetAttribute("max"));
        Assert.Equal("5", input.GetAttribute("step"));
    }

    /// <summary>
    /// The value moves while the thumb is dragged, not when it is let go: the readout and whatever
    /// the slider governs follow the pointer.
    /// </summary>
    [Fact]
    public void Input_moves_the_value()
    {
        var moved = new List<double>();

        var cut = Slider(45, EventCallback.Factory.Create<double>(this, moved.Add));

        cut.Find("input[type=range]").Input("60");

        Assert.Equal([60d], moved);
    }

    [Fact]
    public void An_unreadable_input_moves_nothing()
    {
        var moved = new List<double>();

        var cut = Slider(45, EventCallback.Factory.Create<double>(this, moved.Add));

        cut.Find("input[type=range]").Input("");

        Assert.Empty(moved);
    }

    [Fact]
    public void Disabled_reaches_the_input()
    {
        var cut = Slider(45, more: p => p.Add(x => x.Disabled, true));

        Assert.True(cut.Find("input[type=range]").HasAttribute("disabled"));
    }

    [Fact]
    public void Class_and_attributes_land_where_they_belong()
    {
        var cut = Slider(45, more: p => p
            .Add(x => x.Class, "panel__share")
            .AddUnmatched("data-testid", "share"));

        Assert.Contains("panel__share", cut.Find(".slider").GetAttribute("class"));
        Assert.Equal("share", cut.Find("input[type=range]").GetAttribute("data-testid"));
    }
}
