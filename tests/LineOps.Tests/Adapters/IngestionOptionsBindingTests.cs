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

    private static IServiceProvider Container(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // No data layer here: every adapter registration is decided from configuration alone,
        // which is the whole point of these tests.
        services.AddLineOpsIngestion(configuration);

        return services.BuildServiceProvider();
    }

    private static IReadOnlyList<string> OddsSourceKeys(IServiceProvider provider)
        => provider.GetServices<LineOps.Core.Contracts.IOddsSource>().Select(s => s.Key).ToList();

    private static IReadOnlyList<string> StatsSourceKeys(IServiceProvider provider)
        => provider.GetServices<LineOps.Core.Contracts.IStatsSource>().Select(s => s.Key).ToList();

    [Fact]
    public void WithNoOddsKeyThereIsNoOddsSourceAtAll()
    {
        using var scope = Container([]).CreateScope();

        // Nothing stands in. An offline fixture source used to, and its prices landed in the
        // same tables as real ones under a different source id, where every reader downstream
        // treated them alike. Empty is the honest answer, and the reliability layer is written
        // to read it as unconfigured rather than as an outage.
        Assert.Empty(OddsSourceKeys(scope.ServiceProvider));
    }

    [Fact]
    public void AKeyedOddsProviderIsTheOnlyThingThatRegistersOne()
    {
        using var scope = Container(new Dictionary<string, string?>
        {
            ["Ingestion:OddsApiIo:Enabled"] = "true",
            ["Ingestion:OddsApiIo:ApiKey"] = "test-key"
        }).CreateScope();

        Assert.Equal(["odds-api-io"], OddsSourceKeys(scope.ServiceProvider));
    }

    [Fact]
    public void AnEnabledProviderWithNoKeyRegistersNothing()
    {
        using var scope = Container(new Dictionary<string, string?>
        {
            ["Ingestion:OddsApiIo:Enabled"] = "true"
        }).CreateScope();

        // Half-configured is not configured. Registering it would spend every scan on a 401,
        // which the reliability layer would then correctly report as a failing source — an
        // outage manufactured out of a missing key.
        Assert.Empty(OddsSourceKeys(scope.ServiceProvider));
    }

    [Fact]
    public void EspnIsTheStatsSourceAndTheOnlyOne()
    {
        using var scope = Container([]).CreateScope();

        // ESPN is keyless and on by default (ADR 0011), which is why a clone with no keys still
        // fills the board with real fixtures and real box scores.
        Assert.Equal(["espn"], StatsSourceKeys(scope.ServiceProvider));
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
