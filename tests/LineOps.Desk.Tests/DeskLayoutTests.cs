using LineOps.Desk.Tests.Support;
using LineOps.Desk.Windowing;

namespace LineOps.Desk.Tests;

/// <summary>
/// The desk written down and put back. A reload used to lose the row, the ceiling and the
/// primary — they lived only in the circuit — and there was no way to keep an arrangement.
/// </summary>
public class DeskLayoutTests
{
    private static WindowManager NewManager() => new(new SampleCatalog());

    private static WindowDefinition Window(string key) => SampleCatalog.Find(key);

    [Fact]
    public void A_desk_survives_being_written_down_and_read_back()
    {
        var before = NewManager();
        before.UpdateSettings(s => s.MaxConcurrentWindows = 5);
        before.Open(Window(SampleCatalog.List));
        var item = before.Open(Window(SampleCatalog.Item), new Dictionary<string, object> { ["ItemId"] = 3470 }, "First item");
        var health = before.Open(Window(SampleCatalog.Health));
        before.ToggleMinimise(health.Id);
        before.Focus(item.Id);

        var json = before.Capture().Serialise();

        var after = NewManager();
        after.Restore(DeskLayout.Parse(json)!);

        Assert.Equal(
            [SampleCatalog.List, SampleCatalog.Item, SampleCatalog.Health],
            after.Windows.OrderBy(w => w.Sequence).Select(w => w.Definition.Key));

        var restoredItem = after.Windows.Single(w => w.Definition.Key == SampleCatalog.Item);

        // The id comes back as the int the component parameter is declared as, not as JSON.
        Assert.Equal(3470, Assert.IsType<int>(restoredItem.Parameters["ItemId"]));
        Assert.Equal("First item", restoredItem.Title);
        Assert.Equal(restoredItem.Id, after.FocusedId);
        Assert.True(after.Windows.Single(w => w.Definition.Key == SampleCatalog.Health).Minimised);
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
                new DeskLayoutWindow("retired", null, 1, false, null),   // folded into another window
                new DeskLayoutWindow(SampleCatalog.Notes, null, 1, false, null)
            ]
        };

        var manager = NewManager();
        manager.Restore(DeskLayout.Parse(layout.Serialise())!);

        Assert.Equal(SampleCatalog.Notes, Assert.Single(manager.Windows).Definition.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("{\"version\":99,\"windows\":[]}")]
    public void An_unreadable_or_foreign_layout_is_ignored(string? json)
        => Assert.Null(DeskLayout.Parse(json));

    [Fact]
    public void The_scale_travels_with_the_layout()
    {
        var before = NewManager();
        before.UpdateSettings(s =>
        {
            s.UiScale = 1.25;
            s.TextScale = 1.1;
        });

        var after = NewManager();
        after.Restore(DeskLayout.Parse(before.Capture().Serialise())!);

        Assert.Equal(1.25, after.Settings.UiScale);
        Assert.Equal(1.1, after.Settings.TextScale);
    }

    /// <summary>
    /// Every browser that used the desk before scale existed holds a layout with no scale in it.
    /// Its missing fields read as 0, and 0% is not a scale anyone chose — it is 100%. Bumping the
    /// layout version would have been the other way to say so, at the price of every saved row.
    /// </summary>
    [Fact]
    public void A_layout_saved_before_scale_existed_restores_at_100_percent()
    {
        const string before = "{\"version\":1,\"maxConcurrentWindows\":3,\"windows\":[]}";

        var manager = NewManager();
        manager.Restore(DeskLayout.Parse(before)!);

        Assert.Equal(1, manager.Settings.UiScale);
        Assert.Equal(1, manager.Settings.TextScale);
    }

    /// <summary>
    /// localStorage holds whatever was written, including by hand. A desk at 900% cannot show the
    /// Window manager that would set it back, so a stored scale is held to the range the control
    /// offers rather than trusted.
    /// </summary>
    [Theory]
    [InlineData(9.0, 0.1, DeskSettings.MaxUiScale, DeskSettings.MinTextScale)]
    [InlineData(0.1, 9.0, DeskSettings.MinUiScale, DeskSettings.MaxTextScale)]
    [InlineData(-1.0, double.NaN, 1.0, 1.0)]
    public void A_stored_scale_is_held_to_the_range_the_control_offers(
        double ui, double text, double expectedUi, double expectedText)
    {
        var manager = NewManager();
        manager.Restore(new DeskLayout { MaxConcurrentWindows = 4, UiScale = ui, TextScale = text });

        Assert.Equal(expectedUi, manager.Settings.UiScale);
        Assert.Equal(expectedText, manager.Settings.TextScale);
    }

    [Fact]
    public void A_saved_workspace_keeps_the_row_but_not_windows_opened_on_something()
    {
        var manager = NewManager();
        manager.UpdateSettings(s => s.MaxConcurrentWindows = 5);
        manager.Open(Window(SampleCatalog.List));
        manager.Open(Window(SampleCatalog.Item), new Dictionary<string, object> { ["ItemId"] = 1 });
        manager.Open(Window(SampleCatalog.Notes));

        var saved = manager.SaveWorkspace("  Sunday  ");

        Assert.NotNull(saved);
        Assert.Equal("Sunday", saved.Name);
        Assert.Equal([SampleCatalog.List, SampleCatalog.Notes], saved.WindowKeys);

        // Saving under the same name replaces it, and it travels with the layout.
        manager.SaveWorkspace("sunday");
        var restored = NewManager();
        restored.Restore(DeskLayout.Parse(manager.Capture().Serialise())!);

        Assert.Equal("sunday", Assert.Single(restored.UserWorkspaces).Name);
    }
}
