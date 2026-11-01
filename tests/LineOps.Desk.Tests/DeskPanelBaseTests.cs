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

    /// <summary>
    /// A panel that watches "games", optionally for some ids only, and keeps what each refresh
    /// was told it heard.
    /// </summary>
    private sealed class AddressedBench : DeskPanelBase
    {
        public HashSet<string>? Ids { get; set; }

        public List<IReadOnlySet<string>> Heard { get; } = [];

        public int PlainRefreshes { get; private set; }

        protected override TimeSpan RefreshSettle => TimeSpan.FromMilliseconds(30);

        protected override IReadOnlySet<string> Watches { get; } = new HashSet<string> { "games" };

        protected override IReadOnlyDictionary<string, IReadOnlySet<string>> WatchesIds =>
            Ids is null
                ? base.WatchesIds
                : new Dictionary<string, IReadOnlySet<string>> { ["games"] = Ids };

        protected override Task RefreshAsync(IReadOnlySet<string> heard)
        {
            lock (Heard)
                Heard.Add(heard);

            return base.RefreshAsync(heard);
        }

        protected override Task RefreshAsync()
        {
            PlainRefreshes++;
            return Task.CompletedTask;
        }

        public void Ask() => RequestRefresh();

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
        }
    }

    private DeskSignals Signals => Services.GetRequiredService<DeskSignals>();

    [Fact]
    public void A_panel_with_no_ids_hears_every_addressed_notice_of_its_topic()
    {
        var panel = RenderComponent<AddressedBench>();

        Signal("games", "7");

        panel.WaitForAssertion(() => Assert.Equal(1, panel.Instance.PlainRefreshes));
        Assert.Equal(["games:7"], Assert.Single(panel.Instance.Heard));
    }

    [Fact]
    public void A_panel_watching_ids_hears_its_own_and_the_bare_topic_only()
    {
        var panel = RenderComponent<AddressedBench>();
        panel.Instance.Ids = ["7"];

        Signal("games", "8");
        Thread.Sleep(100);
        Assert.Empty(panel.Instance.Heard);

        Signal("games", "7");
        panel.WaitForAssertion(() => Assert.Single(panel.Instance.Heard));
        Assert.Equal(["games:7"], panel.Instance.Heard[0]);

        Signals.Publish(new HashSet<string> { "games" });
        panel.WaitForAssertion(() => Assert.Equal(2, panel.Instance.Heard.Count));
        Assert.Equal(["games"], panel.Instance.Heard[1]);
    }

    [Fact]
    public void Only_the_tokens_the_panel_hears_reach_its_refresh()
    {
        var panel = RenderComponent<AddressedBench>();
        panel.Instance.Ids = ["7"];

        Signals.Publish(new HashSet<string> { "games:7", "games:8", "odds:7", "journal" });

        panel.WaitForAssertion(() => Assert.Single(panel.Instance.Heard));
        Assert.Equal(["games:7"], panel.Instance.Heard[0]);
    }

    [Fact]
    public void Notices_inside_the_settle_fold_into_one_refresh_that_names_them_all()
    {
        var panel = RenderComponent<AddressedBench>();

        Signal("games", "1");
        Signal("games", "2");

        panel.WaitForAssertion(() => Assert.Single(panel.Instance.Heard));
        Thread.Sleep(100);
        Assert.Single(panel.Instance.Heard);
        Assert.Equal(new HashSet<string> { "games:1", "games:2" }, panel.Instance.Heard[0]);
    }

    [Fact]
    public void A_refresh_the_panel_asked_for_itself_names_nothing()
    {
        var panel = RenderComponent<AddressedBench>();

        // Asked for without saying what changed, so the refresh means "everything" — even when a
        // notice lands in the same settle.
        panel.Instance.Ask();
        Signal("games", "7");

        panel.WaitForAssertion(() => Assert.Single(panel.Instance.Heard));
        Assert.Empty(panel.Instance.Heard[0]);
        Assert.Equal(1, panel.Instance.PlainRefreshes);
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
