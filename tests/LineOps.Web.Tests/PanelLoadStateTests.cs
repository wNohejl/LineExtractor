using Bunit;
using LineOps.Data;
using LineOps.Web.Components.Panels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// A panel that has not loaded yet holds its shape; a panel whose load failed says so and
/// offers a retry. Before this, both read as the panel's "nothing here yet" sentence — the
/// Board said there was nothing on it while it was still asking, and a failed load rethrew
/// into the circuit. Runs is the panel under test because its whole body is one grid fed by
/// one database factory, which a test can script without a database.
/// </summary>
public class PanelLoadStateTests : DeskTestContext
{
    /// <summary>
    /// Hands out what each call is scripted to: a failure, or a load that never finishes.
    /// Past the end of the script every call hangs, which is "still loading" for as long as
    /// the test cares to look.
    /// </summary>
    private sealed class ScriptedFactory : IDbContextFactory<LineOpsDbContext>
    {
        private readonly Queue<bool> _fails;

        public ScriptedFactory(params bool[] fails) => _fails = new Queue<bool>(fails);

        public int Calls { get; private set; }

        public LineOpsDbContext CreateDbContext() => throw new NotSupportedException();

        public Task<LineOpsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            Calls++;

            return _fails.Count > 0 && _fails.Dequeue()
                ? Task.FromException<LineOpsDbContext>(new InvalidOperationException("connection refused (raw text)"))
                : new TaskCompletionSource<LineOpsDbContext>().Task;
        }
    }

    private ScriptedFactory Use(ScriptedFactory factory)
    {
        Services.AddSingleton<IDbContextFactory<LineOpsDbContext>>(factory);
        return factory;
    }

    [Fact]
    public void A_failed_load_renders_the_error_state_not_the_new_state()
    {
        Use(new ScriptedFactory(fails: true));

        var panel = RenderComponent<RunsPanel>();

        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".empty--error")));
        Assert.Contains("The runs did not load.", panel.Find(".empty--error").TextContent);
        Assert.Contains("Try again", panel.Find(".empty--error button").TextContent);
        Assert.DoesNotContain("No runs recorded yet.", panel.Markup);
        Assert.DoesNotContain("connection refused", panel.Markup);
        Assert.Empty(panel.FindAll(".deskgrid__skeleton"));
    }

    [Fact]
    public void Retry_reruns_the_load()
    {
        // Fails once, then the second attempt is still in flight when the test looks.
        var factory = Use(new ScriptedFactory(true, false));

        var panel = RenderComponent<RunsPanel>();
        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".empty--error")));
        Assert.Equal(1, factory.Calls);

        panel.Find(".empty--error button").Click();

        panel.WaitForAssertion(() => Assert.Equal(2, factory.Calls));
        Assert.Empty(panel.FindAll(".empty--error"));
        Assert.NotNull(panel.Find(".deskgrid__skeleton"));
    }

    /// <summary>A bare panel whose loads the test starts and finishes by hand.</summary>
    private sealed class RacingPanel : PanelBase
    {
        public Task Load(Func<Task> work) => WithActivityAsync(work, () => LineOps.Desk.Windowing.PulseState.Healthy);

        public bool IsFailed => Failed;

        public bool IsLoaded => Loaded;

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
        }
    }

    [Fact]
    public async Task A_slow_load_that_fails_after_a_newer_one_succeeded_does_not_mark_the_panel_failed()
    {
        var panel = RenderComponent<RacingPanel>();
        var slow = new TaskCompletionSource();
        Task older = Task.CompletedTask;
        Task newer = Task.CompletedTask;

        // A starts and is still out; B starts after it and succeeds; then A fails.
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
    public void A_head_to_head_that_failed_to_load_does_not_say_there_were_no_meetings()
    {
        // No BoardService is registered, so the panel's first read throws — a failed load.
        var panel = RenderComponent<HeadToHeadPanel>(p => p.Add(x => x.GameId, 1));

        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".empty--error")));
        Assert.Contains("The meetings did not load.", panel.Find(".empty--error").TextContent);
        Assert.DoesNotContain("No previous meeting on record", panel.Markup);
    }

    [Fact]
    public void First_load_shows_a_skeleton_not_the_new_state()
    {
        Use(new ScriptedFactory());

        var panel = RenderComponent<RunsPanel>();

        Assert.NotNull(panel.Find(".deskgrid__skeleton"));
        Assert.DoesNotContain("No runs recorded yet.", panel.Markup);
        Assert.Empty(panel.FindAll(".empty--error"));
    }
}
