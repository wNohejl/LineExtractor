using Bunit;
using XLines.Data.CrossReference;
using XLines.Web.Components.Snippets;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using static XLines.Web.Tests.Support.Eventually;

namespace XLines.Web.Tests;

/// <summary>
/// The tale of the tape and the fighter view hand over only their latest read (<see cref="SnippetRead"/>):
/// re-aimed while reading, the first read is cancelled and nothing it brings back late is drawn or
/// thrown; closed, the read in flight is cancelled. Each sits in an error boundary, as it does in its
/// window, because neither draws a failure of its own: the latest read's failure still stops the
/// window, and a superseded one's must not.
/// </summary>
public class FightGlanceLatestReadTests : DeskTestContext
{
    private readonly ScriptedDatabase _db = new();

    public FightGlanceLatestReadTests()
    {
        _db.Register(Services);
        Services.AddScoped<FightService>();
        // A missing profile offers its read in place, through the desk's job runner.
        Services.AddScoped<XLines.Desk.DeskJobRunner>();
    }

    /// <summary>The boundary's own failure content, which bUnit draws as Blazor does.</summary>
    private static bool Stopped(IRenderedFragment cut) => cut.Markup.Contains("blazor-error-boundary");

    private IRenderedComponent<ErrorBoundary> InBoundary<T>(Action<ComponentParameterCollectionBuilder<T>> parameters)
        where T : IComponent
        => RenderComponent<ErrorBoundary>(p => p.AddChildContent(parameters));

    /// <summary>
    /// Re-aims the glance while its first read is held, lets that read fail once it was cancelled,
    /// and waits for the render that follows it: a late failure reaching the boundary, or a late
    /// answer drawn, is the slow read that used to land last. The second read stays held, then
    /// disposal cancels it.
    /// </summary>
    private async Task ReAimedWhileReading<T>(
        Action<ComponentParameterCollectionBuilder<T>> first,
        Action<ComponentParameterCollectionBuilder<T>> second,
        Action<IRenderedFragment> drawn)
        where T : IComponent
    {
        var release = new TaskCompletionSource();
        _db.Holds.Enqueue(async ct =>
        {
            await release.Task;
            throw new InvalidOperationException("The first read came back late, and failed.");
        });
        _db.Holds.Enqueue(ScriptedDatabase.UntilCancelled);

        var cut = InBoundary(first);
        WaitFor(() => _db.Reads.Count == 1, () => $"reads of the first: {_db.Reads.Count}");
        var firstRead = _db.Reads.Single();

        cut.SetParametersAndRender(p => p.AddChildContent(second));

        WaitFor(() => _db.Reads.Count == 2, () => $"reads after re-aiming: {_db.Reads.Count}");
        var secondRead = _db.Reads.Last();
        WaitFor(() => firstRead.IsCancellationRequested, () => "the first read was not cancelled");
        Assert.False(secondRead.IsCancellationRequested);

        var glance = cut.FindComponent<T>();
        var renders = glance.RenderCount;
        release.SetResult();
        WaitFor(() => glance.RenderCount > renders, () => "the late read never finished");

        Assert.False(Stopped(cut), "a superseded read's failure stopped the window");
        drawn(cut);

        await cut.InvokeAsync(() => ((IDisposable)glance.Instance).Dispose());
        WaitFor(() => secondRead.IsCancellationRequested, () => "the pending read was not cancelled on dispose");
    }

    private static void StillLoading(IRenderedFragment cut) => Assert.NotEmpty(cut.FindAll(".skel__line"));

    [Fact]
    public Task The_tale_of_the_tape_hands_over_only_its_latest_read()
        => ReAimedWhileReading<TaleOfTheTape>(
            p => p.Add(x => x.GameId, 1),
            p => p.Add(x => x.GameId, 2),
            cut =>
            {
                StillLoading(cut);
                Assert.DoesNotContain("not a fight", cut.Markup);
            });

    [Fact]
    public Task The_fighter_view_hands_over_only_its_latest_read()
        => ReAimedWhileReading<FighterView>(
            p => p.Add(x => x.TeamId, 1),
            p => p.Add(x => x.TeamId, 2),
            StillLoading);

    [Fact]
    public void The_tale_of_the_tape_answers_once_its_latest_read_does()
    {
        var cut = InBoundary<TaleOfTheTape>(p => p.Add(x => x.GameId, 1));
        cut.WaitForAssertion(() => Assert.Contains("not a fight", cut.Markup), Patience);
    }

    [Fact]
    public void A_latest_tape_read_that_fails_still_stops_the_window()
    {
        _db.Holds.Enqueue(_ => Task.FromException(new InvalidOperationException("The database is down.")));

        var cut = InBoundary<TaleOfTheTape>(p => p.Add(x => x.GameId, 1));
        cut.WaitForAssertion(() => Assert.True(Stopped(cut)), Patience);
    }

    [Fact]
    public void A_latest_fighter_read_that_fails_still_stops_the_window()
    {
        _db.Holds.Enqueue(_ => Task.FromException(new InvalidOperationException("The database is down.")));

        var cut = InBoundary<FighterView>(p => p.Add(x => x.TeamId, 1));
        cut.WaitForAssertion(() => Assert.True(Stopped(cut)), Patience);
    }

    [Fact]
    public void The_fighter_view_reads_again_only_for_another_fighter()
    {
        var cut = RenderComponent<FighterView>(p => p.Add(x => x.TeamId, 1));
        WaitFor(() => _db.Reads.Count == 1, () => $"reads of the first fighter: {_db.Reads.Count}");

        // The Team window drawing again hands the same fighter back.
        cut.SetParametersAndRender(p => p.Add(x => x.TeamId, 1));
        cut.Render();
        Assert.Single(_db.Reads);

        cut.SetParametersAndRender(p => p.Add(x => x.TeamId, 2));
        WaitFor(() => _db.Reads.Count == 2, () => $"reads after another fighter: {_db.Reads.Count}");
    }
}
