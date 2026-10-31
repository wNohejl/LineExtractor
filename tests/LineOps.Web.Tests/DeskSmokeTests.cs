using Bunit;
using LineOps.Desk.Windowing;
using LineOps.Web.Windowing;
using Microsoft.Extensions.DependencyInjection;
using TheDesk = LineOps.Desk.Windowing.Desk;

namespace LineOps.Web.Tests;

/// <summary>
/// The desk on this application's own catalogue. How the desk behaves — keys, history, capacity,
/// eviction, follow-ups — is proven in LineOps.Desk.Tests on a catalogue with no application in
/// it; these are the few places where the answer depends on what LineOps lists: that the settings
/// chord finds its Window manager, the strip and the palette show its windows, a desk with a game
/// on it survives a reload, its widest pair fits a narrow desk, and the wager door re-aims the one
/// wager window.
/// </summary>
public class DeskSmokeTests : DeskTestContext
{
    private static WindowManager Manager(int capacity = 4)
    {
        var manager = new WindowManager(new AppWindowCatalog());
        manager.UpdateSettings(s => s.MaxConcurrentWindows = capacity);
        return manager;
    }

    private static WindowDefinition Window(string key) => WindowCatalog.Find(key)!;

    [Fact]
    public void The_settings_chord_opens_the_apps_window_manager()
    {
        var desk = Manager();

        TheDesk.OpenSettings(desk);

        Assert.Equal(WindowCatalog.Desk, Assert.Single(desk.Windows).Definition.Key);
    }

    [Fact]
    public void The_strip_has_one_key_per_window_that_opens_on_its_own()
    {
        Services.AddSingleton(Manager());

        var bar = RenderComponent<WindowBar>();

        Assert.Equal(WindowCatalog.All.Count(d => !d.RequiresSubject), bar.FindAll(".bar__key").Count);
    }

    [Fact]
    public void The_palette_opened_empty_lists_the_apps_windows_and_workspaces()
    {
        Services.AddSingleton<IDeskSearch>(new NoSearch());
        var palette = RenderComponent<CommandPalette>();
        Services.GetRequiredService<WindowManager>().RequestPalette();
        palette.WaitForState(() => palette.FindAll("[role=dialog]").Count == 1);

        var titles = palette.FindAll("[role=option]").Select(o => o.TextContent).ToList();

        Assert.Contains(titles, t => t.Contains(Window(WindowCatalog.Journal).Title));
        Assert.Equal(WindowCatalog.All.Count(d => !d.RequiresSubject) + WindowCatalog.Workspaces.Count, titles.Count);
    }

    private sealed class NoSearch : IDeskSearch
    {
        public Task<IReadOnlyList<DeskSearchResult>> SearchAsync(string text, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DeskSearchResult>>([]);
    }

    [Fact]
    public void A_desk_with_a_game_on_it_survives_a_reload()
    {
        var before = Manager(capacity: 5);
        before.Open(Window(WindowCatalog.Board));
        before.Open(Window(WindowCatalog.Game), new Dictionary<string, object> { ["GameId"] = 3470 }, "Mets at Braves");

        var after = new WindowManager(new AppWindowCatalog());
        after.Restore(DeskLayout.Parse(before.Capture().Serialise())!);

        var game = after.Windows.Single(w => w.Definition.Key == WindowCatalog.Game);
        Assert.Equal(3470, Assert.IsType<int>(game.Parameters["GameId"]));
        Assert.Equal("Mets at Braves", game.Title);
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(800)]
    public void The_journal_and_the_board_never_run_past_the_desk(double width)
    {
        var desk = new WindowManager(new AppWindowCatalog());
        desk.SetViewport(width, 900);
        desk.UpdateSettings(s => s.MaxConcurrentWindows = 4);
        desk.Open(Window(WindowCatalog.Journal));
        desk.Open(Window(WindowCatalog.Board));

        Assert.All(desk.Windows, w => Assert.True(w.X + w.Width <= width + 0.5,
            $"{w.Definition.Key} ends at {w.X + w.Width}, past a {width}px desk"));
    }

    [Fact]
    public void The_wager_door_re_aims_the_one_wager_window()
    {
        var desk = Manager();
        var launcher = new FollowUpLauncher();

        launcher.Launch(desk, WindowCatalog.Wager,
            new Dictionary<string, object> { ["GameId"] = 7, ["Market"] = "h2h" }, 7, "Bills at Chiefs");
        launcher.Launch(desk, WindowCatalog.Wager,
            new Dictionary<string, object> { ["GameId"] = 8, ["Market"] = "spread" }, 8, "Jets at Dolphins");

        var window = Assert.Single(desk.Windows);
        Assert.Equal(8, window.Parameters["GameId"]);
        Assert.Equal("Place wager · Jets at Dolphins", window.Title);
    }
}
