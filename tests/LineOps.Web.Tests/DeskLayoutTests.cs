using LineOps.Desk.Windowing;
using LineOps.Web.Windowing;

namespace LineOps.Web.Tests;

/// <summary>
/// The desk written down and put back. A reload used to lose the row, the ceiling and the
/// primary — they lived only in the circuit — and there was no way to keep an arrangement.
/// </summary>
public class DeskLayoutTests
{
    private static WindowManager NewManager() => new(new AppWindowCatalog());

    private static WindowDefinition Window(string key) => WindowCatalog.Find(key)!;

    [Fact]
    public void A_desk_survives_being_written_down_and_read_back()
    {
        var before = NewManager();
        before.UpdateSettings(s => s.MaxConcurrentWindows = 5);
        before.Open(Window(WindowCatalog.Board));
        var game = before.Open(Window(WindowCatalog.Game), new Dictionary<string, object> { ["GameId"] = 3470 }, "Mets at Braves");
        var ops = before.Open(Window(WindowCatalog.Ops));
        before.ToggleMinimise(ops.Id);
        before.Focus(game.Id);

        var json = before.Capture().Serialise();

        var after = NewManager();
        after.Restore(DeskLayout.Parse(json)!);

        Assert.Equal(
            [WindowCatalog.Board, WindowCatalog.Game, WindowCatalog.Ops],
            after.Windows.OrderBy(w => w.Sequence).Select(w => w.Definition.Key));

        var restoredGame = after.Windows.Single(w => w.Definition.Key == WindowCatalog.Game);

        // The id comes back as the int the component parameter is declared as, not as JSON.
        Assert.Equal(3470, Assert.IsType<int>(restoredGame.Parameters["GameId"]));
        Assert.Equal("Mets at Braves", restoredGame.Title);
        Assert.Equal(restoredGame.Id, after.FocusedId);
        Assert.True(after.Windows.Single(w => w.Definition.Key == WindowCatalog.Ops).Minimised);
        Assert.Equal(5, after.Settings.MaxConcurrentWindows);
    }

    [Fact]
    public void A_window_the_catalogue_no_longer_has_is_dropped_not_fatal()
    {
        var layout = new DeskLayout
        {
            MaxConcurrentWindows = 4,
            Windows =
            [
                new DeskLayoutWindow("slate", null, 1, false, null),   // retired into the Board
                new DeskLayoutWindow(WindowCatalog.Journal, null, 1, false, null)
            ]
        };

        var manager = NewManager();
        manager.Restore(DeskLayout.Parse(layout.Serialise())!);

        Assert.Equal(WindowCatalog.Journal, Assert.Single(manager.Windows).Definition.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("{\"version\":99,\"windows\":[]}")]
    public void An_unreadable_or_foreign_layout_is_ignored(string? json)
        => Assert.Null(DeskLayout.Parse(json));

    [Fact]
    public void A_saved_workspace_keeps_the_row_but_not_windows_opened_on_something()
    {
        var manager = NewManager();
        manager.UpdateSettings(s => s.MaxConcurrentWindows = 5);
        manager.Open(Window(WindowCatalog.Board));
        manager.Open(Window(WindowCatalog.Game), new Dictionary<string, object> { ["GameId"] = 1 });
        manager.Open(Window(WindowCatalog.Journal));

        var saved = manager.SaveWorkspace("  Sunday  ");

        Assert.NotNull(saved);
        Assert.Equal("Sunday", saved.Name);
        Assert.Equal([WindowCatalog.Board, WindowCatalog.Journal], saved.WindowKeys);

        // Saving under the same name replaces it, and it travels with the layout.
        manager.SaveWorkspace("sunday");
        var restored = NewManager();
        restored.Restore(DeskLayout.Parse(manager.Capture().Serialise())!);

        Assert.Equal("sunday", Assert.Single(restored.UserWorkspaces).Name);
    }
}
