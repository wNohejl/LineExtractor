using LineOps.Desk.Windowing;
using LineOps.Web.Windowing;

namespace LineOps.Web.Tests;

/// <summary>
/// Going back on the desk.
///
/// <para>
/// The desk is one route, so the browser's Back left the application, and nothing inside it went
/// back either. Opening a window on a full desk closed another for good; following a source's
/// name to its runs focused the Runs window on whatever it was already showing. Each open, close
/// and re-aim is now a step the desk can take back, and the host mirrors the steps into the
/// browser's history so every Back key reaches them.
/// </para>
/// </summary>
public class DeskHistoryTests
{
    private static WindowManager Desk(int capacity = 3)
    {
        var manager = new WindowManager(new AppWindowCatalog());
        manager.UpdateSettings(s => s.MaxConcurrentWindows = capacity);
        return manager;
    }

    private static WindowDefinition Window(string key) => WindowCatalog.Find(key)!;

    private static string[] Row(WindowManager desk)
        => desk.Windows.OrderBy(w => w.Sequence).Select(w => w.Definition.Key).ToArray();

    [Fact]
    public void A_window_closed_to_make_room_comes_back()
    {
        var desk = Desk(capacity: 2);
        desk.Open(Window(WindowCatalog.Ops));
        desk.Open(Window(WindowCatalog.Incidents));
        var ops = desk.Windows.Single(w => w.Definition.Key == WindowCatalog.Ops);

        desk.Open(Window(WindowCatalog.Runs));
        Assert.Equal([WindowCatalog.Incidents, WindowCatalog.Runs], Row(desk));
        Assert.NotNull(desk.LastEviction);

        Assert.True(desk.Back());

        Assert.Equal([WindowCatalog.Ops, WindowCatalog.Incidents], Row(desk));
        Assert.Same(ops, desk.Windows.Single(w => w.Definition.Key == WindowCatalog.Ops));
        Assert.Null(desk.LastEviction);
    }

    [Fact]
    public void A_singleton_opened_on_a_subject_shows_that_subject_and_back_restores_the_last()
    {
        var desk = Desk();
        var runs = desk.Open(Window(WindowCatalog.Runs));

        desk.Open(Window(WindowCatalog.Runs), new Dictionary<string, object> { ["SourceId"] = 4 }, "Runs · ESPN");

        Assert.Same(runs, desk.Windows.Single());
        Assert.Equal(4, runs.Parameters["SourceId"]);
        Assert.Equal("Runs · ESPN", runs.Title);

        desk.Back();

        Assert.Same(runs, desk.Windows.Single());
        Assert.Null(runs.Parameters["SourceId"]);
        Assert.Equal("Runs", runs.Title);
    }

    [Fact]
    public void Focusing_a_singleton_is_not_a_step()
    {
        var desk = Desk();
        desk.Open(Window(WindowCatalog.Ops));
        var depth = desk.HistoryDepth;

        desk.Open(Window(WindowCatalog.Ops));

        Assert.Equal(depth, desk.HistoryDepth);
    }

    [Fact]
    public void Forward_redoes_and_a_new_step_forgets_the_road_ahead()
    {
        var desk = Desk();
        desk.Open(Window(WindowCatalog.Ops));
        desk.Open(Window(WindowCatalog.Runs));

        desk.Back();
        Assert.Equal([WindowCatalog.Ops], Row(desk));
        Assert.True(desk.CanGoForward);

        desk.Forward();
        Assert.Equal([WindowCatalog.Ops, WindowCatalog.Runs], Row(desk));

        desk.Back();
        desk.Open(Window(WindowCatalog.Incidents));
        Assert.False(desk.CanGoForward);
    }

    [Fact]
    public void A_closed_window_returns_to_its_slot()
    {
        var desk = Desk();
        desk.Open(Window(WindowCatalog.Ops));
        var incidents = desk.Open(Window(WindowCatalog.Incidents));
        desk.Open(Window(WindowCatalog.Runs));

        desk.Close(incidents.Id);
        desk.Back();

        Assert.Equal([WindowCatalog.Ops, WindowCatalog.Incidents, WindowCatalog.Runs], Row(desk));
        Assert.Same(incidents, desk.Find(incidents.Id));
    }

    [Fact]
    public void The_browser_can_walk_several_steps_at_once()
    {
        var desk = Desk();
        desk.Open(Window(WindowCatalog.Ops));
        desk.Open(Window(WindowCatalog.Incidents));
        desk.Open(Window(WindowCatalog.Runs));
        Assert.Equal(3, desk.HistoryDepth);

        desk.TraverseTo(1);
        Assert.Equal([WindowCatalog.Ops], Row(desk));

        desk.TraverseTo(3);
        Assert.Equal([WindowCatalog.Ops, WindowCatalog.Incidents, WindowCatalog.Runs], Row(desk));
    }

    [Fact]
    public void Depth_keeps_counting_past_what_the_desk_remembers()
    {
        var desk = Desk(capacity: 1);

        for (var i = 0; i < 60; i++)
            desk.Open(Window(i % 2 == 0 ? WindowCatalog.Ops : WindowCatalog.Runs));

        // The browser numbers its entries by this; capping it would stop them matching.
        Assert.Equal(60, desk.HistoryDepth);

        Assert.Equal([WindowCatalog.Runs], Row(desk));

        desk.TraverseTo(59);
        Assert.Equal([WindowCatalog.Ops], Row(desk));
    }
}
