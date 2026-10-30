using Bunit;
using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components.Web;

namespace LineOps.Desk.Tests;

/// <summary>
/// The stepper: one capsule of two halves that nudges a value held by the field beside it.
/// It never shows the value itself, and a half that would cross a bound is disabled rather
/// than silently doing nothing.
/// </summary>
public class DeskStepperTests : DeskTestContext
{
    private IRenderedComponent<DeskStepper> Stepper(int value, Action<int>? changed = null, int min = 1, int max = 12, int step = 1)
        => RenderComponent<DeskStepper>(p => p
            .Add(x => x.Label, "Concurrent windows")
            .Add(x => x.Value, value)
            .Add(x => x.Min, min)
            .Add(x => x.Max, max)
            .Add(x => x.Step, step)
            .Add(x => x.ValueChanged, (int n) => changed?.Invoke(n)));

    [Fact]
    public void Renders_one_capsule_of_two_labelled_halves()
    {
        var cut = Stepper(4);

        var capsule = cut.Find(".stepper");
        Assert.Equal("group", capsule.GetAttribute("role"));
        Assert.Equal("Concurrent windows", capsule.GetAttribute("aria-label"));

        var halves = capsule.QuerySelectorAll("button.stepper__half");
        Assert.Equal(2, halves.Length);
        Assert.Equal("Concurrent windows: one fewer", halves[0].GetAttribute("aria-label"));
        Assert.Equal("Concurrent windows: one more", halves[1].GetAttribute("aria-label"));
        Assert.All(halves, h => Assert.Equal("button", h.GetAttribute("type")));

        // It sits beside the field that shows the value; it does not show it again.
        Assert.DoesNotContain("4", capsule.TextContent);
    }

    [Fact]
    public void Halves_step_the_value_by_step()
    {
        var seen = new List<int>();
        var cut = Stepper(6, seen.Add, step: 2);

        cut.Find("[aria-label='Concurrent windows: one more']").Click();
        cut.Find("[aria-label='Concurrent windows: one fewer']").Click();

        Assert.Equal([8, 4], seen);
    }

    [Fact]
    public void A_step_past_a_bound_stops_at_the_bound()
    {
        var seen = new List<int>();
        var cut = Stepper(11, seen.Add, step: 5);

        cut.Find("[aria-label='Concurrent windows: one more']").Click();

        Assert.Equal([12], seen);
    }

    [Fact]
    public void Halves_are_disabled_at_the_bounds()
    {
        var low = Stepper(1);
        Assert.True(low.Find("[aria-label='Concurrent windows: one fewer']").HasAttribute("disabled"));
        Assert.False(low.Find("[aria-label='Concurrent windows: one more']").HasAttribute("disabled"));

        var high = Stepper(12);
        Assert.False(high.Find("[aria-label='Concurrent windows: one fewer']").HasAttribute("disabled"));
        Assert.True(high.Find("[aria-label='Concurrent windows: one more']").HasAttribute("disabled"));
    }

    [Fact]
    public void Disabled_disables_both_halves()
    {
        var cut = RenderComponent<DeskStepper>(p => p
            .Add(x => x.Label, "Count")
            .Add(x => x.Value, 5)
            .Add(x => x.Min, 1)
            .Add(x => x.Max, 9)
            .Add(x => x.Disabled, true));

        Assert.All(cut.FindAll("button"), b => Assert.True(b.HasAttribute("disabled")));
    }

    [Fact]
    public void Class_and_unmatched_attributes_pass_through()
    {
        var cut = RenderComponent<DeskStepper>(p => p
            .Add(x => x.Label, "Count")
            .Add(x => x.Max, 9)
            .Add(x => x.Class, "extra")
            .AddUnmatched("data-test", "s1"));

        var capsule = cut.Find(".stepper");
        Assert.Contains("extra", capsule.ClassList);
        Assert.Equal("s1", capsule.GetAttribute("data-test"));
    }

    /// <summary>
    /// The keyboard's way in: the capsule is one Tab stop of its own, and arrows nudge the value
    /// as the halves do — up and right for more, down and left for fewer — while Home and End
    /// reach the bounds.
    /// </summary>
    [Theory]
    [InlineData("ArrowUp", 6)]
    [InlineData("ArrowRight", 6)]
    [InlineData("ArrowDown", 4)]
    [InlineData("ArrowLeft", 4)]
    [InlineData("Home", 1)]
    [InlineData("End", 12)]
    public void Keys_nudge_the_value(string key, int expected)
    {
        var seen = new List<int>();
        var cut = Stepper(5, seen.Add);

        cut.Find(".stepper").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Equal([expected], seen);
    }

    /// <summary>
    /// One stop, not three: the halves leave the Tab order, so the half under focus cannot
    /// disable itself at a bound and drop the keyboard on the page.
    /// </summary>
    [Fact]
    public void The_capsule_is_the_one_tab_stop()
    {
        var capsule = Stepper(5).Find(".stepper");

        Assert.Equal("0", capsule.GetAttribute("tabindex"));
        Assert.All(capsule.QuerySelectorAll("button.stepper__half"), h => Assert.Equal("-1", h.GetAttribute("tabindex")));
    }

    [Fact]
    public void A_key_past_a_bound_stops_at_the_bound_and_one_at_the_bound_says_nothing()
    {
        var seen = new List<int>();

        Stepper(11, seen.Add, step: 5).Find(".stepper").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        Stepper(12, seen.Add).Find(".stepper").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Stepper(1, seen.Add).Find(".stepper").KeyDown(new KeyboardEventArgs { Key = "Home" });

        Assert.Equal([12], seen);
    }

    [Fact]
    public void A_disabled_stepper_ignores_its_keys_and_leaves_the_tab_order()
    {
        var seen = new List<int>();

        var cut = RenderComponent<DeskStepper>(p => p
            .Add(x => x.Label, "Count")
            .Add(x => x.Value, 5)
            .Add(x => x.Min, 1)
            .Add(x => x.Max, 9)
            .Add(x => x.Disabled, true)
            .Add(x => x.ValueChanged, (int n) => seen.Add(n)));

        cut.Find(".stepper").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        Assert.Empty(seen);
        Assert.Null(cut.Find(".stepper").GetAttribute("tabindex"));
    }

    [Fact]
    public void An_unhandled_key_is_left_alone()
    {
        var seen = new List<int>();

        Stepper(5, seen.Add).Find(".stepper").KeyDown(new KeyboardEventArgs { Key = "a" });

        Assert.Empty(seen);
    }
}
