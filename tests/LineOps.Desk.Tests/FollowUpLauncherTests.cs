using Bunit;
using LineOps.Desk;
using LineOps.Desk.Primitives;
using LineOps.Desk.Windowing;
using LineOps.Desk.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Desk.Tests;

/// <summary>
/// Follow-ups opened from a row: a desk window while there is room, a floating dialog once
/// there is not.
///
/// The floating half of that switch is the half with no other owner. A window announces
/// itself through <see cref="WindowManager.Changed"/> and the desk redraws; a floating
/// follow-up is drawn by the panel that launched it, and the panel finds out only if the
/// launcher says so. The row that opens one lives inside a grid cell, so the press is
/// handled — and re-rendered — by the strip, not by the panel holding the layer. Without an
/// announcement the dialog sits in the list unpainted until something unrelated redraws the
/// panel, which reads as a dialog that did not open.
/// </summary>
public class FollowUpLauncherTests : DeskTestContext
{
    // ------------------------------------------------------------- whose status ----

    /// <summary>A hosted view that says how it stands the moment it is drawn, as every panel does.</summary>
    public sealed class ReportingPanel : DeskPanelBase
    {
        [Parameter] public int ItemId { get; set; }

        protected override void OnInitialized() => Report(PulseState.Warn, "no moves");

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddAttribute(1, "class", "reporting");
            builder.CloseElement();
        }
    }

    /// <summary>The sample catalogue with the reporting view added as a chart.</summary>
    private sealed class ProbeCatalog : IWindowCatalog
    {
        private readonly SampleCatalog _app = new();

        public static readonly WindowDefinition Probe = new()
        {
            Key = "probe-chart",
            Title = "Chart",
            Icon = "",
            ComponentType = typeof(ReportingPanel)
        };

        public IReadOnlyList<WindowDefinition> All => [.. _app.All, Probe];

        public IReadOnlyList<Workspace> Workspaces => _app.Workspaces;

        public string DefaultPrimary => _app.DefaultPrimary;

        /// <summary>A view to stand under the probe key in place of <see cref="Probe"/>.</summary>
        public WindowDefinition? Override { get; set; }

        public WindowDefinition? Find(string key) => key == Probe.Key ? Override ?? Probe : ((IWindowCatalog)_app).Find(key);
    }

    /// <summary>
    /// List row → Chart on a full desk opens the chart as a floating dialog drawn inside the
    /// List's window. The List's window id cascaded straight through to it, so the
    /// dialog's "no moves" landed in the List's title bar. The dialog is not a window and has
    /// no bar of its own; what it reports goes nowhere rather than to its host.
    /// </summary>
    [Fact]
    public void A_follow_up_dialog_does_not_write_its_hosts_status()
    {
        var desk = new WindowManager(new ProbeCatalog());
        desk.UpdateSettings(s => s.MaxConcurrentWindows = 1);
        var list = desk.Open(SampleCatalog.Find(SampleCatalog.List));
        desk.SetPulse(list.Id, PulseState.Healthy, "12 rows");
        Services.AddSingleton(desk);

        var launcher = new FollowUpLauncher();
        launcher.Launch(desk, ProbeCatalog.Probe.Key, "ItemId", 7, "Home v Away");
        Assert.Single(launcher.Open);

        // The layer as the List draws it: inside the cascade its window gives every panel.
        var host = RenderComponent<CascadingValue<string>>(p => p
            .Add(x => x.Name, "WindowId")
            .Add(x => x.Value, list.Id)
            .AddChildContent<FollowUpLayer>(layer => layer.Add(x => x.Launcher, launcher)));

        Assert.NotNull(host.Find(".reporting"));
        Assert.Equal(PulseState.Healthy, list.Pulse);
        Assert.Equal("12 rows", list.StatusText);
    }

    /// <summary>A hosted view with a draft key, as an order dialog has.</summary>
    public sealed class DraftingPanel : DeskPanelBase
    {
        [Parameter] public int ItemId { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "class", "draft");
            builder.AddAttribute(2, "onclick", EventCallback.Factory.Create(this, () => MarkUnsaved(true, "this order")));
            builder.CloseElement();
        }
    }

    /// <summary>
    /// Status stays out of the host's bar, but unsaved input does not: closing the List takes the
    /// order dialog floating over it, and what was typed in it. So the dialog's draft marks the
    /// List, whose close key then asks; and once the dialog closes, the mark goes with it.
    /// </summary>
    [Fact]
    public void A_follow_up_dialog_still_marks_its_host_unsaved()
    {
        var catalog = new ProbeCatalog();
        var desk = new WindowManager(catalog);
        desk.UpdateSettings(s => s.MaxConcurrentWindows = 1);
        var list = desk.Open(SampleCatalog.Find(SampleCatalog.List));
        Services.AddSingleton(desk);

        var order = new WindowDefinition
        {
            Key = ProbeCatalog.Probe.Key,
            Title = "Order",
            Icon = "",
            ComponentType = typeof(DraftingPanel)
        };
        var launcher = new FollowUpLauncher();
        catalog.Override = order;
        launcher.Launch(desk, order.Key, "ItemId", 7, "Home v Away");
        var followUp = Assert.Single(launcher.Open);

        var host = RenderComponent<CascadingValue<string>>(p => p
            .Add(x => x.Name, "WindowId")
            .Add(x => x.Value, list.Id)
            .AddChildContent<FollowUpLayer>(layer => layer.Add(x => x.Launcher, launcher)));

        host.Find(".draft").Click();

        Assert.True(list.HasUnsavedInput);
        Assert.Equal("this order", list.UnsavedInputLabel);
        Assert.False(desk.TryClose(list.Id));

        host.InvokeAsync(() => launcher.Close(followUp));
        host.WaitForAssertion(() => Assert.Empty(host.FindAll(".draft")));

        Assert.False(list.HasUnsavedInput);
    }

    // ------------------------------------------------------------- the switch ----

    /// <summary>A desk with no room left, so follow-ups take the floating path.</summary>
    private static WindowManager FullDesk()
    {
        var manager = new WindowManager(new SampleCatalog());

        manager.UpdateSettings(s => s.MaxConcurrentWindows = 1);
        manager.Open(SampleCatalog.Find(SampleCatalog.List));

        Assert.True(manager.AtCapacity);

        return manager;
    }

    private static WindowManager EmptyDesk() => new(new SampleCatalog());

    /// <summary>
    /// The bug this suite exists for: the dialog must announce itself the moment it is added.
    /// </summary>
    [Fact]
    public void Floating_a_follow_up_announces_the_change()
    {
        var launcher = new FollowUpLauncher();
        var changes = 0;
        launcher.Changed += () => changes++;

        launcher.Launch(FullDesk(), SampleCatalog.Chart, "ItemId", 7, "Home v Away");

        Assert.Single(launcher.Open);
        Assert.Equal(1, changes);
    }

    /// <summary>
    /// A follow-up that became a window is the desk's business, not the floating layer's.
    /// The manager announces that one itself, and repeating it here would redraw every host
    /// panel for a change none of them rendered.
    /// </summary>
    [Fact]
    public void Opening_as_a_window_leaves_the_floating_layer_alone()
    {
        var launcher = new FollowUpLauncher();
        var changes = 0;
        launcher.Changed += () => changes++;

        launcher.Launch(EmptyDesk(), SampleCatalog.Chart, "ItemId", 7, "Home v Away");

        Assert.Empty(launcher.Open);
        Assert.Equal(0, changes);
    }

    /// <summary>
    /// Re-opening the same view for the same subject raises what is already there. The stack
    /// order is rendered, so the raise has to be announced too.
    /// </summary>
    [Fact]
    public void Re_launching_the_same_subject_raises_it_and_announces_that()
    {
        var launcher = new FollowUpLauncher();
        var desk = FullDesk();

        launcher.Launch(desk, SampleCatalog.Chart, "ItemId", 7, "Home v Away");
        launcher.Launch(desk, SampleCatalog.Details, "ItemId", 7, "Home v Away");

        var changes = 0;
        launcher.Changed += () => changes++;

        launcher.Launch(desk, SampleCatalog.Chart, "ItemId", 7, "Home v Away");

        Assert.Equal(2, launcher.Open.Count);
        Assert.Equal(1, changes);

        // The re-launched one is now nearest the front.
        Assert.Equal(SampleCatalog.Chart, launcher.Open.MaxBy(f => f.Order)!.Key);
    }

    /// <summary>The same view against a different subject is a second dialog, not a raise.</summary>
    [Fact]
    public void A_different_subject_is_its_own_follow_up()
    {
        var launcher = new FollowUpLauncher();
        var desk = FullDesk();

        launcher.Launch(desk, SampleCatalog.Chart, "ItemId", 7, "Home v Away");
        launcher.Launch(desk, SampleCatalog.Chart, "ItemId", 8, "Other v Team");

        Assert.Equal(2, launcher.Open.Count);
    }

    [Fact]
    public void Raising_a_follow_up_announces_the_change()
    {
        var launcher = new FollowUpLauncher();
        var desk = FullDesk();

        launcher.Launch(desk, SampleCatalog.Chart, "ItemId", 7, "Home v Away");

        var changes = 0;
        launcher.Changed += () => changes++;

        launcher.Raise(launcher.Open[0]);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void Closing_a_follow_up_announces_the_change()
    {
        var launcher = new FollowUpLauncher();
        var desk = FullDesk();

        launcher.Launch(desk, SampleCatalog.Chart, "ItemId", 7, "Home v Away");

        var changes = 0;
        launcher.Changed += () => changes++;

        launcher.Close(launcher.Open[0]);

        Assert.Empty(launcher.Open);
        Assert.Equal(1, changes);
    }

    /// <summary>
    /// Closing every follow-up of one kind is what an action does once it settles. It only
    /// announces when it actually took something down.
    /// </summary>
    [Fact]
    public void Closing_a_whole_kind_announces_only_when_something_went()
    {
        var launcher = new FollowUpLauncher();
        var desk = FullDesk();

        launcher.Launch(desk, SampleCatalog.Order, "ItemId", 7, "Home v Away");
        launcher.Launch(desk, SampleCatalog.Chart, "ItemId", 7, "Home v Away");

        var changes = 0;
        launcher.Changed += () => changes++;

        launcher.CloseAll(SampleCatalog.Order);

        Assert.Single(launcher.Open);
        Assert.Equal(1, changes);

        launcher.CloseAll(SampleCatalog.Order);

        Assert.Equal(1, changes);
    }

    /// <summary>An unknown key is a no-op, and a no-op has nothing to announce.</summary>
    [Fact]
    public void An_unknown_view_changes_nothing()
    {
        var launcher = new FollowUpLauncher();
        var changes = 0;
        launcher.Changed += () => changes++;

        launcher.Launch(FullDesk(), "not-a-window", "ItemId", 7, "Home v Away");

        Assert.Empty(launcher.Open);
        Assert.Equal(0, changes);
    }

    /// <summary>A door names more than the item: the size and the store travel with it.</summary>
    [Fact]
    public void A_follow_up_can_carry_several_parameters()
    {
        var manager = EmptyDesk();
        var launcher = new FollowUpLauncher();

        launcher.Launch(manager, SampleCatalog.Order,
            new Dictionary<string, object> { ["ItemId"] = 7, ["Size"] = "small", ["Colour"] = "red", ["Store"] = "north" },
            7, "First item");

        var window = Assert.Single(manager.Windows);
        Assert.Equal("north", window.Parameters["Store"]);
        Assert.Equal(7, window.Parameters["ItemId"]);
    }

    [Fact]
    public void Relaunching_a_floating_follow_up_re_aims_it_rather_than_stacking_another()
    {
        var launcher = new FollowUpLauncher();
        var desk = FullDesk();

        launcher.Launch(desk, SampleCatalog.Order, new Dictionary<string, object> { ["ItemId"] = 7, ["Size"] = "small" }, 7, "x");
        launcher.Launch(desk, SampleCatalog.Order, new Dictionary<string, object> { ["ItemId"] = 7, ["Size"] = "large" }, 7, "x");

        var only = Assert.Single(launcher.Open);
        Assert.Equal("large", only.Parameters["Size"]);
    }

    /// <summary>
    /// The order window is a singleton: opening it again focuses the one on the desk rather than
    /// adding a second. So a door that names another item or size must re-aim that window, or the
    /// press lands on the order it was already showing.
    /// </summary>
    [Fact]
    public void Relaunching_a_singleton_window_re_aims_it_rather_than_showing_the_old_subject()
    {
        var manager = EmptyDesk();
        var launcher = new FollowUpLauncher();

        launcher.Launch(manager, SampleCatalog.Order,
            new Dictionary<string, object> { ["ItemId"] = 7, ["Size"] = "small", ["Store"] = "north" }, 7, "First item");
        launcher.Launch(manager, SampleCatalog.Order,
            new Dictionary<string, object> { ["ItemId"] = 8, ["Size"] = "large" }, 8, "Second item");

        var window = Assert.Single(manager.Windows);
        Assert.Equal(8, window.Parameters["ItemId"]);
        Assert.Equal("large", window.Parameters["Size"]);
        Assert.Equal("Order · Second item", window.Title);
        // Not named by the second press, so not left at the first press's store.
        Assert.Null(window.Parameters["Store"]);
        Assert.Empty(launcher.Open);
    }
}
