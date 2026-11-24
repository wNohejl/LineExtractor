using Bunit;
using XLines.Core.Entities;
using XLines.Data;
using XLines.Data.CrossReference;
using XLines.Desk.Windowing;
using XLines.Ingestion.Services;
using XLines.Web.Components;
using XLines.Web.Components.Panels;
using XLines.Web.Tests.Support;
using XLines.Web.Windowing;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace XLines.Web.Tests;

/// <summary>
/// The Journal and the Head-to-head window watch games for the games they show, not for every
/// game in the database: a score in another league used to re-run each of their whole loads. A
/// notice addressed to a game they hold still reloads them, and so does a bare one, which cannot
/// say what changed.
/// </summary>
public class HeldGamesRefreshTests : WagerTestContext
{
    /// <summary>Long enough for a short settle and the reload behind it to have run, if one was queued.</summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(600);

    private readonly JournalParlayTests.JournalOnHand _journal = new();

    public HeldGamesRefreshTests()
    {
        Services.AddSingleton(_journal);
        Services.AddSingleton<IDbContextFactory<XLinesDbContext>>(new JournalParlayTests.NoDatabase());
    }

    private DeskSignals Signals => Services.GetRequiredService<DeskSignals>();

    private void Publish(params string[] tokens) => Signals.Publish(tokens.ToHashSet());

    /// <summary>The Journal reading <see cref="JournalParlayTests.JournalOnHand"/>, counting its reads, with a short settle.</summary>
    public sealed class CountingJournal : JournalPanel
    {
        [Inject] private JournalParlayTests.JournalOnHand OnHand { get; set; } = default!;

        public int Reads;

        protected override TimeSpan RefreshSettle => TimeSpan.FromMilliseconds(50);

        protected override Task<JournalRead> FetchAsync(JournalFilter filter)
        {
            Interlocked.Increment(ref Reads);

            return Task.FromResult(new JournalRead(
                OnHand.Entries.ToList(), OnHand.Parlays.ToList(), OnHand.Books, ["nfl"], 0));
        }
    }

    /// <summary>
    /// The Head-to-head window with a short settle. It reads the database through services this
    /// project cannot build, so every load fails at its first read; the count of those attempts
    /// is the count of loads, and the game it is about is the one id it holds without a result.
    /// </summary>
    public sealed class QuickHeadToHead : HeadToHeadPanel
    {
        protected override TimeSpan RefreshSettle => TimeSpan.FromMilliseconds(50);
    }

    private static Game Game(int id) => new()
    {
        Id = id,
        Sport = new Sport { Key = "nfl", Name = "NFL" },
        AwayTeam = new Team { Name = "Buffalo Bills", Abbrev = "BUF" },
        HomeTeam = new Team { Name = "Kansas City Chiefs", Abbrev = "KC" },
        StartsAt = DateTimeOffset.UtcNow.AddHours(4),
        Status = GameStatus.Scheduled,
        SeasonYear = 2026
    };

    private static JournalEntry Entry(int id, int gameId) => new()
    {
        Id = id,
        GameId = gameId,
        Game = Game(gameId),
        Market = Markets.Moneyline,
        Outcome = "Kansas City Chiefs",
        PriceTaken = -110,
        Stake = 50m,
        Book = "draftkings",
        PlacedAt = DateTimeOffset.UtcNow.AddHours(-1)
    };

    private IRenderedComponent<CountingJournal> RenderJournal()
    {
        var window = Services.GetRequiredService<WindowManager>().Open(WindowCatalog.Find(WindowCatalog.Journal)!);
        var cut = RenderComponent<CountingJournal>(p => p.AddCascadingValue("WindowId", window.Id));
        cut.WaitForAssertion(() => Assert.Equal(1, cut.Instance.Reads), Settle);
        return cut;
    }

    [Fact]
    public void The_journal_sits_still_for_a_game_it_does_not_hold()
    {
        _journal.Entries.Add(Entry(7, 21));
        var cut = RenderJournal();

        Publish("games:999", "games:22");

        Thread.Sleep(Quiet);
        Assert.Equal(1, cut.Instance.Reads);
    }

    [Fact]
    public void The_journal_reloads_for_an_entrys_game_a_legs_game_a_bare_notice_and_its_own_topic()
    {
        _journal.Entries.Add(Entry(7, 21));
        _journal.Parlays.Add(new Parlay { Legs = [Entry(8, 34)] });
        var cut = RenderJournal();

        Publish("games:999", "games:21");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.Instance.Reads), Settle);

        Publish("games:34");
        cut.WaitForAssertion(() => Assert.Equal(3, cut.Instance.Reads), Settle);

        Publish("games");
        cut.WaitForAssertion(() => Assert.Equal(4, cut.Instance.Reads), Settle);

        Publish("journal");
        cut.WaitForAssertion(() => Assert.Equal(5, cut.Instance.Reads), Settle);
    }

    [Fact]
    public void An_empty_journal_hears_only_bare_games_notices()
    {
        var cut = RenderJournal();

        Publish("games:21");
        Thread.Sleep(Quiet);
        Assert.Equal(1, cut.Instance.Reads);

        Publish("games");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.Instance.Reads), Settle);
    }

    [Fact]
    public void The_head_to_head_window_reloads_for_its_own_game_and_a_bare_notice_only()
    {
        var loads = 0;
        Services.AddScoped<BoardService>(_ =>
        {
            Interlocked.Increment(ref loads);
            throw new InvalidOperationException("No database in a render test.");
        });

        var cut = RenderComponent<QuickHeadToHead>(p => p.Add(x => x.GameId, 5));
        cut.WaitForAssertion(() => Assert.Equal(1, Volatile.Read(ref loads)), Settle);

        Publish("games:999");
        Thread.Sleep(Quiet);
        Assert.Equal(1, Volatile.Read(ref loads));

        Publish("games:5");
        cut.WaitForAssertion(() => Assert.Equal(2, Volatile.Read(ref loads)), Settle);

        Publish("games");
        cut.WaitForAssertion(() => Assert.Equal(3, Volatile.Read(ref loads)), Settle);
    }

    /// <summary>The meetings themselves, which a render here cannot load: their ids and the upcoming game's, under games only.</summary>
    [Fact]
    public void The_head_to_head_window_holds_its_meetings_and_its_own_game()
    {
        var held = HeadToHeadPanel.HeldGames([12, 30, 5]);

        Assert.Equal(["games"], held.Keys);
        Assert.Equal(new HashSet<string> { "12", "30", "5" }, held["games"]);
    }

    [Fact]
    public void The_journal_holds_entry_and_leg_games_and_skips_free_text_entries()
    {
        var held = JournalPanel.HeldGames(
            [Entry(1, 21), new JournalEntry { Id = 2, GameId = null }],
            [new Parlay { Legs = [Entry(3, 34), Entry(4, 21)] }]);

        Assert.Equal(new HashSet<string> { "21", "34" }, held["games"]);
    }
}
