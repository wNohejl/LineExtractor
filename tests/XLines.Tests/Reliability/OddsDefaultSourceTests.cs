using System.Net;
using System.Reflection;
using XLines.Core.Contracts;
using XLines.Core.Entities;
using XLines.Data;
using XLines.Ingestion;
using XLines.Ingestion.Adapters;
using XLines.Ingestion.Configuration;
using XLines.Ingestion.Services;
using XLines.Reliability;
using XLines.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace XLines.Tests.Reliability;

/// <summary>
/// The Odds API as the default stats source (ADR 0026), through the real container, the real jobs
/// and real Postgres — and, first, proof that a desk running on ESPN (the owner's: ESPN on, a key in
/// configuration, scanning Manual) is untouched by it.
///
/// <para>
/// Each test has a database of its own on the shared container, initialised as a host initialises
/// one (migrations, then the seed), because these tests use real team names and assert counts that
/// another test's rows would disturb. The provider is a recording handler serving the fixtures built
/// from the published v4 guide; no request leaves the machine, and the wire's log is how "nothing was
/// spent" is proven.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class OddsDefaultSourceTests(PostgresFixture fixture)
{
    // ---- The harness -------------------------------------------------------------------------

    /// <summary>Every provider at once: answers by path and logs every request.</summary>
    private sealed class Wire : HttpMessageHandler
    {
        public List<Uri> Asked { get; } = [];
        public Func<Uri, string> Body { get; set; } = _ => "[]";
        public int? Remaining { get; set; }

        public IEnumerable<Uri> OddsApi => Asked.Where(u => u.Host == "api.the-odds-api.com");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            lock (Asked)
                Asked.Add(uri);

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body(uri)) };
            if (uri.Host == "api.the-odds-api.com")
            {
                var billed = uri.AbsolutePath.Contains("/scores") ? 2 : uri.AbsolutePath.Contains("/odds") ? 3 : 0;
                response.Headers.Add("x-requests-last", billed.ToString());
                if (Remaining is { } left)
                    response.Headers.Add("x-requests-remaining", left.ToString());
            }

            return Task.FromResult(response);
        }
    }

    private static Func<Uri, string> Recorded => uri => uri.AbsolutePath.Contains("/scores") ? Fixtures.Text("the-odds-api.scores.json")
        : uri.AbsolutePath.Contains("/events") && uri.AbsolutePath.Contains("baseball_mlb") ? Fixtures.Text("the-odds-api.events.json")
        : "[]";

    /// <summary>A database of this test's own, migrated and seeded as a host's startup does it.</summary>
    private async Task<string> FreshDatabaseAsync()
    {
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = "oddsdefault_" + Guid.NewGuid().ToString("N")[..12]
        }.ConnectionString;

        await using var db = Context(connection);
        await new DatabaseInitializer(db, NullLogger<DatabaseInitializer>.Instance).InitialiseAsync();
        return connection;
    }

    private static XLinesDbContext Context(string connection)
        => new(new DbContextOptionsBuilder<XLinesDbContext>().UseNpgsql(connection).Options);

    /// <summary>
    /// A host as Program.cs builds one: the ingestion registration from configuration, the runtime key
    /// a desk takes, and every provider's client pointed at the wire.
    /// </summary>
    private static ServiceProvider Host(string connection, Wire wire, bool espn, string? configKey = "test-key", bool desk = true)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Ingestion:Sports:0"] = "mlb",
            ["Ingestion:Sports:1"] = "nba",
            ["Ingestion:Sports:2"] = "nfl",
            ["Ingestion:Sports:3"] = "ufc",
            ["Ingestion:Espn:Enabled"] = espn ? "true" : "false",
            ["Ingestion:MlbStatsApi:Enabled"] = "false",
            ["Ingestion:Gamecast:Enabled"] = "false",
            ["Ingestion:TheOddsApi:Enabled"] = configKey is null ? "false" : "true",
            ["Ingestion:TheOddsApi:ApiKey"] = configKey ?? ""
        };

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<XLinesDbContext>(o => o.UseNpgsql(connection));
        services.AddScoped<BudgetCalculator>();
        services.AddScoped(sp => new DatabaseInitializer(sp.GetRequiredService<XLinesDbContext>(), NullLogger<DatabaseInitializer>.Instance));
        services.AddXLinesIngestion(configuration);
        if (desk)
            services.AddRuntimeOddsKey();

        // After registration, so the checks it makes see only what the host registered.
        var registeredSchedule = services.Any(d => d.ServiceType == typeof(TheOddsApiScheduleAdapter));
        services.AddHttpClient<TheOddsApiAdapter>().ConfigurePrimaryHttpMessageHandler(() => wire);
        if (registeredSchedule)
            services.AddHttpClient<TheOddsApiScheduleAdapter>().ConfigurePrimaryHttpMessageHandler(() => wire);
        if (espn)
            services.AddHttpClient<EspnStatsAdapter>().ConfigurePrimaryHttpMessageHandler(() => wire);

        return services.BuildServiceProvider();
    }

    private static IngestionJobs Jobs(IServiceProvider host) => host.GetRequiredService<IngestionJobs>();

    private static SourceRegistry Registry(IServiceProvider host)
    {
        var scope = host.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SourceRegistry>();
    }

    private static async Task<Sport> SportAsync(XLinesDbContext db, string key)
        => await db.Sports.SingleAsync(s => s.Key == key);

    private static async Task<int> SourceIdAsync(XLinesDbContext db, string key)
        => await db.Sources.Where(s => s.Key == key).Select(s => s.Id).SingleAsync();

    /// <summary>A month's spend recorded against The Odds API's prices row, as a run of this month would.</summary>
    private static async Task SpendAsync(string connection, int credits)
    {
        await using var db = Context(connection);
        db.IngestionRuns.Add(new IngestionRun
        {
            SourceId = await SourceIdAsync(db, SourceAccounts.TheOddsApi),
            JobKey = "odds:lines:mlb",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            FinishedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            Status = RunStatus.Success,
            RequestsMade = 1,
            CreditsSpent = credits
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// An ESPN-written desk in miniature: four MLB clubs under ESPN's names, ids and abbreviations; a
    /// final with its close; tonight's game; a settled entry and an open one.
    /// </summary>
    private static async Task<(Game Final, Game Tonight)> SeedEspnDeskAsync(string connection)
    {
        await using var db = Context(connection);
        var mlb = await SportAsync(db, "mlb");
        var espn = await SourceIdAsync(db, "espn");

        Team Club(string name, string abbrev, string id) => new()
        {
            SportId = mlb.Id,
            Name = name,
            Abbrev = abbrev,
            ExternalIds = new Dictionary<string, string> { ["espn"] = id }
        };

        var yankees = Club("New York Yankees", "NYY", "10");
        var redSox = Club("Boston Red Sox", "BOS", "2");
        var athletics = Club("Athletics", "ATH", "11");
        var angels = Club("Los Angeles Angels", "LAA", "3");
        db.Teams.AddRange(yankees, redSox, athletics, angels);
        await db.SaveChangesAsync();

        var final = new Game
        {
            SportId = mlb.Id,
            HomeTeamId = yankees.Id,
            AwayTeamId = redSox.Id,
            StartsAt = new DateTimeOffset(2026, 9, 20, 17, 5, 0, TimeSpan.Zero),
            SeasonYear = 2026,
            SeasonType = SeasonType.Regular,
            Status = GameStatus.Final,
            HomeScore = 4,
            AwayScore = 1,
            ExternalIds = new Dictionary<string, string> { ["espn"] = "401" }
        };

        var tonight = new Game
        {
            SportId = mlb.Id,
            HomeTeamId = athletics.Id,
            AwayTeamId = angels.Id,
            StartsAt = DateTimeOffset.UtcNow.AddHours(5),
            SeasonYear = 2026,
            SeasonType = SeasonType.Regular,
            Status = GameStatus.Scheduled,
            ExternalIds = new Dictionary<string, string> { ["espn"] = "402" }
        };

        db.Games.AddRange(final, tonight);
        await db.SaveChangesAsync();

        db.ClosingLines.AddRange(
            new ClosingLine { GameId = final.Id, SourceId = espn, Book = "espn", Market = Markets.Moneyline, Outcome = "New York Yankees", PriceAmerican = -140, CapturedAt = final.StartsAt, PromotedAt = final.StartsAt },
            new ClosingLine { GameId = final.Id, SourceId = espn, Book = "espn", Market = Markets.Moneyline, Outcome = "Boston Red Sox", PriceAmerican = 120, CapturedAt = final.StartsAt, PromotedAt = final.StartsAt });

        db.JournalEntries.AddRange(
            new JournalEntry { GameId = final.Id, Market = Markets.Moneyline, Outcome = "New York Yankees", Book = "draftkings", PriceTaken = -125, Stake = 100m, PlacedAt = final.StartsAt.AddHours(-6) },
            new JournalEntry { GameId = tonight.Id, Market = Markets.Moneyline, Outcome = "Athletics", Book = "fanduel", PriceTaken = 110, Stake = 50m, PlacedAt = DateTimeOffset.UtcNow.AddHours(-1) });

        await db.SetSettingAsync(AppSetting.LinePolling, AppSetting.LinePollingManual);
        await db.SaveChangesAsync();

        // Settled once, as the owner's journal is: the graded entry and its CLV are on record.
        await new SettlementService(db, NullLogger<SettlementService>.Instance).SettleAsync();
        return (final, tonight);
    }

    /// <summary>Every row of the tables the owner's desk keeps, as Postgres itself prints them: byte for byte.</summary>
    private static async Task<Dictionary<string, List<string>>> SnapshotAsync(string connection)
    {
        string[] tables = ["Games", "Teams", "Players", "ClosingLines", "OddsSnapshots", "JournalEntries", "Parlays", "JournalTombstones", "AppSettings", "Sources", "Bouts"];

        await using var db = Context(connection);
        var snapshot = new Dictionary<string, List<string>>();

        foreach (var table in tables)
        {
            snapshot[table] = await db.Database
                // The table names are this test's own constants, not input.
                .SqlQueryRaw<string>("SELECT row_to_json(t)::text AS \"Value\" FROM \"" + table + "\" t ORDER BY 1")
                .ToListAsync();
        }

        return snapshot;
    }

    private static void AssertUnchanged(Dictionary<string, List<string>> before, Dictionary<string, List<string>> after)
    {
        foreach (var (table, rows) in before)
            Assert.True(rows.SequenceEqual(after[table]), $"{table} changed:\n- {string.Join("\n- ", rows.Except(after[table]))}\n+ {string.Join("\n+ ", after[table].Except(rows))}");
    }

    private static Task TickAsync(IServiceProvider host)
    {
        var scheduler = new IngestionScheduler(host.GetRequiredService<IServiceScopeFactory>(),
            host.GetRequiredService<Microsoft.Extensions.Options.IOptions<IngestionOptions>>(), NullLogger<IngestionScheduler>.Instance);

        return (Task)typeof(IngestionScheduler).GetMethod("TickAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(scheduler, [CancellationToken.None])!;
    }

    // ---- The owner's desk: ESPN on, a key in configuration, Manual ----------------------------

    /// <summary>The job list a desk on ESPN offered before ADR 0026, in its order: unchanged.</summary>
    private static readonly string[] EspnDeskJobs =
    [
        IngestionJobs.EspnSlate, IngestionJobs.EspnLive, IngestionJobs.EspnResults, IngestionJobs.EspnClosingLines,
        IngestionJobs.EspnGamecastBackport, IngestionJobs.EspnFighters,
        "odds:lines:mlb", "odds:lines:nba", "odds:lines:nfl", "odds:lines:ufc",
        IngestionJobs.Settle
    ];

    [Fact]
    public async Task On_a_desk_running_on_espn_the_default_source_is_not_registered_and_the_jobs_are_as_they_were()
    {
        var connection = await FreshDatabaseAsync();
        var wire = new Wire();
        await using var host = Host(connection, wire, espn: true);

        var registry = Registry(host);
        Assert.Equal(StatsPort.Espn, registry.Port);
        Assert.Equal(["espn"], registry.StatsSources.Select(s => s.Key));
        Assert.Null(registry.Scores);
        Assert.Equal([TheOddsApiAdapter.SourceKey], registry.OddsSources.Select(s => s.Key));
        Assert.Empty(host.CreateScope().ServiceProvider.GetServices<IScoreSource>());

        var jobs = await Jobs(host).DescribeAsync();
        Assert.Equal(EspnDeskJobs, jobs.Select(j => j.Key));
        Assert.All(jobs.Where(j => j.Key.StartsWith("odds:lines:")), j => Assert.True(j.Available, j.Unavailable));
        Assert.Empty(wire.Asked);
    }

    [Fact]
    public async Task On_a_desk_running_on_espn_a_key_typed_at_runtime_registers_no_default_source_either()
    {
        var connection = await FreshDatabaseAsync();
        await using var host = Host(connection, new Wire(), espn: true, configKey: null);

        host.GetRequiredService<RuntimeOddsKey>().TheOddsApi = "typed-in-the-desk";

        var registry = Registry(host);
        Assert.Equal(StatsPort.Espn, registry.Port);
        Assert.Equal(["espn"], registry.StatsSources.Select(s => s.Key));
        Assert.Null(registry.Scores);
    }

    /// <summary>
    /// The owner's constraint, as a test: every new job run against an ESPN desk holding a journal
    /// changes no row of the spine, the closes, the journal or the settings, spends nothing, and
    /// the journal's settlement and CLV come out exactly as they were.
    /// </summary>
    [Fact]
    public async Task On_a_desk_running_on_espn_every_new_job_leaves_the_spine_and_the_journal_byte_for_byte_as_they_were()
    {
        var connection = await FreshDatabaseAsync();
        await SeedEspnDeskAsync(connection);
        var wire = new Wire { Body = Recorded };
        await using var host = Host(connection, wire, espn: true);

        var before = await SnapshotAsync(connection);

        var jobs = Jobs(host);
        await jobs.DescribeAsync();
        var fixtures = await jobs.RunAsync(IngestionJobs.OddsFixtures);
        var automatic = await jobs.RunAsync(IngestionJobs.OddsScores);
        var mlb = await jobs.RunAsync(IngestionJobs.OddsScoresPrefix + "mlb");
        await jobs.RunAsync(IngestionJobs.Settle);

        Assert.All(new[] { fixtures, automatic, mlb }, o => Assert.Contains("ESPN is the stats source on this host", o.Detail));

        AssertUnchanged(before, await SnapshotAsync(connection));
        Assert.Empty(wire.OddsApi);

        await using var db = Context(connection);
        Assert.Empty(await db.IngestionRuns.Where(r => r.JobKey.StartsWith("odds:")).ToListAsync());
    }

    [Fact]
    public async Task On_a_desk_running_on_espn_in_manual_the_scheduler_spends_nothing()
    {
        var connection = await FreshDatabaseAsync();
        await SeedEspnDeskAsync(connection);
        var wire = new Wire();
        await using var host = Host(connection, wire, espn: true);

        await TickAsync(host);

        // ESPN's own free passes may run, against the wire; nothing reaches The Odds API.
        Assert.Empty(wire.OddsApi);
    }

    // ---- The default: ESPN off, a key -------------------------------------------------------

    /// <summary>
    /// Every pull under "Costs credits" is marked metered; with no key each is dimmed, waits on the
    /// key, and says where it goes — in place of "No odds source registered.", which a live desk
    /// showed beside a price of "free".
    /// </summary>
    private static void AssertMeteredPullsNeedAKey(IReadOnlyList<IngestionJob> jobs)
    {
        var metered = jobs.Where(j => j.Group == JobGroups.Lines).ToList();
        Assert.NotEmpty(metered);

        Assert.All(metered, j =>
        {
            Assert.True(j.Metered, j.Key);
            Assert.True(j.NeedsKey, j.Key);
            Assert.False(j.Available, j.Key);
            Assert.Equal(IngestionJobs.NoOddsKey, j.Unavailable);
            Assert.Contains("Ops → Odds feeds", j.Hint);
        });
    }

    /// <summary>The live desk the defect was found on: ESPN on, no Odds API key.</summary>
    [Fact]
    public async Task On_a_desk_running_on_espn_without_a_key_the_lines_pulls_ask_for_the_key()
    {
        var connection = await FreshDatabaseAsync();
        await using var host = Host(connection, new Wire(), espn: true, configKey: null);

        var jobs = await Jobs(host).DescribeAsync();

        Assert.Equal(EspnDeskJobs, jobs.Select(j => j.Key));
        AssertMeteredPullsNeedAKey(jobs);
    }

    [Fact]
    public async Task Without_a_key_there_is_no_stats_source_and_the_desk_says_where_to_enter_one()
    {
        var connection = await FreshDatabaseAsync();
        var wire = new Wire();
        await using var host = Host(connection, wire, espn: false, configKey: null);

        Assert.Equal(StatsPort.None, Registry(host).Port);

        var jobs = await Jobs(host).DescribeAsync();
        var fixtures = Assert.Single(jobs, j => j.Key == IngestionJobs.OddsFixtures);
        Assert.False(fixtures.Available);
        Assert.Equal(IngestionJobs.NoOddsKey, fixtures.Unavailable);
        Assert.DoesNotContain(jobs, j => EspnOptIn.IsEspnJob(j.Key));
        AssertMeteredPullsNeedAKey(jobs);

        var outcome = await Jobs(host).RunAsync(IngestionJobs.OddsFixtures);
        Assert.False(outcome.Succeeded);
        Assert.Contains("No Odds API key is held", outcome.Detail);
        Assert.Empty(wire.Asked);
    }

    [Fact]
    public async Task A_key_typed_at_runtime_makes_the_odds_api_the_stats_source_and_removing_it_ends_that()
    {
        var connection = await FreshDatabaseAsync();
        await using var host = Host(connection, new Wire(), espn: false, configKey: null);
        var key = host.GetRequiredService<RuntimeOddsKey>();

        Assert.Equal(StatsPort.None, Registry(host).Port);

        key.TheOddsApi = "typed-in-the-desk";
        var registry = Registry(host);
        Assert.Equal(StatsPort.TheOddsApi, registry.Port);
        Assert.Equal([TheOddsApiScheduleAdapter.SourceKey], registry.StatsSources.Select(s => s.Key));
        Assert.NotNull(registry.Scores);

        key.TheOddsApi = null;
        Assert.Equal(StatsPort.None, Registry(host).Port);
    }

    [Fact]
    public async Task A_configured_key_with_espn_off_makes_the_odds_api_the_stats_source_on_a_host_with_no_desk()
    {
        var connection = await FreshDatabaseAsync();
        await using var host = Host(connection, new Wire(), espn: false, desk: false);

        var registry = Registry(host);
        Assert.Equal(StatsPort.TheOddsApi, registry.Port);
        Assert.Equal([TheOddsApiScheduleAdapter.SourceKey], registry.StatsSources.Select(s => s.Key));
    }

    /// <summary>
    /// The default path end to end: fixtures (free) then scores (two credits) yield final games with
    /// scores and a season, on the ESPN-style clubs already held rather than beside them, a
    /// doubleheader as two games, and journal entries on them settled.
    /// </summary>
    [Fact]
    public async Task Fixtures_then_scores_make_finals_on_the_clubs_already_held_and_settle_the_journal()
    {
        var connection = await FreshDatabaseAsync();
        var (espnFinal, _) = await SeedEspnDeskAsync(connection);
        var wire = new Wire { Body = Recorded, Remaining = 420 };
        await using var host = Host(connection, wire, espn: false);

        Dictionary<int, string> heldTeams;
        await using (var db = Context(connection))
            heldTeams = await db.Teams.ToDictionaryAsync(t => t.Id, t => $"{t.Name}|{t.Abbrev}");
        var heldFinal = await SnapshotAsync(connection);

        var fixtures = await Jobs(host).RunAsync(IngestionJobs.OddsFixtures);
        Assert.True(fixtures.Succeeded, fixtures.Detail);
        Assert.Contains("No credits spent", fixtures.Detail);
        Assert.All(wire.OddsApi, u => Assert.Contains("/events", u.AbsolutePath));

        int dh1, dh2;
        await using (var db = Context(connection))
        {
            var mlb = await SportAsync(db, "mlb");
            var games = await db.Games.Include(g => g.HomeTeam).Include(g => g.AwayTeam)
                .Where(g => g.SportId == mlb.Id)
                .ToListAsync();
            var fromEvents = games.Where(g => g.ExternalIds.ContainsKey(TheOddsApiScheduleAdapter.SourceKey)).ToList();

            Assert.Equal(4, fromEvents.Count);
            Assert.All(fromEvents, g => Assert.Equal((2026, SeasonType.Regular), (g.SeasonYear, g.SeasonType)));

            // The doubleheader: two games, same sides, same day.
            var pair = fromEvents.Where(g => g.HomeTeam!.Name == "New York Yankees" && g.AwayTeam!.Name == "Boston Red Sox").OrderBy(g => g.StartsAt).ToList();
            Assert.Equal(2, pair.Count);
            (dh1, dh2) = (pair[0].Id, pair[1].Id);

            // No club twice: "Oakland Athletics" landed on ESPN's "Athletics"; the rest by name.
            var names = await db.Teams.Where(t => t.SportId == mlb.Id).Select(t => t.Name).ToListAsync();
            Assert.Equal(names.Count, names.Distinct().Count());
            Assert.DoesNotContain("Oakland Athletics", names);
            Assert.Equal("SEA", (await db.Teams.SingleAsync(t => t.Name == "Seattle Mariners")).Abbrev);

            // A held club is not renamed, re-abbreviated or re-pointed: it gains this source's name for it, nothing else.
            foreach (var team in await db.Teams.Where(t => heldTeams.Keys.Contains(t.Id)).ToListAsync())
                Assert.Equal(heldTeams[team.Id], $"{team.Name}|{team.Abbrev}");

            db.JournalEntries.AddRange(
                new JournalEntry { GameId = dh1, Market = Markets.Moneyline, Outcome = "New York Yankees", Book = "draftkings", PriceTaken = -150, Stake = 30m, PlacedAt = pair[0].StartsAt.AddHours(-3) },
                new JournalEntry { GameId = dh2, Market = Markets.Moneyline, Outcome = "Boston Red Sox", Book = "fanduel", PriceTaken = 130, Stake = 10m, PlacedAt = pair[1].StartsAt.AddHours(-3) });
            await db.SaveChangesAsync();
        }

        var scores = await Jobs(host).RunAsync(IngestionJobs.OddsScoresPrefix + "mlb");
        Assert.True(scores.Succeeded, scores.Detail);
        Assert.Contains("2 credits spent", scores.Detail);
        Assert.Single(wire.OddsApi, u => u.AbsolutePath.Contains("/scores") && u.Query.Contains("daysFrom=3"));

        await using (var db = Context(connection))
        {
            var first = await db.Games.SingleAsync(g => g.Id == dh1);
            var second = await db.Games.SingleAsync(g => g.Id == dh2);
            Assert.Equal((GameStatus.Final, 5, 3), (first.Status, first.HomeScore, first.AwayScore));
            Assert.Equal((GameStatus.Final, 2, 7), (second.Status, second.HomeScore, second.AwayScore));

            // The game in play and the one not started are not finals.
            var seattle = await db.Games.SingleAsync(g => g.HomeTeam!.Name == "Seattle Mariners");
            Assert.NotEqual(GameStatus.Final, seattle.Status);
            Assert.Null(seattle.HomeScore);

            var entries = await db.JournalEntries.Where(e => e.GameId == dh1 || e.GameId == dh2).OrderBy(e => e.GameId).ToListAsync();
            Assert.Equal([EntryResult.Win, EntryResult.Win], entries.Select(e => e.Result));
            Assert.Equal(50m, entries[0].Payout!.Value, precision: 2);
            Assert.Equal(23m, entries[1].Payout!.Value, precision: 2);

            // The credits are on the account's month, with the provider's remainder beside them.
            var runs = await db.IngestionRuns.Where(r => r.Source!.Key == TheOddsApiScheduleAdapter.SourceKey).ToListAsync();
            Assert.Equal(2, runs.Sum(r => r.CreditsSpent));
            Assert.Contains(runs, r => r.ProviderCreditsRemaining == 420);
            var odds = await db.Sources.SingleAsync(s => s.Key == SourceAccounts.TheOddsApi);
            Assert.Equal(80, await MonthlyCredits.SpentAsync(db, odds, DateTimeOffset.UtcNow));
        }

        // The ESPN final, its close and the journal entry graded on it are exactly as they were.
        var after = await SnapshotAsync(connection);
        Assert.Equal(heldFinal["ClosingLines"], after["ClosingLines"]);
        Assert.Contains(after["Games"], r => r.Contains($"\"Id\":{espnFinal.Id},") && r.Contains("\"HomeScore\":4") && r.Contains("\"AwayScore\":1"));
        Assert.Equal(heldFinal["JournalEntries"].Where(r => r.Contains($"\"GameId\":{espnFinal.Id},")), after["JournalEntries"].Where(r => r.Contains($"\"GameId\":{espnFinal.Id},")));
    }

    // ---- Credits: never refused after the press, never spent unasked -------------------------

    [Fact]
    public async Task A_spent_month_shows_scores_and_lines_unavailable_with_the_reason_and_spends_nothing()
    {
        var connection = await FreshDatabaseAsync();
        await SpendAsync(connection, 500);
        var wire = new Wire { Body = Recorded };
        await using var host = Host(connection, wire, espn: false);

        var jobs = await Jobs(host).DescribeAsync();
        var scores = Assert.Single(jobs, j => j.Key == IngestionJobs.OddsScoresPrefix + "mlb");
        var lines = Assert.Single(jobs, j => j.Key == IngestionJobs.OddsLinesPrefix + "mlb");

        Assert.False(scores.Available);
        Assert.Contains("500 credits are spent", scores.Unavailable);
        Assert.Contains("reset on", scores.Unavailable);
        Assert.False(lines.Available);

        var outcome = await Jobs(host).RunAsync(IngestionJobs.OddsScoresPrefix + "mlb");
        Assert.Contains("cannot cover it", outcome.Detail);
        Assert.Empty(wire.OddsApi);
    }

    [Fact]
    public async Task A_pull_that_would_dip_into_the_reserve_asks_first_naming_its_cost_and_the_remainder()
    {
        var connection = await FreshDatabaseAsync();
        await SpendAsync(connection, 399);
        await using var host = Host(connection, new Wire(), espn: false);

        var jobs = await Jobs(host).DescribeAsync();
        var scores = Assert.Single(jobs, j => j.Key == IngestionJobs.OddsScoresPrefix + "mlb");

        Assert.True(scores.Available);
        Assert.Equal("This spends 2 credits and leaves 99 of 500, under the 100 held in reserve until "
                     + new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(1).ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture)
                     + ". Spend them?", scores.Confirm);

        // Fixtures are free whatever is left: no question, no price.
        var fixtures = Assert.Single(jobs, j => j.Key == IngestionJobs.OddsFixtures);
        Assert.Null(fixtures.Confirm);
        Assert.Equal(0, fixtures.EstimatedCredits);
    }

    [Fact]
    public async Task A_small_pull_with_plenty_left_runs_on_the_press_and_states_its_price()
    {
        var connection = await FreshDatabaseAsync();
        await using var host = Host(connection, new Wire(), espn: false);

        var jobs = await Jobs(host).DescribeAsync();
        var scores = Assert.Single(jobs, j => j.Key == IngestionJobs.OddsScoresPrefix + "nfl");
        var ufc = Assert.Single(jobs, j => j.Key == IngestionJobs.OddsScoresPrefix + "ufc");
        var ufcLines = Assert.Single(jobs, j => j.Key == IngestionJobs.OddsLinesPrefix + "ufc");

        Assert.True(scores.Available);
        Assert.Null(scores.Confirm);
        Assert.Equal(2, scores.EstimatedCredits);
        Assert.Equal(JobGroups.Lines, scores.Group);
        Assert.Contains("resets on", scores.Hint);

        // UFC: no scores from this provider, and no bout to price without ESPN — both said, neither offered.
        Assert.False(ufc.Available);
        Assert.Contains("need ESPN", ufc.Unavailable);
        Assert.False(ufcLines.Available);
        Assert.Contains("ESPN is off", ufcLines.Unavailable);
    }

    [Fact]
    public async Task Scores_are_never_pulled_unattended_while_scanning_is_on_request()
    {
        var connection = await FreshDatabaseAsync();
        await using (var db = Context(connection))
        {
            var mlb = await SportAsync(db, "mlb");
            var home = new Team { SportId = mlb.Id, Name = "Texas Rangers", Abbrev = "TEX" };
            var away = new Team { SportId = mlb.Id, Name = "Toronto Blue Jays", Abbrev = "TOR" };
            db.Teams.AddRange(home, away);
            await db.SaveChangesAsync();
            db.Games.Add(new Game { SportId = mlb.Id, HomeTeamId = home.Id, AwayTeamId = away.Id, StartsAt = DateTimeOffset.UtcNow.AddHours(-6), SeasonYear = 2026 });
            await db.SaveChangesAsync();
        }

        var wire = new Wire();
        await using var host = Host(connection, wire, espn: false);

        var manual = await Jobs(host).RunAsync(IngestionJobs.OddsScores);
        Assert.Contains("on request only", manual.Detail);
        Assert.Empty(wire.OddsApi);

        // The scheduler's tick in Manual: the free fixtures, and nothing metered.
        await TickAsync(host);
        Assert.NotEmpty(wire.OddsApi);
        Assert.All(wire.OddsApi, u => Assert.Contains("/events", u.AbsolutePath));

        // Chosen automatic: one call for the league owed, and not a second the same day.
        await using (var db = Context(connection))
            await db.SetSettingAsync(AppSetting.LinePolling, AppSetting.LinePollingScheduled);

        await Jobs(host).RunAsync(IngestionJobs.OddsScores);
        await Jobs(host).RunAsync(IngestionJobs.OddsScores);

        var calls = wire.OddsApi.Where(u => u.AbsolutePath.Contains("/scores")).ToList();
        Assert.Single(calls);
        Assert.Contains("baseball_mlb", calls[0].AbsolutePath);
    }

    [Fact]
    public async Task Unattended_scores_never_dip_into_the_reserve()
    {
        var connection = await FreshDatabaseAsync();
        await SpendAsync(connection, 399);
        await using (var db = Context(connection))
        {
            var nfl = await SportAsync(db, "nfl");
            var home = new Team { SportId = nfl.Id, Name = "Buffalo Bills", Abbrev = "BUF" };
            var away = new Team { SportId = nfl.Id, Name = "Miami Dolphins", Abbrev = "MIA" };
            db.Teams.AddRange(home, away);
            await db.SaveChangesAsync();
            db.Games.Add(new Game { SportId = nfl.Id, HomeTeamId = home.Id, AwayTeamId = away.Id, StartsAt = DateTimeOffset.UtcNow.AddHours(-6), SeasonYear = 2026 });
            await db.SetSettingAsync(AppSetting.LinePolling, AppSetting.LinePollingScheduled);
            await db.SaveChangesAsync();
        }

        var wire = new Wire();
        await using var host = Host(connection, wire, espn: false);

        var outcome = await Jobs(host).RunAsync(IngestionJobs.OddsScores);

        Assert.Contains("Held back for NFL", outcome.Detail);
        Assert.DoesNotContain(wire.OddsApi, u => u.AbsolutePath.Contains("/scores"));
    }

    [Fact]
    public async Task A_game_beyond_the_three_days_is_said_to_be_unfinishable_and_is_never_asked_for()
    {
        var connection = await FreshDatabaseAsync();
        await using (var db = Context(connection))
        {
            var nba = await SportAsync(db, "nba");
            var home = new Team { SportId = nba.Id, Name = "Boston Celtics", Abbrev = "BOS" };
            var away = new Team { SportId = nba.Id, Name = "Miami Heat", Abbrev = "MIA" };
            db.Teams.AddRange(home, away);
            await db.SaveChangesAsync();
            db.Games.Add(new Game { SportId = nba.Id, HomeTeamId = home.Id, AwayTeamId = away.Id, StartsAt = DateTimeOffset.UtcNow.AddDays(-5), SeasonYear = 2026 });
            await db.SetSettingAsync(AppSetting.LinePolling, AppSetting.LinePollingScheduled);
            await db.SaveChangesAsync();
        }

        var wire = new Wire();
        await using var host = Host(connection, wire, espn: false);

        var outcome = await Jobs(host).RunAsync(IngestionJobs.OddsScores);

        Assert.Contains("1 game started more than three days ago has no final and cannot get one from The Odds API", outcome.Detail);
        Assert.Empty(wire.OddsApi);
    }

    [Fact]
    public async Task The_new_source_row_is_seeded_off_displayable_and_on_the_same_budget()
    {
        var connection = await FreshDatabaseAsync();
        await using var db = Context(connection);

        var row = await db.Sources.SingleAsync(s => s.Key == TheOddsApiScheduleAdapter.SourceKey);
        Assert.False(row.Enabled);
        Assert.Equal(SourceKind.Stats, row.Kind);
        Assert.Equal(SourceLicence.Displayable, row.Licence);
        Assert.Equal(500, row.MonthlyCreditBudget);
    }
}
