using Bunit;
using LineOps.Desk.Primitives;
using LineOps.Desk.Tests.Support;
using LineOps.Desk.Windowing;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using DeskShell = LineOps.Desk.Windowing.Desk;

namespace LineOps.Desk.Tests;

/// <summary>
/// A window's status re-renders that window, not the desk.
///
/// <para>
/// The manager used to raise one event for everything, and every piece of chrome listened to it:
/// a panel reporting "3 items" re-rendered the desk, the header, the bar, the footer and every
/// window's chrome on it — and a window reports at least twice as it loads. Status now travels on
/// its own signal, to the window it belongs to and to the bar that carries its dot; the layout
/// signal is left to what the row's shape changes.
/// </para>
///
/// <para>
/// Counts are ceilings, never exact numbers: a MudBlazor upgrade may add a render of its own, and
/// what this guards is that the desk stops paying for a status line, not how many passes a
/// render takes.
/// </para>
/// </summary>
public class WindowRenderCountTests : DeskTestContext
{
    /// <summary>
    /// The desk, counting its own passes. bUnit's <c>RenderCount</c> counts a component whenever
    /// anything under it renders, so the desk's would rise with every window it holds — which is
    /// the cost being removed, not the one being measured.
    /// </summary>
    public sealed class CountedDesk : DeskShell
    {
        public int Passes { get; private set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            Passes++;
            base.BuildRenderTree(builder);
        }
    }

    private (IRenderedComponent<CountedDesk> Desk, WindowManager Manager, WindowInstance First, WindowInstance Second) Render()
    {
        Services.AddSingleton<IDeskAlerts>(new FakeAlerts());

        var manager = Services.GetRequiredService<WindowManager>();
        var first = manager.Open(manager.Catalog.Find(FakeWindowCatalog.One)!);
        var second = manager.Open(manager.Catalog.Find(FakeWindowCatalog.Two)!);

        var desk = RenderComponent<CountedDesk>();

        return (desk, manager, first, second);
    }

    private static IRenderedComponent<AppWindow> WindowOf(IRenderedComponent<CountedDesk> desk, WindowInstance instance)
        => desk.FindComponents<AppWindow>().Single(w => w.Instance.Window.Id == instance.Id);

    /// <summary>
    /// What each piece of chrome has rendered so far. A window's and the bar's counts are their
    /// own, since nothing under them renders by itself; the header's includes the bar, which sits
    /// in it.
    /// </summary>
    private sealed record Counts(int Desk, int First, int Second, int Bar, int Header, int Footer);

    private static Counts Take(IRenderedComponent<CountedDesk> desk, WindowInstance first, WindowInstance second) => new(
        desk.Instance.Passes,
        WindowOf(desk, first).RenderCount,
        WindowOf(desk, second).RenderCount,
        desk.FindComponent<WindowBar>().RenderCount,
        desk.FindComponent<DeskHeader>().RenderCount,
        desk.FindComponent<DeskFooter>().RenderCount);

    [Fact]
    public void A_status_change_re_renders_its_window_and_the_bar_and_nothing_else()
    {
        var (desk, manager, first, second) = Render();
        var before = Take(desk, first, second);

        desk.InvokeAsync(() => manager.SetPulse(first.Id, PulseState.Healthy, "3 items"));
        var after = Take(desk, first, second);

        Assert.Equal(before.Desk, after.Desk);
        Assert.Equal(before.Footer, after.Footer);
        Assert.Equal(before.Second, after.Second);
        Assert.InRange(after.First - before.First, 1, 1);

        // The bar carries every open window's dot, so it must still hear a status change — and
        // the header around it rises by the bar's pass alone, not one of its own.
        Assert.InRange(after.Bar - before.Bar, 1, 1);
        Assert.Equal(after.Bar - before.Bar, after.Header - before.Header);
        Assert.Contains("pulse--healthy", desk.Find(".bar__pulse").ClassList);

        var window = WindowOf(desk, first);
        Assert.Equal("3 items", window.Find(".win__status").TextContent);
        Assert.Contains("pulse--healthy", window.Find(".pulse").ClassList);
    }

    [Fact]
    public void A_relayout_re_renders_the_desk_once_and_each_window_at_most_once()
    {
        var (desk, manager, first, second) = Render();
        var before = Take(desk, first, second);

        desk.InvokeAsync(manager.Relayout);
        var after = Take(desk, first, second);

        Assert.InRange(after.Desk - before.Desk, 1, 1);
        Assert.InRange(after.First - before.First, 0, 1);
        Assert.InRange(after.Second - before.Second, 0, 1);
    }

    [Fact]
    public void A_status_reported_twice_the_same_renders_nothing_more()
    {
        var (desk, manager, first, second) = Render();

        desk.InvokeAsync(() => manager.SetPulse(first.Id, PulseState.Healthy, "3 items"));
        var before = Take(desk, first, second);

        desk.InvokeAsync(() => manager.SetPulse(first.Id, PulseState.Healthy, "3 items"));

        Assert.Equal(before, Take(desk, first, second));
    }

    /// <summary>
    /// A change to the desk that moves no window — a workspace saved — redraws the desk and leaves
    /// every window's chrome as it was.
    /// </summary>
    [Fact]
    public void A_desk_change_that_moves_no_window_redraws_no_window()
    {
        var (desk, manager, first, second) = Render();
        var before = Take(desk, first, second);

        desk.InvokeAsync(() => manager.SaveWorkspace("Morning"));
        var after = Take(desk, first, second);

        Assert.InRange(after.Desk - before.Desk, 1, 1);
        Assert.Equal(before.First, after.First);
        Assert.Equal(before.Second, after.Second);
    }

    /// <summary>
    /// The window skips a render whose version it has already drawn, so a layout change has to
    /// move the version of every window it moves — or the row reflows and a column keeps its old
    /// width.
    /// </summary>
    [Fact]
    public void A_window_the_layout_moves_is_drawn_where_it_now_is()
    {
        var (desk, manager, first, _) = Render();
        var width = first.Width;

        desk.InvokeAsync(() => manager.SetViewport(1600, 900));

        Assert.NotEqual(width, first.Width);
        Assert.Contains($"width:{Px(first.Width)}", WindowOf(desk, first).Find(".win").GetAttribute("style"));
    }

    /// <summary>
    /// A status reported while another window holds the row lands on a window that is not drawn;
    /// restored, it must show what it last said rather than what it said before.
    /// </summary>
    [Fact]
    public void A_window_restored_from_behind_a_maximised_one_shows_its_latest_status()
    {
        var (desk, manager, first, second) = Render();

        desk.InvokeAsync(() => manager.ToggleMaximise(second.Id));
        Assert.Single(desk.FindComponents<AppWindow>());

        desk.InvokeAsync(() => manager.SetPulse(first.Id, PulseState.Warn, "stale"));
        desk.InvokeAsync(() => manager.ToggleMaximise(second.Id));

        var window = WindowOf(desk, first);
        Assert.Equal("stale", window.Find(".win__status").TextContent);
        Assert.Contains("pulse--warn", window.Find(".pulse").ClassList);
    }

    /// <summary>Focus is the desk's to give, as a parameter; the window it leaves and the one it reaches both redraw.</summary>
    [Fact]
    public void Focus_moving_redraws_the_two_windows_it_moves_between()
    {
        var (desk, manager, first, second) = Render();
        Assert.Contains("win--focused", WindowOf(desk, second).Find(".win").ClassList);

        desk.InvokeAsync(() => manager.Focus(first.Id));

        Assert.Contains("win--focused", WindowOf(desk, first).Find(".win").ClassList);
        Assert.DoesNotContain("win--focused", WindowOf(desk, second).Find(".win").ClassList);
    }

    private static string Px(double value)
        => $"{value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}px";
}
