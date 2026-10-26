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
public record CanonicalTeamRef(string Name, string? SourceTeamId = null, string? Abbrev = null)
{
    /// <summary>
    /// What tells this side apart from another of the same name, should the name be taken — a
    /// fighter's weight class. Two UFC fighters have been billed "Bruno Silva"; teams never share
    /// a name, so a club sends none.
    /// </summary>
    public string? Qualifier { get; init; }
}

/// <summary>
/// A fight's card and result, as the provider states them, carried on its <see cref="CanonicalGame"/>
/// (ADR 0021). Home is the red corner. Null on every game that is not a fight.
/// </summary>
public record CanonicalBout(
    string EventId,
    string EventName,
    string? EventShortName,
    string? CardSegment,
    int? MatchNumber,
    string? WeightClass,
    int ScheduledRounds,
    bool TitleFight,
    string? HomeRecord,
    string? AwayRecord,
    BoutOutcome? Outcome,
    string? Method,
    string? MethodDetail,
    int? EndRound,
    int? EndSeconds);

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
    SeasonType? SeasonType = null,
    /// <summary>The card and result, when the game is a fight.</summary>
    CanonicalBout? Bout = null);

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

/// <summary>
/// One player's line from one game. <paramref name="TeamName"/> is the side the player was on
/// in <i>that</i> game — which a box score states and a roster does not — so a line keeps its
/// team after the player has moved on. Null from a source that does not say.
/// </summary>
public record CanonicalPlayerStat(
    string SourcePlayerId,
    string SourceGameId,
    string StatLineJson,
    string? SourceTeamId = null,
    string? TeamName = null);

/// <summary>
/// What a fetch cost us. Providers meter differently — odds-api.io counts requests,
/// The Odds API bills credits as markets x regions — so adapters report both and the
/// budget guard reconciles against the source's configured ceiling. CreditsRemaining is the
/// provider's own count of what is left on the key, when it reports one.
/// </summary>
public record FetchCost(int Requests, int Credits = 0, int? CreditsRemaining = null);

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

    /// <summary>
    /// The markets to request for one sport. Totals matter more in baseball than a spread that
    /// is nearly always ±1.5, so a source can ask for a different list per sport; by default it
    /// is the same list everywhere.
    /// </summary>
    IReadOnlyList<string> MarketsFor(string sportKey) => SupportedMarkets;

    /// <summary>
    /// What one scan of one sport bills, in the provider's credits — zero for a source that bills
    /// in requests or not at all. The adapter says, because the billing rule is its provider's:
    /// a hand-set figure in configuration drifted from the market list the first time someone
    /// added a market.
    /// </summary>
    int CreditsPerScan(string sportKey) => 0;
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

/// <summary>One figure as ESPN states it: its key, the label it gives it, and the value as written.</summary>
public record FighterStat(string Key, string Label, string Value);

/// <summary>
/// A fighter's career figures (ADR 0021): the rates ESPN computes over the whole career, and the
/// record broken down — wins and losses by knockout and by submission, title fights, no contests.
/// </summary>
public record FighterCareer(IReadOnlyList<FighterStat> Rates, IReadOnlyList<FighterStat> Record);

/// <summary>
/// One fight from a fighter's recorded history, with ESPN's full stat sheet for it where ESPN kept
/// one. <paramref name="Ufc"/> is false for a fight outside the promotion — a regional card before
/// the fighter was signed, which ESPN records without stats.
/// </summary>
public record FighterFight(
    string Id,
    string? EventId,
    string? Event,
    bool Ufc,
    DateTimeOffset? Date,
    string? OpponentId,
    string? Opponent,
    string? Result,
    string? Method,
    string? MethodDetail,
    int? Round,
    string? Clock,
    bool Title,
    IReadOnlyList<FighterStat> Stats);

/// <summary>A fighter as the provider describes them: the few typed facts, and the figures kept whole.</summary>
public record CanonicalFighter(
    string SourceFighterId,
    string FullName,
    string? Nickname,
    string? Country,
    string? Association,
    string? Stance,
    string? WeightClass,
    decimal? HeightInches,
    decimal? ReachInches,
    DateOnly? BornOn,
    string? Record,
    FighterCareer Career,
    IReadOnlyList<FighterFight> History);

public record FighterFetchResult(CanonicalFighter? Fighter, FetchCost Cost);

/// <summary>
/// A provider that can describe one fighter in full — bio, career rates and every recorded fight.
/// ESPN, three requests a fighter. Separate from <see cref="IStatsSource"/> because it is asked per
/// fighter rather than per day, and only for sports that are fought.
/// </summary>
public interface IFighterSource
{
    string Key { get; }

    Task<FighterFetchResult> FetchFighterAsync(string sportKey, string sourceFighterId, CancellationToken ct);
}

/// <summary>A finished game whose closing line is owed, as a stats provider identifies it.</summary>
/// <param name="SourceEventId">
/// The event the game sits in, where that is not the game itself: a fight is one competition of a
/// card (ADR 0021). Null for a team sport, whose event and competition share an id.
/// </param>
public record ClosingLineRequest(
    string SourceGameId, string HomeTeamName, string AwayTeamName, DateTimeOffset StartsAt, string? SourceEventId = null);

/// <summary>
/// A stats provider that can state, for one finished game, what the line closed at — without a
/// box-score walk. Implemented by ESPN, whose summary drops its odds block for older games while
/// its odds endpoint still carries the close; that is how a season with no lines gets them back.
/// </summary>
public interface IClosingLineSource
{
    string Key { get; }

    Task<IReadOnlyList<CanonicalOdds>> FetchClosingLinesAsync(
        string sportKey, ClosingLineRequest game, CancellationToken ct);
}
