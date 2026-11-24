using Bunit;
using XLines.Desk.Primitives;
using XLines.Desk.Windowing;
using XLines.Desk.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using DeskShell = XLines.Desk.Windowing.Desk;

namespace XLines.Desk.Tests;

/// <summary>
/// What a column is, kept as one thing: collapsed or maximised, every window in it stays mounted
/// with what it holds; maximised is the column's, not one tab's; and every step that records
/// history leaves Back meaning that step, with the notice saying so when a step is refused.
/// </summary>
public class ColumnStateTests : SampleDeskTestContext
{
    private static WindowDefinition Window(string key) => SampleCatalog.Find(key);

    private static WindowManager Desk(int columns)
    {
        var manager = new WindowManager(new SampleCatalog());
        manager.UpdateSettings(s =>
        {
            s.MaxConcurrentWindows = columns;
            s.PrimaryWindowKey = null;
        });
        return manager;
    }

    private static string[][] Shape(WindowManager desk)
        => desk.Columns.Select(c => c.Tabs.Select(t => t.Definition.Key).ToArray()).ToArray();

    // ------------------------------------------------------------ kept mounted ----

    /// <summary>A panel that counts how often it was built and holds a draft, as a form does.</summary>
    public sealed class KeptDraft : DeskPanelBase
    {
        public static int Built;

        protected override void OnInitialized() => Built++;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "class", "draft");
            builder.AddAttribute(2, "onclick", EventCallback.Factory.Create(this, () => MarkUnsaved(true, "this draft")));
            builder.CloseElement();
        }
    }

    private static readonly WindowDefinition Draft = new()
    {
        Key = "kept-draft",
        Title = "Draft",
        Icon = "",
        ComponentType = typeof(KeptDraft)
    };

    private WindowManager Hosted(int columns)
    {
        Services.AddSingleton<IDeskAlerts>(new FakeAlerts());
        var manager = Services.GetRequiredService<WindowManager>();
        manager.UpdateSettings(s =>
        {
            s.MaxConcurrentWindows = columns;
            s.PrimaryWindowKey = null;
        });
        return manager;
    }

    private static AngleSharp.Dom.IElement Win(IRenderedComponent<DeskShell> desk, WindowInstance window)
        => desk.Find($"#win-{window.Id}");

    [Fact]
    public void A_collapsed_column_keeps_its_panel_and_its_input()
    {
        KeptDraft.Built = 0;
        var manager = Hosted(columns: 2);
        var draft = manager.Open(Draft);
        manager.Open(Window(SampleCatalog.Health));
        var desk = RenderComponent<DeskShell>();
        Win(desk, draft).QuerySelector(".draft")!.Click();

        desk.InvokeAsync(() => manager.ToggleMinimise(draft.Id));

        // Collapsed, the body is still on the page — hidden, inert and out of the tree.
        var body = Win(desk, draft).QuerySelector($"#winbody-{draft.Id}")!;
        Assert.True(body.HasAttribute("hidden"));
        Assert.True(body.HasAttribute("inert"));
        Assert.Equal("true", body.GetAttribute("aria-hidden"));
        Assert.NotNull(body.QuerySelector(".draft"));
        Assert.True(draft.HasUnsavedInput);

        desk.InvokeAsync(() => manager.Focus(draft.Id));

        Assert.False(Win(desk, draft).QuerySelector($"#winbody-{draft.Id}")!.HasAttribute("hidden"));
        Assert.Equal(1, KeptDraft.Built);
        Assert.True(draft.HasUnsavedInput);
    }

    [Fact]
    public void Maximising_another_column_keeps_every_other_window_mounted_and_out_of_reach()
    {
        KeptDraft.Built = 0;
        var manager = Hosted(columns: 2);
        var draft = manager.Open(Draft);
        var health = manager.Open(Window(SampleCatalog.Health));
        var desk = RenderComponent<DeskShell>();
        Win(desk, draft).QuerySelector(".draft")!.Click();

        desk.InvokeAsync(() => manager.ToggleMaximise(health.Id));

        var hidden = Win(desk, draft);
        Assert.Contains("win--behind", hidden.ClassList);
        Assert.True(hidden.HasAttribute("inert"));
        Assert.Equal("true", hidden.GetAttribute("aria-hidden"));
        Assert.Null(hidden.QuerySelector(".win__bar"));
        Assert.NotNull(hidden.QuerySelector(".draft"));
        Assert.DoesNotContain("win--behind", Win(desk, health).ClassList);

        desk.InvokeAsync(() => manager.ToggleMaximise(health.Id));

        Assert.DoesNotContain("win--behind", Win(desk, draft).ClassList);
        Assert.NotNull(Win(desk, draft).QuerySelector(".win__bar"));
        Assert.Equal(1, KeptDraft.Built);
        Assert.True(draft.HasUnsavedInput);
    }

    [Fact]
    public void A_panel_gone_takes_its_windows_unsaved_flag_with_it()
    {
        var manager = Hosted(columns: 2);
        var draft = manager.Open(Draft);
        var window = RenderComponent<AppWindow>(p => p.Add(x => x.Window, draft));
        window.Find(".draft").Click();
        Assert.True(draft.HasUnsavedInput);

        DisposeComponents();

        Assert.False(draft.HasUnsavedInput);
        Assert.Null(draft.UnsavedInputLabel);
    }

    // -------------------------------------------------------- maximised column ----

    [Fact]
    public void Maximised_is_the_columns_so_the_tab_chosen_next_restores_in_one_press()
    {
        var desk = Desk(columns: 2);
        var list = desk.Open(Window(SampleCatalog.List));
        desk.Open(Window(SampleCatalog.Health));
        desk.Focus(desk.Columns[0].Showing.Id);
        var jobs = desk.Open(Window(SampleCatalog.Jobs));
        Assert.Equal(desk.ColumnOf(list.Id)!.Id, desk.ColumnOf(jobs.Id)!.Id);

        desk.ToggleMaximise(jobs.Id);
        desk.Focus(list.Id);

        // The bar of the window now showing reads the column's state: it offers to restore.
        Assert.True(list.Maximised);
        Assert.Equal(desk.ColumnOf(list.Id)!.Id, desk.MaximisedColumn!.Id);

        desk.ToggleMaximise(list.Id);

        Assert.Null(desk.MaximisedColumn);
        Assert.All(desk.Windows, w => Assert.False(w.Maximised));
    }

    [Fact]
    public void A_tab_moved_out_of_a_maximised_column_is_not_left_unrendered()
    {
        var desk = Desk(columns: 1);
        var list = desk.Open(Window(SampleCatalog.List));
        var health = desk.Open(Window(SampleCatalog.Health));
        desk.ToggleMaximise(health.Id);
        desk.UpdateSettings(s => s.MaxConcurrentWindows = 2);

        Assert.True(desk.MoveToOwnColumn(list.Id));

        Assert.Equal(list.Id, desk.FocusedId);
        Assert.False(list.Maximised);
        Assert.True(desk.MaximisedColumn is null || desk.MaximisedColumn.Tabs.Contains(list));
    }

    [Fact]
    public void Focusing_a_window_outside_the_maximised_column_gives_the_row_back()
    {
        var desk = Desk(columns: 2);
        var list = desk.Open(Window(SampleCatalog.List));
        var health = desk.Open(Window(SampleCatalog.Health));
        desk.ToggleMaximise(health.Id);

        desk.Focus(list.Id);

        Assert.Null(desk.MaximisedColumn);
    }

    [Fact]
    public void Back_puts_a_maximised_column_back_as_it_was()
    {
        var desk = Desk(columns: 2);
        desk.Open(Window(SampleCatalog.List));
        var health = desk.Open(Window(SampleCatalog.Health));
        desk.ToggleMaximise(health.Id);

        desk.Open(Window(SampleCatalog.Jobs));
        Assert.True(desk.Back());

        Assert.Equal(desk.ColumnOf(health.Id)!.Id, desk.MaximisedColumn?.Id);
        Assert.True(health.Maximised);
    }

    // ----------------------------------------------------------------- history ----

    [Fact]
    public void Moving_a_tab_to_its_own_column_is_a_step_Back_takes_back_width_and_all()
    {
        var desk = Desk(columns: 2);
        var list = desk.Open(Window(SampleCatalog.List));
        var health = desk.Open(Window(SampleCatalog.Health));
        desk.Focus(desk.Columns[0].Showing.Id);
        var jobs = desk.Open(Window(SampleCatalog.Jobs));
        desk.AdjustSplit(jobs.Id, health.Id, 800, 400);
        var shape = Shape(desk);
        var weight = list.Weight;
        desk.UpdateSettings(s => s.MaxConcurrentWindows = 3);
        var depth = desk.HistoryDepth;

        Assert.True(desk.MoveToOwnColumn(jobs.Id));
        Assert.Equal(depth + 1, desk.HistoryDepth);

        Assert.True(desk.Back());

        Assert.Equal(shape, Shape(desk));
        Assert.Equal(weight, jobs.Weight);
        Assert.Equal(list.Weight, jobs.Weight);
    }

    [Fact]
    public void A_step_after_a_close_takes_its_reopen_away()
    {
        var desk = Desk(columns: 3);
        var notes = desk.Open(Window(SampleCatalog.Notes));
        var item = desk.Open(Window(SampleCatalog.Item), new Dictionary<string, object> { ["ItemId"] = 1 }, "First item");
        desk.Close(notes.Id);
        Assert.Equal("Closed Notes.", desk.Notice);

        desk.Follow(item.Id, Window(SampleCatalog.Company), new Dictionary<string, object> { ["CompanyId"] = 7 }, "Seventh company");
        Assert.Null(desk.Notice);

        desk.Close(desk.Open(Window(SampleCatalog.Notes)).Id);
        desk.PageBack(item.Id);
        Assert.Null(desk.Notice);

        desk.Close(desk.Open(Window(SampleCatalog.Notes)).Id);
        desk.PageForward(item.Id);
        Assert.Null(desk.Notice);
    }

    [Fact]
    public void A_re_aimed_singleton_takes_the_reopen_away_too()
    {
        var desk = Desk(columns: 3);
        desk.Open(Window(SampleCatalog.Details), new Dictionary<string, object> { ["ItemId"] = 1 });
        desk.Close(desk.Open(Window(SampleCatalog.Notes)).Id);
        Assert.NotNull(desk.Notice);

        desk.Open(Window(SampleCatalog.Details), new Dictionary<string, object> { ["ItemId"] = 2 });

        Assert.Null(desk.Notice);
    }

    // ---------------------------------------------------------------- refusals ----

    private const string Refused = "Nothing changed: Notes holds input that has not been saved. Save or discard it, then try again.";

    [Fact]
    public void A_workspace_refused_for_unsaved_input_says_which_window_holds_it()
    {
        var desk = Desk(columns: 3);
        var notes = desk.Open(Window(SampleCatalog.Notes));
        desk.SetUnsaved(notes.Id, true, "this note");

        Assert.False(desk.ApplyWorkspace(desk.Catalog.Workspaces[0]));

        Assert.Equal(Refused, desk.Notice);
        Assert.Empty(desk.NoticeTitles);
    }

    [Fact]
    public void Back_refused_for_unsaved_input_says_which_window_holds_it()
    {
        var desk = Desk(columns: 3);
        desk.Open(Window(SampleCatalog.List));
        var notes = desk.Open(Window(SampleCatalog.Notes));
        desk.SetUnsaved(notes.Id, true, "this note");

        Assert.False(desk.Back());
        Assert.Equal(Refused, desk.Notice);

        desk.ClearNotice();
        Assert.False(desk.TraverseTo(desk.HistoryDepth - 1));
        Assert.Equal(Refused, desk.Notice);
    }

    // --------------------------------------------------------------- follow-ups ----

    [Fact]
    public void A_follow_up_floats_when_the_desk_is_at_its_ceiling_with_a_column_free()
    {
        var desk = Desk(columns: 2);
        for (var i = 0; i < DeskSettings.MaxOpenWindows; i++)
            desk.Open(Window(SampleCatalog.Chart), new Dictionary<string, object> { ["ItemId"] = i });
        desk.UpdateSettings(s => s.MaxConcurrentWindows = 4);
        Assert.False(desk.AtCapacity);
        Assert.True(desk.AtCeiling);

        var launcher = new FollowUpLauncher();
        launcher.Launch(desk, SampleCatalog.Chart, "ItemId", 99, "Ninety-ninth");

        Assert.Equal(DeskSettings.MaxOpenWindows, desk.OpenCount);
        Assert.Single(launcher.Open);
    }
}
