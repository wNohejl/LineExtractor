using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Data.Changes;
using LineOps.Ingestion.Configuration;
using LineOps.Ingestion.Services;
using LineOps.Reliability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LineOps.Tests.Reliability;

/// <summary>
/// The live pass is its own job (spec 2026-10-24 §5.2–5.3): it reads the scoreboard for the
/// league-days with a game in play and writes only what is being played or has just finished,
/// and the scheduler ticks at <see cref="EspnScheduleOptions.LivePoll"/> only while that is so.
///
/// <para>
/// Before this the live poll ran the whole slate every ninety seconds — every event on the day,
/// every team's identity, the MLB spine — which is fine for a scoreboard and wasteful for a
/// gamecast that wants the score fifteen seconds after it moves.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class LivePassTests(PostgresFixture fixture)
{
    private sealed class StubSource(string key, Func<StatsFetchResult> scoreboard) : IStatsSource
    {
        public string Key => key;

        public int Calls { get; private set; }

        public Task<StatsFetchResult> FetchScheduleAsync(string s, DateOnly d, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(scoreboard());
        }

        public Task<StatsFetchResult> FetchRosterAsync(string s, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<StatsFetchResult> FetchBoxScoresAsync(string s, DateOnly d, CancellationToken ct)
            => throw new NotSupportedException("The live pass never fetches box scores.");
    }

    private sealed record Scaffold(Sport Sport, string SourceKey, Team Home, Team Away);

    private static async Task<Scaffold> SeedAsync(LineOpsDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sport = new Sport { Key = $"live-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);

        var sourceKey = $"stub-{suffix}";
        db.Sources.Add(new Source { Key = sourceKey, Name = "Stub stats", Kind = SourceKind.Stats, BaseUrl = "local://stub" });
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = $"Home {suffix}", Abbrev = "HOM" };
        var away = new Team { SportId = sport.Id, Name = $"Away {suffix}", Abbrev = "AWY" };
        db.Teams.AddRange(home, away);
        await db.SaveChangesAsync();

        return new Scaffold(sport, sourceKey, home, away);
    }

    private static async Task<Game> GameAsync(
        LineOpsDbContext db, Scaffold s, string eventId, DateTimeOffset startsAt, GameStatus status,
        int? homeScore = null, int? awayScore = null)
    {
        var game = new Game
        {
            SportId = s.Sport.Id,
            HomeTeamId = s.Home.Id,
            AwayTeamId = s.Away.Id,
            StartsAt = startsAt,
            Status = status,
            HomeScore = homeScore,
            AwayScore = awayScore,
            SeasonYear = startsAt.Year,
            ExternalIds = new Dictionary<string, string> { [s.SourceKey] = eventId }
        };

        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game;
    }

    private ServiceProvider Services(Scaffold s, StubSource source)
    {
        var services = new ServiceCollection();
        services.AddDbContext<LineOpsDbContext>(o => o
            .UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(new ChangeNotifier(NullLogger<ChangeNotifier>.Instance)));
        services.AddLogging();
        services.AddScoped<EntityResolver>();
        services.AddScoped(sp => new BudgetCalculator(sp.GetRequiredService<LineOpsDbContext>()));
        services.AddScoped<CreditBudgetGuard>();
        services.AddScoped<StatsIngestionService>();
        services.AddScoped(_ => new SourceRegistry([], [source]));
        return services.BuildServiceProvider();
    }

    private static IOptions<IngestionOptions> OptionsFor(Scaffold s)
        => Options.Create(new IngestionOptions { Sports = [s.Sport.Key] });

    private IngestionJobs Jobs(Scaffold s, StubSource source)
        => new(Services(s, source).GetRequiredService<IServiceScopeFactory>(), OptionsFor(s), NullLogger<IngestionJobs>.Instance);

    [Fact]
    public async Task The_live_pass_resolves_the_game_in_play_and_the_one_just_finished_and_nothing_else()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        // Whole seconds: Postgres keeps microseconds, and a start compared after a round trip must match.
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // Started an hour ago and not yet marked live here — the next poll is what finds out.
        var live = await GameAsync(db, s, "e-live", now.AddHours(-1), GameStatus.Scheduled);
        // Live here, final on ESPN: the last score and the status have to land.
        var justFinal = await GameAsync(db, s, "e-just", now.AddHours(-3), GameStatus.Live, 10, 10);
        // Tonight's game. The slate moves its start; the live pass has no business doing so.
        var later = await GameAsync(db, s, "e-later", now.AddHours(2), GameStatus.Scheduled);
        // Final here for hours. A score correction is the results sweep's to make, not this pass's.
        var longFinal = await GameAsync(db, s, "e-long", now.AddHours(-20), GameStatus.Final, 3, 2);

        var source = new StubSource(s.SourceKey, () => new StatsFetchResult(
            Games:
            [
                new CanonicalGame("e-live", s.Sport.Key, s.Home.Name, s.Away.Name, live.StartsAt, "live", 7, 3),
                new CanonicalGame("e-just", s.Sport.Key, s.Home.Name, s.Away.Name, justFinal.StartsAt, "final", 17, 10),
                new CanonicalGame("e-later", s.Sport.Key, s.Home.Name, s.Away.Name, later.StartsAt.AddMinutes(30), "scheduled"),
                new CanonicalGame("e-long", s.Sport.Key, s.Home.Name, s.Away.Name, longFinal.StartsAt, "final", 4, 2)
            ],
            Players: [], PlayerStats: [], Cost: new FetchCost(1)));

        var outcome = await Jobs(s, source).RunAsync(IngestionJobs.EspnLive);

        Assert.True(outcome.Succeeded, outcome.Detail);
        Assert.True(source.Calls > 0);

        await using var read = fixture.CreateContext();
        var games = await read.Games.Where(g => g.SportId == s.Sport.Id).ToDictionaryAsync(g => g.Id);

        Assert.Equal((GameStatus.Live, 7, 3), (games[live.Id].Status, games[live.Id].HomeScore, games[live.Id].AwayScore));
        Assert.Equal((GameStatus.Final, 17, 10), (games[justFinal.Id].Status, games[justFinal.Id].HomeScore, games[justFinal.Id].AwayScore));
        Assert.Equal(later.StartsAt, games[later.Id].StartsAt);
        Assert.Equal((GameStatus.Final, 3, 2), (games[longFinal.Id].Status, games[longFinal.Id].HomeScore, games[longFinal.Id].AwayScore));
    }

    [Fact]
    public async Task The_live_pass_leaves_team_identity_to_the_slate()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        // Whole seconds: Postgres keeps microseconds, and a start compared after a round trip must match.
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var live = await GameAsync(db, s, "e-live", now.AddMinutes(-40), GameStatus.Live, 0, 0);

        var source = new StubSource(s.SourceKey, () => new StatsFetchResult(
            Games:
            [
                new CanonicalGame("e-live", s.Sport.Key, s.Home.Name, s.Away.Name, live.StartsAt, "live", 3, 0,
                    Home: new CanonicalTeamRef(s.Home.Name, "h1", "NEW"),
                    Away: new CanonicalTeamRef(s.Away.Name, "a1", "OLD"))
            ],
            Players: [], PlayerStats: [], Cost: new FetchCost(1)));

        await Jobs(s, source).RunAsync(IngestionJobs.EspnLive);

        await using var read = fixture.CreateContext();
        var home = await read.Teams.SingleAsync(t => t.Id == s.Home.Id);
        var game = await read.Games.SingleAsync(g => g.Id == live.Id);

        Assert.Equal(3, game.HomeScore);
        Assert.Equal("HOM", home.Abbrev);
        Assert.False(home.ExternalIds.ContainsKey(s.SourceKey));
    }

    [Fact]
    public async Task Nothing_in_play_means_no_request()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        await GameAsync(db, s, "e-later", DateTimeOffset.UtcNow.AddHours(2), GameStatus.Scheduled);

        var source = new StubSource(s.SourceKey, () => throw new InvalidOperationException("Should not be asked."));

        var outcome = await Jobs(s, source).RunAsync(IngestionJobs.EspnLive);

        Assert.True(outcome.Succeeded, outcome.Detail);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task The_catalogue_lists_the_live_pass()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        var source = new StubSource(s.SourceKey, () => throw new NotSupportedException());

        var jobs = await Jobs(s, source).DescribeAsync();

        var entry = Assert.Single(jobs, j => j.Key == IngestionJobs.EspnLive);
        Assert.True(entry.Available);
    }

    /// <summary>
    /// A live poll runs every fifteen seconds all game, and most see the same board as the one
    /// before. A poll that changed nothing leaves no trace — no run row, no notice — or every open
    /// Runs and Ops window reloads four times a minute for nothing. One that moved something is
    /// recorded exactly as before.
    /// </summary>
    [Fact]
    public async Task A_live_poll_that_changed_nothing_leaves_no_run_and_no_notice()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var live = await GameAsync(db, s, "e-live", now.AddMinutes(-40), GameStatus.Live, 0, 0);
        var sourceId = await db.Sources.Where(x => x.Key == s.SourceKey).Select(x => x.Id).SingleAsync();

        var clock = "5:00";
        var source = new StubSource(s.SourceKey, () => new StatsFetchResult(
            Games:
            [
                new CanonicalGame("e-live", s.Sport.Key, s.Home.Name, s.Away.Name, live.StartsAt, "live", 7, 3,
                    Live: new CanonicalLive(2, clock, null, $"{clock} - 2nd", [7], [3]))
            ],
            Players: [], PlayerStats: [], Cost: new FetchCost(1)));

        var jobs = Jobs(s, source);

        async Task<int> RunsAsync()
        {
            await using var read = fixture.CreateContext();
            return await read.IngestionRuns.CountAsync(r => r.SourceId == sourceId && r.JobKey == IngestionJobs.EspnLive);
        }

        await jobs.RunAsync(IngestionJobs.EspnLive);
        Assert.Equal(1, await RunsAsync());

        var (connection, heard) = await ListenAsync();
        await using (connection)
        {
            var outcome = await jobs.RunAsync(IngestionJobs.EspnLive);
            Assert.True(outcome.Succeeded, outcome.Detail);
            Assert.Equal(2, source.Calls);

            var tokens = (await DrainAsync(connection, heard)).SelectMany(p => p.Split(',')).ToList();

            Assert.Equal(1, await RunsAsync());
            Assert.DoesNotContain(DataTopics.Runs, tokens);
            Assert.DoesNotContain(DataTopics.Address(DataTopics.Games, live.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)), tokens);
        }

        clock = "4:30";
        await jobs.RunAsync(IngestionJobs.EspnLive);
        Assert.Equal(2, await RunsAsync());

        await using var after = fixture.CreateContext();
        Assert.Equal("4:30", (await after.Games.SingleAsync(g => g.Id == live.Id)).DisplayClock);
    }

    /// <summary>A live poll that fails is recorded whatever it changed: a failure is what Ops is for.</summary>
    [Fact]
    public async Task A_live_poll_that_fails_is_still_recorded()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        await GameAsync(db, s, "e-live", DateTimeOffset.UtcNow.AddMinutes(-40), GameStatus.Live, 0, 0);
        var sourceId = await db.Sources.Where(x => x.Key == s.SourceKey).Select(x => x.Id).SingleAsync();

        var source = new StubSource(s.SourceKey, () => throw new HttpRequestException("scoreboard down"));

        var outcome = await Jobs(s, source).RunAsync(IngestionJobs.EspnLive);

        Assert.False(outcome.Succeeded);

        await using var read = fixture.CreateContext();
        var run = await read.IngestionRuns.SingleAsync(r => r.SourceId == sourceId && r.JobKey == IngestionJobs.EspnLive);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("scoreboard down", run.Error);
    }

    /// <summary>
    /// A zero or negative interval in configuration would make <c>Task.Delay</c> throw on every
    /// tick, and the loop would spin on error logs. Either reads as its default.
    /// </summary>
    [Fact]
    public async Task A_non_positive_tick_or_live_poll_reads_as_its_default()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        var source = new StubSource(s.SourceKey, () => throw new NotSupportedException());
        var services = Services(s, source);

        var options = Options.Create(new IngestionOptions
        {
            Sports = [s.Sport.Key],
            TickInterval = TimeSpan.Zero,
            GamePasses = new EspnScheduleOptions { LivePoll = TimeSpan.FromSeconds(-5) }
        });

        var scheduler = new IngestionScheduler(
            services.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<IngestionScheduler>.Instance);

        var game = await GameAsync(db, s, "e-later", DateTimeOffset.UtcNow.AddHours(2), GameStatus.Scheduled);
        Assert.Equal(new IngestionOptions().TickInterval, await scheduler.NextDelayAsync(DateTimeOffset.UtcNow, CancellationToken.None));

        game.StartsAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();
        Assert.Equal(new EspnScheduleOptions().LivePoll, await scheduler.NextDelayAsync(DateTimeOffset.UtcNow, CancellationToken.None));
    }

    private async Task<(NpgsqlConnection Connection, List<string> Heard)> ListenAsync()
    {
        var heard = new List<string>();
        var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        connection.Notification += (_, e) => { lock (heard) heard.Add(e.Payload); };

        await using var listen = new NpgsqlCommand($"LISTEN {DataTopics.Channel}", connection);
        await listen.ExecuteNonQueryAsync();

        return (connection, heard);
    }

    private static async Task<List<string>> DrainAsync(NpgsqlConnection connection, List<string> heard)
    {
        // Notices arrive asynchronously; give the server a moment, then read what came.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            while (true)
                await connection.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        lock (heard)
            return [.. heard];
    }

    [Fact]
    public void The_live_poll_is_fifteen_seconds()
        => Assert.Equal(TimeSpan.FromSeconds(15), new EspnScheduleOptions().LivePoll);

    [Fact]
    public async Task The_scheduler_ticks_at_the_live_poll_while_a_game_is_in_play_and_at_the_tick_interval_otherwise()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        var source = new StubSource(s.SourceKey, () => throw new NotSupportedException());
        var services = Services(s, source);
        var options = OptionsFor(s);

        var scheduler = new IngestionScheduler(
            services.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<IngestionScheduler>.Instance);

        var game = await GameAsync(db, s, "e-later", DateTimeOffset.UtcNow.AddHours(2), GameStatus.Scheduled);
        Assert.Equal(options.Value.TickInterval, await scheduler.NextDelayAsync(DateTimeOffset.UtcNow, CancellationToken.None));

        game.StartsAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();
        Assert.Equal(TimeSpan.FromSeconds(15), await scheduler.NextDelayAsync(DateTimeOffset.UtcNow, CancellationToken.None));

        game.Status = GameStatus.Final;
        await db.SaveChangesAsync();
        Assert.Equal(options.Value.TickInterval, await scheduler.NextDelayAsync(DateTimeOffset.UtcNow, CancellationToken.None));
    }
}
