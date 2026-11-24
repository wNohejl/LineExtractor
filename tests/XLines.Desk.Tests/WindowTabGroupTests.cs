using XLines.Desk.Windowing;
using XLines.Desk.Tests.Support;

namespace XLines.Desk.Tests;

/// <summary>
/// The column limit tabs instead of closing (ADR 0027).
///
/// <para>
/// Opening past the limit used to close the least recently used window and offer it back from
/// the header. Now the limit is how many columns sit side by side: the next window joins a column
/// as a tab, nothing closes, and the strip in front of each group lists its windows most recently
/// shown first. These are the rules of where a window lands, how a strip orders itself, what a
/// close does to a group, and what survives a reload.
/// </para>
/// </summary>
public class WindowTabGroupTests
{
    private static WindowDefinition Window(string key) => SampleCatalog.Find(key);

    private static WindowManager Desk(int columns, string? primary = null)
    {
        var manager = new WindowManager(new SampleCatalog());
        manager.UpdateSettings(s =>
        {
            s.MaxConcurrentWindows = columns;
            s.PrimaryWindowKey = primary;
        });
        return manager;
    }

    private static string[] Keys(WindowColumn column) => column.Tabs.Select(t => t.Definition.Key).ToArray();

    private static string[][] Shape(WindowManager desk) => desk.Columns.Select(Keys).ToArray();

    // ----------------------------------------------------------------- the limit ----

    [Fact]
    public void At_the_limit_a_window_opens_as_a_tab_and_nothing_closes()
    {
        var desk = Desk(columns: 2);
        var list = desk.Open(Window(SampleCatalog.List));
        var health = desk.Open(Window(SampleCatalog.Health));

        var jobs = desk.Open(Window(SampleCatalog.Jobs));

        Assert.Equal(3, desk.OpenCount);
        Assert.Equal(2, desk.ColumnCount);
        Assert.Equal(1, desk.TabbedCount);
        Assert.Contains(list, desk.Windows);
        Assert.Contains(health, desk.Windows);
        Assert.Null(desk.Notice);

        // The new window is in view and is the one the operator is in.
        Assert.True(desk.IsShowing(jobs));
        Assert.Equal(jobs.Id, desk.FocusedId);
    }

    [Fact]
    public void A_tab_takes_its_columns_place_and_width_and_the_row_does_not_move()
    {
        var desk = Desk(columns: 2);
        var list = desk.Open(Window(SampleCatalog.List));
        var health = desk.Open(Window(SampleCatalog.Health));
        var (x, width) = (list.X, list.Width);
        var healthX = health.X;

        desk.Focus(desk.Columns[0].Showing.Id);

        var jobs = desk.Open(Window(SampleCatalog.Jobs));

        Assert.Same(list, desk.ColumnOf(jobs.Id)!.Tabs[1]);
        Assert.Equal(x, jobs.X);
        Assert.Equal(width, jobs.Width);
        Assert.Equal(healthX, health.X);
    }

    [Fact]
    public void At_the_limit_a_window_opens_as_a_tab_in_the_window_last_selected()
    {
        var desk = Desk(columns: 2);
        var list = desk.Open(Window(SampleCatalog.List));
        desk.Open(Window(SampleCatalog.Health));
        desk.Focus(list.Id);

        var jobs = desk.Open(Window(SampleCatalog.Jobs));

        Assert.Equal([SampleCatalog.Jobs, SampleCatalog.List], Keys(desk.ColumnOf(jobs.Id)!));
        Assert.True(desk.IsShowing(jobs));
    }

    [Fact]
    public void Windows_opened_one_after_another_stack_in_the_column_being_worked_in()
    {
        var desk = Desk(columns: 3);
        desk.Open(Window(SampleCatalog.List));
        desk.Open(Window(SampleCatalog.Health));
        desk.Open(Window(SampleCatalog.Alerts));

        desk.Open(Window(SampleCatalog.Jobs));
        desk.Open(Window(SampleCatalog.Notes));

        Assert.Equal(
            [
                [SampleCatalog.List],
                [SampleCatalog.Health],
                [SampleCatalog.Notes, SampleCatalog.Jobs, SampleCatalog.Alerts],
            ],
            Shape(desk));
    }

    [Fact]
    public void The_selected_window_takes_the_tab_even_when_it_is_the_primary()
    {
        var desk = Desk(columns: 3, primary: SampleCatalog.Health);
        var health = desk.Open(Window(SampleCatalog.Health));
        desk.Open(Window(SampleCatalog.List));
        desk.Open(Window(SampleCatalog.Alerts));
        desk.Focus(health.Id);

        var jobs = desk.Open(Window(SampleCatalog.Jobs));

        Assert.Equal([SampleCatalog.Jobs, SampleCatalog.Health], Keys(desk.ColumnOf(jobs.Id)!));
    }

    [Fact]
    public void The_header_names_where_the_next_window_goes_before_the_click()
    {
        var desk = Desk(columns: 2);
        desk.Open(Window(SampleCatalog.List));
        Assert.Null(desk.NextTabTarget);

        desk.Open(Window(SampleCatalog.Health));

        Assert.Equal(SampleCatalog.Health, desk.NextTabTarget!.Showing.Definition.Key);
        Assert.Equal("2 of 2 columns in use, 2 windows open. The next window opens as a tab in Health.",
            DeskHeader.CapacitySentence(desk));
    }

    // ------------------------------------------------------------- no duplicates ----

    [Fact]
    public void Asking_again_for_a_subject_already_open_brings_that_window_forward_rather_than_a_copy()
    {
        var desk = Desk(columns: 1);
        var first = desk.Open(Window(SampleCatalog.Item), new Dictionary<string, object> { ["ItemId"] = 1 }, "First item");
        var second = desk.Open(Window(SampleCatalog.Item), new Dictionary<string, object> { ["ItemId"] = 2 }, "Second item");
        Assert.False(desk.IsShowing(first));

        var again = desk.Open(Window(SampleCatalog.Item), new Dictionary<string, object> { ["ItemId"] = 1 }, "First item");

        Assert.Same(first, again);
        Assert.Equal(2, desk.OpenCount);
        Assert.True(desk.IsShowing(first));
        Assert.NotSame(first, second);
    }

    [Fact]
    public void A_window_with_no_subject_opens_once()
    {
        var desk = Desk(columns: 2);
        var chart = desk.Open(Window(SampleCatalog.Chart));

        Assert.Same(chart, desk.Open(Window(SampleCatalog.Chart)));
        Assert.Equal(1, desk.OpenCount);
    }

    // ------------------------------------------------------- dragging into a window ----

    [Fact]
    public void A_window_dropped_on_another_joins_its_column_in_front_and_its_own_column_closes_up()
    {
        var desk = Desk(columns: 3);
        var list = desk.Open(Window(SampleCatalog.List));
        var health = desk.Open(Window(SampleCatalog.Health));
        var alerts = desk.Open(Window(SampleCatalog.Alerts));

        Assert.True(desk.MoveIntoColumn(list.Id, alerts.Id));

        Assert.Equal([[SampleCatalog.Health], [SampleCatalog.List, SampleCatalog.Alerts]], Shape(desk));
        Assert.Equal(list.Id, desk.FocusedId);
        Assert.Equal(alerts.X, list.X);
        Assert.Equal(alerts.Width, list.Width);
        Assert.True(health.X < list.X);
    }

    [Fact]
    public void A_tab_behind_can_be_dragged_out_of_its_group_into_another_window()
    {
        var desk = Desk(columns: 2);
        var list = desk.Open(Window(SampleCatalog.List));
        var health = desk.Open(Window(SampleCatalog.Health));
        var jobs = desk.Open(Window(SampleCatalog.Jobs));
        Assert.Equal([SampleCatalog.Jobs, SampleCatalog.Health], Keys(desk.ColumnOf(jobs.Id)!));

        Assert.True(desk.MoveIntoColumn(health.Id, list.Id));

        Assert.Equal([[SampleCatalog.Health, SampleCatalog.List], [SampleCatalog.Jobs]], Shape(desk));
        Assert.True(desk.IsShowing(health));
        Assert.True(desk.IsShowing(jobs));
    }

    [Fact]
    public void Dropping_a_window_on_its_own_column_does_nothing_and_Back_undoes_a_drop()
    {
        var desk = Desk(columns: 2);
        var list = desk.Open(Window(SampleCatalog.List));
        var health = desk.Open(Window(SampleCatalog.Health));
        var jobs = desk.Open(Window(SampleCatalog.Jobs));

        Assert.False(desk.MoveIntoColumn(jobs.Id, health.Id));

        Assert.True(desk.MoveIntoColumn(list.Id, jobs.Id));
        Assert.Single(desk.Columns);

        Assert.True(desk.Back());
        Assert.Equal(2, desk.ColumnCount);
    }

    // ------------------------------------------------------------- recency order ----

    [Fact]
    public void The_column_is_most_recently_shown_first()
    {
        var desk = Desk(columns: 1);
        var list = desk.Open(Window(SampleCatalog.List));
        var health = desk.Open(Window(SampleCatalog.Health));
        var jobs = desk.Open(Window(SampleCatalog.Jobs));
        var column = () => desk.Columns.Single();

        Assert.Equal([jobs, health, list], column().Tabs);

        desk.Focus(list.Id);
        Assert.Equal([list, jobs, health], column().Tabs);

        desk.Focus(health.Id);
        Assert.Equal([health, list, jobs], column().Tabs);

        // Selecting the one showing changes nothing about the order.
        desk.Focus(health.Id);
        Assert.Equal([health, list, jobs], column().Tabs);
        Assert.Equal(health.Id, desk.FocusedId);
    }

    [Fact]
    public void Opening_a_singleton_that_waits_as_a_tab_brings_it_forward_rather_than_doubling_it()
    {
        var desk = Desk(columns: 1);
        var health = desk.Open(Window(SampleCatalog.Health));
        desk.Open(Window(SampleCatalog.Jobs));
        Assert.False(desk.IsShowing(health));

        var again = desk.Open(Window(SampleCatalog.Health));

        Assert.Same(health, again);
        Assert.Equal(2, desk.OpenCount);
        Assert.True(desk.IsShowing(health));
        Assert.Equal(health.Id, desk.FocusedId);
    }

    // ------------------------------------------------------------------- closing ----

    [Fact]
    public void Closing_a_tab_behind_leaves_the_one_showing()
    {
        var desk = Desk(columns: 1);
        var list = desk.Open(Window(SampleCatalog.List));
        var health = desk.Open(Window(SampleCatalog.Health));
        var jobs = desk.Open(Window(SampleCatalog.Jobs));

        desk.Close(list.Id);

        Assert.Equal([jobs, health], desk.Columns.Single().Tabs);
        Assert.Equal(jobs.Id, desk.FocusedId);
        Assert.Equal("Closed List.", desk.Notice);
    }

    [Fact]
    public void Closing_the_tab_showing_reveals_the_next_most_recent_and_focus_goes_to_it()
    {
        var desk = Desk(columns: 1);
        var list = desk.Open(Window(SampleCatalog.List));
        var health = desk.Open(Window(SampleCatalog.Health));
        var jobs = desk.Open(Window(SampleCatalog.Jobs));
        desk.Focus(list.Id);

        desk.Close(list.Id);

        Assert.Equal([jobs, health], desk.Columns.Single().Tabs);
        Assert.Equal(jobs.Id, desk.FocusedId);
    }

    [Fact]
    public void A_group_left_with_one_window_is_a_plain_window_again()
    {
        var desk = Desk(columns: 1);
        var list = desk.Open(Window(SampleCatalog.List));
        var health = desk.Open(Window(SampleCatalog.Health));
        Assert.True(desk.Columns.Single().IsGroup);

        desk.Close(health.Id);

        Assert.False(desk.Columns.Single().IsGroup);
        Assert.True(desk.IsShowing(list));
    }

    [Fact]
    public void Back_after_closing_a_tab_puts_it_back_in_its_group()
    {
        var desk = Desk(columns: 2);
        desk.Open(Window(SampleCatalog.List));
        desk.Open(Window(SampleCatalog.Health));
        var jobs = desk.Open(Window(SampleCatalog.Jobs));
        var before = Shape(desk);

        desk.Close(jobs.Id);
        Assert.True(desk.Back());

        Assert.Equal(before, Shape(desk));
        Assert.Contains(jobs, desk.Windows);
    }

    // --------------------------------------------------------------- moving out ----

    [Fact]
    public void A_tab_moves_to_its_own_column_only_while_one_is_free()
    {
        var desk = Desk(columns: 2);
        desk.Open(Window(SampleCatalog.List));
        desk.Open(Window(SampleCatalog.Health));
        desk.Focus(desk.Columns[0].Showing.Id);
        var jobs = desk.Open(Window(SampleCatalog.Jobs));

        Assert.False(desk.CanMoveToOwnColumn(jobs.Id));
        Assert.False(desk.MoveToOwnColumn(jobs.Id));

        desk.UpdateSettings(s => s.MaxConcurrentWindows = 3);

        Assert.True(desk.MoveToOwnColumn(jobs.Id));
        Assert.Equal(
            [[SampleCatalog.List], [SampleCatalog.Jobs], [SampleCatalog.Health]],
            Shape(desk));
        Assert.Equal(jobs.Id, desk.FocusedId);
        Assert.All(desk.Windows, w => Assert.True(w.X + w.Width <= desk.DeskWidth + 0.5));
    }

    [Fact]
    public void A_plain_window_has_nowhere_to_move_out_of()
    {
        var desk = Desk(columns: 3);
        var list = desk.Open(Window(SampleCatalog.List));

        Assert.False(desk.CanMoveToOwnColumn(list.Id));
    }

    [Fact]
    public void A_lowered_limit_folds_the_extra_columns_left_to_right_behind_what_shows()
    {
        var desk = Desk(columns: 5);
        foreach (var key in new[] { SampleCatalog.List, SampleCatalog.Health, SampleCatalog.Alerts, SampleCatalog.Jobs, SampleCatalog.Notes })
            desk.Open(Window(key));

        desk.UpdateSettings(s => s.MaxConcurrentWindows = 3);

        Assert.Equal(
            [
                [SampleCatalog.List, SampleCatalog.Jobs],
                [SampleCatalog.Health, SampleCatalog.Notes],
                [SampleCatalog.Alerts],
            ],
            Shape(desk));

        // The window the operator was in folded behind another, so focus is on what they now see.
        Assert.True(desk.IsShowing(desk.Find(desk.FocusedId!)!));
    }

    // -------------------------------------------------------------- the ceiling ----

    [Fact]
    public void The_ceiling_refuses_an_open_and_says_so_without_closing_anything()
    {
        var desk = Desk(columns: 2);
        for (var i = 0; i < DeskSettings.MaxOpenWindows; i++)
            desk.Open(Window(SampleCatalog.Chart), new Dictionary<string, object> { ["ItemId"] = i });
        var depth = desk.HistoryDepth;
        var focused = desk.FocusedId;

        var landed = desk.Open(Window(SampleCatalog.Health));

        Assert.Equal(DeskSettings.MaxOpenWindows, desk.OpenCount);
        Assert.DoesNotContain(desk.Windows, w => w.Definition.Key == SampleCatalog.Health);
        Assert.Equal($"The desk holds {DeskSettings.MaxOpenWindows} windows, its most. Close one to open Health.", desk.Notice);
        Assert.Empty(desk.NoticeTitles);
        Assert.Equal(focused, landed.Id);
        Assert.Equal(depth, desk.HistoryDepth);
        Assert.True(desk.AtCeiling);
    }

    // ------------------------------------------------------------ the whole row ----

    [Fact]
    public void Minimising_collapses_the_whole_group_and_focusing_a_tab_opens_it_out()
    {
        var desk = Desk(columns: 2);
        var list = desk.Open(Window(SampleCatalog.List));
        desk.Open(Window(SampleCatalog.Health));
        desk.Focus(desk.Columns[0].Showing.Id);
        var jobs = desk.Open(Window(SampleCatalog.Jobs));

        desk.ToggleMinimise(jobs.Id);
        Assert.All(desk.ColumnOf(jobs.Id)!.Tabs, t => Assert.True(t.Minimised));

        desk.Focus(list.Id);

        Assert.All(desk.ColumnOf(jobs.Id)!.Tabs, t => Assert.False(t.Minimised));
        Assert.True(desk.IsShowing(list));
    }

    [Fact]
    public void Maximising_a_tab_brings_it_forward_and_the_column_holds_the_row()
    {
        var desk = Desk(columns: 2);
        var list = desk.Open(Window(SampleCatalog.List));
        desk.Open(Window(SampleCatalog.Health));
        desk.Open(Window(SampleCatalog.Jobs));

        desk.ToggleMaximise(list.Id);

        Assert.True(desk.IsShowing(list));
        Assert.Equal(desk.ColumnOf(list.Id)!.Id, desk.MaximisedColumn!.Id);
        Assert.Equal(desk.DeskWidth - 16, list.Width, 1);
    }

    [Fact]
    public void Reordering_by_any_tab_moves_its_whole_column()
    {
        var desk = Desk(columns: 2);
        var list = desk.Open(Window(SampleCatalog.List));
        var health = desk.Open(Window(SampleCatalog.Health));
        desk.Focus(desk.Columns[0].Showing.Id);
        desk.Open(Window(SampleCatalog.Jobs));

        desk.Reorder([health.Id, list.Id]);

        Assert.Equal([[SampleCatalog.Health], [SampleCatalog.Jobs, SampleCatalog.List]], Shape(desk));
    }

    [Fact]
    public void Moving_the_focused_window_with_the_keyboard_moves_its_column()
    {
        var desk = Desk(columns: 2);
        desk.Open(Window(SampleCatalog.List));
        desk.Open(Window(SampleCatalog.Health));
        desk.Focus(desk.Columns[0].Showing.Id);
        var jobs = desk.Open(Window(SampleCatalog.Jobs));

        XLines.Desk.Windowing.Desk.MoveFocused(desk, 1);

        Assert.Equal([[SampleCatalog.Health], [SampleCatalog.Jobs, SampleCatalog.List]], Shape(desk));
        Assert.Equal(jobs.Id, desk.FocusedId);
    }

    [Fact]
    public void Every_tab_in_a_column_shares_its_width_through_a_divider_drag()
    {
        var desk = Desk(columns: 2);
        var list = desk.Open(Window(SampleCatalog.List));
        var health = desk.Open(Window(SampleCatalog.Health));
        desk.Focus(desk.Columns[0].Showing.Id);
        var jobs = desk.Open(Window(SampleCatalog.Jobs));

        desk.AdjustSplit(jobs.Id, health.Id, 800, 400);

        Assert.Equal(list.Width, jobs.Width);
        Assert.Equal(list.Weight, jobs.Weight);

        // The tab behind shows at the width the column has; nothing reflows when it comes forward.
        var width = jobs.Width;
        desk.Focus(list.Id);
        Assert.Equal(width, list.Width);
    }

    [Fact]
    public void A_columns_minimum_is_its_widest_tabs()
    {
        var desk = Desk(columns: 1);
        var health = desk.Open(Window(SampleCatalog.Health));
        desk.Open(Window(SampleCatalog.List));

        Assert.Equal(680, desk.MinWidthOf(health));
    }

    // ------------------------------------------------------------- persistence ----

    [Fact]
    public void A_layout_round_trips_its_groups_their_order_and_the_focus()
    {
        var before = Desk(columns: 2);
        before.Open(Window(SampleCatalog.List));
        before.Open(Window(SampleCatalog.Health));
        before.Open(Window(SampleCatalog.Jobs));
        var alerts = before.Open(Window(SampleCatalog.Alerts));
        before.Focus(before.Windows.Single(w => w.Definition.Key == SampleCatalog.List).Id);

        var after = new WindowManager(new SampleCatalog());
        after.Restore(DeskLayout.Parse(before.Capture().Serialise())!);

        Assert.Equal(Shape(before), Shape(after));
        Assert.Equal(SampleCatalog.List, after.Find(after.FocusedId!)!.Definition.Key);
        Assert.Equal(alerts.Definition.Key, after.Columns[1].Showing.Definition.Key);
    }

    /// <summary>
    /// A layout written before tab groups carries no column or rank: every window was its own
    /// column. It still restores, and a row longer than the limit folds into tabs.
    /// </summary>
    [Fact]
    public void A_layout_saved_before_tabs_still_restores()
    {
        const string old = """
            {"version":1,"maxConcurrentWindows":2,"ceilingChosen":true,"primaryWindowKey":null,"primaryShare":0.45,
             "resolutionMode":0,"customWidth":1920,"customHeight":1080,
             "windows":[
               {"key":"list","titleOverride":null,"weight":1.9,"minimised":false,"parameters":null},
               {"key":"health","titleOverride":null,"weight":1,"minimised":false,"parameters":null},
               {"key":"jobs","titleOverride":null,"weight":1,"minimised":false,"parameters":null}],
             "focused":1,
             "workspaces":[{"name":"Mine","windowKeys":["list","jobs"]}]}
            """;

        var desk = new WindowManager(new SampleCatalog());
        desk.Restore(DeskLayout.Parse(old)!);

        Assert.Equal([[SampleCatalog.List, SampleCatalog.Jobs], [SampleCatalog.Health]], Shape(desk));
        Assert.Equal(SampleCatalog.Health, desk.Find(desk.FocusedId!)!.Definition.Key);
        Assert.Null(Assert.Single(desk.UserWorkspaces).Columns);
    }

    [Fact]
    public void A_saved_workspace_keeps_its_groups()
    {
        var desk = Desk(columns: 2);
        desk.Open(Window(SampleCatalog.List));
        desk.Open(Window(SampleCatalog.Health));
        desk.Open(Window(SampleCatalog.Jobs));
        var shape = Shape(desk);

        var workspace = desk.SaveWorkspace("Morning")!;
        Assert.Equal("3 windows in 2 columns, saved here", workspace.Description);

        desk.CloseAll();
        Assert.True(desk.ApplyWorkspace(workspace));
        Assert.Equal(shape, Shape(desk));

        // And through the browser's storage.
        var after = new WindowManager(new SampleCatalog());
        after.Restore(DeskLayout.Parse(desk.Capture().Serialise())!);
        Assert.True(after.ApplyWorkspace(after.UserWorkspaces.Single()));
        Assert.Equal(shape, Shape(after));
    }

    [Fact]
    public void A_workspace_longer_than_the_limit_keeps_the_limit_and_tabs_the_rest()
    {
        var desk = Desk(columns: 2);

        desk.ApplyWorkspace(new Workspace("Big", "", [SampleCatalog.Health, SampleCatalog.Alerts, SampleCatalog.Jobs]));

        Assert.Equal(2, desk.Settings.MaxConcurrentWindows);
        Assert.Equal([[SampleCatalog.Health, SampleCatalog.Jobs], [SampleCatalog.Alerts]], Shape(desk));
    }

    // ------------------------------------------------------------------- access ----

    private sealed class Visitor : IDeskAccess
    {
        public bool CanOperate => false;
    }

    [Fact]
    public void A_visitor_never_gets_a_tab_for_a_window_they_may_not_see()
    {
        var before = Desk(columns: 1);
        before.Open(Window(SampleCatalog.List));
        before.Open(Window(SampleCatalog.Notes));
        before.Open(Window(SampleCatalog.Health));
        Assert.Equal(3, before.Columns.Single().Tabs.Count);

        var visitor = new WindowManager(new SampleCatalog(), new Visitor());
        visitor.Restore(DeskLayout.Parse(before.Capture().Serialise())!);

        Assert.Equal([[SampleCatalog.Health, SampleCatalog.List]], Shape(visitor));
        Assert.Throws<InvalidOperationException>(() => visitor.Open(Window(SampleCatalog.Notes)));
    }
}
