using Bunit;
using LineOps.Desk.Primitives;
using LineOps.Desk.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace LineOps.Desk.Tests;

/// <summary>
/// The accent picker: one swatch per hue with radiogroup semantics, the chosen one ringed. It
/// follows the gate's keyboard contract, wrapping at the ends, so an operator who learnt one
/// control has learnt the other.
/// </summary>
public class DeskAccentPickerTests : DeskTestContext
{
    private IRenderedComponent<DeskAccentPicker> Picker(
        DeskAccent value,
        EventCallback<DeskAccent> changed = default,
        bool isDark = true)
        => RenderComponent<DeskAccentPicker>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.IsDark, isDark)
            .Add(x => x.ValueChanged, changed));

    [Fact]
    public void Renders_a_radiogroup_with_one_swatch_per_accent()
    {
        var cut = Picker(DeskAccent.Blue);

        Assert.Equal("Accent", cut.Find("[role=radiogroup]").GetAttribute("aria-label"));
        Assert.Equal(DeskAccents.All.Count, cut.FindAll("[role=radio]").Count);
    }

    /// <summary>
    /// A swatch is a coloured circle with no words on it, so its name has to be carried as an
    /// attribute or a screen reader meets five unlabelled buttons. Its colour is not written on
    /// it: it carries the Standard desk's data-theme and its own data-accent, and the accent block
    /// in themes.css that choosing it would apply is what paints it.
    /// </summary>
    [Fact]
    public void Every_swatch_is_named_and_painted_by_its_own_accent_block()
    {
        var radios = Picker(DeskAccent.Blue).FindAll("[role=radio]");

        for (var i = 0; i < radios.Count; i++)
        {
            var accent = DeskAccents.All[i];

            Assert.Equal(DeskAccents.Label(accent), radios[i].GetAttribute("aria-label"));
            Assert.Equal(DeskAccents.Key(accent), radios[i].GetAttribute("data-accent"));
            Assert.Equal("apple-dark", radios[i].GetAttribute("data-theme"));
            Assert.Null(radios[i].GetAttribute("style"));
            Assert.Equal("button", radios[i].GetAttribute("type"));
        }
    }

    [Fact]
    public void Swatches_are_drawn_on_the_light_desk_when_it_is_showing()
    {
        var purple = Picker(DeskAccent.Blue, isDark: false)
            .Find($"[data-accent={DeskAccents.Key(DeskAccent.Purple)}]");

        Assert.Equal("apple-light", purple.GetAttribute("data-theme"));
    }

    /// <summary>
    /// The Window manager's theme tiles are <c>.swatches</c>/<c>.swatch</c>, one row above on the
    /// same panel. The picker keeps to its own names so a rule for either cannot land on the other.
    /// </summary>
    [Fact]
    public void The_picker_does_not_wear_the_theme_tiles_classes()
    {
        var cut = Picker(DeskAccent.Blue);

        Assert.Empty(cut.FindAll(".swatches"));
        Assert.Empty(cut.FindAll(".swatch"));
        Assert.Single(cut.FindAll(".accents"));
    }

    [Fact]
    public void Only_the_chosen_swatch_is_checked_ringed_and_in_the_tab_order()
    {
        var radios = Picker(DeskAccent.Indigo).FindAll("[role=radio]");

        var chosen = radios.Single(r => r.GetAttribute("aria-checked") == "true");

        Assert.Equal("indigo", chosen.GetAttribute("data-accent"));
        Assert.Contains("accent-swatch--on", chosen.GetAttribute("class"));
        Assert.Equal("0", chosen.GetAttribute("tabindex"));
        Assert.All(radios.Where(r => r != chosen), r =>
        {
            Assert.Equal("-1", r.GetAttribute("tabindex"));
            Assert.DoesNotContain("accent-swatch--on", r.GetAttribute("class"));
        });
    }

    [Fact]
    public void Clicking_a_swatch_raises_its_accent()
    {
        DeskAccent? chosen = null;
        var cut = Picker(DeskAccent.Blue, EventCallback.Factory.Create<DeskAccent>(this, a => chosen = a));

        cut.Find("[data-accent=teal]").Click();

        Assert.Equal(DeskAccent.Teal, chosen);
    }

    [Fact]
    public void Clicking_the_chosen_swatch_raises_nothing()
    {
        var raised = 0;
        var cut = Picker(DeskAccent.Blue, EventCallback.Factory.Create<DeskAccent>(this, _ => raised++));

        cut.Find("[data-accent=blue]").Click();

        Assert.Equal(0, raised);
    }

    [Theory]
    [InlineData(DeskAccent.Blue, "ArrowRight", DeskAccent.Teal)]
    [InlineData(DeskAccent.Blue, "ArrowDown", DeskAccent.Teal)]
    [InlineData(DeskAccent.Blue, "ArrowLeft", DeskAccent.Graphite)]
    [InlineData(DeskAccent.Blue, "ArrowUp", DeskAccent.Graphite)]
    [InlineData(DeskAccent.Graphite, "ArrowRight", DeskAccent.Blue)]
    [InlineData(DeskAccent.Purple, "Home", DeskAccent.Blue)]
    [InlineData(DeskAccent.Blue, "End", DeskAccent.Graphite)]
    public void Arrows_move_the_choice_and_wrap(DeskAccent from, string key, DeskAccent expected)
    {
        DeskAccent? chosen = null;
        var cut = Picker(from, EventCallback.Factory.Create<DeskAccent>(this, a => chosen = a));

        cut.Find("[role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Equal(expected, chosen);
    }

    [Theory]
    [InlineData(DeskAccent.Blue, "Home")]
    [InlineData(DeskAccent.Graphite, "End")]
    [InlineData(DeskAccent.Teal, "Enter")]
    public void A_key_that_lands_on_the_choice_raises_nothing(DeskAccent from, string key)
    {
        var raised = 0;
        var cut = Picker(from, EventCallback.Factory.Create<DeskAccent>(this, _ => raised++));

        cut.Find("[role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Equal(0, raised);
    }
}
