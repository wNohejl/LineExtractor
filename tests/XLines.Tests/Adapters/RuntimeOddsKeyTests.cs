using XLines.Core.Contracts;
using XLines.Ingestion;
using XLines.Ingestion.Adapters;
using XLines.Ingestion.Configuration;
using XLines.Ingestion.Services;
using XLines.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace XLines.Tests.Adapters;

/// <summary>
/// A key supplied while the host runs turns the feed on, and taking it away turns it off, with
/// no restart; a host that never asked for that is unchanged.
/// </summary>
public class RuntimeOddsKeyTests
{
    private static ServiceProvider Container(bool runtimeKey, Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddXLinesIngestion(configuration);
        if (runtimeKey)
            services.AddRuntimeOddsKey();

        return services.BuildServiceProvider();
    }

    private static IReadOnlyList<string> Live(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var odds = scope.ServiceProvider.GetServices<IOddsSource>();
        return new SourceRegistry(odds, []).OddsSources.Select(s => s.Key).ToList();
    }

    [Fact]
    public void Without_a_key_the_adapter_is_registered_and_the_registry_lists_no_feed()
    {
        using var provider = Container(runtimeKey: true);

        Assert.Empty(Live(provider));
    }

    [Fact]
    public void A_supplied_key_makes_the_feed_live_for_the_next_scope_and_removing_it_ends_that()
    {
        using var provider = Container(runtimeKey: true);
        var key = provider.GetRequiredService<RuntimeOddsKey>();

        key.TheOddsApi = "  typed-in-the-desk  ";
        Assert.Equal([TheOddsApiAdapter.SourceKey], Live(provider));
        Assert.Equal("typed-in-the-desk", key.TheOddsApi);

        key.TheOddsApi = " ";
        Assert.Empty(Live(provider));
    }

    [Fact]
    public void A_configured_key_is_registered_once_whether_or_not_the_host_takes_a_runtime_key()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Ingestion:TheOddsApi:Enabled"] = "true",
            ["Ingestion:TheOddsApi:ApiKey"] = "from-configuration"
        };

        using var provider = Container(runtimeKey: true, settings);

        Assert.Equal([TheOddsApiAdapter.SourceKey], Live(provider));
    }

    [Fact]
    public void A_configured_key_without_its_switch_stays_off_on_a_host_that_takes_a_runtime_key()
    {
        using var provider = Container(runtimeKey: true, new Dictionary<string, string?>
        {
            ["Ingestion:TheOddsApi:Enabled"] = "false",
            ["Ingestion:TheOddsApi:ApiKey"] = "from-configuration"
        });

        Assert.Empty(Live(provider));
    }

    [Fact]
    public void A_host_that_does_not_take_a_runtime_key_registers_nothing_without_configuration()
    {
        using var provider = Container(runtimeKey: false);

        Assert.Empty(provider.GetServices<IOddsSource>());
    }

    // ---- The default stats source (ADR 0026): one stats port at a time --------------------------

    private static IReadOnlyList<string> Stats(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        return new SourceRegistry([], scope.ServiceProvider.GetServices<IStatsSource>()).StatsSources.Select(s => s.Key).ToList();
    }

    private static readonly Dictionary<string, string?> ConfiguredKey = new()
    {
        ["Ingestion:TheOddsApi:Enabled"] = "true",
        ["Ingestion:TheOddsApi:ApiKey"] = "from-configuration"
    };

    [Fact]
    public void Espn_on_with_a_configured_key_registers_no_default_source_in_either_host()
    {
        var settings = new Dictionary<string, string?>(ConfiguredKey) { [EspnOptIn.Switch] = "true" };

        foreach (var runtimeKey in new[] { false, true })
        {
            using var provider = Container(runtimeKey, settings);

            Assert.DoesNotContain(provider.GetServices<IScoreSource>(), _ => true);
            Assert.Equal(["espn"], Stats(provider));
        }
    }

    [Fact]
    public void Espn_off_with_a_configured_key_makes_the_odds_api_the_stats_source()
    {
        using var provider = Container(runtimeKey: false, ConfiguredKey);

        Assert.Equal([TheOddsApiScheduleAdapter.SourceKey], Stats(provider));
        Assert.Single(provider.CreateScope().ServiceProvider.GetServices<IScoreSource>());
    }

    [Fact]
    public void Espn_off_with_a_key_typed_at_runtime_lists_the_odds_api_as_the_stats_source_until_it_is_removed()
    {
        using var provider = Container(runtimeKey: true);
        var key = provider.GetRequiredService<RuntimeOddsKey>();

        Assert.Empty(Stats(provider));

        key.TheOddsApi = "typed-in-the-desk";
        Assert.Equal([TheOddsApiScheduleAdapter.SourceKey], Stats(provider));

        key.TheOddsApi = null;
        Assert.Empty(Stats(provider));
    }

    [Fact]
    public void Espn_off_and_no_key_on_a_host_without_a_desk_registers_no_stats_source()
    {
        using var provider = Container(runtimeKey: false);

        Assert.Empty(provider.GetServices<IStatsSource>());
    }

    [Fact]
    public async Task The_supplied_key_is_the_one_sent_ahead_of_configurations()
    {
        var options = new IngestionOptions();
        options.TheOddsApi.ApiKey = "from-configuration";

        var handler = new StubHttp("[]");
        var adapter = new TheOddsApiAdapter(
            handler.Client("https://api.the-odds-api.com/v4/"),
            Options.Create(options), NullLogger<TheOddsApiAdapter>.Instance,
            new RuntimeOddsKey { TheOddsApi = "typed-in-the-desk" });

        await adapter.FetchSlateAsync("mlb", ["moneyline"], CancellationToken.None);

        Assert.Contains("apiKey=typed-in-the-desk", handler.Last!.Query);
    }
}
