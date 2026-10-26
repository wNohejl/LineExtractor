using Bunit;
using LineOps.Desk.Primitives;
using LineOps.Reliability;
using LineOps.Web.Components.Desk;
using Microsoft.AspNetCore.Components;

namespace LineOps.Web.Tests;

/// <summary>
/// The disclosure: a leading triangle that says whether the details are showing, on a summary
/// that announces it. The rotation is CSS; what is pinned is the state, the roles and the
/// binding.
/// </summary>
public class DeskDisclosureTests : DeskTestContext
{
    private static RenderFragment Text(string text) => b => b.AddContent(0, text);

    [Fact]
    public void Renders_details_with_a_leading_triangle_and_the_state()
    {
        var cut = RenderComponent<DeskDisclosure>(p => p
            .Add(x => x.Label, "Runbook for freshness")
            .Add(x => x.ChildContent, Text("Check the poller.")));

        var details = cut.Find("details.disclosure");
        Assert.False(details.HasAttribute("open"));

        var summary = cut.Find("summary.disclosure__summary");
        Assert.Equal("false", summary.GetAttribute("aria-expanded"));

        // The triangle leads, before the label.
        var first = summary.FirstElementChild!;
        Assert.Contains("disclosure__triangle", first.ClassList);
        Assert.Equal("true", first.GetAttribute("aria-hidden"));
        Assert.NotNull(first.QuerySelector("svg"));
        Assert.Equal("Runbook for freshness", summary.QuerySelector(".disclosure__label")!.TextContent.Trim());
    }

    [Fact]
    public void Pressing_the_summary_opens_and_closes_it()
    {
        var cut = RenderComponent<DeskDisclosure>(p => p
            .Add(x => x.Label, "Details")
            .Add(x => x.ChildContent, Text("Body")));

        cut.Find("summary").Click();
        Assert.True(cut.Find("details").HasAttribute("open"));
        Assert.Equal("true", cut.Find("summary").GetAttribute("aria-expanded"));
        Assert.Contains("Body", cut.Find(".disclosure__content").TextContent);

        cut.Find("summary").Click();
        Assert.False(cut.Find("details").HasAttribute("open"));
        Assert.Equal("false", cut.Find("summary").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Open_starts_it_open_and_a_bound_owner_hears_the_change()
    {
        var seen = new List<bool>();
        var cut = RenderComponent<DeskDisclosure>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.OpenChanged, (bool open) => seen.Add(open))
            .Add(x => x.Summary, Text("Details")));

        Assert.True(cut.Find("details").HasAttribute("open"));
        Assert.Equal("Details", cut.Find(".disclosure__label").TextContent.Trim());

        cut.Find("summary").Click();
        Assert.Equal([false], seen);
    }

    [Fact]
    public void An_owner_changing_Open_moves_it()
    {
        var cut = RenderComponent<DeskDisclosure>(p => p
            .Add(x => x.Label, "Details")
            .Add(x => x.Open, true));

        cut.SetParametersAndRender(p => p.Add(x => x.Open, false));

        Assert.False(cut.Find("details").HasAttribute("open"));
    }

    [Fact]
    public void Class_and_unmatched_attributes_pass_through()
    {
        var cut = RenderComponent<DeskDisclosure>(p => p
            .Add(x => x.Label, "Details")
            .Add(x => x.Class, "runbook")
            .AddUnmatched("data-test", "d1"));

        var details = cut.Find("details");
        Assert.Contains("disclosure", details.ClassList);
        Assert.Contains("runbook", details.ClassList);
        Assert.Equal("d1", details.GetAttribute("data-test"));
    }

    /// <summary>The runbook said nothing about whether it was open; its marker was hidden behind a book glyph.</summary>
    [Fact]
    public void The_runbook_is_a_disclosure_that_shows_its_state()
    {
        var key = Runbook.All.Keys.First();

        var cut = RenderComponent<RunbookSteps>(p => p
            .Add(x => x.RuleKey, key)
            .Add(x => x.Open, true));

        var details = cut.Find("details.disclosure");
        Assert.Contains("runbook", details.ClassList);
        Assert.True(details.HasAttribute("open"));

        var summary = cut.Find("summary");
        Assert.Equal("true", summary.GetAttribute("aria-expanded"));
        Assert.NotNull(summary.QuerySelector(".disclosure__triangle"));
        Assert.Contains(Runbook.Find(key)!.Heading, summary.TextContent);
        Assert.NotNull(cut.Find(".disclosure__content .runbook__body"));

        cut.Find("summary").Click();
        Assert.False(cut.Find("details").HasAttribute("open"));
    }
}
