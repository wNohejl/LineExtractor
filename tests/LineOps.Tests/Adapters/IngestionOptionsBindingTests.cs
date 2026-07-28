using LineOps.Ingestion;
using LineOps.Ingestion.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LineOps.Tests.Adapters;

/// <summary>
/// How configuration actually binds, as opposed to how it looks like it binds.
///
/// The .NET configuration binder appends to an array property that already holds a non-empty
/// default instead of replacing it. That bit twice — once on backfill sources, where it walked
/// every day of the season twice, and once on bookmakers, where the duplicate went out in the
/// request URL. Worse than the duplication, a non-empty default cannot be *narrowed* from
/// config at all: asking for one book silently keeps the defaults too.
///
/// So every list option defaults to empty and resolves its fallback in code. These pin that,
/// because the failure is silent in both directions — nothing throws, you just quietly get
/// data you did not ask for.
/// </summary>
public class IngestionOptionsBindingTests
{
    private static IngestionOptions Bind(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLineOpsIngestion(configuration);

        return services.BuildServiceProvider().GetRequiredService<IOptions<IngestionOptions>>().Value;
    }

    [Fact]
    public void BookmakersListedOverTheDefaultDoNotDuplicate()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Ingestion:OddsApiIo:Bookmakers:0"] = "draftkings",
            ["Ingestion:OddsApiIo:Bookmakers:1"] = "fanduel",
            ["Ingestion:OddsApiIo:Bookmakers:2"] = "betmgm"
        });

        Assert.Equal(["draftkings", "fanduel", "betmgm"], options.OddsApiIo.EffectiveBookmakers);
    }

    [Fact]
    public void BackfillSourcesDoNotDuplicateAgainstTheirDefault()
    {
        // This one walked every day twice before it was caught.
        var options = Bind(new Dictionary<string, string?>
        {
            ["Ingestion:Backfill:Sources:0"] = "espn"
        });

        Assert.Equal(["espn"], options.Backfill.EffectiveSources);
    }

    [Fact]
    public void NamingSportsNarrowsThemRatherThanAddingToTheDefaults()
    {
        // The case that is about to matter: MLB is the only sport in season, and the next
        // leagues get added here one at a time. Asking for two must not give back four.
        var options = Bind(new Dictionary<string, string?>
        {
            ["Ingestion:Sports:0"] = "mlb",
            ["Ingestion:Sports:1"] = "nfl"
        });

        Assert.Equal(["mlb", "nfl"], options.EffectiveSports);
    }

    [Fact]
    public void NamingNoSportsFallsBackToTheMajors()
    {
        var options = Bind([]);

        Assert.Equal(["nfl", "nba", "mlb", "nhl"], options.EffectiveSports);
    }

    [Fact]
    public void NamingOneBookDropsTheDefaults()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Ingestion:OddsApiIo:Bookmakers:0"] = "betmgm"
        });

        Assert.Equal(["betmgm"], options.OddsApiIo.EffectiveBookmakers);
    }

    [Fact]
    public void OrderIsPreservedBecauseForSourcesItIsAPreferenceOrder()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Ingestion:OddsApiIo:Bookmakers:0"] = "caesars",
            ["Ingestion:OddsApiIo:Bookmakers:1"] = "draftkings"
        });

        Assert.Equal("caesars", options.OddsApiIo.EffectiveBookmakers[0]);
    }

    /// <summary>Schedules are the data layer's business; these tests are about source selection.</summary>
    private sealed class EmptySchedule : LineOps.Core.Contracts.IScheduleReader
    {
        public Task<IReadOnlyList<LineOps.Core.Contracts.ScheduledGame>> GetUpcomingAsync(
            string sportKey, TimeSpan window, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LineOps.Core.Contracts.ScheduledGame>>([]);
    }

    private static IServiceProvider Container(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // The demo odds source prices the real slate, so it needs somewhere to read it from.
        // Supplied here because AddLineOpsData wants a connection string these tests have no
        // use for.
        services.AddSingleton<LineOps.Core.Contracts.IScheduleReader, EmptySchedule>();
        services.AddLineOpsIngestion(configuration);

        return services.BuildServiceProvider();
    }

    private static IReadOnlyList<string> OddsSourceKeys(IServiceProvider provider)
        => provider.GetServices<LineOps.Core.Contracts.IOddsSource>().Select(s => s.Key).ToList();

    private static IReadOnlyList<string> StatsSourceKeys(IServiceProvider provider)
        => provider.GetServices<LineOps.Core.Contracts.IStatsSource>().Select(s => s.Key).ToList();

    [Fact]
    public void WithNoRealOddsProviderTheDemoFeedStandsIn()
    {
        using var scope = Container([]).CreateScope();

        // A cold clone has to be usable, so demo odds fill the gap.
        Assert.Contains("demo", OddsSourceKeys(scope.ServiceProvider));
    }

    [Fact]
    public void AConfiguredOddsProviderMakesTheDemoFeedStandDown()
    {
        using var scope = Container(new Dictionary<string, string?>
        {
            ["Ingestion:OddsApiIo:Enabled"] = "true",
            ["Ingestion:OddsApiIo:ApiKey"] = "test-key"
        }).CreateScope();

        var keys = OddsSourceKeys(scope.ServiceProvider);

        // Fabricated prices alongside real ones land in the same table under a different
        // source id, where every downstream reader treats them alike.
        Assert.Contains("odds-api-io", keys);
        Assert.DoesNotContain("demo", keys);
    }

    [Fact]
    public void AnEnabledProviderWithNoKeyDoesNotDisplaceTheDemoFeed()
    {
        using var scope = Container(new Dictionary<string, string?>
        {
            ["Ingestion:OddsApiIo:Enabled"] = "true"
        }).CreateScope();

        var keys = OddsSourceKeys(scope.ServiceProvider);

        // Half-configured is not configured: standing down here would leave no odds at all.
        Assert.DoesNotContain("odds-api-io", keys);
        Assert.Contains("demo", keys);
    }

    [Fact]
    public void EspnBeingOnMakesTheDemoStatsSourceStandDown()
    {
        using var scope = Container([]).CreateScope();

        var keys = StatsSourceKeys(scope.ServiceProvider);

        // ESPN is enabled by default, so demo stats — the source that invented rosters with no
        // games to attach them to — never registers.
        Assert.Contains("espn", keys);
        Assert.DoesNotContain("demo-stats", keys);
    }

    [Fact]
    public void NamingNoMarketsAsksForAllOfThem()
    {
        var options = Bind([]);

        Assert.Equal(LineOps.Core.Entities.Markets.V1, options.TheOddsApi.EffectiveMarkets);
    }

    [Theory]
    [InlineData("moneyline")]
    [InlineData("h2h")]
    [InlineData("ML")]
    [InlineData(" Moneyline ")]
    public void TheMoneylineIsSpelledHowevertheOperatorSpellsIt(string spelling)
    {
        // The canonical key is "h2h" — the provider's word, not anyone's first guess. Bound
        // strictly, "moneyline" would reach the adapter unrecognised and be forwarded verbatim:
        // a call that still bills a credit and returns nothing.
        var options = Bind(new Dictionary<string, string?>
        {
            ["Ingestion:TheOddsApi:Markets:0"] = spelling
        });

        Assert.Equal([LineOps.Core.Entities.Markets.Moneyline], options.TheOddsApi.EffectiveMarkets);
    }

    [Fact]
    public void AMarketNobodyModelsIsDroppedRatherThanForwarded()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Ingestion:TheOddsApi:Markets:0"] = "moneyline",
            ["Ingestion:TheOddsApi:Markets:1"] = "player_strikeouts"
        });

        // A typo should cost the market, not the money. Forwarding it spends a credit per call
        // for the rest of the month on a market the platform cannot store.
        Assert.Equal([LineOps.Core.Entities.Markets.Moneyline], options.TheOddsApi.EffectiveMarkets);
    }

    [Fact]
    public void NarrowingTheMarketsIsWhatMakesAScanCheaper()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Ingestion:TheOddsApi:Markets:0"] = "moneyline",
            ["Ingestion:TheOddsApi:Markets:1"] = "spread"
        });

        // Billed markets x regions, so this is the difference between 2 credits a scan and 3 —
        // a standing 50% surcharge avoided on every call for the rest of the month.
        Assert.Equal(2, options.TheOddsApi.EffectiveMarkets.Length);
        Assert.DoesNotContain(LineOps.Core.Entities.Markets.Total, options.TheOddsApi.EffectiveMarkets);
    }

    [Fact]
    public void ABookSpelledTwoWaysIsStillOneBook()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Ingestion:OddsApiIo:Bookmakers:0"] = "DraftKings",
            ["Ingestion:OddsApiIo:Bookmakers:1"] = "draftkings"
        });

        Assert.Single(options.OddsApiIo.EffectiveBookmakers);
    }
}
