using System.Collections.Concurrent;
using System.Reflection;
using Bunit;
using XLines.Core.Entities;
using XLines.Data;
using XLines.Data.CrossReference;
using XLines.Desk.Windowing;
using XLines.Ingestion.Services;
using XLines.Web.Components.Panels;
using XLines.Web.Services;
using XLines.Web.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static XLines.Web.Tests.Support.Eventually;

namespace XLines.Web.Tests;

/// <summary>
/// The latest load wins in the Players, Journal, Performance and Game windows, as it does on the
/// Board (<see cref="BoardLoadTests"/>). The base class lets loads overlap and keeps a superseded
/// one from reporting, but not from handing over its rows; so each of these cancels the reads of
/// the load in flight when a newer one starts, the cancelled one returns quietly rather than as a
/// failure, and a closed window lets go of the read it was still waiting on. Every read here is
/// held until its own token is cancelled, which is all these questions need.
/// </summary>
internal static class LatestLoad
{
    /// <summary>Starts the panel's own load again, as a filter or a notice does, without waiting for it.</summary>
    public static Task Reload<TPanel>(IRenderedComponent<TPanel> panel) where TPanel : IComponent
    {
        var load = typeof(TPanel).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                   ?? typeof(TPanel).BaseType!.GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return panel.InvokeAsync(() => { _ = (Task)load.Invoke(panel.Instance, [])!; });
    }

    /// <summary>
    /// The three questions, in order: the newer load cancels the older's read and not its own,
    /// the cancelled one says nothing, and disposal cancels the one left.
    /// </summary>
    public static async Task NewerCancelsOlderAndDisposalCancelsTheRest<TPanel>(
        IRenderedComponent<TPanel> panel, ConcurrentQueue<CancellationToken> reads) where TPanel : IComponent
    {
        WaitFor(() => reads.Count == 1, () => $"reads of the first load: {reads.Count}");
        var first = reads.Single();

        await Reload(panel);

        WaitFor(() => reads.Count == 2, () => $"reads after the reload: {reads.Count}");
        var second = reads.Last();

        WaitFor(() => first.IsCancellationRequested, () => "the first load was not cancelled");
        Assert.False(second.IsCancellationRequested);

        // The cancelled load returned quietly rather than as a failure of the window.
        Assert.Null(PanelProbe.LoadError(panel.Instance));

        await panel.InvokeAsync(() => ((IDisposable)panel.Instance).Dispose());
        WaitFor(() => second.IsCancellationRequested, () => "the pending load was not cancelled on dispose");
    }
}

/// <summary>The Players window reads the database directly: every connection is held at a gate.</summary>
public class PlayersLoadTests : DeskTestContext
{
    private readonly ConnectionGate _gate = new();

    public PlayersLoadTests()
    {
        Services.AddSingleton<IDbContextFactory<XLinesDbContext>>(new GatedDbFactory(GatedDatabase.Options(_gate)));
    }

    [Fact]
    public async Task A_newer_load_cancels_the_one_in_flight_and_disposal_cancels_the_rest()
    {
        // The reads share one context and go one after another, so one is at the gate at a time.
        var panel = RenderComponent<PlayersPanel>();
        await LatestLoad.NewerCancelsOlderAndDisposalCancelsTheRest(panel, _gate.Reads);
    }
}

/// <summary>The Journal's one read is its FetchAsync, held here on the load's own token.</summary>
public class JournalLoadTests : WagerTestContext
{
    public JournalLoadTests()
    {
        Services.AddSingleton<IDbContextFactory<XLinesDbContext>>(new JournalParlayTests.NoDatabase());
    }

    public sealed class HeldJournal : JournalPanel
    {
        [Parameter] public ConcurrentQueue<CancellationToken> Into { get; set; } = default!;

        protected override async Task<JournalRead> FetchAsync(JournalFilter filter)
        {
            var ct = ReadToken;
            Into.Enqueue(ct);
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("A held read does not come back.");
        }
    }

    [Fact]
    public async Task A_newer_load_cancels_the_one_in_flight_and_disposal_cancels_the_rest()
    {
        var reads = new ConcurrentQueue<CancellationToken>();
        var panel = RenderComponent<HeldJournal>(p => p.Add(j => j.Into, reads));
        await LatestLoad.NewerCancelsOlderAndDisposalCancelsTheRest(panel, reads);
    }
}

/// <summary>Performance reads through the journal service; the first of its two reads is held.</summary>
public class PerformanceLoadTests : DeskTestContext
{
    private readonly HeldJournal _journal = new();

    public PerformanceLoadTests() => Services.AddSingleton<JournalService>(_journal);

    private sealed class HeldJournal() : JournalService(null!, null!)
    {
        public ConcurrentQueue<CancellationToken> Reads { get; } = new();

        public override async Task<PerformanceSet> PerformanceAsync(PerformanceFilter filter, CancellationToken ct = default)
        {
            Reads.Enqueue(ct);
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("A held read does not come back.");
        }

        public override Task<decimal> StartingBankrollAsync(CancellationToken ct = default) => Task.FromResult(0m);
    }

    [Fact]
    public async Task A_newer_load_cancels_the_one_in_flight_and_disposal_cancels_the_rest()
    {
        var panel = RenderComponent<PerformancePanel>();
        await LatestLoad.NewerCancelsOlderAndDisposalCancelsTheRest(panel, _journal.Reads);
    }
}

/// <summary>The Game window reads through its data seam; the row, its first read, is held.</summary>
public class GameLoadTests : DeskTestContext
{
    private readonly HeldGameData _data = new();

    public GameLoadTests() => Services.AddSingleton<IGameWindowData>(_data);

    private sealed class HeldGameData : IGameWindowData
    {
        public ConcurrentQueue<CancellationToken> Reads { get; } = new();

        public async Task<BoardRow?> RowAsync(int gameId, CancellationToken ct = default)
        {
            Reads.Enqueue(ct);
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("A held read does not come back.");
        }

        public Task<Game?> GameAsync(int gameId, CancellationToken ct = default) => Task.FromResult<Game?>(null);

        public Task<GameWindowDetail> DetailAsync(Game game, int? seasonYear, bool withBets, CancellationToken ct = default)
            => Task.FromResult(GameWindowDetail.Empty);
    }

    [Fact]
    public async Task A_newer_load_cancels_the_one_in_flight_and_disposal_cancels_the_rest()
    {
        var panel = RenderComponent<GamePanel>(p => p.Add(g => g.GameId, BoardRows.GameId));
        await LatestLoad.NewerCancelsOlderAndDisposalCancelsTheRest(panel, _data.Reads);
    }
}
