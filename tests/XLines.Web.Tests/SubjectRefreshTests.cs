using System.Reflection;
using Bunit;
using XLines.Data;
using XLines.Data.CrossReference;
using XLines.Desk.Windowing;
using XLines.Web.Components.Panels;
using XLines.Web.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static XLines.Web.Tests.Support.Eventually;

namespace XLines.Web.Tests;

/// <summary>
/// The Team and Player windows watch games for their subject's games, not for every game in the
/// database: a score in another league used to re-run each of their whole loads. The games on
/// screen are not the filter — the subject's own new game has an id the window never held — so a
/// notice addressed to games asks the database whether any is the subject's, and reloads only if
/// one is. A bare notice reloads as before. And a newer load cancels the one in flight, so a
/// superseded load cannot hand over the season that was asked for before.
/// </summary>
public class SubjectRefreshTests : WagerTestContext
{
    /// <summary>Long enough for a short settle and the reload behind it to have run, if one was queued.</summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(600);

    private DeskSignals Signals => Services.GetRequiredService<DeskSignals>();

    private void Publish(params string[] tokens) => Signals.Publish(tokens.ToHashSet());

    /// <summary>The Team window with a short settle, answering the own-game check from <see cref="Own"/>.</summary>
    public sealed class QuickTeam : TeamPanel
    {
        public static readonly HashSet<int> Own = [42];

        public int Checks;
        public bool CheckFails;

        protected override TimeSpan RefreshSettle => TimeSpan.FromMilliseconds(50);

        protected override Task<bool> PlaysInAnyAsync(int[] ids, CancellationToken ct)
        {
            Interlocked.Increment(ref Checks);

            return CheckFails
                ? throw new InvalidOperationException("No database in a render test.")
                : Task.FromResult(ids.Any(Own.Contains));
        }
    }

    /// <summary>The Player window with a short settle, answering the own-game check from <see cref="QuickTeam.Own"/>.</summary>
    public sealed class QuickPlayer : PlayerPanel
    {
        public int Checks;

        protected override TimeSpan RefreshSettle => TimeSpan.FromMilliseconds(50);

        protected override Task<bool> PlaysInAnyAsync(int[] ids, CancellationToken ct)
        {
            Interlocked.Increment(ref Checks);
            return Task.FromResult(ids.Any(QuickTeam.Own.Contains));
        }
    }

    /// <summary>A context factory that counts its contexts and refuses each: one per Player load.</summary>
    private sealed class CountingFactory : IDbContextFactory<XLinesDbContext>
    {
        public int Created;

        public XLinesDbContext CreateDbContext()
        {
            Interlocked.Increment(ref Created);
            throw new InvalidOperationException("No database in a render test.");
        }
    }

    // ---- Which notices reload --------------------------------------------------------------

    /// <summary>
    /// Each Team load fails at its first service, as the Head-to-head bench's does; the count of
    /// those attempts is the count of loads. Nothing is ever held, so a reload for game 42 can only
    /// come from the check — exactly the subject's new game the held-ids filter would miss.
    /// </summary>
    [Fact]
    public void The_team_window_reloads_for_its_own_new_game_and_a_bare_notice_only()
    {
        var loads = 0;
        Services.AddScoped<MatchupCrossReference>(_ =>
        {
            Interlocked.Increment(ref loads);
            throw new InvalidOperationException("No database in a render test.");
        });

        var cut = RenderComponent<QuickTeam>(p => p.Add(x => x.TeamId, 7));
        cut.WaitForAssertion(() => Assert.Equal(1, Volatile.Read(ref loads)), Settle);

        // Someone else's game: asked about once, and no reload.
        Publish("games:999", "games:998");
        cut.WaitForAssertion(() => Assert.Equal(1, Volatile.Read(ref cut.Instance.Checks)), Settle);
        Thread.Sleep(Quiet);
        Assert.Equal(1, Volatile.Read(ref loads));

        // The team's own game, an id the window never held.
        Publish("games:999", "games:42");
        cut.WaitForAssertion(() => Assert.Equal(2, Volatile.Read(ref loads)), Settle);

        // A bare notice cannot say what changed: it reloads without asking.
        Publish("games");
        cut.WaitForAssertion(() => Assert.Equal(3, Volatile.Read(ref loads)), Settle);
        Assert.Equal(2, Volatile.Read(ref cut.Instance.Checks));
    }

    [Fact]
    public void A_check_that_fails_reloads_the_team_window()
    {
        var loads = 0;
        Services.AddScoped<MatchupCrossReference>(_ =>
        {
            Interlocked.Increment(ref loads);
            throw new InvalidOperationException("No database in a render test.");
        });

        var cut = RenderComponent<QuickTeam>(p => p.Add(x => x.TeamId, 7));
        cut.WaitForAssertion(() => Assert.Equal(1, Volatile.Read(ref loads)), Settle);
        cut.Instance.CheckFails = true;

        Publish("games:999");
        cut.WaitForAssertion(() => Assert.Equal(2, Volatile.Read(ref loads)), Settle);
    }

    [Fact]
    public void The_player_window_reloads_for_its_own_new_game_and_a_bare_notice_only()
    {
        var factory = new CountingFactory();
        Services.AddSingleton<IDbContextFactory<XLinesDbContext>>(factory);

        var cut = RenderComponent<QuickPlayer>(p => p.Add(x => x.PlayerId, 11));
        cut.WaitForAssertion(() => Assert.Equal(1, Volatile.Read(ref factory.Created)), Settle);

        Publish("games:999");
        cut.WaitForAssertion(() => Assert.Equal(1, Volatile.Read(ref cut.Instance.Checks)), Settle);
        Thread.Sleep(Quiet);
        Assert.Equal(1, Volatile.Read(ref factory.Created));

        Publish("games:42");
        cut.WaitForAssertion(() => Assert.Equal(2, Volatile.Read(ref factory.Created)), Settle);

        Publish("games");
        cut.WaitForAssertion(() => Assert.Equal(3, Volatile.Read(ref factory.Created)), Settle);
        Assert.Equal(2, Volatile.Read(ref cut.Instance.Checks));
    }

    // ---- The decision and the query ----------------------------------------------------------

    [Fact]
    public void Only_addressed_games_are_asked_about()
    {
        Assert.Equal(new[] { 12, 30 }, TeamPanel.AddressedGames(new HashSet<string> { "games:12", "games:30" })!.Order());

        // Nothing said what changed, a bare topic, or something that is not a game id: reload.
        Assert.Null(TeamPanel.AddressedGames(new HashSet<string>()));
        Assert.Null(TeamPanel.AddressedGames(new HashSet<string> { "games:12", "games" }));
        Assert.Null(TeamPanel.AddressedGames(new HashSet<string> { "games:x" }));
        Assert.Null(TeamPanel.AddressedGames(new HashSet<string> { "gamesx:12" }));

        // Past the cap the check is no cheaper than the reload.
        var many = Enumerable.Range(1, TeamPanel.MostGamesChecked + 1).Select(i => $"games:{i}").ToHashSet();
        Assert.Null(TeamPanel.AddressedGames(many));
        Assert.NotNull(TeamPanel.AddressedGames(many.Take(TeamPanel.MostGamesChecked).ToHashSet()));
    }

    private static XLinesDbContext NoConnection() => new(new DbContextOptionsBuilder<XLinesDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none")
        .Options);

    /// <summary>One read of games by primary key, narrowed to the team's side of them.</summary>
    [Fact]
    public void The_team_check_reads_games_by_id_on_either_side()
    {
        using var db = NoConnection();
        var sql = TeamPanel.OwnGames(db, 7, [12, 30]).ToQueryString();

        Assert.Contains("= ANY (", sql);
        Assert.Contains("\"HomeTeamId\"", sql);
        Assert.Contains("\"AwayTeamId\"", sql);
    }

    /// <summary>The player's current team on either side, or a stat line of theirs, within the ids.</summary>
    [Fact]
    public void The_player_check_reads_their_teams_games_or_their_stat_lines()
    {
        using var db = NoConnection();
        var sql = PlayerPanel.OwnGames(db, 11, [12, 30]).ToQueryString();

        Assert.Contains("= ANY (", sql);
        Assert.Contains("\"HomeTeamId\"", sql);
        Assert.Contains("\"PlayerId\"", sql);
        Assert.Equal(2, sql.Split("EXISTS").Length - 1);
    }

    // ---- Latest wins -----------------------------------------------------------------------

    private ConnectionGate Gated()
    {
        var gate = new ConnectionGate();
        var options = GatedDatabase.Options(gate);

        Services.AddSingleton<IDbContextFactory<XLinesDbContext>>(new GatedDbFactory(options));
        Services.AddScoped(_ => new XLinesDbContext(options));
        Services.AddScoped(sp => new MatchupCrossReference(sp.GetRequiredService<XLinesDbContext>()));
        Services.AddScoped(sp => new GameLogService(sp.GetRequiredService<XLinesDbContext>()));
        return gate;
    }

    private static T? Field<T>(object panel, Type owner, string name)
        => (T?)owner.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel);

    /// <summary>
    /// A season picked while the first load waits: the first is told to stop, and returns quietly
    /// without handing over — the season asked for stays, rather than the superseded load's
    /// default landing on it — and a closed window lets go of the load it was still waiting on.
    /// </summary>
    private async Task AssertANewerLoadCancelsTheOneInFlight<TPanel>(IRenderedComponent<TPanel> panel, ConnectionGate gate)
        where TPanel : DeskPanelBase
    {
        WaitFor(() => gate.Reads.Count == 1, () => $"reads at render: {gate.Reads.Count}");
        var first = gate.Reads.Single();

        var pick = typeof(TPanel).BaseType!.GetMethod("SetSeasonAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await panel.InvokeAsync(() => { _ = (Task)pick.Invoke(panel.Instance, [2024])!; });

        WaitFor(() => gate.Reads.Count == 2, () => $"reads after the pick: {gate.Reads.Count}");
        var second = gate.Reads.Skip(1).Single();

        WaitFor(() => first.IsCancellationRequested, () => "first load cancelled");
        Assert.False(second.IsCancellationRequested);

        // The superseded load handed over nothing, and did not report itself as a failure.
        Assert.Null(PanelProbe.LoadError(panel.Instance));
        Assert.Equal(2024, Field<int?>(panel.Instance, typeof(TPanel).BaseType!, "_seasonYear"));
        Assert.Empty(Field<IReadOnlyList<int>>(panel.Instance, typeof(TPanel).BaseType!, "_seasons")!);

        await panel.InvokeAsync(() => ((IDisposable)panel.Instance).Dispose());
        WaitFor(() => second.IsCancellationRequested, () => "second load cancelled on dispose");
    }

    [Fact]
    public Task A_newer_team_load_cancels_the_one_in_flight_and_the_superseded_one_hands_over_nothing()
    {
        var gate = Gated();
        var panel = RenderComponent<QuickTeam>(p => p.Add(x => x.TeamId, 7));
        return AssertANewerLoadCancelsTheOneInFlight(panel, gate);
    }

    [Fact]
    public Task A_newer_player_load_cancels_the_one_in_flight_and_the_superseded_one_hands_over_nothing()
    {
        var gate = Gated();
        var panel = RenderComponent<QuickPlayer>(p => p.Add(x => x.PlayerId, 11));
        return AssertANewerLoadCancelsTheOneInFlight(panel, gate);
    }
}
