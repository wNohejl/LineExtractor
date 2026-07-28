using LineOps.Core.Contracts;

namespace LineOps.Ingestion.Services;

/// <summary>
/// Lookup over the registered adapters. Everything downstream depends on the interfaces,
/// so adding a provider is a registration change rather than a code change.
/// </summary>
public class SourceRegistry(IEnumerable<IOddsSource> oddsSources, IEnumerable<IStatsSource> statsSources)
{
    public IReadOnlyList<IOddsSource> OddsSources { get; } = oddsSources.ToList();
    public IReadOnlyList<IStatsSource> StatsSources { get; } = statsSources.ToList();

    public IOddsSource? FindOdds(string key)
        => OddsSources.FirstOrDefault(s => s.Key == key);

    public IStatsSource? FindStats(string key)
        => StatsSources.FirstOrDefault(s => s.Key == key);
}
