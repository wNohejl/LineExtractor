using Bunit;
using LineOps.Desk.Primitives;

namespace LineOps.Web.Tests;

/// <summary>
/// The inset card for a task the operator completes in place, and the one footer order.
///
/// <para>
/// Journal put Save before Cancel; Incidents put Resolve before Mark mitigated; History
/// put the destructive re-walk after the primary. The card pins the order: anything
/// destructive at the far left, then Cancel, then the primary at the right edge — and the
/// footer is not drawn at all when there is nothing to put in it.
/// </para>
/// </summary>
public class PanelFormTests : DeskTestContext
{
    [Fact]
    public void Renders_the_title_as_a_tight_h4_rule()
    {
        var cut = RenderComponent<PanelForm>(p => p
            .Add(x => x.Title, "New entry")
            .AddChildContent("<input />"));

        var rule = cut.Find("h4.panel-h");

        Assert.Equal("New entry", rule.TextContent);
        Assert.Contains("margin:0", rule.GetAttribute("style"));
    }

    [Fact]
    public void Footer_pins_right_without_a_destructive_slot()
    {
        var cut = RenderComponent<PanelForm>(p => p
            .AddChildContent("<input />")
            .Add(x => x.Footer, "<button>Cancel</button><button>Save</button>"));

        Assert.Empty(cut.FindAll(".pform__danger"));
        Assert.Equal(2, cut.FindAll(".pform__keys button").Count);
    }

    [Fact]
    public void Destructive_sits_before_the_keys()
    {
        var cut = RenderComponent<PanelForm>(p => p
            .AddChildContent("<input />")
            .Add(x => x.Destructive, "<button>Delete</button>")
            .Add(x => x.Footer, "<button>Save</button>"));

        var foot = cut.Find(".pform__foot").Children;

        Assert.Contains("pform__danger", foot[0].ClassList);
        Assert.Contains("pform__keys", foot[1].ClassList);
    }

    [Fact]
    public void Renders_no_footer_without_footer_or_destructive()
    {
        var cut = RenderComponent<PanelForm>(p => p.AddChildContent("<input />"));

        Assert.Empty(cut.FindAll(".pform__foot"));
    }

    [Fact]
    public void Forwards_Id_to_the_root()
    {
        var cut = RenderComponent<PanelForm>(p => p
            .Add(x => x.Id, "wager-form")
            .AddChildContent("<input />"));

        Assert.Equal("wager-form", cut.Find(".pform").Id);
    }

    [Fact]
    public void Merges_a_callers_class_and_style()
    {
        var cut = RenderComponent<PanelForm>(p => p
            .AddChildContent("<input />")
            .AddUnmatched("class", "x")
            .AddUnmatched("style", "margin:0"));

        var root = cut.Find(".pform");

        Assert.Contains("x", root.ClassList);
        Assert.Contains("margin:0", root.GetAttribute("style"));
    }
}
