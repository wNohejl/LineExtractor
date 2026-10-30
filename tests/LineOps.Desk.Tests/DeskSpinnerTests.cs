using Bunit;
using LineOps.Desk.Primitives;

namespace LineOps.Desk.Tests;

/// <summary>
/// The spinner: work under way whose length cannot be known. It is a named status, so a reader
/// hears what is happening, and the ring that turns is decoration a reader never meets.
/// </summary>
public class DeskSpinnerTests : DeskTestContext
{
    [Fact]
    public void Is_a_status_with_a_name()
    {
        var spinner = RenderComponent<DeskSpinner>().Find(".spinner");

        Assert.Equal("status", spinner.GetAttribute("role"));
        Assert.Equal("Loading", spinner.GetAttribute("aria-label"));
        Assert.Equal("true", spinner.QuerySelector(".spinner__ring")!.GetAttribute("aria-hidden"));
    }

    [Fact]
    public void The_name_says_what_is_under_way_when_the_caller_knows()
    {
        var cut = RenderComponent<DeskSpinner>(p => p.Add(x => x.Label, "Fetching items"));

        Assert.Equal("Fetching items", cut.Find("[role=status]").GetAttribute("aria-label"));
    }

    [Theory]
    [InlineData(DeskKeySize.Small, "spinner--sm")]
    [InlineData(DeskKeySize.Medium, "spinner--md")]
    public void Small_and_medium_sizes(DeskKeySize size, string expected)
    {
        var cut = RenderComponent<DeskSpinner>(p => p.Add(x => x.Size, size));

        Assert.Contains(expected, cut.Find(".spinner").GetAttribute("class"));
    }

    [Fact]
    public void Medium_is_the_default()
    {
        Assert.Contains("spinner--md", RenderComponent<DeskSpinner>().Find(".spinner").GetAttribute("class"));
    }

    [Fact]
    public void A_one_off_class_and_extra_attributes_join_it()
    {
        var cut = RenderComponent<DeskSpinner>(p => p
            .Add(x => x.Class, "panel__spinner")
            .AddUnmatched("data-testid", "busy"));

        var spinner = cut.Find(".spinner");

        Assert.Contains("panel__spinner", spinner.GetAttribute("class"));
        Assert.Equal("busy", spinner.GetAttribute("data-testid"));
    }
}
