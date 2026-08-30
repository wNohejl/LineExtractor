using AngleSharp.Dom;
using Bunit;
using LineOps.Web.Components.Windowing;
using LineOps.Web.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// The toolbar's one rule: every window is in the strip exactly once, and which run it sits
/// in says whether it is open.
///
/// This is pinned because it is the rule that quietly broke before. The bar drew every
/// catalogue entry in one run and let an open one grow a name in place, which is one item
/// per window on paper — but the header also carried its own Window manager button beside
/// the catalogue's Window manager key, so one window really did appear twice, and a named
/// tab wedged between icon keys made the whole strip read as two interleaved lists. A count
/// is the cheapest thing that would have caught it.
/// </summary>
public class WindowBarTests : DeskTestContext
{
    private WindowManager NewDesk()
    {
        var manager = new WindowManager();
        Services.AddSingleton(manager);
        return manager;
    }

    private static string[] Keys(IRenderedFragment bar) => bar
        .FindAll(".bar__closed .bar__key")
        .Select(k => k.GetAttribute("aria-label") ?? string.Empty)
        .ToArray();

    private static string[] Tabs(IRenderedFragment bar) => bar
        .FindAll(".bar__open .tab .tab__name")
        .Select(t => t.TextContent.Trim())
        .ToArray();

    [Fact]
    public void Every_openable_window_gets_one_key_and_no_tabs_when_the_desk_is_empty()
    {
        NewDesk();

        var bar = RenderComponent<WindowBar>();

        var openable = WindowCatalog.All.Count(d => !d.RequiresSubject);

        Assert.Equal(openable, Keys(bar).Length);
        Assert.Empty(Tabs(bar));
    }

    [Fact]
    public void Opening_a_window_moves_it_from_the_key_run_to_the_tab_run()
    {
        var manager = NewDesk();
        var board = WindowCatalog.Find(WindowCatalog.Board)!;

        var bar = RenderComponent<WindowBar>();
        var before = Keys(bar).Length;

        manager.Open(board);
        bar.Render();

        // The one item moved across; it did not appear in both runs, and nothing else moved.
        Assert.Equal(before - 1, Keys(bar).Length);
        Assert.DoesNotContain("Open Board", Keys(bar));
        Assert.Equal(["Board"], Tabs(bar));
    }

    [Fact]
    public void Closing_a_window_gives_its_key_back()
    {
        var manager = NewDesk();
        var ops = WindowCatalog.Find(WindowCatalog.Ops)!;

        var bar = RenderComponent<WindowBar>();
        var before = Keys(bar).Length;

        var window = manager.Open(ops);
        bar.Render();
        manager.Close(window.Id);
        bar.Render();

        Assert.Equal(before, Keys(bar).Length);
        Assert.Contains("Open Ops", Keys(bar));
        Assert.Empty(Tabs(bar));
    }

    [Fact]
    public void A_destination_is_a_tab_and_never_a_key()
    {
        var manager = NewDesk();
        var game = WindowCatalog.Find(WindowCatalog.Game)!;

        var bar = RenderComponent<WindowBar>();

        // It has no key to press before it exists, because it is about a subject.
        Assert.DoesNotContain(Keys(bar), k => k.Contains("Game", StringComparison.Ordinal));

        manager.Open(game, titleOverride: "Marlins at Nationals");
        bar.Render();

        Assert.Equal(["Marlins at Nationals"], Tabs(bar));
        Assert.DoesNotContain(Keys(bar), k => k.Contains("Marlins", StringComparison.Ordinal));
    }

    [Fact]
    public void Tabs_run_in_the_desks_own_order_so_the_strip_maps_the_row()
    {
        var manager = NewDesk();

        var board = manager.Open(WindowCatalog.Find(WindowCatalog.Board)!);
        var ops = manager.Open(WindowCatalog.Find(WindowCatalog.Ops)!);

        var bar = RenderComponent<WindowBar>();
        Assert.Equal(["Board", "Ops"], Tabs(bar));

        manager.Reorder([ops.Id, board.Id]);
        bar.Render();

        // Dragging a column past another has to move its tab too, or the strip stops being
        // a map of the desk and becomes a second arrangement to keep in your head.
        Assert.Equal(["Ops", "Board"], Tabs(bar));
    }

    [Fact]
    public void The_focused_window_is_the_current_tab_and_the_marbles_resting_place()
    {
        var manager = NewDesk();

        manager.Open(WindowCatalog.Find(WindowCatalog.Board)!);
        var ops = manager.Open(WindowCatalog.Find(WindowCatalog.Ops)!);
        manager.Focus(ops.Id);

        var bar = RenderComponent<WindowBar>();

        var current = bar.FindAll(".tab--current");
        Assert.Single(current);
        Assert.Equal("Ops", current[0].QuerySelector(".tab__name")!.TextContent.Trim());

        // Selection and the marble's rest are two signals on one element; the glide module
        // finds the second by attribute, so it has to travel with the first.
        var resting = bar.FindAll(".bar__open [data-glide-rest]");
        Assert.Single(resting);
        Assert.Contains("tab--current", resting[0].GetAttribute("class"));
    }

    [Fact]
    public void A_tab_carries_its_windows_pulse_and_a_close()
    {
        var manager = NewDesk();
        var window = manager.Open(WindowCatalog.Find(WindowCatalog.Ops)!);
        window.Pulse = PulseState.Critical;

        var bar = RenderComponent<WindowBar>();

        Assert.NotNull(bar.Find(".tab .bar__pulse.pulse--critical"));

        var close = bar.Find(".tab .tab__close");
        Assert.Equal("Close Ops", close.GetAttribute("aria-label"));

        // Out of the tab order on purpose: the strip is a roving toolbar with one tab stop.
        Assert.Equal("-1", close.GetAttribute("tabindex"));

        close.Click();
        Assert.Equal(0, manager.OpenCount);
    }
}
