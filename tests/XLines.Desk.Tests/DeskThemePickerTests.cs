using Bunit;
using XLines.Desk.Primitives;
using XLines.Desk.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace XLines.Desk.Tests;

/// <summary>
/// The theme picker: one grid of tiles and one radiogroup, the catalogue's headings a filter over
/// it rather than shelves. Filtered, it draws only that heading's families, the arrows travel the
/// tiles showing, and the group keeps a Tab stop even when the filter hides the chosen tile.
/// </summary>
public class DeskThemePickerTests : DeskTestContext
{
    private DeskThemeFamily _chosen = DeskThemeFamily.Standard;

    private IRenderedComponent<DeskThemePicker> Picker(string? group, DeskThemeFamily value = DeskThemeFamily.Standard)
    {
        _chosen = value;
        return RenderComponent<DeskThemePicker>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.Group, group)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<DeskThemeFamily>(this, f => _chosen = f)));
    }

    private static IReadOnlyList<DeskThemeFamily> In(string group)
        => DeskThemeFamilies.Catalog.Where(f => f.Group == group).Select(f => f.Family).ToList();

    [Fact]
    public void Unfiltered_it_is_one_grid_of_every_family_with_no_headings()
    {
        var cut = Picker(group: null);

        var grid = cut.Find(".themes");
        Assert.Contains("swatches", grid.ClassList);
        Assert.Equal("radiogroup", grid.GetAttribute("role"));
        Assert.Equal("Theme", grid.GetAttribute("aria-label"));

        // The tiles are the grid's own children: no shelves, no headings between them.
        Assert.All(grid.Children, c => Assert.Equal("radio", c.GetAttribute("role")));
        Assert.Equal(DeskThemeFamilies.All.Select(DeskThemeFamilies.Name), grid.Children.Select(c => c.TextContent.Trim()));
        Assert.Empty(cut.FindAll(".themes__group, .themes__head, [role=group]"));
    }

    [Theory]
    [InlineData(DeskThemeGroups.BuiltIn)]
    [InlineData(DeskThemeGroups.Character)]
    [InlineData(DeskThemeGroups.Soft)]
    [InlineData(DeskThemeGroups.Vivid)]
    public void Filtered_it_shows_only_that_heading_s_families(string group)
    {
        var cut = Picker(group);

        var expected = In(group).Select(DeskThemeFamilies.Name);
        Assert.Equal(expected, cut.FindAll(".themes [role=radio]").Select(r => r.TextContent.Trim()));
    }

    [Fact]
    public void An_unknown_heading_or_all_shows_every_family()
    {
        Assert.Equal(DeskThemeFamilies.All.Count, Picker(string.Empty).FindAll("[role=radio]").Count);
        Assert.Equal(DeskThemeFamilies.All.Count, Picker("Not a heading").FindAll("[role=radio]").Count);
    }

    [Fact]
    public void The_filters_are_all_then_each_heading_in_the_catalogue_s_order()
    {
        Assert.Equal(["All", .. DeskThemeFamilies.Groups], DeskThemePicker.ThemeFilters.Select(o => o.Label));
        Assert.Equal(string.Empty, DeskThemePicker.ThemeFilters[0].Value);
        Assert.Equal(DeskThemeGroups.Soft, DeskThemePicker.GroupOf(DeskThemeFamily.Nord));
    }

    [Fact]
    public void Arrows_travel_the_tiles_showing_and_stop_at_their_ends()
    {
        var soft = In(DeskThemeGroups.Soft);
        var cut = Picker(DeskThemeGroups.Soft, soft[0]);

        cut.Find(".themes").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.Equal(soft[1], _chosen);

        cut.SetParametersAndRender(p => p.Add(x => x.Value, _chosen));
        cut.Find(".themes").KeyDown(new KeyboardEventArgs { Key = "End" });
        Assert.Equal(soft[^1], _chosen);

        // Past the last tile showing is not the next heading's first: the ends do not wrap or leak.
        cut.SetParametersAndRender(p => p.Add(x => x.Value, _chosen));
        cut.Find(".themes").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.Equal(soft[^1], _chosen);

        cut.Find(".themes").KeyDown(new KeyboardEventArgs { Key = "Home" });
        Assert.Equal(soft[0], _chosen);
    }

    /// <summary>
    /// The filter can hide the chosen tile. The group still needs its one Tab stop, so the first
    /// tile showing holds it — nothing is ticked — and the first arrow lands on that tile.
    /// </summary>
    [Fact]
    public void With_the_chosen_tile_filtered_out_the_first_tile_showing_holds_the_tab_stop()
    {
        var vivid = In(DeskThemeGroups.Vivid);
        var cut = Picker(DeskThemeGroups.Vivid, DeskThemeFamily.Standard);

        var radios = cut.FindAll(".themes [role=radio]");
        Assert.All(radios, r => Assert.Equal("false", r.GetAttribute("aria-checked")));
        Assert.Equal("0", radios[0].GetAttribute("tabindex"));
        Assert.All(radios.Skip(1), r => Assert.Equal("-1", r.GetAttribute("tabindex")));
        Assert.Empty(cut.FindAll(".swatch__tick"));

        cut.Find(".themes").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.Equal(vivid[0], _chosen);
    }

    [Fact]
    public void The_chosen_tile_showing_is_ticked_and_holds_the_tab_stop()
    {
        var cut = Picker(DeskThemeGroups.Soft, DeskThemeFamily.Nord);

        var on = Assert.Single(cut.FindAll(".themes [role=radio]"), r => r.GetAttribute("aria-checked") == "true");
        Assert.Equal(DeskThemeFamilies.Name(DeskThemeFamily.Nord), on.TextContent.Trim());
        Assert.Equal("0", on.GetAttribute("tabindex"));
        Assert.NotNull(on.QuerySelector(".swatch--on .swatch__tick"));
    }
}
