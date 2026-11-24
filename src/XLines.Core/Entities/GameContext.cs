using System.Text.Json;
using XLines.Core.Contracts;

namespace XLines.Core.Entities;

/// <summary>
/// The circumstances of one game as one provider states them: the venue and its surface, the
/// weather, the crowd, day or night, the provider's own pre-game win probability, and — for
/// baseball — the confirmed batting orders. The officials are rows of their own
/// (<see cref="GameOfficial"/>), because they are asked about across games.
///
/// <para>
/// One row per game and source, keyed by both, as a stat line is: ESPN and the MLB Stats API both
/// describe a baseball game, they do not always agree (a forecast taken at different hours, a
/// venue named two ways), and a single row written by both would flip with whichever read last.
/// A reader takes the source it prefers and falls back to the other. The row also says, by its
/// <see cref="SourceId"/>, whose words these are — which is what the data tier reads (ADR 0024):
/// both providers are personal-use.
/// </para>
///
/// <para>
/// Written fill-and-update, never erase: a value a fetch states replaces the one held, and a
/// value a fetch leaves out leaves the held one standing. A summary read before the gates close
/// has no attendance; the one after does; a third read that has lost it again does not take it
/// away. Nothing here is ever derived — a figure the provider did not state is null (ADR 0003).
/// </para>
/// </summary>
public class GameContext : Contracts.IFromSource
{
    /// <summary>Column lengths, shared by the context's mapping and the writer's truncation.</summary>
    public const int VenueNameLength = 128;
    public const int VenueCityLength = 64;
    public const int VenueStateLength = 32;
    public const int SurfaceLength = 16;
    public const int WeatherConditionLength = 64;
    public const int WindDirectionLength = 32;
    public const int DayNightLength = 8;

    public int GameId { get; set; }
    public Game? Game { get; set; }

    public int SourceId { get; set; }
    public Source? Source { get; set; }

    public string? VenueName { get; set; }
    public string? VenueCity { get; set; }
    public string? VenueState { get; set; }

    /// <summary>True under a roof. Null where the provider did not say.</summary>
    public bool? Indoor { get; set; }

    /// <summary><see cref="GameSurfaces.Grass"/> or <see cref="GameSurfaces.Turf"/>.</summary>
    public string? Surface { get; set; }

    public string? WeatherCondition { get; set; }
    public int? TemperatureF { get; set; }
    public int? WindSpeedMph { get; set; }

    /// <summary>In the provider's words: "Out To LF", "L To R".</summary>
    public string? WindDirection { get; set; }

    /// <summary>0 to 100.</summary>
    public int? PrecipitationPercent { get; set; }

    public int? Attendance { get; set; }

    /// <summary><see cref="GameDayNight.Day"/> or <see cref="GameDayNight.Night"/>.</summary>
    public string? DayNight { get; set; }

    /// <summary>
    /// The provider's pre-game chance the home side wins, in percent (0–100) as ESPN's predictor
    /// states it — not a fraction of one, unlike the in-play <c>GamePlay.HomeWinPct</c>.
    /// </summary>
    public decimal? HomeWinPercent { get; set; }

    public decimal? AwayWinPercent { get; set; }

    /// <summary>ESPN's <c>matchupQuality</c>, 0–100.</summary>
    public decimal? MatchupQuality { get; set; }

    /// <summary>
    /// The home side's confirmed batting order as jsonb, leadoff first —
    /// <c>[{"battingOrder":1,"sourcePlayerId":"660271","fullName":"Shohei Ohtani","position":"DH"}]</c>
    /// (<see cref="SerializeLineup"/>). jsonb rather than rows because a lineup is read whole, per
    /// game, and never asked about across games; replaced whole by a fetch that posts one, and
    /// left standing by a fetch that posts none.
    /// </summary>
    public string? HomeLineup { get; set; }

    public string? AwayLineup { get; set; }

    /// <summary>When this row was first written.</summary>
    public DateTimeOffset CapturedAt { get; set; }

    /// <summary>When any field last changed. Not touched by a fetch that stated nothing new.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    private static readonly JsonSerializerOptions LineupJson = new(JsonSerializerDefaults.Web);

    /// <summary>A batting order as the <see cref="HomeLineup"/>/<see cref="AwayLineup"/> column holds it.</summary>
    public static string SerializeLineup(IReadOnlyList<CanonicalLineupSlot> lineup)
        => JsonSerializer.Serialize(lineup, LineupJson);

    /// <summary>A stored batting order read back; empty for none, or for a column that is not a lineup.</summary>
    public static IReadOnlyList<CanonicalLineupSlot> ParseLineup(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<CanonicalLineupSlot>>(stored, LineupJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>
/// One official at one game, as one provider lists them: a referee of an NBA crew, the home-plate
/// umpire of an MLB one.
///
/// <para>
/// Rows rather than a jsonb list on <see cref="GameContext"/>, because the question officials
/// answer is asked across games — how a referee's games go to the total, how wide an umpire's zone
/// plays — and that is a query <i>by</i> official, which an index on <see cref="Name"/> serves and
/// a list inside each game does not. A game's crew is replaced as a set by a fetch that names one,
/// and left standing by a fetch that names nobody.
/// </para>
/// </summary>
public class GameOfficial : Contracts.IFromSource
{
    public const int NameLength = 128;
    public const int RoleLength = 48;

    public long Id { get; set; }

    public int GameId { get; set; }
    public Game? Game { get; set; }

    public int SourceId { get; set; }
    public Source? Source { get; set; }

    /// <summary>As the provider names them: "Scott Foster", "Ángel Hernández".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>As the provider gives it — "Crew Chief", "Home Plate", "Referee". Null where not given.</summary>
    public string? Role { get; set; }

    /// <summary>The provider's order for the crew, first first.</summary>
    public int Order { get; set; }

    /// <summary>When this crew was written for this game.</summary>
    public DateTimeOffset CapturedAt { get; set; }
}
