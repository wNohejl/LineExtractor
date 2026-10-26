using Bunit;
using LineOps.Desk.Windowing;
using LineOps.Web.Windowing;
using Microsoft.Extensions.DependencyInjection;
using TheDesk = LineOps.Desk.Windowing.Desk;

namespace LineOps.Web.Tests;

/// <summary>
/// The desk from the keyboard.
///
/// <para>
/// Reordering the row was a tab drag and nothing else, settings were a key in the strip with no
/// chord, and the only way back from an eviction was a footer key that said "Undo" without saying
/// what it would undo. windowing.js now turns Ctrl+Alt+arrows, Ctrl+, and Ctrl+Z into calls on the
/// desk, and holds nothing but the chords: which window moves, where it lands and what opens are
/// all decided here, where a test can reach them.
/// </para>
/// </summary>
public class DeskKeyboardTests : DeskTestContext
{
    private static WindowManager Manager(int capacity = 4)
    {
        var manager = new WindowManager(new AppWindowCatalog());
        manager.UpdateSettings(s => s.MaxConcurrentWindows = capacity);
        return manager;
    }

    private static WindowDefinition Window(string key) => WindowCatalog.Find(key)!;

    private static string[] Row(WindowManager desk)
        => desk.Windows.OrderBy(w => w.Sequence).Select(w => w.Definition.Key).ToArray();

    // ---- Moving a window -------------------------------------------------------

    [Fact]
    public void Shifted_moves_the_window_one_place()
    {
        string[] order = ["a", "b", "c"];

        Assert.Equal(["b", "a", "c"], TheDesk.Shifted(order, "b", -1));
        Assert.Equal(["a", "c", "b"], TheDesk.Shifted(order, "b", +1));
    }

    /// <summary>
    /// The row does not wrap: Ctrl+Alt+Left on the leftmost window is a key that does nothing,
    /// which is what the end of a list does everywhere else on the platform.
    /// </summary>
    [Fact]
    public void Shifted_at_the_end_returns_the_same_order()
    {
        string[] order = ["a", "b", "c"];

        Assert.Equal(order, TheDesk.Shifted(order, "a", -1));
        Assert.Equal(order, TheDesk.Shifted(order, "c", +1));
        Assert.Equal(order, TheDesk.Shifted(order, "missing", +1));
    }

    [Fact]
    public void Moving_shifts_the_focused_window_and_only_it()
    {
        var desk = Manager();
        desk.Open(Window(WindowCatalog.Ops));
        desk.Open(Window(WindowCatalog.Incidents));
        var runs = desk.Open(Window(WindowCatalog.Runs));
        desk.Focus(runs.Id);

        TheDesk.MoveFocused(desk, -1);

        Assert.Equal([WindowCatalog.Ops, WindowCatalog.Runs, WindowCatalog.Incidents], Row(desk));
        Assert.Equal(runs.Id, desk.FocusedId);
    }

    /// <summary>A move that cannot happen is not a step: Back must not have a no-op to walk through.</summary>
    [Fact]
    public void A_move_past_the_end_leaves_no_history()
    {
        var desk = Manager();
        desk.Open(Window(WindowCatalog.Ops));
        var incidents = desk.Open(Window(WindowCatalog.Incidents));
        desk.Focus(incidents.Id);
        var depth = desk.HistoryDepth;

        TheDesk.MoveFocused(desk, +1);

        Assert.Equal([WindowCatalog.Ops, WindowCatalog.Incidents], Row(desk));
        Assert.Equal(depth, desk.HistoryDepth);
    }

    // ---- Settings --------------------------------------------------------------

    [Fact]
    public void Settings_opens_the_window_manager()
    {
        var desk = Manager();

        TheDesk.OpenSettings(desk);

        Assert.Equal([WindowCatalog.Desk], Row(desk));
    }

    // ---- The palette's commands ------------------------------------------------

    [Fact]
    public async Task The_palette_lists_the_keyboard_commands_with_their_chords()
    {
        var desk = Manager();
        var commands = new DeskCommands(desk);

        var found = await commands.SearchAsync("move window", CancellationToken.None);

        Assert.Equal(["Move window left", "Move window right"], found.Select(r => r.Title));
        Assert.All(found, r => Assert.StartsWith("Ctrl+Alt+", r.Detail));

        var settings = Assert.Single(await commands.SearchAsync("settings", CancellationToken.None));
        Assert.Equal("Ctrl+,", settings.Detail);
    }

    [Fact]
    public async Task Choosing_move_window_left_moves_the_focused_window()
    {
        var desk = Manager();
        desk.Open(Window(WindowCatalog.Ops));
        var incidents = desk.Open(Window(WindowCatalog.Incidents));
        desk.Focus(incidents.Id);

        var left = (await new DeskCommands(desk).SearchAsync("left", CancellationToken.None)).Single();
        left.Open();

        Assert.Equal([WindowCatalog.Incidents, WindowCatalog.Ops], Row(desk));
    }

    // ---- Undo, labelled --------------------------------------------------------

    /// <summary>
    /// "Undo" said nothing about what would come back. The key names the window it reopens, the
    /// way a Mac's Edit menu says "Undo Close Board" rather than "Undo".
    /// </summary>
    [Fact]
    public void Footer_undo_names_the_window_it_reopens()
    {
        var desk = Services.GetRequiredService<WindowManager>();
        desk.UpdateSettings(s => s.MaxConcurrentWindows = 2);
        desk.Open(Window(WindowCatalog.Journal));
        desk.Open(Window(WindowCatalog.Ops));
        desk.Open(Window(WindowCatalog.Runs));
        Assert.NotNull(desk.LastEviction);

        var footer = RenderComponent<DeskFooter>();

        var undo = footer.Find(".ftr__undo");
        Assert.Equal("Reopen Journal", undo.TextContent.Trim());
        Assert.Equal("Ctrl+Z", undo.GetAttribute("title"));
    }

    [Theory]
    [InlineData(new[] { "Journal" }, "Reopen Journal")]
    [InlineData(new[] { "Journal", "Board" }, "Reopen 2 windows")]
    [InlineData(new string[0], "Undo")]
    public void The_undo_label_names_the_evicted_windows(string[] titles, string label)
        => Assert.Equal(label, DeskFooter.UndoLabel(titles));
}
