using Bunit;
using LineOps.Desk.Primitives;

namespace LineOps.Web.Tests;

/// <summary>
/// The one row of controls that scope or act on a whole panel.
///
/// <para>
/// Pickers and search are the content; a message and the actions are slots. The actions
/// slot is where the panel's single Filled key lives in every window, pinned to the right
/// edge, and it is not drawn at all when a panel has nothing to put there — a visitor's Ops
/// window renders the pickers with no empty box after them.
/// </para>
/// </summary>
public class PanelToolbarTests : DeskTestContext
{
    [Fact]
    public void Omits_the_actions_slot_when_none_is_given()
    {
        var cut = RenderComponent<PanelToolbar>(p => p.AddChildContent("<select></select>"));

        Assert.Empty(cut.FindAll(".ptoolbar__actions"));
    }

    [Fact]
    public void Renders_actions_in_the_right_pinned_slot()
    {
        var cut = RenderComponent<PanelToolbar>(p => p
            .AddChildContent("<select></select>")
            .Add(x => x.Actions, "<button>Sync</button>"));

        Assert.Equal("Sync", cut.Find(".ptoolbar__actions button").TextContent);
    }

    [Fact]
    public void Renders_the_message_between_content_and_actions()
    {
        var cut = RenderComponent<PanelToolbar>(p => p
            .AddChildContent("<select></select>")
            .Add(x => x.Message, "Settled 3")
            .Add(x => x.Actions, "<button>Sync</button>"));

        var children = cut.Find(".ptoolbar").Children;

        Assert.Equal("SELECT", children[0].TagName);
        Assert.Contains("ptoolbar__msg", children[1].ClassList);
        Assert.Equal("Settled 3", children[1].TextContent);
        Assert.Contains("ptoolbar__actions", children[2].ClassList);
    }

    [Fact]
    public void Claims_no_aria_role()
    {
        // The ARIA toolbar pattern promises one tab stop with arrow keys between the
        // controls; these are ordinary controls, so the row must not announce otherwise.
        var cut = RenderComponent<PanelToolbar>(p => p.AddChildContent("<select></select>"));

        Assert.Null(cut.Find(".ptoolbar").GetAttribute("role"));
    }

    [Fact]
    public void Merges_a_callers_class_and_style()
    {
        var cut = RenderComponent<PanelToolbar>(p => p
            .AddChildContent("<select></select>")
            .AddUnmatched("class", "x")
            .AddUnmatched("style", "margin:0"));

        var root = cut.Find(".ptoolbar");

        Assert.Contains("x", root.ClassList);
        Assert.Contains("margin:0", root.GetAttribute("style"));
    }
}
