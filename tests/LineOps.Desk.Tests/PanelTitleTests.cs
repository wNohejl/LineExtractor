using Bunit;
using LineOps.Desk.Primitives;

namespace LineOps.Desk.Tests;

/// <summary>
/// The title block a subject window opens with — a game, a team, a player, a pairing.
///
/// <para>
/// Those windows used to set their own name with <c>PanelHeader Tight</c>, the uppercase
/// section rule, so the thing the window was about was its smallest type. The title is an
/// <c>h2</c> in the title ramp, with tags on its baseline and one quiet meta line under it
/// that carries the window-scoping keys at its right edge. What is pinned here is the
/// structure: the rank, the slots, that an empty meta row is not drawn, and that a caller's
/// class and style extend the block rather than erase it.
/// </para>
/// </summary>
public class PanelTitleTests : DeskTestContext
{
    [Fact]
    public void Renders_the_name_as_an_h2_with_the_title_class()
    {
        var cut = RenderComponent<PanelTitle>(p => p.AddChildContent("Mets at Braves"));

        Assert.Equal("Mets at Braves", cut.Find("h2.ptitle__name").TextContent);
    }

    [Fact]
    public void Renders_no_meta_row_without_meta_or_keys()
    {
        var cut = RenderComponent<PanelTitle>(p => p.AddChildContent("Mets at Braves"));

        Assert.Empty(cut.FindAll(".ptitle__meta"));
    }

    [Fact]
    public void Pins_keys_in_their_own_slot_on_the_meta_row()
    {
        var cut = RenderComponent<PanelTitle>(p => p
            .AddChildContent("Mets at Braves")
            .Add(x => x.Detail, "7:10 PM ET")
            .Add(x => x.Keys, "<button>Season</button>"));

        Assert.Equal("7:10 PM ET", cut.Find(".ptitle__meta .ptitle__text").TextContent);
        Assert.Equal("Season", cut.Find(".ptitle__keys button").TextContent);
    }

    [Fact]
    public void Keeps_each_detail_fact_its_own_child()
    {
        // Two facts on the line are two flex items with the row's gap between them, not
        // one run of text — Razor strips the whitespace before a code block, so a
        // sentence-shaped line would read "7:10 PM ETStarters".
        var cut = RenderComponent<PanelTitle>(p => p
            .AddChildContent("Mets at Braves")
            .Add(x => x.Detail, "<span>7:10 PM ET</span><span>Starters Senga v Sale</span>"));

        var facts = cut.FindAll(".ptitle__text > span");

        Assert.Equal(2, facts.Count);
        Assert.Equal("7:10 PM ET", facts[0].TextContent);
    }

    [Fact]
    public void Merges_a_callers_class_and_style()
    {
        // Blazor matches parameters case-insensitively, so a splatted style= *is* the Style
        // parameter and the two can never both arrive; the named parameter is the path
        // worth pinning beside the class splat.
        var cut = RenderComponent<PanelTitle>(p => p
            .AddChildContent("Mets at Braves")
            .Add(x => x.Style, "margin:0")
            .AddUnmatched("class", "x"));

        var root = cut.Find(".ptitle");

        Assert.Contains("x", root.ClassList);
        Assert.Contains("ptitle", root.ClassList);
        Assert.Contains("margin:0", root.GetAttribute("style"));
    }
}
