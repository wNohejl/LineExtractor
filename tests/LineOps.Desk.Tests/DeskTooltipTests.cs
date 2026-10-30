using Bunit;
using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace LineOps.Desk.Tests;

/// <summary>
/// The tooltip: a sentence of explanation that appears on hover and on focus, after a pause,
/// beside the thing it explains. It describes its trigger and never renames it — the control it
/// wraps keeps its own accessible name — and its text is a plain string, so a test (and a
/// stylesheet) can reach what the reader will be told without hovering anything.
/// </summary>
public class DeskTooltipTests : DeskTestContext
{
    private IRenderedComponent<DeskTooltip> Tip(
        string text = "Counts every open item.",
        DeskTooltipPlacement placement = DeskTooltipPlacement.Top,
        RenderFragment? child = null)
        => RenderComponent<DeskTooltip>(p => p
            .Add(x => x.Text, text)
            .Add(x => x.Placement, placement)
            .AddChildContent(child ?? (b =>
            {
                b.OpenElement(0, "button");
                b.AddAttribute(1, "type", "button");
                b.AddAttribute(2, "aria-label", "Open items");
                b.AddContent(3, "12");
                b.CloseElement();
            })));

    [Fact]
    public void The_text_is_the_tooltip_s_text()
    {
        var cut = Tip("Counts every open item.");

        Assert.Equal("Counts every open item.", cut.Instance.Text);
        Assert.Equal("Counts every open item.", cut.FindComponent<MudTooltip>().Instance.Text);
    }

    /// <summary>
    /// The text reaches the page once the tooltip is up, and not before. A pointer asks for it
    /// by hovering and the keyboard by focusing; the body arrives through the popover layer the
    /// desk already hosts.
    /// </summary>
    [Theory]
    [InlineData("hover")]
    [InlineData("focus")]
    public void Hover_or_focus_puts_the_text_on_the_page(string how)
    {
        var cut = Render(b =>
        {
            b.OpenComponent<MudPopoverProvider>(0);
            b.CloseComponent();
            b.OpenComponent<DeskTooltip>(1);
            b.AddAttribute(2, nameof(DeskTooltip.Text), "Counts every open item.");
            b.AddAttribute(3, nameof(DeskTooltip.ChildContent), (RenderFragment)(c => c.AddContent(0, "12")));
            b.CloseComponent();
        });

        Assert.DoesNotContain("Counts every open item.", cut.Markup);

        var trigger = cut.Find(".mud-tooltip-root");
        if (how == "hover")
            trigger.PointerEnter();
        else
            trigger.FocusIn();

        cut.WaitForAssertion(() => Assert.Contains("Counts every open item.", cut.Markup));
    }

    [Fact]
    public void The_trigger_keeps_its_own_accessible_name()
    {
        var button = Tip().Find("button");

        Assert.Equal("Open items", button.GetAttribute("aria-label"));
        Assert.Null(button.GetAttribute("title"));
    }

    /// <summary>The HIG's behaviour: hover and keyboard focus both raise it, after a pause, with an arrow at the trigger.</summary>
    [Fact]
    public void It_shows_on_hover_and_on_focus_after_a_pause()
    {
        var mud = Tip().FindComponent<MudTooltip>().Instance;

        Assert.True(mud.ShowOnHover);
        Assert.True(mud.ShowOnFocus);
        Assert.False(mud.ShowOnClick);
        Assert.Equal(300, mud.Delay);
        Assert.True(mud.Arrow);
    }

    [Theory]
    [InlineData(DeskTooltipPlacement.Top, Placement.Top)]
    [InlineData(DeskTooltipPlacement.Bottom, Placement.Bottom)]
    [InlineData(DeskTooltipPlacement.Start, Placement.Start)]
    [InlineData(DeskTooltipPlacement.End, Placement.End)]
    public void Placement_maps_to_the_side_it_names(DeskTooltipPlacement placement, Placement expected)
    {
        Assert.Equal(expected, Tip(placement: placement).FindComponent<MudTooltip>().Instance.Placement);
    }

    [Fact]
    public void Above_is_the_default_side()
    {
        var cut = RenderComponent<DeskTooltip>(p => p.Add(x => x.Text, "A note"));

        Assert.Equal(Placement.Top, cut.FindComponent<MudTooltip>().Instance.Placement);
    }
}
