using LineOps.Desk.Windowing;
using LineOps.Web.Windowing;

namespace LineOps.Web.Tests;

/// <summary>
/// Following a link inside a window.
///
/// <para>
/// A team named in a game, a player named on a roster, a meeting named in a head to head: each
/// opened a window of its own, so three clicks into a matchup was three more columns, and on a
/// full desk each one closed something to make room. The window the reader was in is where they
/// were reading, so a subject window now goes there itself and keeps the pages it came through,
/// with Back and Forward in its own bar.
/// </para>
/// </summary>
public class WindowNavigationTests
{
    private static WindowManager Desk(int capacity = 3)
    {
        var manager = new WindowManager(new AppWindowCatalog());
        manager.UpdateSettings(s => s.MaxConcurrentWindows = capacity);
        return manager;
    }

    private static WindowDefinition Window(string key) => WindowCatalog.Find(key)!;

    private static WindowInstance OpenGame(WindowManager desk, int gameId = 1)
        => desk.Open(Window(WindowCatalog.Game), new Dictionary<string, object> { ["GameId"] = gameId }, "Mets at Braves");

    private static Dictionary<string, object> Team(int id) => new() { ["TeamId"] = id };

    [Fact]
    public void A_link_in_a_subject_window_goes_there_in_the_same_window()
    {
        var desk = Desk();
        var game = OpenGame(desk);

        var landed = desk.Follow(game.Id, Window(WindowCatalog.Team), Team(7), "Atlanta Braves");

        Assert.Same(game, landed);
        Assert.Same(game, desk.Windows.Single());
        Assert.Equal(WindowCatalog.Team, game.Definition.Key);
        Assert.Equal(7, game.Parameters["TeamId"]);
        Assert.False(game.Parameters.ContainsKey("GameId"));
        Assert.Equal("Atlanta Braves", game.Title);
        Assert.True(game.CanPageBack);
        Assert.Equal("Mets at Braves", game.BackTitle);
    }

    [Fact]
    public void Back_and_forward_walk_the_pages_a_window_came_through()
    {
        var desk = Desk();
        var window = OpenGame(desk);
        desk.Follow(window.Id, Window(WindowCatalog.Team), Team(7), "Atlanta Braves");
        desk.Follow(window.Id, Window(WindowCatalog.Player), new Dictionary<string, object> { ["PlayerId"] = 42 }, "Ronald Acuña Jr.");

        Assert.True(desk.PageBack(window.Id));
        Assert.Equal(WindowCatalog.Team, window.Definition.Key);
        Assert.Equal("Atlanta Braves", window.Title);

        Assert.True(desk.PageBack(window.Id));
        Assert.Equal(WindowCatalog.Game, window.Definition.Key);
        Assert.Equal(1, window.Parameters["GameId"]);
        Assert.False(window.CanPageBack);

        Assert.True(desk.PageForward(window.Id));
        Assert.True(desk.PageForward(window.Id));
        Assert.Equal(WindowCatalog.Player, window.Definition.Key);
        Assert.False(window.CanPageForward);
    }

    [Fact]
    public void A_new_link_after_going_back_forgets_the_pages_ahead()
    {
        var desk = Desk();
        var window = OpenGame(desk);
        desk.Follow(window.Id, Window(WindowCatalog.Team), Team(7), "Atlanta Braves");
        desk.PageBack(window.Id);

        desk.Follow(window.Id, Window(WindowCatalog.Team), Team(8), "New York Mets");

        Assert.False(window.CanPageForward);
        Assert.Equal(8, window.Parameters["TeamId"]);
    }

    [Fact]
    public void Following_inside_a_window_never_closes_another_to_make_room()
    {
        var desk = Desk(capacity: 2);
        desk.Open(Window(WindowCatalog.Board));
        var game = OpenGame(desk);

        desk.Follow(game.Id, Window(WindowCatalog.Team), Team(7), "Atlanta Braves");

        Assert.Equal(2, desk.Windows.Count);
        Assert.Null(desk.LastEviction);
    }

    [Fact]
    public void A_link_in_a_catalogue_window_still_opens_a_window_beside_it()
    {
        var desk = Desk();
        var board = desk.Open(Window(WindowCatalog.Board));

        var game = desk.Follow(board.Id, Window(WindowCatalog.Game), new Dictionary<string, object> { ["GameId"] = 1 }, "Mets at Braves");

        Assert.NotSame(board, game);
        Assert.Equal(WindowCatalog.Board, board.Definition.Key);
        Assert.Equal(2, desk.Windows.Count);
    }

    [Fact]
    public void Following_to_the_page_already_showing_is_not_a_step()
    {
        var desk = Desk();
        var game = OpenGame(desk);
        var depth = desk.HistoryDepth;

        desk.Follow(game.Id, Window(WindowCatalog.Game), new Dictionary<string, object> { ["GameId"] = 1 }, "Mets at Braves");

        Assert.False(game.CanPageBack);
        Assert.Equal(depth, desk.HistoryDepth);
    }

    [Fact]
    public void The_desk_Back_undoes_a_step_taken_inside_a_window()
    {
        var desk = Desk();
        var window = OpenGame(desk);
        desk.Follow(window.Id, Window(WindowCatalog.Team), Team(7), "Atlanta Braves");

        Assert.True(desk.Back());

        Assert.Equal(WindowCatalog.Game, window.Definition.Key);
        Assert.Equal(1, window.Parameters["GameId"]);
        Assert.False(window.Parameters.ContainsKey("TeamId"));
        Assert.False(window.CanPageBack);

        Assert.True(desk.Forward());

        Assert.Equal(WindowCatalog.Team, window.Definition.Key);
        Assert.True(window.CanPageBack);
    }
}
