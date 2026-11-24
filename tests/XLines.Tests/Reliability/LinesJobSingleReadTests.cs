using XLines.Core.Entities;
using XLines.Data;
using XLines.Ingestion;
using XLines.Ingestion.Adapters;
using XLines.Ingestion.Services;
using XLines.Reliability;
using XLines.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace XLines.Tests.Reliability;

/// <summary>
/// One league's lines entry read alone (<see cref="IngestionJobs.DescribeLinesAsync"/>) against the
/// same entry in the full menu, on every host shape the menu has: ESPN on, the default source with a
/// key, no key at all, and a spent month. A Board row press reads the single entry, so it must say
/// exactly what the pull menu says, price, refusal and question included.
/// </summary>
[Collection(PostgresCollection.Name)]
public class LinesJobSingleReadTests(PostgresFixture fixture)
{
    private async Task<string> FreshDatabaseAsync()
    {
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = "linesjob_" + Guid.NewGuid().ToString("N")[..12]
        }.ConnectionString;

        await using var db = Context(connection);
        await new DatabaseInitializer(db, NullLogger<DatabaseInitializer>.Instance).InitialiseAsync();
        return connection;
    }

    private static XLinesDbContext Context(string connection)
        => new(new DbContextOptionsBuilder<XLinesDbContext>().UseNpgsql(connection).Options);

    /// <summary>A host as Program.cs builds one, with every provider's client pointed at the wire.</summary>
    private static ServiceProvider Host(string connection, bool espn, string? configKey)
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
        services.AddRuntimeOddsKey();

        // Answers everything with an empty list; these tests make no pull.
        var wire = new StubHttp("[]");
        var registeredSchedule = services.Any(d => d.ServiceType == typeof(TheOddsApiScheduleAdapter));
        services.AddHttpClient<TheOddsApiAdapter>().ConfigurePrimaryHttpMessageHandler(() => wire);
        if (registeredSchedule)
            services.AddHttpClient<TheOddsApiScheduleAdapter>().ConfigurePrimaryHttpMessageHandler(() => wire);
        if (espn)
            services.AddHttpClient<EspnStatsAdapter>().ConfigurePrimaryHttpMessageHandler(() => wire);

        return services.BuildServiceProvider();
    }

    /// <summary>A month's spend against The Odds API's prices row, so the allowance refuses or asks.</summary>
    private static async Task SpendAsync(string connection, int credits)
    {
        await using var db = Context(connection);
        db.IngestionRuns.Add(new IngestionRun
        {
            SourceId = await db.Sources.Where(s => s.Key == SourceAccounts.TheOddsApi).Select(s => s.Id).SingleAsync(),
            JobKey = "odds:lines:mlb",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            FinishedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            Status = RunStatus.Success,
            RequestsMade = 1,
            CreditsSpent = credits
        });
        await db.SaveChangesAsync();
    }

    public static TheoryData<bool, string?, int> Hosts => new()
    {
        { true, "test-key", 0 },    // the owner's desk: ESPN on, a key
        { true, null, 0 },          // ESPN on, no key
        { false, "test-key", 0 },   // the default source on a key
        { false, null, 0 },         // nothing: every pull asks for a key
        { false, "test-key", 399 }, // under the reserve: the pull asks first
        { false, "test-key", 500 }  // a spent month: refused with the reason
    };

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task The_single_read_equals_the_menus_entry_for_every_league_the_menu_lists(bool espn, string? key, int spent)
    {
        var connection = await FreshDatabaseAsync();
        if (spent > 0)
            await SpendAsync(connection, spent);
        await using var host = Host(connection, espn, key);
        var jobs = host.GetRequiredService<IngestionJobs>();

        var menu = (await jobs.DescribeAsync()).Where(j => j.Key.StartsWith(IngestionJobs.OddsLinesPrefix, StringComparison.Ordinal)).ToList();
        Assert.Equal(4, menu.Count);

        foreach (var entry in menu)
            Assert.Equal(entry, await jobs.DescribeLinesAsync(entry.Key[IngestionJobs.OddsLinesPrefix.Length..]));
    }

    /// <summary>
    /// The lines and scores hints state the month the account row carries, not the free plan's 500:
    /// the budget is configurable, and a key on a larger plan was told the wrong allowance.
    /// </summary>
    [Fact]
    public async Task The_hints_state_the_configured_monthly_budget()
    {
        var connection = await FreshDatabaseAsync();
        await using (var db = Context(connection))
        {
            await db.Sources.Where(s => s.Key == SourceAccounts.TheOddsApi)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.MonthlyCreditBudget, 900));
        }

        await using var host = Host(connection, espn: false, configKey: "test-key");
        var jobs = host.GetRequiredService<IngestionJobs>();

        var lines = await jobs.DescribeLinesAsync("mlb");
        Assert.NotNull(lines);
        Assert.Contains("The free month is 900 credits; it resets on ", lines.Hint);

        var scores = (await jobs.DescribeAsync()).Single(j => j.Key == IngestionJobs.OddsScoresPrefix + "mlb");
        Assert.Contains("The free month is 900 credits; it resets on ", scores.Hint);
        Assert.DoesNotContain("500 credits;", scores.Hint);
    }

    /// <summary>The seeded row says what it always said.</summary>
    [Fact]
    public async Task The_seeded_budget_reads_as_the_free_months_500()
    {
        var connection = await FreshDatabaseAsync();
        await using var host = Host(connection, espn: false, configKey: "test-key");

        var lines = await host.GetRequiredService<IngestionJobs>().DescribeLinesAsync("mlb");

        Assert.NotNull(lines);
        Assert.Contains("The free month is 500 credits; it resets on ", lines.Hint);
    }

    /// <summary>
    /// A lines pull reports what its own runs spent. It used to sum every run of the job key from the
    /// last five minutes, so a press soon after another — or the other host's pull on the same
    /// database — reported the earlier spend as its own.
    /// </summary>
    [Fact]
    public async Task A_lines_pull_reports_only_its_own_runs_credits()
    {
        var connection = await FreshDatabaseAsync();

        // An earlier press of the same job a minute ago, which this pull must not claim.
        await using (var db = Context(connection))
        {
            db.IngestionRuns.Add(new IngestionRun
            {
                SourceId = await db.Sources.Where(s => s.Key == SourceAccounts.TheOddsApi).Select(s => s.Id).SingleAsync(),
                JobKey = IngestionJobs.OddsLinesPrefix + "mlb",
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                FinishedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                Status = RunStatus.Success,
                RequestsMade = 1,
                CreditsSpent = 40
            });
            await db.SaveChangesAsync();
        }

        long before;
        await using (var db = Context(connection))
            before = await db.IngestionRuns.MaxAsync(r => r.Id);

        // Bills three credits on every call and reports what is left, as The Odds API's headers do.
        await using var host = HostWith(connection, new StubHttp("[]") { RequestsLast = 3, RequestsRemaining = 400 });
        var outcome = await host.GetRequiredService<IngestionJobs>().RunAsync(IngestionJobs.OddsLinesPrefix + "mlb");

        int own;
        await using (var db = Context(connection))
            own = await db.IngestionRuns.Where(r => r.Id > before).SumAsync(r => r.CreditsSpent);

        Assert.True(own > 0, "the pull should have billed at least one call");
        Assert.Equal($"{outcome.Rows:N0} price {(outcome.Rows == 1 ? "move" : "moves")}, {own} credits spent.", outcome.Detail);
    }

    /// <summary>The default-source host with a key, every provider client on the given wire.</summary>
    private static ServiceProvider HostWith(string connection, HttpMessageHandler wire)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Ingestion:Sports:0"] = "mlb",
            ["Ingestion:Espn:Enabled"] = "false",
            ["Ingestion:MlbStatsApi:Enabled"] = "false",
            ["Ingestion:Gamecast:Enabled"] = "false",
            ["Ingestion:TheOddsApi:Enabled"] = "true",
            ["Ingestion:TheOddsApi:ApiKey"] = "test-key"
        };

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<XLinesDbContext>(o => o.UseNpgsql(connection));
        services.AddScoped<BudgetCalculator>();
        services.AddScoped(sp => new DatabaseInitializer(sp.GetRequiredService<XLinesDbContext>(), NullLogger<DatabaseInitializer>.Instance));
        services.AddXLinesIngestion(configuration);
        services.AddRuntimeOddsKey();
        services.AddHttpClient<TheOddsApiAdapter>().ConfigurePrimaryHttpMessageHandler(() => wire);
        services.AddHttpClient<TheOddsApiScheduleAdapter>().ConfigurePrimaryHttpMessageHandler(() => wire);

        return services.BuildServiceProvider();
    }

    /// <summary>A league the menu has no entry for reads as null, as the lookup in the menu did.</summary>
    [Theory]
    [InlineData("nhl")]
    [InlineData("MLB")]
    [InlineData("")]
    public async Task A_league_the_menu_does_not_list_reads_as_null(string sport)
    {
        var connection = await FreshDatabaseAsync();
        await using var host = Host(connection, espn: false, configKey: "test-key");
        var jobs = host.GetRequiredService<IngestionJobs>();

        Assert.DoesNotContain(await jobs.DescribeAsync(), j => j.Key == IngestionJobs.OddsLinesPrefix + sport);
        Assert.Null(await jobs.DescribeLinesAsync(sport));
    }
}
