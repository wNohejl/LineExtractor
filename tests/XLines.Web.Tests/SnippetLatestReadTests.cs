using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Bunit;
using XLines.Core.Entities;
using XLines.Data;
using XLines.Data.CrossReference;
using XLines.Web.Components.Panels;
using XLines.Web.Components.Snippets;
using XLines.Web.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static XLines.Web.Tests.Support.Eventually;

namespace XLines.Web.Tests;

/// <summary>
/// A database that answers every query with no rows, and holds a connection opening for as long as
/// the test says. The glances read through services this project cannot fake, so the database is
/// what is scripted: the first read held past the moment the glance moves on, the next answered at
/// once or held until its own token is cancelled. A held read released after it was superseded is
/// the slow read that used to land last.
/// </summary>
public sealed class ScriptedDatabase : DbConnectionInterceptor, IDbCommandInterceptor
{
    private const string Nowhere = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none";

    /// <summary>Every connection opening's token, in order.</summary>
    public ConcurrentQueue<CancellationToken> Reads { get; } = new();

    /// <summary>What each opening, in order, waits for; an opening with none left answers at once.</summary>
    public ConcurrentQueue<Func<CancellationToken, Task>> Holds { get; } = new();

    /// <summary>Held until the test lets go, without heeding the token: the worst case, a read that comes back anyway.</summary>
    public static Func<CancellationToken, Task> Until(TaskCompletionSource release) => _ => release.Task;

    /// <summary>Held until the read's own token is cancelled, as a database that never answers.</summary>
    public static readonly Func<CancellationToken, Task> UntilCancelled = ct => Task.Delay(Timeout.Infinite, ct);

    public DbContextOptions<XLinesDbContext> Options => new DbContextOptionsBuilder<XLinesDbContext>()
        .UseNpgsql(Nowhere)
        .AddInterceptors(this)
        .Options;

    /// <summary>The services the glances read through, each over this database.</summary>
    public void Register(IServiceCollection services)
    {
        services.AddScoped(_ => new XLinesDbContext(Options));
        services.AddScoped<BoardService>();
        services.AddScoped<GameLogService>();
        services.AddSingleton<IDbContextFactory<XLinesDbContext>>(new GatedDbFactory(Options));
    }

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        Reads.Enqueue(cancellationToken);

        if (Holds.TryDequeue(out var hold))
            await hold(cancellationToken);

        // Nothing is ever opened: every command below is answered here.
        return InterceptionResult.Suppress();
    }

    public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(new DataTable().CreateDataReader()));
}

/// <summary>
/// The glances that never cancelled a superseded read now hand over only the latest
/// (<see cref="SnippetRead"/>): re-aimed while reading, the first read is cancelled and nothing it
/// brings back late is drawn; closed, the read in flight is cancelled.
/// </summary>
public class SnippetLatestReadTests : DeskTestContext
{
    private readonly ScriptedDatabase _db = new();

    public SnippetLatestReadTests() => _db.Register(Services);

    /// <summary>
    /// Re-aims the glance while its first read is held, lets that read go once it was cancelled,
    /// and waits for the render that follows it: whatever <paramref name="drawn"/> reads then is
    /// what the late read left on screen. The second read stays held, then disposal cancels it.
    /// </summary>
    private async Task ReAimedWhileReading<T>(
        Func<IRenderedComponent<T>> render, Action<IRenderedComponent<T>> reAim, Action<IRenderedComponent<T>> drawn)
        where T : IComponent
    {
        var release = new TaskCompletionSource();
        _db.Holds.Enqueue(ScriptedDatabase.Until(release));
        _db.Holds.Enqueue(ScriptedDatabase.UntilCancelled);

        var cut = render();
        WaitFor(() => _db.Reads.Count == 1, () => $"reads of the first game: {_db.Reads.Count}");
        var first = _db.Reads.Single();

        reAim(cut);

        WaitFor(() => _db.Reads.Count == 2, () => $"reads after re-aiming: {_db.Reads.Count}");
        var second = _db.Reads.Last();
        WaitFor(() => first.IsCancellationRequested, () => "the first read was not cancelled");
        Assert.False(second.IsCancellationRequested);

        // The first load's own render, once its read comes back late.
        var renders = cut.RenderCount;
        release.SetResult();
        WaitFor(() => cut.RenderCount > renders, () => "the late read never finished");

        drawn(cut);
        Assert.DoesNotContain("did not load", cut.Markup);

        await cut.InvokeAsync(() => ((IDisposable)cut.Instance).Dispose());
        WaitFor(() => second.IsCancellationRequested, () => "the pending read was not cancelled on dispose");
    }

    private static void StillLoading<T>(IRenderedComponent<T> cut) where T : IComponent
        => Assert.NotEmpty(cut.FindAll(".skel__line"));

    [Fact]
    public Task The_player_glance_hands_over_only_its_latest_read()
        => ReAimedWhileReading(
            () => RenderComponent<PlayerRecentSnippet>(p => p.Add(x => x.PlayerId, 1)),
            cut => cut.SetParametersAndRender(p => p.Add(x => x.PlayerId, 2)),
            cut =>
            {
                StillLoading(cut);
                Assert.DoesNotContain("No appearances on record", cut.Markup);
            });

    [Fact]
    public Task The_meetings_glance_hands_over_only_its_latest_read()
        => ReAimedWhileReading(
            () => RenderComponent<HeadToHeadSnippet>(p => p.Add(x => x.GameId, 1)),
            cut => cut.SetParametersAndRender(p => p.Add(x => x.GameId, 2)),
            cut =>
            {
                StillLoading(cut);
                Assert.DoesNotContain("No previous meeting on record", cut.Markup);
            });

    [Fact]
    public Task The_odds_glance_hands_over_only_its_latest_read()
        => ReAimedWhileReading(
            () => RenderComponent<OddsSnippet>(p => p.Add(x => x.GameId, 1)),
            cut => cut.SetParametersAndRender(p => p.Add(x => x.GameId, 2)),
            cut =>
            {
                StillLoading(cut);
                Assert.DoesNotContain("not on the board", cut.Markup);
            });

    [Fact]
    public Task The_game_windows_head_to_head_hands_over_only_its_latest_read()
        => ReAimedWhileReading(
            () => RenderComponent<GameHeadToHead>(p => p
                .Add(x => x.GameId, 1)
                .Add(x => x.HomeName, "Chiefs")
                .Add(x => x.AwayName, "Bills")),
            cut => cut.SetParametersAndRender(p => p.Add(x => x.GameId, 2)),
            // The label's answer is drawn only once the latest read has answered.
            cut => Assert.DoesNotContain("on record", cut.Markup));

    [Fact]
    public Task A_market_opened_in_place_hands_over_only_its_latest_read()
        => ReAimedWhileReading(
            () => RenderComponent<MarketMovement>(p => p
                .Add(x => x.GameId, 1)
                .Add(x => x.Market, Markets.Moneyline)
                .Add(x => x.MarketName, "Moneyline")
                .Add(x => x.FirstSide, "Bills")
                .Add(x => x.SecondSide, "Chiefs")),
            cut => cut.SetParametersAndRender(p => p.Add(x => x.GameId, 2)),
            cut =>
            {
                StillLoading(cut);
                Assert.DoesNotContain("No book priced this market", cut.Markup);
            });

    [Fact]
    public void The_player_glance_reads_again_only_for_another_player()
    {
        var reads = 0;
        Services.AddScoped<GameLogService>(sp =>
        {
            // Resolved as the read starts, on the renderer's thread, so the count is final once a
            // render returns.
            Interlocked.Increment(ref reads);
            return new GameLogService(sp.GetRequiredService<XLinesDbContext>());
        });

        var cut = RenderComponent<PlayerRecentSnippet>(p => p.Add(x => x.PlayerId, 1));
        cut.WaitForAssertion(() => Assert.Contains("No appearances on record", cut.Markup));
        Assert.Equal(1, reads);

        // A parent redraw on a signal hands the same player back.
        cut.SetParametersAndRender(p => p.Add(x => x.PlayerId, 1));
        cut.Render();
        Assert.Equal(1, reads);

        cut.SetParametersAndRender(p => p.Add(x => x.PlayerId, 2));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void Meetings_handed_over_by_the_host_cancel_the_glances_own_read()
    {
        _db.Holds.Enqueue(ScriptedDatabase.UntilCancelled);

        var cut = RenderComponent<HeadToHeadSnippet>(p => p.Add(x => x.GameId, 1));
        WaitFor(() => _db.Reads.Count == 1, () => $"reads: {_db.Reads.Count}");

        cut.SetParametersAndRender(p => p.Add(x => x.Rows, (IReadOnlyList<HeadToHeadRow>)[]));

        WaitFor(() => _db.Reads.Single().IsCancellationRequested, () => "the glance's own read was not cancelled");
        Assert.Contains("No previous meeting on record", cut.Markup);
    }
}

/// <summary>
/// The Head-to-head window re-aimed at another game while it loads hands over only the new game's
/// read: the names were written before the meetings were read, so the old game's late meetings
/// could land under the new names. It still hears its own game, and a closed window lets go.
/// </summary>
public class HeadToHeadLatestLoadTests : DeskTestContext
{
    private readonly ScriptedDatabase _db = new();

    public HeadToHeadLatestLoadTests() => _db.Register(Services);

    /// <summary>The window with what it listens for in reach.</summary>
    public sealed class ProbedHeadToHead : HeadToHeadPanel
    {
        public IReadOnlyDictionary<string, IReadOnlySet<string>> Held => WatchesIds;
    }

    [Fact]
    public void A_window_re_aimed_mid_load_shows_only_the_new_game()
    {
        var release = new TaskCompletionSource();
        _db.Holds.Enqueue(ScriptedDatabase.Until(release));

        var cut = RenderComponent<ProbedHeadToHead>(p => p.Add(x => x.GameId, 1));
        WaitFor(() => _db.Reads.Count == 1, () => $"reads of the first game: {_db.Reads.Count}");
        var first = _db.Reads.Single();

        // The new game's reads answer at once: no meetings on record.
        cut.SetParametersAndRender(p => p.Add(x => x.GameId, 2));

        WaitFor(() => first.IsCancellationRequested, () => "the first game's load was not cancelled");
        cut.WaitForAssertion(() => Assert.Contains("No previous meeting on record", cut.Markup), Patience);
        Assert.Equal(new HashSet<string> { "2" }, cut.Instance.Held["games"]);

        // The first game's load comes back late and leaves nothing: no failure, no other game held.
        var renders = cut.RenderCount;
        release.SetResult();
        WaitFor(() => cut.RenderCount > renders, () => "the late load never finished");

        Assert.Null(PanelProbe.LoadError(cut));
        Assert.Contains("No previous meeting on record", cut.Markup);
        Assert.Equal(new HashSet<string> { "2" }, cut.Instance.Held["games"]);
    }

    [Fact]
    public async Task A_closed_window_cancels_its_load_and_still_held_its_own_game()
    {
        _db.Holds.Enqueue(ScriptedDatabase.UntilCancelled);

        var cut = RenderComponent<ProbedHeadToHead>(p => p.Add(x => x.GameId, 5));
        WaitFor(() => _db.Reads.Count == 1, () => $"reads: {_db.Reads.Count}");

        // Set before the reads, so the window hears its own game while they run.
        Assert.Equal(new HashSet<string> { "5" }, cut.Instance.Held["games"]);

        await cut.InvokeAsync(() => ((IDisposable)cut.Instance).Dispose());
        WaitFor(() => _db.Reads.Single().IsCancellationRequested, () => "the load was not cancelled on dispose");
    }
}
