using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Adapters;
using LineOps.Ingestion.Configuration;
using LineOps.Ingestion.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LineOps.Tests.Reliability;

/// <summary>
/// The backport that fills the play store for games that finished before the feed existed
/// (<see cref="GamecastBackport"/>): finals in the gamecast leagues and the backfill seasons with
/// no plays yet, newest first, a batch a run, through <see cref="GamecastWriter"/>. A fake port
/// states each game, so a test can say exactly what ESPN would.
/// </summary>
[Collection(PostgresCollection.Name)]
public class GamecastBackportTests(PostgresFixture fixture)
{
    /// <summary>States each game by its event id, and counts what was asked of it.</summary>
    private sealed class FakeGamecasts(string key) : IGamecastSource
    {
        public string Key => key;

        public Dictionary<string, Func<CanonicalGamecast>> Games { get; } = [];
        public HashSet<string> Failing { get; } = [];
        public Dictionary<string, int> GamecastCalls { get; } = [];
        public Dictionary<string, int> ProbabilityCalls { get; } = [];

        public Task<CanonicalGamecast?> FetchGamecastAsync(string sportKey, string sourceGameId, CancellationToken ct)
        {
            GamecastCalls[sourceGameId] = GamecastCalls.GetValueOrDefault(sourceGameId) + 1;

            if (Failing.Contains(sourceGameId))
                throw new HttpRequestException("ESPN said no.");

            return Task.FromResult(Games.TryGetValue(sourceGameId, out var game) ? game() : null);
        }

        public Task<GamecastRead?> FetchGamecastWithBoxAsync(string sportKey, string sourceGameId, CancellationToken ct)
            => throw new NotSupportedException("The backport reads no box score.");

        public Task<IReadOnlyDictionary<string, CanonicalPlayProbability>> FetchProbabilitiesAsync(
            string sportKey, string sourceGameId, CancellationToken ct)
        {
            ProbabilityCalls[sourceGameId] = ProbabilityCalls.GetValueOrDefault(sourceGameId) + 1;

            IReadOnlyDictionary<string, CanonicalPlayProbability> rows = new Dictionary<string, CanonicalPlayProbability>
            {
                [$"{sourceGameId}-1"] = new($"{sourceGameId}-1", 0.55m, 0.48m, 0.51m)
            };

            return Task.FromResult(rows);
        }

        public Task<StatsFetchResult> FetchBoxScoreAsync(string sportKey, DateOnly date, string sourceGameId, CancellationToken ct)
            => throw new NotSupportedException("The backport reads no box score.");
    }

    /// <summary>
    /// One port per scope, as the host registers ESPN's, each numbering what it was asked — so a
    /// test can see whose request pacing every request went through.
    /// </summary>
    private sealed class PerScope(FakeGamecasts inner, int instance, List<int> log) : IGamecastSource
    {
        public string Key => inner.Key;

        public Task<CanonicalGamecast?> FetchGamecastAsync(string sportKey, string sourceGameId, CancellationToken ct)
        {
            lock (log) log.Add(instance);
            return inner.FetchGamecastAsync(sportKey, sourceGameId, ct);
        }

        public Task<GamecastRead?> FetchGamecastWithBoxAsync(string sportKey, string sourceGameId, CancellationToken ct)
            => inner.FetchGamecastWithBoxAsync(sportKey, sourceGameId, ct);

        public Task<IReadOnlyDictionary<string, CanonicalPlayProbability>> FetchProbabilitiesAsync(
            string sportKey, string sourceGameId, CancellationToken ct)
        {
            lock (log) log.Add(instance);
            return inner.FetchProbabilitiesAsync(sportKey, sourceGameId, ct);
        }

        public Task<StatsFetchResult> FetchBoxScoreAsync(string sportKey, DateOnly date, string sourceGameId, CancellationToken ct)
            => inner.FetchBoxScoreAsync(sportKey, date, sourceGameId, ct);
    }

    private sealed record Scaffold(Sport Sport, Sport Other, string SourceKey, Team Home, Team Away);

    private static readonly DateTimeOffset Now
        = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    /// <summary>The season the backfill names for the test league: it opened sixty days ago.</summary>
    private static readonly DateOnly SeasonStart = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-60);

    private async Task<Scaffold> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var sport = new Sport { Key = $"gcb-{suffix}", Name = "TEST" };
        var other = new Sport { Key = $"gcx-{suffix}", Name = "OTHER" };
        db.Sports.AddRange(sport, other);

        var sourceKey = $"gcb-{suffix}";
        db.Sources.Add(new Source { Key = sourceKey, Name = "Fake ESPN", Kind = SourceKind.Stats, BaseUrl = "local://fake" });
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = $"Home {suffix}", Abbrev = "HOM", ExternalIds = new() { [sourceKey] = "1" } };
        var away = new Team { SportId = sport.Id, Name = $"Away {suffix}", Abbrev = "AWY", ExternalIds = new() { [sourceKey] = "2" } };
        db.Teams.AddRange(home, away);
        await db.SaveChangesAsync();

        return new Scaffold(sport, other, sourceKey, home, away);
    }

    private async Task<Game> GameAsync(
        Scaffold s, string eventId, DateTimeOffset startsAt, GameStatus status = GameStatus.Final, Sport? sport = null)
    {
        await using var db = fixture.CreateContext();

        var game = new Game
        {
            SportId = (sport ?? s.Sport).Id,
            HomeTeamId = s.Home.Id,
            AwayTeamId = s.Away.Id,
            StartsAt = startsAt,
            Status = status,
            HomeScore = status == GameStatus.Final ? 20 : null,
            AwayScore = status == GameStatus.Final ? 23 : null,
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

    private static CanonicalPlay Play(string eventId, int n, int drive)
        => new($"{eventId}-{n}", n, $"{eventId}-d{drive}", Period: 1, Clock: "14:20", ClockSeconds: 860,
            TypeId: "24", Type: "Pass Reception", Text: $"Play {n}", HomeScore: 0, AwayScore: 0,
            IsScoring: false, IsTurnover: false, IsPenalty: false, Yardage: 7, SourceTeamId: "1",
            Start: null, End: null, HomeWinPct: null, SpreadCoverPctHome: null, TotalOverPct: null,
            Wallclock: null, Modified: null);

    /// <summary>
    /// A finished game's summary: two drives, the last still claiming to be current — the payload
    /// can say so, and the store must not keep it — three plays, and the header's periods.
    /// </summary>
    private static CanonicalGamecast Final(string eventId)
        => new(eventId, "final", [Drive(eventId, 1, current: false), Drive(eventId, 2, current: true)],
            [Play(eventId, 1, 1), Play(eventId, 2, 1), Play(eventId, 3, 2)],
            HomeLinescore: [7, 3, 0, 10, 0], AwayLinescore: [0, 10, 7, 3, 3], StatusDetail: "Final/OT");

    private static CanonicalGamecast NoPlays(string eventId) => new(eventId, "final", [], []);

    private ServiceProvider Services(FakeGamecasts source, List<int>? perScope = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<LineOpsDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddLogging();
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

    private GamecastBackport Backport(
        Scaffold s, FakeGamecasts source, int batch = 40, List<int>? perScope = null, TimeSpan? retryAfter = null)
    {
        var options = new IngestionOptions
        {
            Sports = [s.Sport.Key, s.Other.Key],
            Gamecast = new GamecastOptions
            {
                Sports = [s.Sport.Key],
                BackportBatch = batch,
                BackportRetryAfter = retryAfter ?? TimeSpan.FromDays(30)
            }
        };

        options.Backfill.Seasons[s.Sport.Key] = SeasonStart;
        options.Backfill.Seasons[s.Other.Key] = SeasonStart;

        return new(Services(source, perScope).GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options), NullLogger<GamecastBackport>.Instance);
    }

    private async Task<int> PlaysAsync(int gameId)
    {
        await using var db = fixture.CreateContext();
        return await db.GamePlays.CountAsync(p => p.GameId == gameId);
    }

    /// <summary>One side's periods from the linescore column, read as json: Postgres re-spaces jsonb.</summary>
    private static int[] Periods(string linescore, string side)
        => System.Text.Json.JsonDocument.Parse(linescore).RootElement.GetProperty(side)
            .EnumerateArray().Select(p => p.GetInt32()).ToArray();

    [Fact]
    public async Task It_picks_finals_in_the_gamecast_leagues_and_seasons_with_no_plays_newest_first()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);

        var older = await GameAsync(s, "e-older", Now.AddDays(-14));
        var newer = await GameAsync(s, "e-newer", Now.AddDays(-7));
        var held = await GameAsync(s, "e-held", Now.AddDays(-10));
        await GameAsync(s, "e-sched", Now.AddDays(2), GameStatus.Scheduled);
        await GameAsync(s, "e-post", Now.AddDays(-8), GameStatus.Postponed);
        await GameAsync(s, "e-other", Now.AddDays(-9), sport: s.Other);
        await GameAsync(s, "e-last-season", Now.AddDays(-200));

        // Already in the store, from the feed.
        await using (var db = fixture.CreateContext())
        {
            var sourceId = await db.Sources.Where(x => x.Key == s.SourceKey).Select(x => x.Id).SingleAsync();
            db.GamePlays.Add(new GamePlay { GameId = held.Id, SourceId = sourceId, SourcePlayId = "x", UpdatedAt = Now });
            await db.SaveChangesAsync();
        }

        var picked = await Backport(s, source).EligibleAsync(Now, 10, CancellationToken.None);

        Assert.Equal([newer.Id, older.Id], picked.Select(g => g.GameId));
    }

    [Fact]
    public async Task A_run_takes_no_more_than_its_batch()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);

        foreach (var day in new[] { 3, 4, 5 })
        {
            await GameAsync(s, $"e-{day}", Now.AddDays(-day));
            source.Games[$"e-{day}"] = () => Final($"e-{day}");
        }

        var outcome = await Backport(s, source, batch: 2).RunAsync(Now, CancellationToken.None);

        Assert.Equal(2, outcome.Games);
        Assert.Equal(1, outcome.StillOwed);
        Assert.Equal(["e-3", "e-4"], source.GamecastCalls.Keys.Order());
    }

    [Fact]
    public async Task A_batch_writes_plays_drives_probabilities_and_the_linescore_and_leaves_the_result_alone()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);
        var game = await GameAsync(s, "e-f", Now.AddDays(-3));
        source.Games["e-f"] = () => Final("e-f");

        var outcome = await Backport(s, source).RunAsync(Now, CancellationToken.None);

        Assert.Equal(1, outcome.Games);
        Assert.Equal(0, outcome.Misses);

        await using var db = fixture.CreateContext();

        Assert.Equal(3, await db.GamePlays.CountAsync(p => p.GameId == game.Id));
        Assert.Equal(2, await db.GameDrives.CountAsync(d => d.GameId == game.Id));
        Assert.False(await db.GameDrives.AnyAsync(d => d.GameId == game.Id && d.IsCurrent));

        var first = await db.GamePlays.SingleAsync(p => p.GameId == game.Id && p.Sequence == 1);
        Assert.Equal(0.55m, first.HomeWinPct);
        Assert.Equal(0.48m, first.SpreadCoverPctHome);
        Assert.Equal(0.51m, first.TotalOverPct);

        var stored = await db.Games.SingleAsync(g => g.Id == game.Id);
        Assert.Equal("Final/OT", stored.StatusDetail);
        Assert.NotNull(stored.Linescore);
        Assert.Equal([7, 3, 0, 10, 0], Periods(stored.Linescore, "home"));
        Assert.Equal([0, 10, 7, 3, 3], Periods(stored.Linescore, "away"));

        // The score and the status are the scoreboard pass's, whatever the summary says.
        Assert.Equal(GameStatus.Final, stored.Status);
        Assert.Equal((20, 23), (stored.HomeScore, stored.AwayScore));
    }

    [Fact]
    public async Task A_linescore_and_status_line_already_held_are_left_as_they_are()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);
        var game = await GameAsync(s, "e-k", Now.AddDays(-3));
        source.Games["e-k"] = () => Final("e-k");

        await using (var db = fixture.CreateContext())
        {
            var row = await db.Games.SingleAsync(g => g.Id == game.Id);
            row.Linescore = """{"home":[1],"away":[2]}""";
            row.StatusDetail = "Final";
            await db.SaveChangesAsync();
        }

        await Backport(s, source).RunAsync(Now, CancellationToken.None);

        await using var check = fixture.CreateContext();
        var stored = await check.Games.SingleAsync(g => g.Id == game.Id);
        Assert.Equal("Final", stored.StatusDetail);
        Assert.Equal([1], Periods(stored.Linescore!, "home"));
    }

    [Fact]
    public async Task A_run_is_recorded_under_its_job_with_the_requests_it_made()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);

        foreach (var day in new[] { 3, 4 })
        {
            await GameAsync(s, $"e-{day}", Now.AddDays(-day));
            source.Games[$"e-{day}"] = () => Final($"e-{day}");
        }

        var outcome = await Backport(s, source).RunAsync(Now, CancellationToken.None);

        await using var db = fixture.CreateContext();
        var run = await db.IngestionRuns.SingleAsync(r => r.Source!.Key == s.SourceKey);

        Assert.Equal(outcome.RunId, run.Id);
        Assert.Equal(IngestionJobs.EspnGamecastBackport, run.JobKey);
        Assert.Equal(RunStatus.Success, run.Status);
        Assert.Equal(4, run.RequestsMade);
        Assert.Equal(10, run.RowsIngested);
        Assert.NotNull(run.FinishedAt);
    }

    [Fact]
    public async Task A_game_that_fails_or_has_no_plays_is_recorded_and_skipped_next_run()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);

        var broken = await GameAsync(s, "e-404", Now.AddDays(-3));
        var empty = await GameAsync(s, "e-empty", Now.AddDays(-4));
        var good = await GameAsync(s, "e-good", Now.AddDays(-5));
        source.Failing.Add("e-404");
        source.Games["e-empty"] = () => NoPlays("e-empty");
        source.Games["e-good"] = () => Final("e-good");

        var backport = Backport(s, source);
        var first = await backport.RunAsync(Now, CancellationToken.None);

        Assert.Equal(1, first.Games);
        Assert.Equal(2, first.Misses);
        Assert.Equal(0, first.StillOwed);
        Assert.Equal(3, await PlaysAsync(good.Id));

        await using (var db = fixture.CreateContext())
        {
            var misses = await db.GamecastBackportMisses
                .Where(m => m.GameId == broken.Id || m.GameId == empty.Id)
                .ToDictionaryAsync(m => m.GameId);

            Assert.Equal(1, misses[broken.Id].Attempts);
            Assert.Contains("ESPN said no", misses[broken.Id].Reason);
            Assert.Equal("No plays in the summary.", misses[empty.Id].Reason);

            // A run that missed a game is partial, not failed: a game came in.
            Assert.Equal(RunStatus.Partial, (await db.IngestionRuns.SingleAsync(r => r.Id == first.RunId)).Status);
        }

        var second = await backport.RunAsync(Now.AddMinutes(10), CancellationToken.None);

        Assert.Equal(0, second.Games + second.Misses);
        Assert.Equal(0L, second.RunId);
        Assert.Equal(1, source.GamecastCalls["e-404"]);
        Assert.Equal(1, source.GamecastCalls["e-empty"]);
    }

    [Fact]
    public async Task A_missed_game_is_asked_again_once_its_wait_is_over_or_the_misses_are_forgotten()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);

        var game = await GameAsync(s, "e-late", Now.AddDays(-3));
        source.Games["e-late"] = () => NoPlays("e-late");

        var backport = Backport(s, source, retryAfter: TimeSpan.FromDays(7));
        await backport.RunAsync(Now, CancellationToken.None);

        Assert.Empty(await backport.EligibleAsync(Now.AddDays(6), 10, CancellationToken.None));
        Assert.Single(await backport.EligibleAsync(Now.AddDays(8), 10, CancellationToken.None));

        // ESPN has filled it in since; asked again, it comes in and its miss is cleared.
        source.Games["e-late"] = () => Final("e-late");
        Assert.True(await backport.ForgetMissesAsync(CancellationToken.None) >= 1);

        var retried = await backport.RunAsync(Now, CancellationToken.None);

        Assert.Equal(1, retried.Games);
        Assert.Equal(3, await PlaysAsync(game.Id));

        await using var db = fixture.CreateContext();
        Assert.False(await db.GamecastBackportMisses.AnyAsync(m => m.GameId == game.Id));
    }

    [Fact]
    public async Task A_failed_request_waits_a_day_doubling_rather_than_the_whole_retry()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);
        await GameAsync(s, "e-down", Now.AddDays(-3));
        source.Failing.Add("e-down");

        var backport = Backport(s, source);
        await backport.RunAsync(Now, CancellationToken.None);

        Assert.Empty(await backport.EligibleAsync(Now.AddHours(23), 10, CancellationToken.None));
        Assert.Single(await backport.EligibleAsync(Now.AddHours(25), 10, CancellationToken.None));

        var again = Now.AddHours(25);
        await backport.RunAsync(again, CancellationToken.None);

        Assert.Empty(await backport.EligibleAsync(again.AddHours(47), 10, CancellationToken.None));
        Assert.Single(await backport.EligibleAsync(again.AddHours(49), 10, CancellationToken.None));
    }

    [Fact]
    public async Task A_batch_stops_when_requests_keep_failing_and_leaves_the_rest_untouched()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);

        foreach (var day in Enumerable.Range(3, 6))
        {
            await GameAsync(s, $"e-{day}", Now.AddDays(-day));
            source.Failing.Add($"e-{day}");
        }

        var outcome = await Backport(s, source).RunAsync(Now, CancellationToken.None);

        Assert.Equal(RunStatus.Failed, outcome.Status);
        Assert.Equal(GamecastBackport.FailuresBeforeStopping, source.GamecastCalls.Count);
        Assert.Equal(6 - GamecastBackport.FailuresBeforeStopping, outcome.StillOwed);
    }

    [Fact]
    public async Task It_is_due_while_eligible_games_remain_and_never_while_a_game_is_in_play()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);
        var backport = Backport(s, source);

        Assert.False(await backport.IsDueAsync(Now, CancellationToken.None));

        await GameAsync(s, "e-owed", Now.AddDays(-3));
        source.Games["e-owed"] = () => Final("e-owed");
        Assert.True(await backport.IsDueAsync(Now, CancellationToken.None));

        // A game on in any configured league, gamecast league or not, keeps ESPN for the live passes.
        var live = await GameAsync(s, "e-live", Now.AddHours(-1), GameStatus.Live, sport: s.Other);
        Assert.False(await backport.IsDueAsync(Now, CancellationToken.None));

        var run = await backport.RunAsync(Now, CancellationToken.None);
        Assert.Equal(0, run.Games);
        Assert.False(source.GamecastCalls.ContainsKey("e-owed"));

        await using (var db = fixture.CreateContext())
        {
            var row = await db.Games.SingleAsync(g => g.Id == live.Id);
            row.Status = GameStatus.Final;
            await db.SaveChangesAsync();
        }

        Assert.True(await backport.IsDueAsync(Now, CancellationToken.None));

        await backport.RunAsync(Now, CancellationToken.None);
        Assert.False(await backport.IsDueAsync(Now, CancellationToken.None));
    }

    [Fact]
    public async Task Every_request_in_a_run_goes_through_one_port_so_its_pacing_spans_the_games()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);
        var log = new List<int>();

        foreach (var day in new[] { 3, 4, 5 })
        {
            await GameAsync(s, $"e-{day}", Now.AddDays(-day));
            source.Games[$"e-{day}"] = () => Final($"e-{day}");
        }

        await Backport(s, source, perScope: log).RunAsync(Now, CancellationToken.None);

        Assert.Equal(6, log.Count);
        Assert.Single(log.Distinct());
    }

    [Fact]
    public async Task The_catalogue_lists_it_and_runs_it_a_batch_at_a_time_saying_whether_more_is_owed()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);

        foreach (var day in new[] { 3, 4, 5 })
        {
            await GameAsync(s, $"e-{day}", Now.AddDays(-day));
            source.Games[$"e-{day}"] = () => Final($"e-{day}");
        }

        var options = new IngestionOptions
        {
            Sports = [s.Sport.Key],
            Gamecast = new GamecastOptions { Sports = [s.Sport.Key], BackportBatch = 2 }
        };
        options.Backfill.Seasons[s.Sport.Key] = SeasonStart;

        var services = new ServiceCollection();
        services.AddDbContext<LineOpsDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddLogging();
        services.AddScoped<GamecastWriter>();
        services.AddScoped<SourceRegistry>();
        services.AddSingleton<IGamecastSource>(source);
        services.AddSingleton<IOptions<IngestionOptions>>(Options.Create(options));
        services.AddSingleton<GamecastBackport>();
        services.AddSingleton<IngestionJobs>();
        var jobs = services.BuildServiceProvider().GetRequiredService<IngestionJobs>();

        var entry = (await jobs.DescribeAsync()).Single(j => j.Key == IngestionJobs.EspnGamecastBackport);
        Assert.True(entry.Available);
        Assert.Equal(4, entry.EstimatedRequests);

        var first = await jobs.RunAsync(IngestionJobs.EspnGamecastBackport);
        Assert.True(first.Succeeded);
        Assert.True(first.More);

        var second = await jobs.RunAsync(IngestionJobs.EspnGamecastBackport);
        Assert.True(second.Succeeded);
        Assert.False(second.More);
        Assert.Equal(3, source.GamecastCalls.Count);
    }

    [Fact]
    public async Task A_batch_of_nought_switches_it_off()
    {
        var s = await SeedAsync();
        var source = new FakeGamecasts(s.SourceKey);
        await GameAsync(s, "e-off", Now.AddDays(-3));

        var backport = Backport(s, source, batch: 0);

        Assert.False(await backport.IsDueAsync(Now, CancellationToken.None));
        Assert.Equal(0, (await backport.RunAsync(Now, CancellationToken.None)).Games);
        Assert.Empty(source.GamecastCalls);
    }
}
