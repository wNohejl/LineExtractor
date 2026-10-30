using Bunit;
using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace LineOps.Desk.Tests;

/// <summary>
/// The radio group: one choice among several, each drawn as a mark and a label — or as whatever
/// the caller's template paints. One Tab stop, arrows move the choice, and the ends are ends.
/// </summary>
public class DeskRadioGroupTests : DeskTestContext
{
    private static readonly IReadOnlyList<DeskRadioOption<string>> Sizes =
    [
        new("s", "Small", "A small item"),
        new("m", "Medium"),
        new("l", "Large")
    ];

    private IRenderedComponent<DeskRadioGroup<string>> Group(
        string? value,
        EventCallback<string> changed = default,
        Action<ComponentParameterCollectionBuilder<DeskRadioGroup<string>>>? more = null)
        => RenderComponent<DeskRadioGroup<string>>(p =>
        {
            p.Add(x => x.Options, Sizes)
             .Add(x => x.Label, "Size")
             .Add(x => x.Value, value)
             .Add(x => x.ValueChanged, changed);
            more?.Invoke(p);
        });

    [Fact]
    public void Renders_a_radiogroup_with_one_tab_stop()
    {
        var cut = Group("m");

        var group = cut.Find("[role=radiogroup]");
        Assert.Equal("Size", group.GetAttribute("aria-label"));
        Assert.Contains("radiogroup", group.GetAttribute("class"));

        var radios = cut.FindAll("[role=radio]");
        Assert.Equal(["Small", "Medium", "Large"], radios.Select(r => r.TextContent.Trim()));
        Assert.Equal(["false", "true", "false"], radios.Select(r => r.GetAttribute("aria-checked")));
        Assert.Equal(["-1", "0", "-1"], radios.Select(r => r.GetAttribute("tabindex")));
        Assert.Contains("radio--on", radios[1].GetAttribute("class"));
    }

    [Fact]
    public void Options_are_plain_buttons_carrying_their_title()
    {
        var radios = Group("m").FindAll("[role=radio]");

        Assert.All(radios, r => Assert.Equal("button", r.GetAttribute("type")));
        Assert.Equal("A small item", radios[0].GetAttribute("title"));
        Assert.Null(radios[1].GetAttribute("title"));
    }

    /// <summary>The mark is drawn, not read: the reader hears the radio's state from aria-checked.</summary>
    [Fact]
    public void Each_option_draws_a_mark_beside_its_label()
    {
        var radio = Group("m").FindAll("[role=radio]")[0];

        Assert.Equal("true", radio.QuerySelector(".radio__mark")!.GetAttribute("aria-hidden"));
        Assert.Equal("Small", radio.QuerySelector(".radio__label")!.TextContent.Trim());
    }

    /// <summary>Nothing chosen yet still leaves the group reachable by Tab, on its first option.</summary>
    [Fact]
    public void Nothing_chosen_puts_the_tab_stop_on_the_first_option()
    {
        var radios = Group(null).FindAll("[role=radio]");

        Assert.All(radios, r => Assert.Equal("false", r.GetAttribute("aria-checked")));
        Assert.Equal(["0", "-1", "-1"], radios.Select(r => r.GetAttribute("tabindex")));
    }

    [Fact]
    public void Picking_an_option_reports_its_value()
    {
        var picked = new List<string>();

        var cut = Group("m", EventCallback.Factory.Create<string>(this, picked.Add));

        cut.FindAll("[role=radio]")[2].Click();

        Assert.Equal(["l"], picked);
    }

    /// <summary>A radio is never unchosen by pressing it again; that is a checkbox's job.</summary>
    [Fact]
    public void Picking_the_chosen_option_says_nothing()
    {
        var picked = new List<string>();

        var cut = Group("m", EventCallback.Factory.Create<string>(this, picked.Add));

        cut.FindAll("[role=radio]")[1].Click();

        Assert.Empty(picked);
    }

    [Theory]
    [InlineData("ArrowRight", "m")]
    [InlineData("ArrowDown", "m")]
    public void Arrows_move_the_choice(string key, string expected)
    {
        var picked = new List<string>();

        var cut = Group("s", EventCallback.Factory.Create<string>(this, picked.Add));

        cut.Find("[role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Equal([expected], picked);
    }

    [Theory]
    [InlineData("ArrowLeft", "m")]
    [InlineData("ArrowUp", "m")]
    public void Arrows_move_the_choice_back(string key, string expected)
    {
        var picked = new List<string>();

        var cut = Group("l", EventCallback.Factory.Create<string>(this, picked.Add));

        cut.Find("[role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Equal([expected], picked);
    }

    [Theory]
    [InlineData("Home", "s")]
    [InlineData("End", "l")]
    public void Home_and_End_reach_the_ends(string key, string expected)
    {
        var picked = new List<string>();

        var cut = Group("m", EventCallback.Factory.Create<string>(this, picked.Add));

        cut.Find("[role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Equal([expected], picked);
    }

    /// <summary>Like the gate, the group has two ends: past the last option it stays put.</summary>
    [Fact]
    public void The_ends_do_not_wrap()
    {
        var picked = new List<string>();

        Group("l", EventCallback.Factory.Create<string>(this, picked.Add))
            .Find("[role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Group("s", EventCallback.Factory.Create<string>(this, picked.Add))
            .Find("[role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });

        Assert.Empty(picked);
    }

    [Fact]
    public void An_unhandled_key_is_left_alone()
    {
        var picked = new List<string>();

        Group("m", EventCallback.Factory.Create<string>(this, picked.Add))
            .Find("[role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = "a" });

        Assert.Empty(picked);
    }

    [Fact]
    public void A_disabled_group_neither_moves_nor_picks()
    {
        var picked = new List<string>();

        var cut = Group("m", EventCallback.Factory.Create<string>(this, picked.Add), p => p.Add(x => x.Disabled, true));

        Assert.All(cut.FindAll("[role=radio]"), r => Assert.True(r.HasAttribute("disabled")));
        cut.Find("[role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = "End" });

        Assert.Empty(picked);
    }

    /// <summary>
    /// A template paints each option in place of the mark and label — a tile, a preview — while
    /// the group keeps the semantics: the button, its role, its state and its one Tab stop.
    /// </summary>
    [Fact]
    public void A_template_draws_each_option()
    {
        var cut = Group("l", more: p => p.Add(x => x.ItemTemplate, option => builder =>
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", "tile");
            builder.AddContent(2, option.Label.ToUpperInvariant());
            builder.CloseElement();
        }));

        var radios = cut.FindAll("[role=radio]");
        Assert.Equal(["SMALL", "MEDIUM", "LARGE"], radios.Select(r => r.QuerySelector(".tile")!.TextContent));
        Assert.Empty(cut.FindAll(".radio__mark"));
        Assert.All(radios, r => Assert.Contains("radio--drawn", r.GetAttribute("class")));
        Assert.Equal("0", radios[2].GetAttribute("tabindex"));
    }

    [Fact]
    public void Class_and_attributes_reach_the_group()
    {
        var cut = Group("m", more: p => p
            .Add(x => x.Class, "tiles")
            .AddUnmatched("data-testid", "size"));

        var group = cut.Find("[role=radiogroup]");
        Assert.Contains("tiles", group.GetAttribute("class"));
        Assert.Equal("size", group.GetAttribute("data-testid"));
    }
}
