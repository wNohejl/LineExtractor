using LineOps.Desk.Windowing;
using LineOps.Desk.Tests.Support;

namespace LineOps.Desk.Tests;

/// <summary>
/// Going back on the desk.
///
/// <para>
/// The desk is one route, so the browser's Back left the application, and nothing inside it went
/// back either. Opening a window on a full desk closed another for good; following a source's
/// name to its jobs focused the Jobs window on whatever it was already showing. Each open, close
/// and re-aim is now a step the desk can take back, and the host mirrors the steps into the
/// browser's history so every Back key reaches them.
/// </para>
/// </summary>
public class DeskHistoryTests
{
    private static WindowManager Desk(int capacity = 3)
    {
        var manager = new WindowManager(new SampleCatalog());
        manager.UpdateSettings(s => s.MaxConcurrentWindows = capacity);
        return manager;
    }

    private static WindowDefinition Window(string key) => SampleCatalog.Find(key);

    private static string[] Row(WindowManager desk)
        => desk.Windows.OrderBy(w => w.Sequence).Select(w => w.Definition.Key).ToArray();

    [Fact]
    public void A_window_closed_to_make_room_comes_back()
    {
        var desk = Desk(capacity: 2);
        desk.Open(Window(SampleCatalog.Health));
        desk.Open(Window(SampleCatalog.Alerts));
        var health = desk.Windows.Single(w => w.Definition.Key == SampleCatalog.Health);

        desk.Open(Window(SampleCatalog.Jobs));
        Assert.Equal([SampleCatalog.Alerts, SampleCatalog.Jobs], Row(desk));
        Assert.NotNull(desk.LastEviction);

        Assert.True(desk.Back());

        Assert.Equal([SampleCatalog.Health, SampleCatalog.Alerts], Row(desk));
        Assert.Same(health, desk.Windows.Single(w => w.Definition.Key == SampleCatalog.Health));
        Assert.Null(desk.LastEviction);
    }

    [Fact]
    public void A_singleton_opened_on_a_subject_shows_that_subject_and_back_restores_the_last()
    {
        var desk = Desk();
        var jobs = desk.Open(Window(SampleCatalog.Jobs));

        desk.Open(Window(SampleCatalog.Jobs), new Dictionary<string, object> { ["SourceId"] = 4 }, "Jobs · Nightly");

        Assert.Same(jobs, desk.Windows.Single());
        Assert.Equal(4, jobs.Parameters["SourceId"]);
        Assert.Equal("Jobs · Nightly", jobs.Title);

        desk.Back();

        Assert.Same(jobs, desk.Windows.Single());
        Assert.Null(jobs.Parameters["SourceId"]);
        Assert.Equal("Jobs", jobs.Title);
    }

    [Fact]
    public void Focusing_a_singleton_is_not_a_step()
    {
        var desk = Desk();
        desk.Open(Window(SampleCatalog.Health));
        var depth = desk.HistoryDepth;

        desk.Open(Window(SampleCatalog.Health));

        Assert.Equal(depth, desk.HistoryDepth);
    }

    [Fact]
    public void Forward_redoes_and_a_new_step_forgets_the_road_ahead()
    {
        var desk = Desk();
        desk.Open(Window(SampleCatalog.Health));
        desk.Open(Window(SampleCatalog.Jobs));

        desk.Back();
        Assert.Equal([SampleCatalog.Health], Row(desk));
        Assert.True(desk.CanGoForward);

        desk.Forward();
        Assert.Equal([SampleCatalog.Health, SampleCatalog.Jobs], Row(desk));

        desk.Back();
        desk.Open(Window(SampleCatalog.Alerts));
        Assert.False(desk.CanGoForward);
    }

    [Fact]
    public void A_closed_window_returns_to_its_slot()
    {
        var desk = Desk();
        desk.Open(Window(SampleCatalog.Health));
        var alerts = desk.Open(Window(SampleCatalog.Alerts));
        desk.Open(Window(SampleCatalog.Jobs));

        desk.Close(alerts.Id);
        desk.Back();

        Assert.Equal([SampleCatalog.Health, SampleCatalog.Alerts, SampleCatalog.Jobs], Row(desk));
        Assert.Same(alerts, desk.Find(alerts.Id));
    }

    [Fact]
    public void The_browser_can_walk_several_steps_at_once()
    {
        var desk = Desk();
        desk.Open(Window(SampleCatalog.Health));
        desk.Open(Window(SampleCatalog.Alerts));
        desk.Open(Window(SampleCatalog.Jobs));
        Assert.Equal(3, desk.HistoryDepth);

        desk.TraverseTo(1);
        Assert.Equal([SampleCatalog.Health], Row(desk));

        desk.TraverseTo(3);
        Assert.Equal([SampleCatalog.Health, SampleCatalog.Alerts, SampleCatalog.Jobs], Row(desk));
    }

    [Fact]
    public void Depth_keeps_counting_past_what_the_desk_remembers()
    {
        var desk = Desk(capacity: 1);

        for (var i = 0; i < 60; i++)
            desk.Open(Window(i % 2 == 0 ? SampleCatalog.Health : SampleCatalog.Jobs));

        // The browser numbers its entries by this; capping it would stop them matching.
        Assert.Equal(60, desk.HistoryDepth);

        Assert.Equal([SampleCatalog.Jobs], Row(desk));

        desk.TraverseTo(59);
        Assert.Equal([SampleCatalog.Health], Row(desk));
    }
}
