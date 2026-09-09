using Bunit;
using LineOps.Web.Components.Desk;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace LineOps.Web.Tests;

/// <summary>
/// A window's sections. The bar is navigation, so it carries tablist semantics and keeps the
/// held value in range — a window must not come to rest on a tab that is no longer there.
/// </summary>
public class DeskTabsTests : DeskTestContext
{
    private static readonly IReadOnlyList<DeskTab<string>> Sections =
    [
        new("odds", "Odds"),
        new("history", "History"),
        new("runs", "Runs")
    ];

    private IRenderedComponent<DeskTabs<string>> Bar(
        string value,
        EventCallback<string> changed = default,
        IReadOnlyList<DeskTab<string>>? tabs = null)
        => RenderComponent<DeskTabs<string>>(p => p
            .Add(x => x.Tabs, tabs ?? Sections)
            .Add(x => x.Label, "Game sections")
            .Add(x => x.Value, value)
            .Add(x => x.ValueChanged, changed)
            .Add(x => x.ChildContent, v => b => b.AddMarkupContent(0, $"<p>showing {v}</p>")));

    [Fact]
    public void Renders_a_tablist_named_by_its_label()
    {
        var list = Bar("odds").Find("[role=tablist]");

        Assert.Equal("Game sections", list.GetAttribute("aria-label"));
        Assert.Contains("tabs", list.GetAttribute("class"));
    }

    [Fact]
    public void Every_section_is_a_tab_carrying_its_label()
    {
        var tabs = Bar("odds").FindAll("[role=tab]");

        Assert.Equal(3, tabs.Count);
        Assert.Equal(["Odds", "History", "Runs"], tabs.Select(t => t.TextContent.Trim()));
    }

    [Fact]
    public void Only_the_selected_tab_is_selected_and_holds_the_tab_stop()
    {
        var tabs = Bar("history").FindAll("[role=tab]");

        Assert.Equal(["false", "true", "false"], tabs.Select(t => t.GetAttribute("aria-selected")));
        Assert.Equal(["-1", "0", "-1"], tabs.Select(t => t.GetAttribute("tabindex")));
        Assert.Contains("tabs__tab--on", tabs[1].GetAttribute("class"));
    }

    /// <summary>
    /// The ids are generated per instance, and the wiring has to point both ways: every tab
    /// controls the panel, and the panel is labelled by the tab currently selected.
    /// </summary>
    [Fact]
    public void The_generated_ids_wire_the_tabs_to_their_panel_in_both_directions()
    {
        var cut = Bar("history");

        var tabs = cut.FindAll("[role=tab]");
        var panel = cut.Find("[role=tabpanel]");
        var panelId = panel.GetAttribute("id");

        Assert.False(string.IsNullOrWhiteSpace(panelId));
        Assert.All(tabs, t => Assert.Equal(panelId, t.GetAttribute("aria-controls")));
        Assert.Equal(tabs[1].GetAttribute("id"), panel.GetAttribute("aria-labelledby"));
        Assert.Equal(3, tabs.Select(t => t.GetAttribute("id")).Distinct().Count());
    }

    /// <summary>Several bars can share a page, so no two may claim the same ids.</summary>
    [Fact]
    public void Two_bars_on_one_page_do_not_share_ids()
    {
        var first = Bar("odds").Find("[role=tabpanel]").GetAttribute("id");
        var second = Bar("odds").Find("[role=tabpanel]").GetAttribute("id");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void The_panel_shows_the_selected_sections_content()
    {
        Assert.Contains("showing history", Bar("history").Find("[role=tabpanel]").TextContent);
    }

    [Fact]
    public void The_panel_is_focusable_so_the_content_is_reachable_from_the_bar()
    {
        Assert.Equal("0", Bar("odds").Find("[role=tabpanel]").GetAttribute("tabindex"));
    }

    /// <summary>A tab that opens onto "no data" spends a click to say so, so it is not offered.</summary>
    [Fact]
    public void A_section_with_nothing_behind_it_is_not_offered()
    {
        var cut = Bar("odds", tabs:
        [
            new("odds", "Odds"),
            new("history", "History", Available: false),
            new("runs", "Runs")
        ]);

        Assert.Equal(["Odds", "Runs"], cut.FindAll("[role=tab]").Select(t => t.TextContent.Trim()));
    }

    /// <summary>One section is not a choice: no bar, and no tabpanel role to answer to.</summary>
    [Fact]
    public void A_lone_section_renders_on_its_own_with_no_bar()
    {
        var cut = Bar("odds", tabs:
        [
            new("odds", "Odds"),
            new("history", "History", Available: false)
        ]);

        Assert.Empty(cut.FindAll("[role=tablist]"));
        Assert.Empty(cut.FindAll("[role=tabpanel]"));
        Assert.Contains("showing odds", cut.Markup);
    }

    [Fact]
    public void No_available_section_renders_nothing()
    {
        var cut = Bar("odds", tabs: [new("odds", "Odds", Available: false)]);

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void Picking_another_tab_reports_its_value()
    {
        var picked = new List<string>();

        var cut = Bar("odds", EventCallback.Factory.Create<string>(this, picked.Add));

        cut.FindAll("[role=tab]")[2].Click();

        Assert.Equal(["runs"], picked);
    }

    [Fact]
    public void Picking_the_tab_already_selected_says_nothing()
    {
        var picked = new List<string>();

        var cut = Bar("odds", EventCallback.Factory.Create<string>(this, picked.Add));

        cut.FindAll("[role=tab]")[0].Click();

        Assert.Empty(picked);
    }

    [Theory]
    [InlineData("ArrowRight", "runs")]
    [InlineData("ArrowDown", "runs")]
    [InlineData("ArrowLeft", "odds")]
    [InlineData("ArrowUp", "odds")]
    [InlineData("Home", "odds")]
    [InlineData("End", "runs")]
    public void Arrows_move_along_the_bar(string key, string expected)
    {
        var picked = new List<string>();

        var cut = Bar("history", EventCallback.Factory.Create<string>(this, picked.Add));

        cut.Find("[role=tablist]").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Equal([expected], picked);
    }

    /// <summary>A tab bar is a ring you cycle, where the gate beside it is a track with ends.</summary>
    [Fact]
    public void The_bar_wraps_at_both_ends()
    {
        var forward = new List<string>();
        var back = new List<string>();

        Bar("runs", EventCallback.Factory.Create<string>(this, forward.Add))
            .Find("[role=tablist]").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        Bar("odds", EventCallback.Factory.Create<string>(this, back.Add))
            .Find("[role=tablist]").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });

        Assert.Equal(["odds"], forward);
        Assert.Equal(["runs"], back);
    }

    [Fact]
    public void An_unhandled_key_is_left_alone()
    {
        var picked = new List<string>();

        var cut = Bar("odds", EventCallback.Factory.Create<string>(this, picked.Add));

        cut.Find("[role=tablist]").KeyDown(new KeyboardEventArgs { Key = "a" });

        Assert.Empty(picked);
    }

    /// <summary>
    /// The held value names a section that has gone away. The bar falls back to the first
    /// available one and says so, so the caller's state and the screen agree.
    /// </summary>
    [Fact]
    public void A_value_that_is_no_longer_offered_is_corrected_back_to_the_caller()
    {
        var picked = new List<string>();

        var cut = Bar("gone", EventCallback.Factory.Create<string>(this, picked.Add));

        Assert.Equal(["odds"], picked);
        Assert.Equal("true", cut.FindAll("[role=tab]")[0].GetAttribute("aria-selected"));
    }

    /// <summary>A caller that ignores ValueChanged must not be asked to fix the same value forever.</summary>
    [Fact]
    public void The_correction_is_offered_once_for_a_given_value()
    {
        var picked = new List<string>();

        var cut = Bar("gone", EventCallback.Factory.Create<string>(this, picked.Add));

        cut.SetParametersAndRender(p => p.Add(x => x.Value, "gone"));
        cut.SetParametersAndRender(p => p.Add(x => x.Value, "gone"));

        Assert.Equal(["odds"], picked);
    }

    [Fact]
    public void A_one_off_class_joins_the_bar()
    {
        var cut = RenderComponent<DeskTabs<string>>(p => p
            .Add(x => x.Tabs, Sections)
            .Add(x => x.Label, "Game sections")
            .Add(x => x.Value, "odds")
            .Add(x => x.Class, "window__tabs")
            .Add(x => x.ChildContent, v => b => b.AddMarkupContent(0, $"<p>{v}</p>")));

        Assert.Contains("window__tabs", cut.Find("[role=tablist]").GetAttribute("class"));
    }
}
