using Bunit;
using LineOps.Web.Components.Desk;

namespace LineOps.Web.Tests;

/// <summary>
/// The line the desk shows when there is nothing to show, and the one step out of it.
///
/// <para>
/// <c>Action</c> had been a parameter no call site ever filled: four panels named the window
/// the reader needed in prose and left them to find it on the rail. It is load-bearing now,
/// so the slot is pinned — that it renders inside <c>.empty__action</c> rather than running
/// into the sentence, that it is optional, and that a kind still reaches the paragraph when
/// one is present.
/// </para>
/// </summary>
public class EmptyStateTests : DeskTestContext
{
    [Fact]
    public void Renders_the_action_in_its_own_span()
    {
        var cut = RenderComponent<EmptyState>(p => p
            .Add(x => x.ChildContent, "No settled entries yet.")
            .Add(x => x.Action, "<button>Open the Journal</button>"));

        var action = cut.Find(".empty__action");

        Assert.Equal("Open the Journal", action.TextContent);
    }

    [Fact]
    public void Renders_no_action_span_when_there_is_no_action()
    {
        var cut = RenderComponent<EmptyState>(p => p.AddChildContent("Nothing here."));

        Assert.Empty(cut.FindAll(".empty__action"));
    }

    [Fact]
    public void Keeps_its_kind_on_the_paragraph_when_an_action_is_present()
    {
        var cut = RenderComponent<EmptyState>(p => p
            .Add(x => x.Kind, EmptyStateKind.New)
            .Add(x => x.ChildContent, "Nothing yet.")
            .Add(x => x.Action, "<button>Start</button>"));

        var paragraph = cut.Find("p");

        Assert.Contains("empty", paragraph.ClassList);
        Assert.Contains("empty--new", paragraph.ClassList);
    }
}
