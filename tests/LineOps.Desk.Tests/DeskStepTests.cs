using Bunit;
using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components;

namespace LineOps.Desk.Tests;

/// <summary>
/// One step of a vertical stepper, in an ordered list named for the sequence. Each state says
/// something different: a current step is marked as the current one and shows its guidance and
/// controls; a done step folds to its title and what was chosen, which is the link that opens it
/// again; a step still to do says on one line what it is waiting for, so an unreachable control
/// never sits on screen looking broken.
/// </summary>
public class DeskStepTests : DeskTestContext
{
    private static readonly RenderFragment Controls = b =>
    {
        b.OpenElement(0, "input");
        b.AddAttribute(1, "class", "controls");
        b.CloseElement();
    };

    private static readonly RenderFragment Chosen = b => b.AddContent(0, "Large · two items");

    private IRenderedComponent<DeskStep> Step(DeskStepState state, bool? open = null, EventCallback onOpen = default)
        => RenderComponent<DeskStep>(p => p
            .Add(x => x.Number, 2)
            .Add(x => x.Title, "Size and count")
            .Add(x => x.State, state)
            .Add(x => x.Hint, "Pick a size, then say how many.")
            .Add(x => x.Waiting, "Pick an item first.")
            .Add(x => x.Summary, Chosen)
            .Add(x => x.Open, open)
            .Add(x => x.OnOpen, onOpen)
            .Add(x => x.ChildContent, Controls));

    [Fact]
    public void Steps_sit_in_an_ordered_list_named_for_the_sequence()
    {
        var cut = RenderComponent<DeskSteps>(p => p
            .Add(x => x.Label, "Place an order")
            .AddChildContent<DeskStep>(s => s.Add(x => x.Number, 1).Add(x => x.Title, "Item").Add(x => x.State, DeskStepState.Done))
            .AddChildContent<DeskStep>(s => s.Add(x => x.Number, 2).Add(x => x.Title, "Size").Add(x => x.State, DeskStepState.Current)));

        var list = cut.Find("ol.wsteps");

        Assert.Equal("Place an order", list.GetAttribute("aria-label"));
        Assert.Equal(2, list.QuerySelectorAll("li.wstep").Length);
    }

    [Theory]
    [InlineData(DeskStepState.Todo, "wstep--todo")]
    [InlineData(DeskStepState.Current, "wstep--current")]
    [InlineData(DeskStepState.Done, "wstep--done")]
    public void Each_state_has_its_class(DeskStepState state, string expected)
    {
        Assert.Contains(expected, Step(state).Find("li").ClassList);
    }

    [Fact]
    public void The_step_is_named_by_its_title()
    {
        var cut = Step(DeskStepState.Current);

        var title = cut.Find("h3.wstep__title");

        Assert.Equal("Size and count", title.TextContent);
        Assert.Equal(title.Id, cut.Find("li").GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void Only_the_current_step_is_marked_current()
    {
        Assert.Equal("step", Step(DeskStepState.Current).Find("li").GetAttribute("aria-current"));
        Assert.Null(Step(DeskStepState.Done).Find("li").GetAttribute("aria-current"));
        Assert.Null(Step(DeskStepState.Todo).Find("li").GetAttribute("aria-current"));
    }

    [Fact]
    public void The_current_step_is_open_with_its_guidance_and_its_controls()
    {
        var cut = Step(DeskStepState.Current);

        Assert.Contains("wstep--open", cut.Find("li").ClassList);
        Assert.Equal("Pick a size, then say how many.", cut.Find(".wstep__hint").TextContent);
        Assert.Single(cut.FindAll(".wstep__body input.controls"));
        Assert.Equal("2", cut.Find(".wstep__dot").TextContent.Trim());
    }

    [Fact]
    public void A_done_step_folds_to_a_check_and_what_was_chosen()
    {
        var cut = Step(DeskStepState.Done);

        Assert.DoesNotContain("wstep--open", cut.Find("li").ClassList);
        Assert.Empty(cut.FindAll(".wstep__body"));
        Assert.Equal("Large · two items", cut.Find(".wstep__summary").TextContent);
        Assert.NotNull(cut.Find(".wstep__dot").QuerySelector("svg"));
        Assert.Empty(cut.FindAll(".wstep__hint"));
    }

    /// <summary>What was chosen is the way back to it: with OnOpen, the summary is a link that opens the step.</summary>
    [Fact]
    public void A_done_step_s_summary_opens_it_again()
    {
        var opened = 0;

        var cut = Step(DeskStepState.Done, onOpen: EventCallback.Factory.Create(this, () => opened++));

        var link = cut.Find("button.wstep__change");
        Assert.Equal("Change: Size and count", link.GetAttribute("title"));

        link.Click();

        Assert.Equal(1, opened);
    }

    [Fact]
    public void Without_OnOpen_the_summary_is_text_not_a_link()
    {
        var cut = Step(DeskStepState.Done);

        Assert.Empty(cut.FindAll("button"));
        Assert.Single(cut.FindAll(".wstep__summary"));
    }

    [Fact]
    public void A_step_to_do_says_what_it_is_waiting_for_and_shows_no_controls()
    {
        var cut = Step(DeskStepState.Todo);

        Assert.Equal("Pick an item first.", cut.Find(".wstep__wait").TextContent);
        Assert.Empty(cut.FindAll(".wstep__body"));
        Assert.Empty(cut.FindAll(".wstep__summary"));
    }

    /// <summary>The host may hold a step open that is not current — an amount typed beside its review.</summary>
    [Fact]
    public void Open_overrides_the_state_s_own_choice()
    {
        var done = Step(DeskStepState.Done, open: true);
        Assert.Single(done.FindAll(".wstep__body input.controls"));
        Assert.Empty(done.FindAll(".wstep__summary"));

        var current = Step(DeskStepState.Current, open: false);
        Assert.Empty(current.FindAll(".wstep__body"));
    }
}
