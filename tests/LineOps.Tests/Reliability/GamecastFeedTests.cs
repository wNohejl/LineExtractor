using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Adapters;
using LineOps.Ingestion.Configuration;
using LineOps.Ingestion.Services;
using LineOps.Reliability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LineOps.Tests.Reliability;

/// <summary>
/// The feed that follows games in play (spec 2026-10-24 §6.3): every poll it reads the summary
/// of each game on, writes it through <see cref="GamecastWriter"/>, and when a game goes final it
/// writes the box score once and lets the game go. A fake port states each game, so a test can
/// say exactly what ESPN would.
/// </summary>
[Collection(PostgresCollection.Name)]
public class GamecastFeedTests(PostgresFixture fixture)
{
    /// <summary>States each game by its event id, and counts what was asked of it.</summary>
    private sealed class FakeGamecasts(string key) : IGamecastSource
    {
        public string Key => key;

        public Dictionary<string, Func<CanonicalGamecast>> Games { get; } = [];
        public Dictionary<string, Func<StatsFetchResult>> BoxScores { get; } = [];
        public HashSet<string> Failing { get; } = [];

        public Dictionary<string, int> GamecastCalls { get; } = [];
        public Dictionary<string, int> ProbabilityCalls { get; } = [];
        public Dictionary<string, int> BoxScoreCalls { get; } = [];

        public Task<CanonicalGamecast?> FetchGamecastAsync(string sportKey, string sourceGameId, CancellationToken ct)
        {
            Count(GamecastCalls, sourceGameId);

            if (Failing.Contains(sourceGameId))
                throw new HttpRequestException("ESPN said no.");

            return Task.FromResult(Games.TryGetValue(sourceGameId, out var game) ? game() : null);
        }

        public Task<IReadOnlyDictionary<string, CanonicalPlayProbability>> FetchProbabilitiesAsync(
            string sportKey, string sourceGameId, CancellationToken ct)
        {
            Count(ProbabilityCalls, sourceGameId);

            IReadOnlyDictionary<string, CanonicalPlayProbability> rows = new Dictionary<string, CanonicalPlayProbability>
            {
                [$"{sourceGameId}-1"] = new($"{sourceGameId}-1", 0.55m, 0.48m, 0.51m)
            };

            return Task.FromResult(rows);
        }

        public Task<StatsFetchResult> FetchBoxScoreAsync(
            string sportKey, DateOnly date, string sourceGameId, CancellationToken ct)
        {
            Count(BoxScoreCalls, sourceGameId);
            return Task.FromResult(BoxScores[sourceGameId]());
        }

        private static void Count(Dictionary<string, int> calls, string id)
            => calls[id] = calls.GetValueOrDefault(id) + 1;
    }

    private sealed record Scaffold(Sport Sport, string SourceKey, Team Home, Team Away);

    private static readonly DateTimeOffset Now
        = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private async Task<Scaffold> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var sport = new Sport { Key = $"gcf-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);

        var sourceKey = $"gcf-{suffix}";
        db.Sources.Add(new Source { Key = sourceKey, Name = "Fake ESPN", Kind = SourceKind.Stats, BaseUrl = "local://fake" });
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = $"Home {suffix}", Abbrev = "HOM", ExternalIds = new() { [sourceKey] = "1" } };
        var away = new Team { SportId = sport.Id, Name = $"Away {suffix}", Abbrev = "AWY", ExternalIds = new() { [sourceKey] = "2" } };
        db.Teams.AddRange(home, away);
        await db.SaveChangesAsync();

        return new Scaffold(sport, sourceKey, home, away);
    }

    private async Task<Game> GameAsync(Scaffold s, string eventId, DateTimeOffset startsAt, GameStatus status)
    {
        await using var db = fixture.CreateContext();

        var game = new Game
        {
            SportId = s.Sport.Id,
            HomeTeamId = s.Home.Id,
            AwayTeamId = s.Away.Id,
            StartsAt = startsAt,
            Status = status,
            SeasonYear = startsAt.Year,
            ExternalIds = new Dictionary<string, string> { [s.SourceKey] = eventId }
        };

        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game;
    }

    private static CanonicalDrive Drive(string eventId, int sequence, bool current)
        => new($"{eventId}-d{sequence}", sequence, "1", "4 plays", current ? null : "Punt", 12, 4, "2:10",
            IsScore: false, IsCurrent: current, StartPeriod: 1, StartClock: "15:00", StartYardLine: 25,
            EndPeriod: 1, EndClock: "12:50", EndYardLine: null);

    private static CanonicalPlay Play(string eventId, int n, int drive, int home = 0, int away = 0)
        => new($"{eventId}-{n}", n, $"{eventId}-d{drive}", Period: 1, Clock: "14:20", ClockSeconds: 860,
            TypeId: "24", Type: "Pass Reception", Text: $"Play {n}", HomeScore: home, AwayScore: away,
            IsScoring: false, IsTurnover: false, IsPenalty: false, Yardage: 7, SourceTeamId: "1",
            Start: null, End: null, HomeWinPct: null, SpreadCoverPctHome: null, TotalOverPct: null,
            Wallclock: null, Modified: null);

    /// <summary>A game on: one drive in progress, <paramref name="plays"/> plays into it.</summary>
    private static CanonicalGamecast Live(string eventId, int plays = 2)
        => new(eventId, "live", [Drive(eventId, 1, current: true)],
            Enumerable.Range(1, plays).Select(n => Play(eventId, n, 1)).ToList());

    /// <summary>
    /// One instance of the port per scope, as the host registers ESPN's: each wraps the shared
    /// fake and stamps what it was asked with its own number, so a test can see which instance —
    /// and so whose request pacing — every request went through.
    /// </summary>
    private sealed class PerScope(FakeGamecasts inner, int instance, List<(int Instance, string Request)> log) : IGamecastSource
    {
        public string Key => inner.Key;

        public Task<CanonicalGamecast?> FetchGamecastAsync(string sportKey, string sourceGameId, CancellationToken ct)
        {
            lock (log) log.Add((instance, $"summary {sourceGameId}"));
            return inner.FetchGamecastAsync(sportKey, sourceGameId, ct);
        }

        public Task<IReadOnlyDictionary<string, CanonicalPlayProbability>> FetchProbabilitiesAsync(
            string sportKey, string sourceGameId, CancellationToken ct)
        {
            lock (log) log.Add((instance, $"probabilities {sourceGameId}"));
            return inner.FetchProbabilitiesAsync(sportKey, sourceGameId, ct);
        }

        public Task<StatsFetchResult> FetchBoxScoreAsync(
            string sportKey, DateOnly date, string sourceGameId, CancellationToken ct)
        {
            lock (log) log.Add((instance, $"box {sourceGameId}"));
            return inner.FetchBoxScoreAsync(sportKey, date, sourceGameId, ct);
        }
    }

    private ServiceProvider Services(FakeGamecasts source, List<(int Instance, string Request)>? perScope = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<LineOpsDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddLogging();
        services.AddScoped<EntityResolver>();
        services.AddScoped(sp => new BudgetCalculator(sp.GetRequiredService<LineOpsDbContext>()));
        services.AddScoped<CreditBudgetGuard>();
        services.AddScoped<StatsIngestionService>();
        services.AddScoped<GamecastWriter>();

        if (perScope is null)
        {
            services.AddSingleton<IGamecastSource>(source);
        }
        else
        {
            var instances = 0;
            services.AddScoped<IGamecastSource>(_ => new PerScope(source, Interlocked.Increment(ref instances), perScope));
        }

        return services.BuildServiceProvider();
    }

    private GamecastFeed Feed(
        Scaffold s, FakeGamecasts source, int maxFollowed = 4,
        Action<GamecastOptions>? configure = null, List<(int Instance, string Request)>? perScope = null)
    {
        var gamecast = new GamecastOptions { Sports = [s.Sport.Key], MaxFollowedGames = maxFollowed };
        configure?.Invoke(gamecast);

        return new(Services(source, perScope).GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new IngestionOptions { Gamecast = gamecast }),
            NullLogger<GamecastFeed>.Instance);
    }

    private async Task MarkAsync(Game game, GameStatus status)
    {
        await using var db = fixture.CreateContext();
        var row = await db.Games.SingleAsync(g => g.Id == game.Id);
        row.Status = status;
        await db.SaveChangesAsync();
    }

    private async Task<bool> AnyCurrentDriveAsync(int gameId)
    {
        await using var db = fixture.CreateContext();
        return await db.GameDrives.AnyAsync(d => d.GameId == gameId && d.IsCurrent);
    }

    private static StatsFetchResult NoBoxScore => new([], [], [], new FetchCost(1));

    private async Task<int> PlaysAsync(int gameId)
    {
        await using var db = fixture.CreateContext();
        return await db.GamePlays.CountAsync(p => p.GameId == gameId);
    }

    [Fact]
    public async Task It_follows_only_the_games_in_play()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);

        // Started an hour ago and not yet marked live here: in play all the same.
        var live = await GameAsync(s, "e-live", Now.AddHours(-1), GameStatus.Scheduled);
        var later = await GameAsync(s, "e-later", Now.AddHours(2), GameStatus.Scheduled);
        var done = await GameAsync(s, "e-done", Now.AddHours(-6), GameStatus.Final);

        foreach (var id in new[] { "e-live", "e-later", "e-done" })
            source.Games[id] = () => Live(id);

        await Feed(s, source).PollAsync(Now, CancellationToken.None);

        Assert.Equal(1, source.GamecastCalls.GetValueOrDefault("e-live"));
        Assert.False(source.GamecastCalls.ContainsKey("e-later"));
        Assert.False(source.GamecastCalls.ContainsKey("e-done"));

        Assert.Equal(2, await PlaysAsync(live.Id));
        Assert.Equal(0, await PlaysAsync(later.Id));
        Assert.Equal(0, await PlaysAsync(done.Id));
    }

    [Fact]
    public async Task It_follows_no_more_than_the_ceiling_earliest_first()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);

        await GameAsync(s, "e-1", Now.AddHours(-3), GameStatus.Live);
        await GameAsync(s, "e-3", Now.AddHours(-1), GameStatus.Live);
        await GameAsync(s, "e-2", Now.AddHours(-2), GameStatus.Live);

        foreach (var id in new[] { "e-1", "e-2", "e-3" })
            source.Games[id] = () => Live(id);

        var feed = Feed(s, source, maxFollowed: 2);
        await feed.PollAsync(Now, CancellationToken.None);

        Assert.Equal(["e-1", "e-2"], source.GamecastCalls.Keys.Order());
    }

    [Fact]
    public async Task Probabilities_are_asked_at_their_own_slower_cadence_and_merged_on_every_write()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);
        var game = await GameAsync(s, "e-p", Now.AddHours(-1), GameStatus.Live);
        source.Games["e-p"] = () => Live("e-p");

        var feed = Feed(s, source);
        await feed.PollAsync(Now, CancellationToken.None);
        await feed.PollAsync(Now.AddSeconds(15), CancellationToken.None);
        await feed.PollAsync(Now.AddSeconds(30), CancellationToken.None);

        Assert.Equal(3, source.GamecastCalls["e-p"]);
        Assert.Equal(1, source.ProbabilityCalls["e-p"]);

        await feed.PollAsync(Now.AddSeconds(60), CancellationToken.None);
        Assert.Equal(2, source.ProbabilityCalls["e-p"]);

        await using var db = fixture.CreateContext();
        var first = await db.GamePlays.SingleAsync(p => p.GameId == game.Id && p.Sequence == 1);
        Assert.Equal(0.48m, first.SpreadCoverPctHome);
        Assert.Equal(0.51m, first.TotalOverPct);
    }

    [Fact]
    public async Task A_game_gone_final_gets_its_box_score_once_and_is_let_go()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);
        var game = await GameAsync(s, "e-f", Now.AddHours(-3), GameStatus.Live);

        var final = false;
        // Final: a third play, and the drive ESPN dropped from "current" still claiming it in the
        // payload's last state — the store must not keep a current drive on a finished game.
        source.Games["e-f"] = () => final
            ? new CanonicalGamecast("e-f", "final", [Drive("e-f", 1, current: true)],
                [Play("e-f", 1, 1), Play("e-f", 2, 1), Play("e-f", 3, 1, home: 17, away: 10)])
            : Live("e-f");

        source.BoxScores["e-f"] = () => new StatsFetchResult(
            Games: [new CanonicalGame("e-f", s.Sport.Key, s.Home.Name, s.Away.Name, game.StartsAt, "final", 17, 10)],
            Players: [new CanonicalPlayer("p-1", s.Sport.Key, "Pat Passer", "QB", "1", s.Home.Name)],
            PlayerStats: [new CanonicalPlayerStat("p-1", "e-f", """{"passingYards":250}""", "1", s.Home.Name)],
            Cost: new FetchCost(2));

        var feed = Feed(s, source);
        await feed.PollAsync(Now, CancellationToken.None);

        final = true;
        await feed.PollAsync(Now.AddSeconds(15), CancellationToken.None);
        await feed.PollAsync(Now.AddSeconds(30), CancellationToken.None);
        await feed.PollAsync(Now.AddSeconds(45), CancellationToken.None);

        Assert.Equal(2, source.GamecastCalls["e-f"]);
        Assert.Equal(1, source.BoxScoreCalls["e-f"]);
        Assert.Equal(3, await PlaysAsync(game.Id));

        await using var db = fixture.CreateContext();
        Assert.False(await db.GameDrives.AnyAsync(d => d.GameId == game.Id && d.IsCurrent));
        Assert.True(await db.PlayerGameStats.AnyAsync(st => st.GameId == game.Id));

        var stored = await db.Games.SingleAsync(g => g.Id == game.Id);
        Assert.Equal(GameStatus.Final, stored.Status);
        Assert.Equal((17, 10), (stored.HomeScore, stored.AwayScore));
    }

    [Fact]
    public async Task A_game_the_live_pass_marked_final_first_still_gets_its_last_write_and_box_score()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);
        var game = await GameAsync(s, "e-r", Now.AddHours(-3), GameStatus.Live);

        var final = false;
        source.Games["e-r"] = () => final
            ? new CanonicalGamecast("e-r", "final", [Drive("e-r", 1, current: false)],
                [Play("e-r", 1, 1), Play("e-r", 2, 1), Play("e-r", 3, 1)])
            : Live("e-r");

        source.BoxScores["e-r"] = () => new StatsFetchResult(
            Games: [new CanonicalGame("e-r", s.Sport.Key, s.Home.Name, s.Away.Name, game.StartsAt, "final", 3, 0)],
            Players: [], PlayerStats: [], Cost: new FetchCost(2));

        var feed = Feed(s, source);
        await feed.PollAsync(Now, CancellationToken.None);

        // The scoreboard pass landed the final between two polls, so the game is no longer in play.
        await using (var db = fixture.CreateContext())
        {
            var row = await db.Games.SingleAsync(g => g.Id == game.Id);
            row.Status = GameStatus.Final;
            await db.SaveChangesAsync();
        }

        final = true;
        await feed.PollAsync(Now.AddSeconds(15), CancellationToken.None);
        await feed.PollAsync(Now.AddSeconds(30), CancellationToken.None);

        Assert.Equal(2, source.GamecastCalls["e-r"]);
        Assert.Equal(1, source.BoxScoreCalls["e-r"]);
        Assert.Equal(3, await PlaysAsync(game.Id));
    }

    [Fact]
    public async Task One_game_failing_does_not_cost_the_others_or_the_loop()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);

        await GameAsync(s, "e-bad", Now.AddHours(-2), GameStatus.Live);
        var good = await GameAsync(s, "e-good", Now.AddHours(-1), GameStatus.Live);
        source.Games["e-good"] = () => Live("e-good");
        source.Failing.Add("e-bad");

        var feed = Feed(s, source);
        await feed.PollAsync(Now, CancellationToken.None);
        await feed.PollAsync(Now.AddSeconds(15), CancellationToken.None);

        Assert.Equal(2, source.GamecastCalls["e-bad"]);
        Assert.Equal(2, source.GamecastCalls["e-good"]);
        Assert.Equal(2, await PlaysAsync(good.Id));
    }

    [Fact]
    public async Task Every_request_in_a_poll_goes_through_one_port_so_its_pacing_spans_the_games()
    {
        // The adapter spaces its requests by RequestDelay, measured from its own last request; a
        // port per game starts each one's clock afresh and the summaries go out back to back. The
        // pacing itself is the adapter's; what the feed owes it is one instance for the whole poll.
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);
        var log = new List<(int Instance, string Request)>();

        foreach (var (id, hours) in new[] { ("e-a", -3), ("e-b", -2), ("e-c", -1) })
        {
            await GameAsync(s, id, Now.AddHours(hours), GameStatus.Live);
            source.Games[id] = () => Live(id);
        }

        var feed = Feed(s, source, perScope: log);
        await feed.PollAsync(Now, CancellationToken.None);

        Assert.Equal(6, log.Count);
        Assert.Single(log.Select(r => r.Instance).Distinct());

        log.Clear();
        await feed.PollAsync(Now.AddSeconds(15), CancellationToken.None);

        Assert.Equal(3, log.Count);
        Assert.Single(log.Select(r => r.Instance).Distinct());
    }

    [Fact]
    public async Task A_game_marked_final_while_its_summary_still_says_live_is_followed_until_the_summary_agrees()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);
        var game = await GameAsync(s, "e-lag", Now.AddHours(-3), GameStatus.Live);

        var reads = 0;
        source.Games["e-lag"] = () => ++reads switch
        {
            1 => Live("e-lag"),
            // The scoreboard has the final; the summary, cached apart, has not caught up.
            2 or 3 => Live("e-lag", plays: 3),
            // And the final payload still calls its last drive current.
            _ => new CanonicalGamecast("e-lag", "final", [Drive("e-lag", 1, current: true)],
                [Play("e-lag", 1, 1), Play("e-lag", 2, 1), Play("e-lag", 3, 1), Play("e-lag", 4, 1, home: 20, away: 17)])
        };
        source.BoxScores["e-lag"] = () => NoBoxScore;

        var feed = Feed(s, source);
        await feed.PollAsync(Now, CancellationToken.None);
        await MarkAsync(game, GameStatus.Final);

        for (var i = 1; i <= 5; i++)
            await feed.PollAsync(Now.AddSeconds(15 * i), CancellationToken.None);

        Assert.Equal(4, source.GamecastCalls["e-lag"]);
        Assert.Equal(1, source.BoxScoreCalls["e-lag"]);
        Assert.Equal(4, await PlaysAsync(game.Id));
        Assert.False(await AnyCurrentDriveAsync(game.Id));
        Assert.Empty(feed.Following);
    }

    [Fact]
    public async Task A_leaving_game_whose_summary_never_says_final_is_finished_after_a_bounded_number_of_reads()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);
        var game = await GameAsync(s, "e-stuck", Now.AddHours(-3), GameStatus.Live);
        source.Games["e-stuck"] = () => Live("e-stuck");
        source.BoxScores["e-stuck"] = () => NoBoxScore;

        var feed = Feed(s, source);
        await feed.PollAsync(Now, CancellationToken.None);
        Assert.True(await AnyCurrentDriveAsync(game.Id));

        await MarkAsync(game, GameStatus.Final);

        for (var i = 1; i <= 12; i++)
            await feed.PollAsync(Now.AddSeconds(15 * i), CancellationToken.None);

        Assert.Equal(1 + GamecastFeed.LeavingReads, source.GamecastCalls["e-stuck"]);
        Assert.Equal(1, source.BoxScoreCalls["e-stuck"]);
        Assert.False(await AnyCurrentDriveAsync(game.Id));
        Assert.Empty(feed.Following);
    }

    [Fact]
    public async Task A_game_that_keeps_failing_is_backed_off_and_gives_its_slot_to_a_healthy_one()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);

        await GameAsync(s, "e-404", Now.AddHours(-2), GameStatus.Live);
        var good = await GameAsync(s, "e-ok", Now.AddHours(-1), GameStatus.Live);
        source.Failing.Add("e-404");
        source.Games["e-ok"] = () => Live("e-ok");

        var feed = Feed(s, source, maxFollowed: 1);
        var t = 0;

        for (; t < GamecastFeed.FailuresBeforeBackOff; t++)
            await feed.PollAsync(Now.AddSeconds(15 * t), CancellationToken.None);

        Assert.Equal(GamecastFeed.FailuresBeforeBackOff, source.GamecastCalls["e-404"]);
        Assert.False(source.GamecastCalls.ContainsKey("e-ok"));

        // Long past any back-off: still the healthy game holds the one slot.
        for (var i = 0; i < 4; i++, t++)
            await feed.PollAsync(Now.AddSeconds(15 * t).AddMinutes(30 * i), CancellationToken.None);

        Assert.Equal(GamecastFeed.FailuresBeforeBackOff, source.GamecastCalls["e-404"]);
        Assert.Equal(4, source.GamecastCalls["e-ok"]);
        Assert.Equal(2, await PlaysAsync(good.Id));
        Assert.False(source.ProbabilityCalls.ContainsKey("e-404"));
    }

    [Fact]
    public async Task A_backed_off_game_is_asked_again_once_its_back_off_has_passed()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);
        await GameAsync(s, "e-flaky", Now.AddHours(-1), GameStatus.Live);
        source.Failing.Add("e-flaky");

        var feed = Feed(s, source);

        for (var t = 0; t < GamecastFeed.FailuresBeforeBackOff; t++)
            await feed.PollAsync(Now.AddSeconds(15 * t), CancellationToken.None);

        var last = Now.AddSeconds(15 * (GamecastFeed.FailuresBeforeBackOff - 1));
        await feed.PollAsync(last.AddSeconds(30), CancellationToken.None);
        Assert.Equal(GamecastFeed.FailuresBeforeBackOff, source.GamecastCalls["e-flaky"]);

        // It recovers: the next ask after the back-off finds it and follows it again.
        source.Failing.Remove("e-flaky");
        source.Games["e-flaky"] = () => Live("e-flaky");
        await feed.PollAsync(last.AddMinutes(5), CancellationToken.None);
        await feed.PollAsync(last.AddMinutes(5).AddSeconds(15), CancellationToken.None);

        Assert.Equal(GamecastFeed.FailuresBeforeBackOff + 2, source.GamecastCalls["e-flaky"]);
    }

    [Fact]
    public async Task A_game_whose_summary_stays_pre_asks_no_probabilities_and_is_backed_off()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);

        await GameAsync(s, "e-delay", Now.AddHours(-2), GameStatus.Scheduled);
        await GameAsync(s, "e-on", Now.AddHours(-1), GameStatus.Live);
        source.Games["e-delay"] = () => new CanonicalGamecast("e-delay", "pre", [], []);
        source.Games["e-on"] = () => Live("e-on");

        var feed = Feed(s, source, maxFollowed: 1);

        for (var t = 0; t < GamecastFeed.FailuresBeforeBackOff + 2; t++)
            await feed.PollAsync(Now.AddSeconds(15 * t), CancellationToken.None);

        Assert.Equal(GamecastFeed.FailuresBeforeBackOff, source.GamecastCalls["e-delay"]);
        Assert.False(source.ProbabilityCalls.ContainsKey("e-delay"));
        Assert.Equal(2, source.GamecastCalls["e-on"]);
    }

    [Fact]
    public async Task A_postponed_game_is_let_go_and_not_taken_up_again()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);

        // ESPN has it postponed; our row has not caught up and still says live.
        await GameAsync(s, "e-pp", Now.AddHours(-1), GameStatus.Live);
        source.Games["e-pp"] = () => new CanonicalGamecast("e-pp", "postponed", [], []);

        var feed = Feed(s, source);
        await feed.PollAsync(Now, CancellationToken.None);
        await feed.PollAsync(Now.AddSeconds(15), CancellationToken.None);
        await feed.PollAsync(Now.AddSeconds(30), CancellationToken.None);

        Assert.Equal(1, source.GamecastCalls["e-pp"]);
        Assert.Empty(feed.Following);
    }

    [Fact]
    public async Task A_probability_cadence_of_zero_or_less_reads_as_the_default()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);
        await GameAsync(s, "e-z", Now.AddHours(-1), GameStatus.Live);
        source.Games["e-z"] = () => Live("e-z");

        var feed = Feed(s, source, configure: o => o.ProbabilityPoll = TimeSpan.FromSeconds(-5));

        for (var t = 0; t < 3; t++)
            await feed.PollAsync(Now.AddSeconds(15 * t), CancellationToken.None);

        Assert.Equal(1, source.ProbabilityCalls["e-z"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_poll_of_zero_or_less_reads_as_the_default_rather_than_spinning_or_stopping_the_host(int seconds)
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);
        await GameAsync(s, "e-poll", DateTimeOffset.UtcNow.AddHours(-1), GameStatus.Live);
        source.Games["e-poll"] = () => Live("e-poll");

        var feed = Feed(s, source, configure: o => o.Poll = TimeSpan.FromSeconds(seconds));
        await feed.StartAsync(CancellationToken.None);

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);

            while (!source.GamecastCalls.ContainsKey("e-poll") && DateTime.UtcNow < deadline)
                await Task.Delay(50);

            await Task.Delay(500);

            Assert.Equal(1, source.GamecastCalls.GetValueOrDefault("e-poll"));
            Assert.False(feed.ExecuteTask!.IsCompleted);
        }
        finally
        {
            await feed.StopAsync(CancellationToken.None);
        }
    }
}
