using Bunit;
using LineOps.Desk.Primitives;

namespace LineOps.Desk.Tests;

/// <summary>
/// The stack: <c>.stack</c> with its gap named — the vertical counterpart of the row, on the
/// same ramp. Unset, it is the bare <c>.stack</c> it replaces; a stack stretches its children by
/// default, so only the other alignments earn a class. A caller's class and style are added to
/// the stack's own, never substituted for them.
/// </summary>
public class DeskStackTests : DeskTestContext
{
    [Fact]
    public void A_stack_that_sets_nothing_is_a_bare_stack()
    {
        var stack = RenderComponent<DeskStack>(p => p.AddChildContent("<p>One</p><p>Two</p>")).Find("div");

        Assert.Equal("stack", stack.GetAttribute("class"));
        Assert.Null(stack.GetAttribute("style"));
        Assert.Equal(2, stack.Children.Length);
    }

    [Theory]
    [InlineData(DeskSpace.Space2, "--desk-gap:var(--space-2)")]
    [InlineData(DeskSpace.Space6, "--desk-gap:var(--space-6)")]
    [InlineData(DeskSpace.Space12, "--desk-gap:var(--space-12)")]
    public void The_gap_is_a_rung_of_the_ramp(DeskSpace gap, string expected)
    {
        Assert.Equal(expected, RenderComponent<DeskStack>(p => p.Add(x => x.Gap, gap)).Find("div").GetAttribute("style"));
    }

    [Theory]
    [InlineData(DeskAlign.Start, "stack--start")]
    [InlineData(DeskAlign.Center, "stack--center")]
    [InlineData(DeskAlign.End, "stack--end")]
    public void Align_maps_to_its_class(DeskAlign align, string expected)
    {
        Assert.Contains(expected, RenderComponent<DeskStack>(p => p.Add(x => x.Align, align)).Find("div").ClassList);
    }

    [Fact]
    public void Stretch_is_the_stack_s_own_behaviour_and_earns_no_class()
    {
        Assert.Equal("stack", RenderComponent<DeskStack>(p => p.Add(x => x.Align, DeskAlign.Stretch)).Find("div").GetAttribute("class"));
    }

    [Fact]
    public void A_callers_class_and_style_are_added_to_the_stack_s_own()
    {
        var stack = RenderComponent<DeskStack>(p => p
            .Add(x => x.Gap, DeskSpace.Space3)
            .Add(x => x.Class, "named")
            .AddUnmatched("style", "padding:0")
            .AddUnmatched("data-test", "s1")).Find("div");

        Assert.Equal("--desk-gap:var(--space-3); padding:0", stack.GetAttribute("style"));
        Assert.Equal(["stack", "named"], stack.ClassList);
        Assert.Equal("s1", stack.GetAttribute("data-test"));
    }
}
