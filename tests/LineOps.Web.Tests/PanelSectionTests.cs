using Bunit;
using LineOps.Desk.Primitives;

namespace LineOps.Web.Tests;

/// <summary>
/// A stretch of a panel under one rule, spaced by the frame rather than by its caller.
///
/// <para>
/// The rule is still <c>PanelHeader</c>, so nothing looks different; what is new is that
/// the section carries the air between itself and the next, keeps its small keys on the
/// rule's line, and steps its heading rank down when nested, so the outline a screen
/// reader walks is title → section → subsection rather than a flat run of h3s.
/// </para>
/// </summary>
public class PanelSectionTests : DeskTestContext
{
    [Fact]
    public void Renders_the_title_through_PanelHeader_at_h3()
    {
        var cut = RenderComponent<PanelSection>(p => p
            .Add(x => x.Title, "Recent games")
            .AddChildContent("<p>body</p>"));

        Assert.Equal("Recent games", cut.Find("h3.panel-h").TextContent);
        Assert.Equal("body", cut.Find(".psection__body p").TextContent);
    }

    [Fact]
    public void Nested_sections_step_down_a_heading_rank()
    {
        var cut = RenderComponent<PanelSection>(p => p
            .Add(x => x.Title, "Outer")
            .Add<PanelSection>(x => x.ChildContent, child => child
                .Add(y => y.Title, "Inner")
                .AddChildContent("<p>deep</p>")));

        Assert.Equal("Outer", cut.Find("h3.panel-h").TextContent);
        Assert.Equal("Inner", cut.Find("section section h4.panel-h").TextContent);
    }

    [Fact]
    public void Pins_keys_beside_the_rule()
    {
        var cut = RenderComponent<PanelSection>(p => p
            .Add(x => x.Title, "Recent games")
            .Add(x => x.Keys, "<button>Home</button>")
            .AddChildContent("<p>body</p>"));

        Assert.Equal("Home", cut.Find(".psection__head .psection__keys button").TextContent);
    }

    [Fact]
    public void Renders_no_meta_row_without_meta()
    {
        var cut = RenderComponent<PanelSection>(p => p
            .Add(x => x.Title, "Recent games")
            .AddChildContent("<p>body</p>"));

        Assert.Empty(cut.FindAll(".psection__meta"));
    }

    [Fact]
    public void Prefers_TitleContent_over_Title()
    {
        var cut = RenderComponent<PanelSection>(p => p
            .Add(x => x.Title, "ignored")
            .Add(x => x.TitleContent, "<a href=\"#\">Journal</a>")
            .AddChildContent("<p>body</p>"));

        Assert.Equal("Journal", cut.Find("h3.panel-h a").TextContent);
    }

    [Fact]
    public void Merges_a_callers_class_and_style()
    {
        var cut = RenderComponent<PanelSection>(p => p
            .Add(x => x.Title, "Recent games")
            .AddChildContent("<p>body</p>")
            .AddUnmatched("class", "x")
            .AddUnmatched("style", "margin:0"));

        var root = cut.Find(".psection");

        Assert.Contains("x", root.ClassList);
        Assert.Contains("margin:0", root.GetAttribute("style"));
    }
}
