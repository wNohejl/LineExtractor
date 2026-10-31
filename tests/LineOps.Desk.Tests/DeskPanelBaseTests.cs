using Bunit;
using LineOps.Desk.Tests.Support;
using LineOps.Desk.Windowing;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Desk.Tests;

/// <summary>
/// A window's load, retry and refresh plumbing: a failed load names what did not arrive and
/// turns the pulse Critical, Try again repeats exactly what failed, an older load landing late
/// does not overwrite a newer one, a watched topic reloads the panel, and unsaved input in a
/// follow-up marks the window it sits over until the follow-up goes.
/// </summary>
public class DeskPanelBaseTests : DeskTestContext
{
    /// <summary>A bare panel whose loads the test starts by hand, with no settle to wait out.</summary>
    private sealed class Bench : DeskPanelBase
    {
        public int Refreshes { get; private set; }

        public string? Error => LoadError;

        public bool IsFailed => Failed;

        public bool IsLoaded => Loaded;

        protected override TimeSpan RefreshSettle => TimeSpan.Zero;

        protected override IReadOnlySet<string> Watches { get; } = new HashSet<string> { "x" };

        protected override Task RefreshAsync()
        {
            Refreshes++;
            return Task.CompletedTask;
        }

        public Task Load(Func<Task> work) => WithActivityAsync(work, () => PulseState.Healthy, () => "ready");

        public Task Retry() => RetryAsync();

        public void Unsaved(bool dirty) => MarkUnsaved(dirty, "this draft");

        public void Pulse(PulseState state) => Report(state, "said");

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
        }
    }

    private WindowManager Manager => Services.GetRequiredService<WindowManager>();

    private WindowInstance OpenWindow()
    {
        var catalog = Services.GetRequiredService<IWindowCatalog>();
        return Manager.Open(catalog.Find(FakeWindowCatalog.One)!);
    }

    private IRenderedComponent<Bench> InWindow(WindowInstance window) =>
        RenderComponent<Bench>(p => p.AddCascadingValue("WindowId", window.Id));

    [Fact]
    public async Task A_failed_load_names_what_did_not_load()
    {
        var window = OpenWindow();
        var panel = InWindow(window);

        await panel.InvokeAsync(() => panel.Instance.Load(() => throw new InvalidOperationException("raw text")));

        Assert.True(panel.Instance.IsFailed);
        Assert.Equal("This window did not load.", panel.Instance.Error);
        Assert.Equal(PulseState.Critical, window.Pulse);
        Assert.Equal("load failed", window.StatusText);
    }

    [Fact]
    public async Task Retry_repeats_the_last_load()
    {
        var window = OpenWindow();
        var panel = InWindow(window);
        var calls = 0;

        Task Work()
        {
            calls++;
            return calls == 1 ? Task.FromException(new InvalidOperationException("once")) : Task.CompletedTask;
        }

        await panel.InvokeAsync(() => panel.Instance.Load(Work));
        Assert.True(panel.Instance.IsFailed);

        await panel.InvokeAsync(() => panel.Instance.Retry());

        Assert.Equal(2, calls);
        Assert.False(panel.Instance.IsFailed);
        Assert.True(panel.Instance.IsLoaded);
        Assert.Equal(PulseState.Healthy, window.Pulse);
        Assert.Equal("ready", window.StatusText);
    }

    [Fact]
    public async Task An_older_load_landing_late_does_not_overwrite_the_newer()
    {
        var panel = RenderComponent<Bench>();
        var slow = new TaskCompletionSource();
        Task older = Task.CompletedTask;
        Task newer = Task.CompletedTask;

        await panel.InvokeAsync(() =>
        {
            older = panel.Instance.Load(() => slow.Task);
            newer = panel.Instance.Load(() => Task.CompletedTask);
        });
        await newer;

        slow.SetException(new InvalidOperationException("stale read"));
        await older;

        Assert.False(panel.Instance.IsFailed);
        Assert.True(panel.Instance.IsLoaded);
    }

    [Fact]
    public void A_watched_topic_refreshes()
    {
        var panel = RenderComponent<Bench>();
        var signals = Services.GetRequiredService<DeskSignals>();

        signals.Publish(new HashSet<string> { "x" });
        panel.WaitForAssertion(() => Assert.Equal(1, panel.Instance.Refreshes));

        signals.Publish(new HashSet<string> { "y" });

        // Nothing is queued for a topic the panel does not draw from; a short wait proves it.
        Thread.Sleep(50);
        Assert.Equal(1, panel.Instance.Refreshes);
    }

    [Fact]
    public void Unsaved_input_marks_the_host_and_clears_on_disposal()
    {
        var host = OpenWindow();
        var panel = RenderComponent<Bench>(p => p.AddCascadingValue("HostWindowId", host.Id));

        panel.InvokeAsync(() => panel.Instance.Unsaved(true));
        Assert.True(host.HasUnsavedInput);
        Assert.Equal("this draft", host.UnsavedInputLabel);

        DisposeComponents();

        Assert.False(host.HasUnsavedInput);
    }

    [Fact]
    public async Task Report_is_a_no_op_outside_a_window()
    {
        var window = OpenWindow();
        var before = window.Pulse;
        var panel = RenderComponent<Bench>();

        await panel.InvokeAsync(() => panel.Instance.Pulse(PulseState.Warn));
        await panel.InvokeAsync(() => panel.Instance.Load(() => Task.CompletedTask));

        Assert.Equal(before, window.Pulse);
        Assert.Null(window.StatusText);
    }
}
