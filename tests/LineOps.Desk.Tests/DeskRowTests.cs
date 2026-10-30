using Bunit;
using LineOps.Desk.Primitives;

namespace LineOps.Desk.Tests;

/// <summary>
/// The row: <c>.row</c> with its knobs named. The gap is a rung of the spacing ramp rather than
/// a number of pixels, alignment and distribution are classes, and a row that sets neither
/// renders exactly the bare <c>.row</c> it replaces. A caller's class and style are added to the
/// row's own, never substituted for them.
/// </summary>
public class DeskRowTests : DeskTestContext
{
    [Fact]
    public void A_row_that_sets_nothing_is_a_bare_row()
    {
        var row = RenderComponent<DeskRow>(p => p.AddChildContent("<span>One</span><span>Two</span>")).Find("div");

        Assert.Equal("row", row.GetAttribute("class"));
        Assert.Null(row.GetAttribute("style"));
        Assert.Equal(2, row.Children.Length);
    }

    [Theory]
    [InlineData(DeskSpace.None, "--desk-gap:0")]
    [InlineData(DeskSpace.Space1, "--desk-gap:var(--space-1)")]
    [InlineData(DeskSpace.Space3, "--desk-gap:var(--space-3)")]
    [InlineData(DeskSpace.Space16, "--desk-gap:var(--space-16)")]
    public void The_gap_is_a_rung_of_the_ramp(DeskSpace gap, string expected)
    {
        Assert.Equal(expected, RenderComponent<DeskRow>(p => p.Add(x => x.Gap, gap)).Find("div").GetAttribute("style"));
    }

    [Theory]
    [InlineData(DeskJustify.End, "row--end")]
    [InlineData(DeskJustify.Center, "row--center")]
    [InlineData(DeskJustify.Between, "row--between")]
    public void Justify_maps_to_its_class(DeskJustify justify, string expected)
    {
        Assert.Contains(expected, RenderComponent<DeskRow>(p => p.Add(x => x.Justify, justify)).Find("div").ClassList);
    }

    [Theory]
    [InlineData(DeskAlign.Start, "row--top")]
    [InlineData(DeskAlign.End, "row--bottom")]
    [InlineData(DeskAlign.Stretch, "row--stretch")]
    [InlineData(DeskAlign.Baseline, "row--baseline")]
    public void Align_maps_to_its_class(DeskAlign align, string expected)
    {
        Assert.Contains(expected, RenderComponent<DeskRow>(p => p.Add(x => x.Align, align)).Find("div").ClassList);
    }

    [Fact]
    public void NoWrap_keeps_the_row_on_one_line()
    {
        Assert.Contains("row--nowrap", RenderComponent<DeskRow>(p => p.Add(x => x.NoWrap, true)).Find("div").ClassList);
    }

    /// <summary>
    /// The gap is a custom property, so a caller's style is appended after it rather than
    /// replacing it, and a caller's class comes after the row's layout classes rather than in
    /// place of them.
    /// </summary>
    [Fact]
    public void A_callers_class_and_style_are_added_to_the_row_s_own()
    {
        var row = RenderComponent<DeskRow>(p => p
            .Add(x => x.Gap, DeskSpace.Space4)
            .Add(x => x.Justify, DeskJustify.End)
            .Add(x => x.Class, "named")
            .AddUnmatched("style", "margin:0")
            .AddUnmatched("data-test", "r1")).Find("div");

        Assert.Equal("--desk-gap:var(--space-4); margin:0", row.GetAttribute("style"));
        Assert.Equal(["row", "row--end", "named"], row.ClassList);
        Assert.Equal("r1", row.GetAttribute("data-test"));
    }
}
