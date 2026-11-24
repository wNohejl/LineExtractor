namespace XLines.Core.Contracts;

/// <summary>
/// What the line opened at, beside the close it travels with on a <see cref="CanonicalOdds"/>.
///
/// <para>
/// ESPN states <c>open</c> next to <c>close</c> on every side it prices, in the same block the
/// close is parsed from, and the parser threw it away. Either half may be missing: an opening spread
/// is often stated without its price, and a moneyline has no line at all. A side with neither is
/// not an opening and is not attached.
/// </para>
/// </summary>
/// <param name="Line">The opening handicap or total. Null for a moneyline, or where none was stated.</param>
/// <param name="PriceAmerican">The opening price, where the provider stated one.</param>
public record CanonicalOpening(decimal? Line, int? PriceAmerican)
{
    /// <summary>Whether this says anything at all.</summary>
    public bool IsEmpty => Line is null && PriceAmerican is null;
}

/// <summary>One official as the provider lists them for a game: "Scott Foster", "Crew Chief".</summary>
/// <param name="Name">The official's name as given.</param>
/// <param name="Role">
/// The role as given — "Referee", "Umpire", "Crew Chief", "Home Plate", "First Base". Null where
/// the provider lists names alone (ESPN's NBA officials carry only an order).
/// </param>
/// <param name="Order">The provider's own order, where it gives one; the list's order otherwise.</param>
public record CanonicalOfficial(string Name, string? Role, int? Order = null);

/// <summary>
/// One spot in a confirmed batting order: the order (1 leads off), the player as the provider
/// identifies them, and the position they are listed at.
/// </summary>
/// <param name="BattingOrder">1 through 9, as the lineup is posted.</param>
/// <param name="SourcePlayerId">The provider's player id — MLBAM's for the MLB Stats API.</param>
/// <param name="FullName">The name as given.</param>
/// <param name="Position">"SS", "CF", "DH", as given. Null where the lineup names none.</param>
public record CanonicalLineupSlot(int BattingOrder, string SourcePlayerId, string FullName, string? Position);

/// <summary>
/// What a provider says about the circumstances of one game, beyond who plays and the score: where
/// it is played and on what, the weather, the crowd, the officials, the provider's own win
/// probability and — for baseball — the confirmed batting orders.
///
/// <para>
/// Every field is optional and the record is filled with an object initializer, because no provider
/// states all of it and most state it only some of the time: ESPN's <c>gameInfo</c> carries the
/// attendance only once the gates have closed, MLB posts a lineup a few hours before first pitch,
/// and weather is stated for an outdoor game alone. Null is "the provider said nothing", and the
/// writer reads it as no news, never as a reset: a later fetch that knows less erases nothing a
/// fetch that knew more had stored (ADR 0003 — what is not stated is not invented either).
/// </para>
///
/// <para>
/// Keyed by the provider's own game id, as <see cref="CanonicalPlayerStat"/> is, so a context read
/// from a summary reaches the game the scoreboard created without the parser having to carry the
/// game itself.
/// </para>
/// </summary>
/// <param name="SourceGameId">The provider's id for the game: ESPN's event id, MLB's <c>gamePk</c>.</param>
public record CanonicalGameContext(string SourceGameId)
{
    /// <summary>"Wrigley Field".</summary>
    public string? VenueName { get; init; }

    /// <summary>"Chicago".</summary>
    public string? VenueCity { get; init; }

    /// <summary>"IL", where the provider states a state or province.</summary>
    public string? VenueState { get; init; }

    /// <summary>
    /// True for a game played under a roof — a dome, or a retractable roof the provider says is
    /// closed or treats as indoor. Null where it does not say.
    /// </summary>
    public bool? Indoor { get; init; }

    /// <summary><see cref="GameSurfaces.Grass"/> or <see cref="GameSurfaces.Turf"/>; null where not stated.</summary>
    public string? Surface { get; init; }

    /// <summary>The provider's words for the weather: "Partly Cloudy", "Clear".</summary>
    public string? WeatherCondition { get; init; }

    /// <summary>Degrees Fahrenheit, as forecast or observed by the provider.</summary>
    public int? TemperatureF { get; init; }

    /// <summary>Wind speed in miles an hour.</summary>
    public int? WindSpeedMph { get; init; }

    /// <summary>The wind's direction in the provider's words — "Out To LF", "L To R", "NW".</summary>
    public string? WindDirection { get; init; }

    /// <summary>Chance of precipitation, 0 to 100.</summary>
    public int? PrecipitationPercent { get; init; }

    /// <summary>The paid attendance, once the provider states it.</summary>
    public int? Attendance { get; init; }

    /// <summary><see cref="GameDayNight.Day"/> or <see cref="GameDayNight.Night"/>, as MLB states <c>dayNight</c>.</summary>
    public string? DayNight { get; init; }

    /// <summary>
    /// The provider's pre-game chance the home side wins, in percent (0–100), exactly as ESPN's
    /// <c>predictor.homeTeam.gameProjection</c> states it. Percent, not a fraction of one: it is not
    /// the in-play <c>GamePlay.HomeWinPct</c>, and keeping the provider's own scale stops a reader
    /// confusing the two.
    /// </summary>
    public decimal? HomeWinPercent { get; init; }

    /// <summary>The same for the away side. Not derived from the home figure: a draw or a tie takes a share.</summary>
    public decimal? AwayWinPercent { get; init; }

    /// <summary>ESPN's <c>matchupQuality</c>, 0–100, where the predictor states one.</summary>
    public decimal? MatchupQuality { get; init; }

    /// <summary>
    /// The game's officials, in the provider's order. Null or empty is "not stated": a list is
    /// replaced only by another list that names somebody.
    /// </summary>
    public IReadOnlyList<CanonicalOfficial>? Officials { get; init; }

    /// <summary>The home side's confirmed batting order, leadoff first. Null or empty until posted.</summary>
    public IReadOnlyList<CanonicalLineupSlot>? HomeLineup { get; init; }

    /// <summary>The away side's confirmed batting order, leadoff first.</summary>
    public IReadOnlyList<CanonicalLineupSlot>? AwayLineup { get; init; }

    /// <summary>
    /// Whether anything here goes on the game's context row: any field stated, or a batting order
    /// that names somebody. The officials are left out because they are rows of their own, written
    /// whether or not the context row is.
    /// </summary>
    public bool StatesRowFields
        => VenueName is not null || VenueCity is not null || VenueState is not null || Indoor is not null
           || Surface is not null || WeatherCondition is not null || TemperatureF is not null
           || WindSpeedMph is not null || WindDirection is not null || PrecipitationPercent is not null
           || Attendance is not null || DayNight is not null || HomeWinPercent is not null
           || AwayWinPercent is not null || MatchupQuality is not null
           || HomeLineup is { Count: > 0 } || AwayLineup is { Count: > 0 };

    /// <summary>
    /// Whether the provider said anything at all, the officials included. A context that states
    /// nothing is no context, and a parser hands back null rather than an empty one.
    /// </summary>
    public bool StatesAnything => StatesRowFields || Officials is { Count: > 0 };
}

/// <summary>The two playing surfaces a context names. A provider's longer words are mapped onto these.</summary>
public static class GameSurfaces
{
    public const string Grass = "grass";

    /// <summary>Any artificial surface: "Artificial Turf", "FieldTurf", ESPN's <c>grass: false</c>.</summary>
    public const string Turf = "turf";
}

/// <summary>Whether a game is played by day or at night, as MLB's <c>dayNight</c> says it.</summary>
public static class GameDayNight
{
    public const string Day = "day";
    public const string Night = "night";
}
