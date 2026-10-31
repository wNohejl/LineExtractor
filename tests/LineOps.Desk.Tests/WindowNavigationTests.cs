using LineOps.Desk.Windowing;
using LineOps.Desk.Tests.Support;

namespace LineOps.Desk.Tests;

/// <summary>
/// Following a link inside a window.
///
/// <para>
/// A company named on an item, a person named on a company, a record named in a comparison: each
/// opened a window of its own, so three clicks into an item was three more columns, and on a
/// full desk each one closed something to make room. The window the reader was in is where they
/// were reading, so a subject window now goes there itself and keeps the pages it came through,
/// with Back and Forward in its own bar.
/// </para>
/// </summary>
public class WindowNavigationTests
{
    private static WindowManager Desk(int capacity = 3)
    {
        var manager = new WindowManager(new SampleCatalog());
        manager.UpdateSettings(s => s.MaxConcurrentWindows = capacity);
        return manager;
    }

    private static WindowDefinition Window(string key) => SampleCatalog.Find(key);

    private static WindowInstance OpenItem(WindowManager desk, int itemId = 1)
        => desk.Open(Window(SampleCatalog.Item), new Dictionary<string, object> { ["ItemId"] = itemId }, "First item");

    private static Dictionary<string, object> Company(int id) => new() { ["CompanyId"] = id };

    [Fact]
    public void A_link_in_a_subject_window_goes_there_in_the_same_window()
    {
        var desk = Desk();
        var item = OpenItem(desk);

        var landed = desk.Follow(item.Id, Window(SampleCatalog.Company), Company(7), "Seventh company");

        Assert.Same(item, landed);
        Assert.Same(item, desk.Windows.Single());
        Assert.Equal(SampleCatalog.Company, item.Definition.Key);
        Assert.Equal(7, item.Parameters["CompanyId"]);
        Assert.False(item.Parameters.ContainsKey("ItemId"));
        Assert.Equal("Seventh company", item.Title);
        Assert.True(item.CanPageBack);
        Assert.Equal("First item", item.BackTitle);
    }

    [Fact]
    public void Back_and_forward_walk_the_pages_a_window_came_through()
    {
        var desk = Desk();
        var window = OpenItem(desk);
        desk.Follow(window.Id, Window(SampleCatalog.Company), Company(7), "Seventh company");
        desk.Follow(window.Id, Window(SampleCatalog.Person), new Dictionary<string, object> { ["PersonId"] = 42 }, "Forty-second person");

        Assert.True(desk.PageBack(window.Id));
        Assert.Equal(SampleCatalog.Company, window.Definition.Key);
        Assert.Equal("Seventh company", window.Title);

        Assert.True(desk.PageBack(window.Id));
        Assert.Equal(SampleCatalog.Item, window.Definition.Key);
        Assert.Equal(1, window.Parameters["ItemId"]);
        Assert.False(window.CanPageBack);

        Assert.True(desk.PageForward(window.Id));
        Assert.True(desk.PageForward(window.Id));
        Assert.Equal(SampleCatalog.Person, window.Definition.Key);
        Assert.False(window.CanPageForward);
    }

    [Fact]
    public void A_new_link_after_going_back_forgets_the_pages_ahead()
    {
        var desk = Desk();
        var window = OpenItem(desk);
        desk.Follow(window.Id, Window(SampleCatalog.Company), Company(7), "Seventh company");
        desk.PageBack(window.Id);

        desk.Follow(window.Id, Window(SampleCatalog.Company), Company(8), "Eighth company");

        Assert.False(window.CanPageForward);
        Assert.Equal(8, window.Parameters["CompanyId"]);
    }

    [Fact]
    public void Following_inside_a_window_never_closes_another_to_make_room()
    {
        var desk = Desk(capacity: 2);
        desk.Open(Window(SampleCatalog.List));
        var item = OpenItem(desk);

        desk.Follow(item.Id, Window(SampleCatalog.Company), Company(7), "Seventh company");

        Assert.Equal(2, desk.Windows.Count);
        Assert.Null(desk.LastEviction);
    }

    [Fact]
    public void A_link_in_a_catalogue_window_still_opens_a_window_beside_it()
    {
        var desk = Desk();
        var list = desk.Open(Window(SampleCatalog.List));

        var item = desk.Follow(list.Id, Window(SampleCatalog.Item), new Dictionary<string, object> { ["ItemId"] = 1 }, "First item");

        Assert.NotSame(list, item);
        Assert.Equal(SampleCatalog.List, list.Definition.Key);
        Assert.Equal(2, desk.Windows.Count);
    }

    [Fact]
    public void Following_to_the_page_already_showing_is_not_a_step()
    {
        var desk = Desk();
        var item = OpenItem(desk);
        var depth = desk.HistoryDepth;

        desk.Follow(item.Id, Window(SampleCatalog.Item), new Dictionary<string, object> { ["ItemId"] = 1 }, "First item");

        Assert.False(item.CanPageBack);
        Assert.Equal(depth, desk.HistoryDepth);
    }

    [Fact]
    public void The_desk_Back_undoes_a_step_taken_inside_a_window()
    {
        var desk = Desk();
        var window = OpenItem(desk);
        desk.Follow(window.Id, Window(SampleCatalog.Company), Company(7), "Seventh company");

        Assert.True(desk.Back());

        Assert.Equal(SampleCatalog.Item, window.Definition.Key);
        Assert.Equal(1, window.Parameters["ItemId"]);
        Assert.False(window.Parameters.ContainsKey("CompanyId"));
        Assert.False(window.CanPageBack);

        Assert.True(desk.Forward());

        Assert.Equal(SampleCatalog.Company, window.Definition.Key);
        Assert.True(window.CanPageBack);
    }
}
