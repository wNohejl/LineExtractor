using System.Reflection;
using Bunit;
using XLines.Data;
using XLines.Data.Changes;
using XLines.Data.CrossReference;
using XLines.Desk.Windowing;
using XLines.Ingestion.Services;
using XLines.Web.Components.Panels;
using XLines.Web.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static XLines.Web.Tests.Support.Eventually;

namespace XLines.Web.Tests;

/// <summary>
/// The Board's load: its reads go out together, a newer filter cancels the load in flight so a
/// slower superseded read cannot paint the last league under the new one, and a notice of alerts
/// alone recounts the alerts rather than reading the whole board again. There is no database
/// here: every connection the panel opens is held at a gate that only a cancellation releases,
/// which is all these questions need — what was asked, when, and what was told to stop.
/// </summary>
public class BoardLoadTests : WagerTestContext
{
    // The board's games, today's moves, the two alert counts and the league names.
    private const int ReadsPerLoad = 5;

    private readonly ConnectionGate _gate = new();

    public BoardLoadTests()
    {
        var options = GatedDatabase.Options(_gate);

        Services.AddSingleton<IDbContextFactory<XLinesDbContext>>(new GatedDbFactory(options));
        Services.AddScoped(_ => new XLinesDbContext(options));
        Services.AddScoped(sp => new BoardService(sp.GetRequiredService<XLinesDbContext>()));
        Services.AddSingleton<RangePull>();
        Services.AddScoped<XLines.Desk.DeskJobRunner>();
    }

    private IRenderedComponent<BoardPanel> Render()
    {
        var panel = RenderComponent<BoardPanel>();
        WaitFor(() => _gate.Reads.Count == ReadsPerLoad);
        return panel;
    }

    [Fact]
    public void A_load_sends_its_reads_out_together()
    {
        // All five are at the gate at once: none waited on another's answer.
        Render();

        Assert.Equal(ReadsPerLoad, _gate.Reads.Count);
        Assert.All(_gate.Reads, ct => Assert.False(ct.IsCancellationRequested));
    }

    [Fact]
    public async Task A_newer_filter_cancels_the_load_in_flight_and_the_superseded_one_says_nothing()
    {
        var panel = Render();
        var first = _gate.Reads.ToList();

        var change = typeof(BoardPanel).GetMethod("OnSportChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await panel.InvokeAsync(() => { _ = (Task)change.Invoke(panel.Instance, ["nfl"])!; });

        WaitFor(() => _gate.Reads.Count == 2 * ReadsPerLoad, () => $"reads after the change: {_gate.Reads.Count}");
        var second = _gate.Reads.Skip(ReadsPerLoad).ToList();

        // Waited for, not read at once: the cancel is the panel's, on the renderer's own thread,
        // and under a parallel suite it has not always landed by the time the reads are counted.
        WaitFor(() => first.All(ct => ct.IsCancellationRequested), () => $"first load cancelled: {first.Count(ct => ct.IsCancellationRequested)} of {first.Count}; reads {_gate.Reads.Count}");
        Assert.All(second, ct => Assert.False(ct.IsCancellationRequested));

        // The cancelled load returned quietly rather than as a failure of the board.
        Assert.Null(PanelProbe.LoadError(panel));

        // And a closed window lets go of the load it was still waiting on.
        await panel.InvokeAsync(() => ((IDisposable)panel.Instance).Dispose());
        WaitFor(() => second.All(ct => ct.IsCancellationRequested), () => $"second load cancelled on dispose: {second.Count(ct => ct.IsCancellationRequested)} of {second.Count}");
    }

    [Fact]
    public void A_notice_of_alerts_alone_recounts_the_alerts_and_nothing_else()
    {
        Render();
        var signals = Services.GetRequiredService<DeskSignals>();

        signals.Publish(new HashSet<string> { DataTopics.Alerts });

        // The two counts, after the settle — and the load in flight is left to finish.
        WaitFor(() => _gate.Reads.Count == ReadsPerLoad + 2);
        Thread.Sleep(300);
        Assert.Equal(ReadsPerLoad + 2, _gate.Reads.Count);
        Assert.All(_gate.Reads.Take(ReadsPerLoad), ct => Assert.False(ct.IsCancellationRequested));

        // A notice that can change a row reads the board again, in place of the load before it.
        signals.Publish(new HashSet<string> { DataTopics.Alerts, DataTopics.Odds });

        WaitFor(() => _gate.Reads.Count == 2 * ReadsPerLoad + 2);
        Assert.All(_gate.Reads.Take(ReadsPerLoad), ct => Assert.True(ct.IsCancellationRequested));
    }
}
