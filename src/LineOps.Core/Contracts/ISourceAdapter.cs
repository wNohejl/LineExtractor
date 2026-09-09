using LineOps.Core.Entities;

namespace LineOps.Core.Contracts;

/// <summary>
/// A team as the provider identifies it.
///
/// Name alone is a weak key: providers disagree on punctuation and wording, so resolution has
/// to fall back to fuzzy comparison and occasionally gets it wrong. A provider that also gives
/// a stable id and an official abbreviation should hand both over — the id makes resolution
/// exact, and the abbreviation stops us inventing one from the name.
/// </summary>
public record CanonicalTeamRef(string Name, string? SourceTeamId = null, string? Abbrev = null);

/// <summary>Canonical game as normalised at the adapter boundary.</summary>
public record CanonicalGame(
    string SourceGameId,
    string SportKey,
    string HomeTeamName,
    string AwayTeamName,
    DateTimeOffset StartsAt,
    string? Status = null,
    int? HomeScore = null,
    int? AwayScore = null,
    /// <summary>Richer team identity when the provider supplies it. Falls back to the names above.</summary>
    CanonicalTeamRef? Home = null,
    CanonicalTeamRef? Away = null,
    /// <summary>The season the provider says this game belongs to, when it says. See <c>Game.SeasonYear</c>.</summary>
    int? SeasonYear = null,
    SeasonType? SeasonType = null);

/// <summary>Canonical priced outcome as normalised at the adapter boundary.</summary>
public record CanonicalOdds(
    string SourceGameId,
    string Book,
    string Market,
    string Outcome,
    decimal? Line,
    int PriceAmerican,
    DateTimeOffset CapturedAt);

public record CanonicalPlayer(
    string SourcePlayerId,
    string SportKey,
    string FullName,
    string? Position,
    string? SourceTeamId,
    string? TeamName,
    string Status = "active");

public record CanonicalPlayerStat(
    string SourcePlayerId,
    string SourceGameId,
    string StatLineJson);

/// <summary>
/// What a fetch cost us. Providers meter differently — odds-api.io counts requests,
/// The Odds API bills credits as markets x regions — so adapters report both and the
/// budget guard reconciles against the source's configured ceiling.
/// </summary>
public record FetchCost(int Requests, int Credits = 0);

public record OddsFetchResult(
    IReadOnlyList<CanonicalGame> Games,
    IReadOnlyList<CanonicalOdds> Odds,
    FetchCost Cost);

public record StatsFetchResult(
    IReadOnlyList<CanonicalGame> Games,
    IReadOnlyList<CanonicalPlayer> Players,
    IReadOnlyList<CanonicalPlayerStat> PlayerStats,
    FetchCost Cost,
    /// <summary>
    /// Lines the stats provider happened to carry, for games that have already closed.
    ///
    /// <para>
    /// A stats port is not a market and must never be read as one (ADR 0011): one book's number
    /// is a price, and the gap between several books is where a line being off shows up. But a
    /// game that has finished is no longer a pricing decision — it is a record — and a provider
    /// that states what the line closed at is a free historical reference for a fixture nobody
    /// was watching at the time.
    /// </para>
    ///
    /// <para>
    /// Empty for providers that carry none. ESPN supplies these on the same box-score response
    /// the walk already fetches, so they cost no additional request.
    /// </para>
    /// </summary>
    IReadOnlyList<CanonicalOdds>? ClosingLines = null)
{
    /// <summary>Never null at the call site, whatever the adapter passed.</summary>
    public IReadOnlyList<CanonicalOdds> Lines => ClosingLines ?? [];
}

/// <summary>
/// One external odds provider. Implementations own auth, paging, rate-limit awareness
/// and schema normalisation; everything above this interface is provider-agnostic.
/// </summary>
public interface IOddsSource
{
    /// <summary>Matches <see cref="Entities.Source.Key"/> in the database.</summary>
    string Key { get; }

    /// <summary>Markets this adapter can serve. v1 adapters return moneyline/spread/total.</summary>
    IReadOnlyList<string> SupportedMarkets { get; }

    Task<OddsFetchResult> FetchSlateAsync(
        string sportKey,
        IReadOnlyList<string> markets,
        CancellationToken ct);
}

/// <summary>
/// Implemented by adapters that can simulate provider faults on demand.
///
/// This exists so the detect → alert → triage → incident → RCA loop can be exercised
/// deliberately rather than only when a real provider happens to break. Only development
/// fixtures implement it; no adapter that talks to a real provider does.
/// </summary>
public interface IFailureInjectable
{
    /// <summary>null = healthy. "error", "timeout" and "empty" simulate the three failure shapes.</summary>
    string? FailureMode { get; set; }
}

/// <summary>One external stats provider: schedules, scores, rosters and box scores.</summary>
public interface IStatsSource
{
    string Key { get; }

    Task<StatsFetchResult> FetchScheduleAsync(string sportKey, DateOnly date, CancellationToken ct);

    Task<StatsFetchResult> FetchRosterAsync(string sportKey, CancellationToken ct);

    Task<StatsFetchResult> FetchBoxScoresAsync(string sportKey, DateOnly date, CancellationToken ct);
}
