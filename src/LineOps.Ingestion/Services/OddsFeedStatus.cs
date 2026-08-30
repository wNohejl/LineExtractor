using LineOps.Ingestion.Configuration;
using Microsoft.Extensions.Options;

namespace LineOps.Ingestion.Services;

/// <summary>
/// Which odds providers are actually feeding the desk, and why the others are not.
///
/// <para>
/// Odds providers fail to run for undramatic reasons — disabled in configuration, or enabled
/// with an empty key — and every one of them looks identical from the outside: no prices. The
/// adapter is not even registered, so nothing appears in the run log to explain the silence,
/// and "did my key work?" becomes a question you answer by reading source.
/// </para>
///
/// <para>
/// This reports the same decision the DI registration makes, from the same options, so the
/// answer on screen cannot drift from what the container did.
/// </para>
/// </summary>
public class OddsFeedStatus(IOptions<IngestionOptions> options, SourceRegistry registry)
{
    private readonly IngestionOptions _options = options.Value;

    public IReadOnlyList<OddsProviderState> Describe()
    {
        var live = registry.OddsSources.Select(s => s.Key).ToHashSet();

        var states = new List<OddsProviderState>
        {
            Describe("odds-api-io", "odds-api.io", _options.OddsApiIo, live,
                "100 requests/hour, 500/day. One slate is two requests whatever the book list."),

            Describe("the-odds-api", "The Odds API", _options.TheOddsApi, live,
                "500 credits/month, billed as markets x regions per call.")
        };

        return states;
    }

    private OddsProviderState Describe(
        string key, string name, SourceOptions config, IReadOnlySet<string> live, string tier)
    {
        var hasKey = !string.IsNullOrWhiteSpace(config.ApiKey);

        var reason = live.Contains(key) ? tier
            : !config.Enabled ? "Disabled. Set Enabled=true in the Ingestion section."
            : !hasKey ? "Enabled, but no API key. Supply one out of band — never in the repo."
            : "Configured but not registered; check the startup log.";

        return new OddsProviderState(
            Key: key,
            Name: name,
            IsLive: live.Contains(key),
            Books: config.EffectiveBookmakers,
            Reason: reason);
    }
}

public record OddsProviderState(
    string Key,
    string Name,
    bool IsLive,
    IReadOnlyList<string> Books,
    string Reason);
