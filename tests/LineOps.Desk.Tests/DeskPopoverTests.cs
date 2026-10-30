using Bunit;
using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Desk.Tests;

/// <summary>
/// The popover: a panel anchored to the control that opened it, with no scrim. The owner holds
/// whether it is open; the popover holds the consequences — the opener always on the page, the
/// panel only while open, and one popover at a time, so opening a second puts the first away in
/// the same click. The browser half (outside press, edge flip, focus return) is popover.js and
/// is not run here.
/// </summary>
public class DeskPopoverTests : DeskTestContext
{
    private static readonly RenderFragment Opener = b =>
    {
        b.OpenElement(0, "button");
        b.AddAttribute(1, "type", "button");
        b.AddAttribute(2, "class", "opener");
        b.AddAttribute(3, "data-popover-return", "true");
        b.AddContent(4, "More");
        b.CloseElement();
    };

    private IRenderedComponent<DeskPopover> Popover(bool open, Action<bool>? changed = null, DeskPopoverAlign align = DeskPopoverAlign.Start)
        => RenderComponent<DeskPopover>(p => p
            .Add(x => x.Open, open)
            .Add(x => x.OpenChanged, (bool v) => changed?.Invoke(v))
            .Add(x => x.Align, align)
            .Add(x => x.Anchor, Opener)
            .AddChildContent("<p class=\"inside\">Two items</p>"));

    [Fact]
    public void Closed_renders_the_opener_and_no_panel()
    {
        var cut = Popover(open: false);

        Assert.Single(cut.FindAll("button.opener"));
        Assert.Empty(cut.FindAll(".popover__panel"));
    }

    [Fact]
    public void Open_renders_the_panel_with_its_content_and_id()
    {
        var cut = Popover(open: true);

        var panel = cut.Find(".popover__panel");

        Assert.Equal(cut.Instance.PanelId, panel.Id);
        Assert.Equal("Two items", panel.QuerySelector(".inside")?.TextContent);
        Assert.Single(cut.FindAll("button.opener"));
    }

    [Fact]
    public void End_alignment_marks_the_panel()
    {
        Assert.Contains("popover__panel--end", Popover(open: true, align: DeskPopoverAlign.End).Find(".popover__panel").ClassList);
        Assert.DoesNotContain("popover__panel--end", Popover(open: true).Find(".popover__panel").ClassList);
    }

    [Fact]
    public void An_open_popover_is_the_one_the_desk_remembers()
    {
        var cut = Popover(open: true);

        Assert.Same(cut.Instance, Services.GetRequiredService<DeskPopovers>().Current);
    }

    /// <summary>The HIG's one-at-a-time rule: the second opening asks the first's owner to close it.</summary>
    [Fact]
    public void Opening_a_second_popover_closes_the_first()
    {
        var first = new List<bool>();
        var second = new List<bool>();

        Popover(open: true, changed: first.Add);
        var other = Popover(open: true, changed: second.Add);

        Assert.Equal([false], first);
        Assert.Empty(second);
        Assert.Same(other.Instance, Services.GetRequiredService<DeskPopovers>().Current);
    }

    [Fact]
    public void Closing_forgets_the_open_popover()
    {
        var cut = Popover(open: true);

        cut.SetParametersAndRender(p => p.Add(x => x.Open, false));

        Assert.Null(Services.GetRequiredService<DeskPopovers>().Current);
        Assert.Empty(cut.FindAll(".popover__panel"));
    }

    /// <summary>Several routes can arrive for one close; a closed popover asks for nothing.</summary>
    [Fact]
    public async Task Closing_a_closed_popover_says_nothing()
    {
        var seen = new List<bool>();

        var cut = Popover(open: false, changed: seen.Add);
        await cut.InvokeAsync(cut.Instance.CloseAsync);

        Assert.Empty(seen);
    }

    [Fact]
    public async Task Dismissing_an_open_popover_asks_its_owner_to_close_it()
    {
        var seen = new List<bool>();

        var cut = Popover(open: true, changed: seen.Add);
        await cut.Instance.OnDismiss();

        Assert.Equal([false], seen);
    }
}
