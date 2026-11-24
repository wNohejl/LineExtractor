using Bunit;
using XLines.Desk.Primitives;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace XLines.Desk.Tests;

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

    // ---- The structured shape -----------------------------------------------------------------

    private static readonly DeskTooltipRow[] Regions =
    [
        new("North", "+185", "−1.2% margin", Emphasis: true),
        new("South", "+181", "−2.6% margin"),
    ];

    private IRenderedFragment Structured(string text = "")
        => Render(b =>
        {
            b.OpenComponent<MudPopoverProvider>(0);
            b.CloseComponent();
            b.OpenComponent<DeskTooltip>(1);
            b.AddAttribute(2, nameof(DeskTooltip.Title), "Best reading");
            b.AddAttribute(3, nameof(DeskTooltip.Caption), "Taken an hour ago.\nAgainst the reference.");
            b.AddAttribute(4, nameof(DeskTooltip.Rows), (IReadOnlyList<DeskTooltipRow>)Regions);
            b.AddAttribute(5, nameof(DeskTooltip.Footnote), "+3 more in Details");
            b.AddAttribute(6, nameof(DeskTooltip.Text), text);
            b.AddAttribute(7, nameof(DeskTooltip.ChildContent), (RenderFragment)(c => c.AddContent(0, "12")));
            b.CloseComponent();
        });

    private static void Raise(IRenderedFragment cut)
    {
        cut.Find(".mud-tooltip-root").PointerEnter();
        cut.WaitForAssertion(() => cut.Find(".desk-tip"));
    }

    [Fact]
    public void Text_alone_is_the_plain_sentence_it_always_was()
    {
        var cut = Tip("Counts every open item.");

        Assert.False(cut.Instance.IsStructured);
        Assert.Null(cut.FindComponent<MudTooltip>().Instance.TooltipContent);
    }

    [Fact]
    public void The_structured_shape_draws_a_title_a_caption_rows_and_a_footnote()
    {
        var cut = Structured();
        Raise(cut);

        Assert.Equal("Best reading", cut.Find(".desk-tip__title").TextContent);
        Assert.Equal("Taken an hour ago.\nAgainst the reference.", cut.Find(".desk-tip__caption").TextContent);
        Assert.Equal("+3 more in Details", cut.Find(".desk-tip__footnote").TextContent);

        var rows = cut.FindAll(".desk-tip__row");
        Assert.Equal(2, rows.Count);
        Assert.Equal("North", rows[0].QuerySelector(".desk-tip__label")!.TextContent);
        Assert.Equal("+185", rows[0].QuerySelector(".desk-tip__value")!.TextContent);
        Assert.Equal("−1.2% margin", rows[0].QuerySelector(".desk-tip__qualifier")!.TextContent);
    }

    /// <summary>The row the tooltip is about is marked by a class the sheet draws as weight, not by a hue of its own.</summary>
    [Fact]
    public void The_emphasised_row_is_marked_and_the_others_are_not()
    {
        var cut = Structured();
        Raise(cut);

        var rows = cut.FindAll(".desk-tip__row");
        Assert.Contains("desk-tip__row--emphasis", rows[0].ClassName);
        Assert.DoesNotContain("desk-tip__row--emphasis", rows[1].ClassName);
    }

    /// <summary>
    /// The drawn table is hidden from the accessibility tree and the plain text stands beside
    /// it, so a screen reader hears one clean reading rather than a grid's cells out of order.
    /// </summary>
    [Fact]
    public void A_screen_reader_hears_the_parts_as_plain_lines()
    {
        var cut = Structured();
        Raise(cut);

        Assert.Equal("true", cut.Find(".desk-tip__face").GetAttribute("aria-hidden"));
        Assert.Equal(
            "Best reading\nTaken an hour ago.\nAgainst the reference.\nNorth +185, −1.2% margin\nSouth +181, −2.6% margin\n+3 more in Details",
            cut.Find(".desk-tip > .sr-only").TextContent);
    }

    [Fact]
    public void Text_given_is_what_a_screen_reader_hears_instead()
    {
        var cut = Structured("North leads at +185.");
        Raise(cut);

        Assert.Equal("North leads at +185.", cut.Find(".desk-tip > .sr-only").TextContent);
    }

    [Fact]
    public void The_structured_shape_keeps_the_trigger_s_behaviour()
    {
        var cut = RenderComponent<DeskTooltip>(p => p.Add(x => x.Title, "A title"));
        var mud = cut.FindComponent<MudTooltip>().Instance;

        Assert.True(cut.Instance.IsStructured);
        Assert.True(mud.ShowOnHover);
        Assert.True(mud.ShowOnFocus);
        Assert.Equal(300, mud.Delay);
        Assert.True(mud.Arrow);
    }
}
