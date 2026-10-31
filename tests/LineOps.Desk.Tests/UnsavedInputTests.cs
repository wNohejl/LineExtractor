using Bunit;
using LineOps.Desk.Primitives;
using LineOps.Desk.Windowing;
using LineOps.Desk.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Desk.Tests;

/// <summary>
/// A window holding input that has not been saved does not go away without asking.
///
/// <para>
/// A half-typed note died with its window: the close key closed it, Close all closed
/// it, and a full desk evicted it to make room for whatever was opened next, least recently used
/// first — which a form the operator stopped to look something up from always is. The HIG's rule
/// is to confirm before closing when content would be lost. So a panel says it is holding input,
/// the close key asks before discarding it, and eviction passes it by.
/// </para>
/// </summary>
public class UnsavedInputTests : SampleDeskTestContext
{
    private static WindowDefinition Window(string key) => SampleCatalog.Find(key);

    private static WindowManager Desk(int capacity = 4)
    {
        var manager = new WindowManager(new SampleCatalog());
        manager.UpdateSettings(s => s.MaxConcurrentWindows = capacity);
        return manager;
    }

    // ------------------------------------------------------------------ manager ----

    [Fact]
    public void Close_asks_when_a_window_holds_unsaved_input()
    {
        var desk = Desk();
        var notes = desk.Open(Window(SampleCatalog.Notes));
        var asked = new List<WindowInstance>();
        desk.CloseNeedsConfirmation += asked.Add;

        desk.SetUnsaved(notes.Id, true, "this note");

        Assert.False(desk.TryClose(notes.Id));
        Assert.Same(notes, desk.Windows.Single());
        Assert.Same(notes, Assert.Single(asked));
        Assert.True(notes.HasUnsavedInput);
        Assert.Equal("this note", notes.UnsavedInputLabel);
    }

    [Fact]
    public void Close_proceeds_when_clean()
    {
        var desk = Desk();
        var notes = desk.Open(Window(SampleCatalog.Notes));
        var asked = 0;
        desk.CloseNeedsConfirmation += _ => asked++;

        // Typed, then saved: a window that was dirty and is clean again closes like any other.
        desk.SetUnsaved(notes.Id, true, "this note");
        desk.SetUnsaved(notes.Id, false, null);

        Assert.True(desk.TryClose(notes.Id));
        Assert.Empty(desk.Windows);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void Close_itself_does_not_ask()
    {
        // What the confirmation calls once the operator has said Discard.
        var desk = Desk();
        var notes = desk.Open(Window(SampleCatalog.Notes));
        desk.SetUnsaved(notes.Id, true, "this note");

        desk.Close(notes.Id);

        Assert.Empty(desk.Windows);
    }

    [Fact]
    public void Eviction_skips_a_window_with_unsaved_input()
    {
        var desk = Desk(capacity: 2);
        var notes = desk.Open(Window(SampleCatalog.Notes));
        var list = desk.Open(Window(SampleCatalog.List));
        desk.SetUnsaved(notes.Id, true, "this note");

        // Notes is the one ignored longest, so it is the one LRU would have taken.
        var health = desk.Open(Window(SampleCatalog.Health));

        Assert.Equal(2, desk.OpenCount);
        Assert.Contains(notes, desk.Windows);
        Assert.Contains(health, desk.Windows);
        Assert.DoesNotContain(list, desk.Windows);
        Assert.Equal("Closed List to make room for Health.", desk.LastEviction);
    }

    [Fact]
    public void Eviction_refuses_when_every_window_is_dirty()
    {
        var desk = Desk(capacity: 2);
        var notes = desk.Open(Window(SampleCatalog.Notes));
        var list = desk.Open(Window(SampleCatalog.List));
        desk.SetUnsaved(notes.Id, true, "this note");
        desk.SetUnsaved(list.Id, true, "this order");
        var depth = desk.HistoryDepth;

        var landed = desk.Open(Window(SampleCatalog.Health));

        Assert.Equal(2, desk.OpenCount);
        Assert.DoesNotContain(desk.Windows, w => w.Definition.Key == SampleCatalog.Health);
        Assert.Equal("Every window holds unsaved input; save or discard one to open Health.", desk.LastEviction);

        // The operator is put in front of the window they have to deal with first, and an open
        // that did not happen is not a step Back can undo.
        Assert.Same(notes, landed);
        Assert.Equal(notes.Id, desk.FocusedId);
        Assert.Equal(depth, desk.HistoryDepth);
    }

    [Fact]
    public void A_lowered_ceiling_closes_clean_windows_and_leaves_dirty_ones()
    {
        var desk = Desk(capacity: 3);
        var notes = desk.Open(Window(SampleCatalog.Notes));
        var list = desk.Open(Window(SampleCatalog.List));
        var health = desk.Open(Window(SampleCatalog.Health));
        desk.SetUnsaved(notes.Id, true, "this note");
        desk.SetUnsaved(list.Id, true, "this order");

        desk.UpdateSettings(s => s.MaxConcurrentWindows = 1);

        // Over the limit, but nothing typed has gone.
        Assert.DoesNotContain(health, desk.Windows);
        Assert.Contains(notes, desk.Windows);
        Assert.Contains(list, desk.Windows);
    }

    [Fact]
    public void Close_all_asks_once_when_any_window_is_dirty()
    {
        var desk = Desk();
        desk.Open(Window(SampleCatalog.List));
        var notes = desk.Open(Window(SampleCatalog.Notes));
        var health = desk.Open(Window(SampleCatalog.Health));
        desk.SetUnsaved(notes.Id, true, "this note");
        desk.SetUnsaved(health.Id, true, "this note");
        var asked = new List<WindowInstance>();
        desk.CloseNeedsConfirmation += asked.Add;

        Assert.False(desk.TryCloseAll());
        Assert.Equal(3, desk.OpenCount);
        Assert.Same(notes, Assert.Single(asked));

        desk.CloseAll();

        Assert.Empty(desk.Windows);
    }

    [Fact]
    public void Close_all_proceeds_when_every_window_is_clean()
    {
        var desk = Desk();
        desk.Open(Window(SampleCatalog.List));
        desk.Open(Window(SampleCatalog.Notes));

        Assert.True(desk.TryCloseAll());
        Assert.Empty(desk.Windows);
    }

    // ------------------------------------------------------------------ history ----

    /// <summary>
    /// Back undoes the last open, and the last open was Notes the operator is typing in.
    /// The step would close it, so it does not happen: the manager asks, as the close key does.
    /// </summary>
    [Fact]
    public void Back_refuses_to_drop_a_dirty_window()
    {
        var desk = Desk();
        desk.Open(Window(SampleCatalog.List));
        var notes = desk.Open(Window(SampleCatalog.Notes));
        desk.SetUnsaved(notes.Id, true, "this note");
        var asked = new List<WindowInstance>();
        desk.CloseNeedsConfirmation += asked.Add;
        var depth = desk.HistoryDepth;

        Assert.False(desk.Back());

        Assert.Contains(notes, desk.Windows);
        Assert.Same(notes, Assert.Single(asked));
        Assert.Equal(depth, desk.HistoryDepth);
        Assert.False(desk.CanGoForward);
    }

    [Fact]
    public void Back_proceeds_when_the_step_keeps_the_dirty_window()
    {
        // The step closes Health; the dirty Notes is in the moment it returns to.
        var desk = Desk();
        var notes = desk.Open(Window(SampleCatalog.Notes));
        var health = desk.Open(Window(SampleCatalog.Health));
        desk.SetUnsaved(notes.Id, true, "this note");
        var asked = 0;
        desk.CloseNeedsConfirmation += _ => asked++;

        Assert.True(desk.Back());

        Assert.Contains(notes, desk.Windows);
        Assert.DoesNotContain(health, desk.Windows);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void Forward_refuses_to_drop_a_dirty_window()
    {
        // Close Notes, step back to bring it back, start typing: Forward would close it again.
        var desk = Desk();
        desk.Open(Window(SampleCatalog.List));
        var notes = desk.Open(Window(SampleCatalog.Notes));
        desk.Close(notes.Id);
        Assert.True(desk.Back());
        desk.SetUnsaved(notes.Id, true, "this note");
        var asked = new List<WindowInstance>();
        desk.CloseNeedsConfirmation += asked.Add;

        Assert.False(desk.Forward());

        Assert.Contains(notes, desk.Windows);
        Assert.Same(notes, Assert.Single(asked));
        Assert.True(desk.CanGoForward);
    }

    [Fact]
    public void Traversing_the_browsers_history_refuses_to_drop_a_dirty_window()
    {
        var desk = Desk();
        desk.Open(Window(SampleCatalog.List));
        var notes = desk.Open(Window(SampleCatalog.Notes));
        desk.Open(Window(SampleCatalog.Health));
        desk.SetUnsaved(notes.Id, true, "this note");
        var asked = new List<WindowInstance>();
        desk.CloseNeedsConfirmation += asked.Add;
        var depth = desk.HistoryDepth;

        // Two steps back lands before Notes was opened. Nothing moves, not even the
        // first step, which on its own would have been harmless.
        Assert.False(desk.TraverseTo(depth - 2));

        Assert.Equal(depth, desk.HistoryDepth);
        Assert.Equal(3, desk.OpenCount);
        Assert.Same(notes, Assert.Single(asked));
    }

    [Fact]
    public void A_refused_walk_sends_the_browser_back_to_the_desks_entry()
    {
        var desk = Desk();
        desk.Open(Window(SampleCatalog.List));
        var notes = desk.Open(Window(SampleCatalog.Notes));
        desk.Open(Window(SampleCatalog.Health));
        desk.SetUnsaved(notes.Id, true, "this note");
        var depth = desk.HistoryDepth;

        // The browser went two entries back; the desk stayed, so the browser goes two forward.
        Assert.Equal(2, LineOps.Desk.Windowing.Desk.HistoryCorrection(desk, depth - 2));
        Assert.Equal(depth, desk.HistoryDepth);

        // Landing there fires popstate at the desk's own depth, which walks nowhere.
        Assert.Equal(0, LineOps.Desk.Windowing.Desk.HistoryCorrection(desk, depth));

        // A walk that keeps Notes needs no correction.
        Assert.Equal(0, LineOps.Desk.Windowing.Desk.HistoryCorrection(desk, depth - 1));
        Assert.Equal(depth - 1, desk.HistoryDepth);
    }

    [Fact]
    public void Workspace_refuses_to_replace_a_dirty_window()
    {
        var desk = Desk();
        var notes = desk.Open(Window(SampleCatalog.Notes));
        desk.SetUnsaved(notes.Id, true, "this note");
        var asked = new List<WindowInstance>();
        desk.CloseNeedsConfirmation += asked.Add;
        var depth = desk.HistoryDepth;

        Assert.False(desk.ApplyWorkspace(desk.Catalog.Workspaces[0]));

        Assert.Same(notes, desk.Windows.Single());
        Assert.Same(notes, Assert.Single(asked));
        Assert.Equal(depth, desk.HistoryDepth);
    }

    [Fact]
    public void Workspace_applies_over_clean_windows()
    {
        var desk = Desk();
        var notes = desk.Open(Window(SampleCatalog.Notes));

        Assert.True(desk.ApplyWorkspace(desk.Catalog.Workspaces[0]));

        Assert.DoesNotContain(notes, desk.Windows);
    }

    // ------------------------------------------------------------------ chrome ----

    /// <summary>A panel with one key that marks it as holding input, the way a form does.</summary>
    public sealed class DraftPanel : DeskPanelBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "class", "draft");
            builder.AddAttribute(2, "onclick", EventCallback.Factory.Create(this, () => MarkUnsaved(true, "this draft")));
            builder.CloseElement();

            builder.OpenElement(3, "button");
            builder.AddAttribute(4, "class", "save");
            builder.AddAttribute(5, "onclick", EventCallback.Factory.Create(this, () => MarkUnsaved(false)));
            builder.CloseElement();
        }
    }

    /// <summary>Records every question in full, so a test can read the labels it was put with.</summary>
    private sealed class RecordingAlerts : IDeskAlerts
    {
        public bool Answer { get; set; }

        public List<(string Heading, string? Message, string Confirm, string? Cancel, bool Destructive)> Asked { get; } = [];

        public Task<bool> ConfirmAsync(string heading, string confirmLabel, string? message = null,
            string? cancelLabel = "Cancel", bool destructive = false)
        {
            Asked.Add((heading, message, confirmLabel, cancelLabel, destructive));
            return Task.FromResult(Answer);
        }
    }

    private static readonly WindowDefinition Draft = new()
    {
        Key = "draft",
        Title = "Draft",
        Icon = "",
        ComponentType = typeof(DraftPanel)
    };

    private (IRenderedComponent<AppWindow> Window, WindowManager Manager, WindowInstance Instance, RecordingAlerts Alerts) Render()
    {
        var alerts = new RecordingAlerts();
        Services.AddSingleton<IDeskAlerts>(alerts);

        var manager = Services.GetRequiredService<WindowManager>();
        var instance = manager.Open(Draft);
        var window = RenderComponent<AppWindow>(p => p.Add(x => x.Window, instance));

        return (window, manager, instance, alerts);
    }

    [Fact]
    public void A_panel_marks_its_window_through_the_base()
    {
        var (window, _, instance, _) = Render();

        window.Find(".draft").Click();
        Assert.True(instance.HasUnsavedInput);
        Assert.Equal("this draft", instance.UnsavedInputLabel);

        window.Find(".save").Click();
        Assert.False(instance.HasUnsavedInput);
    }

    [Fact]
    public void Close_key_asks_before_discarding_and_keeps_editing_when_told_to()
    {
        var (window, manager, instance, alerts) = Render();
        alerts.Answer = false;
        window.Find(".draft").Click();

        window.Find(".win__ctl--close").Click();

        var asked = Assert.Single(alerts.Asked);
        Assert.Equal("Discard this draft?", asked.Heading);
        Assert.Equal("The window has input that has not been saved.", asked.Message);
        Assert.Equal("Discard", asked.Confirm);
        Assert.Equal("Keep editing", asked.Cancel);
        Assert.True(asked.Destructive);
        Assert.Contains(instance, manager.Windows);
    }

    [Fact]
    public void Discard_closes_the_window()
    {
        var (window, manager, instance, alerts) = Render();
        alerts.Answer = true;
        window.Find(".draft").Click();

        window.Find(".win__ctl--close").Click();

        Assert.Single(alerts.Asked);
        Assert.DoesNotContain(instance, manager.Windows);
    }

    [Fact]
    public void A_clean_window_closes_without_asking()
    {
        var (window, manager, instance, alerts) = Render();

        window.Find(".win__ctl--close").Click();

        Assert.Empty(alerts.Asked);
        Assert.DoesNotContain(instance, manager.Windows);
    }

    [Fact]
    public void An_unlabelled_draft_is_asked_about_as_unsaved_changes()
    {
        var (window, manager, instance, alerts) = Render();
        manager.SetUnsaved(instance.Id, true, null);

        window.Find(".win__ctl--close").Click();

        Assert.Equal("Discard unsaved changes?", Assert.Single(alerts.Asked).Heading);
    }
}
