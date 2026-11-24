using Bunit;
using XLines.Desk.Primitives;
using XLines.Desk.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace XLines.Desk.Tests;

/// <summary>
/// The accent picker: one swatch per hue the family offers, with radiogroup semantics, the chosen
/// one ringed. It follows the gate's keyboard contract, wrapping at the ends, so an operator who
/// learnt one control has learnt the other.
/// </summary>
public class DeskAccentPickerTests : DeskTestContext
{
    private IRenderedComponent<DeskAccentPicker> Picker(
        string? value,
        EventCallback<string> changed = default,
        bool isDark = true,
        DeskThemeFamily family = DeskThemeFamily.Standard)
        => RenderComponent<DeskAccentPicker>(p => p
            .Add(x => x.Family, family)
            .Add(x => x.Value, value)
            .Add(x => x.IsDark, isDark)
            .Add(x => x.ValueChanged, changed));

    [Fact]
    public void Renders_a_radiogroup_with_one_swatch_per_accent()
    {
        var cut = Picker("blue");

        Assert.Equal("Accent", cut.Find("[role=radiogroup]").GetAttribute("aria-label"));
        Assert.Equal(DeskAccents.All.Count, cut.FindAll("[role=radio]").Count);
    }

    /// <summary>
    /// The swatches are the family's own list, whichever family is named — every row of the
    /// catalogue, so a family given accents later is offered them without anyone touching this.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeskThemeTests.EveryFamily), MemberType = typeof(DeskThemeTests))]
    public void Every_family_offers_its_own_list(DeskThemeFamily family)
    {
        var radios = Picker(null, family: family).FindAll("[role=radio]");
        var accents = DeskThemeFamilies.Accents(family);

        Assert.Equal(accents.Select(a => a.Key), radios.Select(r => r.GetAttribute("data-accent")));
        Assert.All(radios, r => Assert.Equal(DeskThemeFamilies.Attribute(family, true), r.GetAttribute("data-theme")));
        Assert.Equal("true", radios[0].GetAttribute("aria-checked"));
    }

    /// <summary>
    /// A swatch is a coloured circle with no words on it, so its name has to be carried as an
    /// attribute or a screen reader meets five unlabelled buttons. Its colour is not written on
    /// it: it carries the family's data-theme and its own data-accent, and the accent block in
    /// themes.css that choosing it would apply is what paints it.
    /// </summary>
    [Fact]
    public void Every_swatch_is_named_and_painted_by_its_own_accent_block()
    {
        var radios = Picker("blue").FindAll("[role=radio]");

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
        var purple = Picker("blue", isDark: false).Find("[data-accent=purple]");

        Assert.Equal("apple-light", purple.GetAttribute("data-theme"));
    }

    /// <summary>
    /// The Window manager's theme tiles are <c>.swatches</c>/<c>.swatch</c>, one row above on the
    /// same panel. The picker keeps to its own names so a rule for either cannot land on the other.
    /// </summary>
    [Fact]
    public void The_picker_does_not_wear_the_theme_tiles_classes()
    {
        var cut = Picker("blue");

        Assert.Empty(cut.FindAll(".swatches"));
        Assert.Empty(cut.FindAll(".swatch"));
        Assert.Single(cut.FindAll(".accents"));
    }

    [Theory]
    [InlineData("indigo")]
    [InlineData("INDIGO")]
    public void Only_the_chosen_swatch_is_checked_ringed_and_in_the_tab_order(string value)
    {
        var radios = Picker(value).FindAll("[role=radio]");

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

    /// <summary>A key the family does not offer is no choice: the family's own swatch is the chosen one.</summary>
    [Fact]
    public void A_key_the_family_does_not_offer_shows_its_own_accent_chosen()
    {
        var radios = Picker("orange").FindAll("[role=radio]");

        Assert.Equal("blue", radios.Single(r => r.GetAttribute("aria-checked") == "true").GetAttribute("data-accent"));
    }

    [Fact]
    public void Clicking_a_swatch_raises_its_accent()
    {
        string? chosen = null;
        var cut = Picker("blue", EventCallback.Factory.Create<string>(this, a => chosen = a));

        cut.Find("[data-accent=teal]").Click();

        Assert.Equal("teal", chosen);
    }

    [Fact]
    public void Clicking_the_chosen_swatch_raises_nothing()
    {
        var raised = 0;
        var cut = Picker("blue", EventCallback.Factory.Create<string>(this, _ => raised++));

        cut.Find("[data-accent=blue]").Click();

        Assert.Equal(0, raised);
    }

    [Theory]
    [InlineData("blue", "ArrowRight", "teal")]
    [InlineData("blue", "ArrowDown", "teal")]
    [InlineData("blue", "ArrowLeft", "graphite")]
    [InlineData("blue", "ArrowUp", "graphite")]
    [InlineData("graphite", "ArrowRight", "blue")]
    [InlineData("purple", "Home", "blue")]
    [InlineData("blue", "End", "graphite")]
    public void Arrows_move_the_choice_and_wrap(string from, string key, string expected)
    {
        string? chosen = null;
        var cut = Picker(from, EventCallback.Factory.Create<string>(this, a => chosen = a));

        cut.Find("[role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Equal(expected, chosen);
    }

    /// <summary>
    /// Alt+ArrowLeft is the browser's Back and Ctrl+Home its top of the page; theme.js lets both
    /// through, so the picker must not also change, and store, the accent on the way.
    /// </summary>
    [Theory]
    [InlineData("ArrowLeft", false, true, false)]
    [InlineData("ArrowRight", false, true, false)]
    [InlineData("Home", true, false, false)]
    [InlineData("End", true, false, false)]
    [InlineData("ArrowDown", false, false, true)]
    public void A_key_with_a_modifier_held_moves_nothing(string key, bool ctrl, bool alt, bool meta)
    {
        var raised = 0;
        var cut = Picker("teal", EventCallback.Factory.Create<string>(this, _ => raised++));

        cut.Find("[role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = key, CtrlKey = ctrl, AltKey = alt, MetaKey = meta });

        Assert.Equal(0, raised);
    }

    [Fact]
    public void A_shifted_arrow_still_moves_the_choice()
    {
        string? chosen = null;
        var cut = Picker("blue", EventCallback.Factory.Create<string>(this, a => chosen = a));

        cut.Find("[role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = "ArrowRight", ShiftKey = true });

        Assert.Equal("teal", chosen);
    }

    [Theory]
    [InlineData("blue", "Home")]
    [InlineData("graphite", "End")]
    [InlineData("teal", "Enter")]
    public void A_key_that_lands_on_the_choice_raises_nothing(string from, string key)
    {
        var raised = 0;
        var cut = Picker(from, EventCallback.Factory.Create<string>(this, _ => raised++));

        cut.Find("[role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Equal(0, raised);
    }
}
