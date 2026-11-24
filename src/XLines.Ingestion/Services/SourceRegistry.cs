using XLines.Core.Contracts;

namespace XLines.Ingestion.Services;

/// <summary>
/// Lookup over the registered adapters. Everything downstream depends on the interfaces,
/// so adding a provider is a registration change rather than a code change.
///
/// <para>
/// A source that is registered but holds no key (<see cref="IOddsSource.IsConfigured"/>,
/// <see cref="IStatsSource.IsConfigured"/>) is left out, so "no key" reads the same to everything
/// downstream whether the adapter was never registered or is waiting for its operator to supply
/// one. The registry is scoped, so a key supplied while the host runs is seen by the next job.
/// </para>
/// </summary>
public class SourceRegistry(IEnumerable<IOddsSource> oddsSources, IEnumerable<IStatsSource> statsSources)
{
    public IReadOnlyList<IOddsSource> OddsSources { get; } = oddsSources.Where(s => s.IsConfigured).ToList();
    public IReadOnlyList<IStatsSource> StatsSources { get; } = statsSources.Where(s => s.IsConfigured).ToList();

    public IOddsSource? FindOdds(string key)
        => OddsSources.FirstOrDefault(s => s.Key == key);

    public IStatsSource? FindStats(string key)
        => StatsSources.FirstOrDefault(s => s.Key == key);

    /// <summary>
    /// ESPN, when it is the stats port — what every <c>espn:*</c> job needs: the registered stats
    /// source that is not the default one (<see cref="IScoreSource"/>, ADR 0026). In a host that is
    /// ESPN's adapter (<c>espn</c>), the only other stats source there is;
    /// a test's stand-in for ESPN reads as ESPN too, as it always did.
    /// </summary>
    public IStatsSource? Espn => StatsSources.FirstOrDefault(s => s is not IScoreSource);

    /// <summary>
    /// The stats port's metered scores, when it has them (The Odds API, ADR 0026). Null with ESPN on,
    /// whose scores come with its free scoreboard, and with no key.
    /// </summary>
    public IScoreSource? Scores => StatsSources.OfType<IScoreSource>().FirstOrDefault();

    /// <summary>Which stats source the desk runs on, for the sentences that say so.</summary>
    public StatsPort Port => Espn is not null ? StatsPort.Espn
        : Scores is not null ? StatsPort.TheOddsApi
        : StatsPort.None;
}

/// <summary>What supplies the desk's schedule and results — the three states Ops and the Board describe.</summary>
public enum StatsPort
{
    /// <summary>No stats source: ESPN is off and no Odds API key is held. The desk asks for a key.</summary>
    None,

    /// <summary>The Odds API's fixtures (free) and scores (metered): the default (ADR 0026).</summary>
    TheOddsApi,

    /// <summary>ESPN, opted in: schedules, scores, box scores, play-by-play, injuries, fight cards.</summary>
    Espn
}
