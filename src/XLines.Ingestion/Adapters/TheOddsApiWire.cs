using System.Globalization;
using System.Text.Json;
using XLines.Core.Contracts;
using XLines.Ingestion.Configuration;
using XLines.Ingestion.Services;

namespace XLines.Ingestion.Adapters;

/// <summary>
/// What every call to The Odds API shares, whichever endpoint it reads: the key in force, the
/// provider's league keys, and the credit headers it bills by. The odds adapter
/// (<see cref="TheOddsApiAdapter"/>) and the fixtures-and-scores adapter
/// (<see cref="TheOddsApiScheduleAdapter"/>) are one account and must agree on all three.
/// </summary>
public sealed class TheOddsApiKey(SourceOptions config, RuntimeOddsKey? runtimeKey)
{
    /// <summary>
    /// The key in force: the one the operator supplied while the host runs, where there is one,
    /// else configuration's. A key typed into the desk is the later and more deliberate of the two.
    /// </summary>
    public string? Value
        => runtimeKey?.TheOddsApi is { Length: > 0 } supplied ? supplied
            : string.IsNullOrWhiteSpace(config.ApiKey) ? null
            : config.ApiKey;

    /// <summary>
    /// A supplied key needs no <c>Enabled</c> switch beside it: supplying it is the switch.
    /// Configuration's key still needs its switch, as it always has.
    /// </summary>
    public bool IsConfigured
        => runtimeKey?.TheOddsApi is { Length: > 0 }
           || (config.Enabled && !string.IsNullOrWhiteSpace(config.ApiKey));

    /// <summary>The key, or the configuration error a call without one is — raised before any request.</summary>
    public string Require(string sourceKey)
        => Value ?? throw new InvalidOperationException($"{sourceKey}: no API key configured.");
}

/// <summary>
/// The provider's own spellings, addresses, headers and event shape, written once. Both adapters
/// read an event through <see cref="ReadEvent"/> and bill through <see cref="CostOf"/>, so a change
/// in what the provider sends is met in one place and the two cannot drift apart.
/// </summary>
public static class TheOddsApiWire
{
    /// <summary>The provider's league key for one of the desk's.</summary>
    public static string MapSport(string sportKey) => sportKey switch
    {
        "nfl" => "americanfootball_nfl",
        "nba" => "basketball_nba",
        "mlb" => "baseball_mlb",
        "nhl" => "icehockey_nhl",
        // Every promotion under one key; only fights matching a UFC bout on the desk are kept (ADR 0021).
        "ufc" => "mma_mixed_martial_arts",
        _ => sportKey
    };

    // ---- Addresses ------------------------------------------------------------------------------
    //
    // Relative to the client's base address. The trailing slashes are the ones each call has always
    // been sent with — `odds/` and `scores/` carry one, `events` does not — and are kept exactly, so
    // that a refactor here never changes a request the provider has been answering.

    /// <summary>
    /// <c>/odds</c>, for games that have not started. Named <paramref name="bookmakers"/> where
    /// there are any (the caller decides whether they fit one region unit), else <c>regions=us</c>.
    /// </summary>
    public static string OddsUrl(
        string sportKey,
        string apiKey,
        IReadOnlyCollection<string>? bookmakers,
        IEnumerable<string> providerMarkets,
        DateTimeOffset commenceFrom)
        => $"sports/{MapSport(sportKey)}/odds/"
           + $"?apiKey={Uri.EscapeDataString(apiKey)}"
           + (bookmakers is { Count: > 0 } ? $"&bookmakers={string.Join(',', bookmakers)}" : "&regions=us")
           + $"&markets={string.Join(',', providerMarkets)}"
           + "&oddsFormat=american"
           + $"&commenceTimeFrom={IsoSeconds(commenceFrom)}";

    /// <summary><c>/events</c>, every fixture starting in the window: a call the provider does not bill.</summary>
    public static string EventsUrl(string sportKey, string apiKey, DateTimeOffset commenceFrom, DateTimeOffset commenceTo)
        => $"sports/{MapSport(sportKey)}/events"
           + $"?apiKey={Uri.EscapeDataString(apiKey)}"
           + "&dateFormat=iso"
           + $"&commenceTimeFrom={IsoSeconds(commenceFrom)}"
           + $"&commenceTimeTo={IsoSeconds(commenceTo)}";

    /// <summary><c>/scores</c>, live and completed games from up to <paramref name="daysFrom"/> days back.</summary>
    public static string ScoresUrl(string sportKey, string apiKey, int daysFrom)
        => $"sports/{MapSport(sportKey)}/scores/"
           + $"?apiKey={Uri.EscapeDataString(apiKey)}"
           + $"&daysFrom={daysFrom.ToString(CultureInfo.InvariantCulture)}"
           + "&dateFormat=iso";

    // ---- Billing --------------------------------------------------------------------------------

    /// <summary>
    /// What the last call billed (<c>x-requests-last</c>), what is left (<c>x-requests-remaining</c>):
    /// the provider's numbers, preferred over any estimate of ours.
    /// </summary>
    public static int? ReadHeaderInt(HttpResponseMessage response, string header)
        => response.Headers.TryGetValues(header, out var values)
           && int.TryParse(values.FirstOrDefault(), out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// One request's cost as the provider reports it. Where it does not say what it billed, the
    /// caller's <paramref name="fallbackCredits"/> stands in — each endpoint's billing rule is its
    /// own, so the estimate belongs to the caller, not to this layer.
    /// </summary>
    public static FetchCost CostOf(HttpResponseMessage response, int fallbackCredits)
        => new(Requests: 1,
            Credits: ReadHeaderInt(response, "x-requests-last") ?? fallbackCredits,
            CreditsRemaining: ReadHeaderInt(response, "x-requests-remaining"));

    // ---- The payload ----------------------------------------------------------------------------

    /// <summary>
    /// A time as the provider writes it, or null where the property is missing, not a string, or not
    /// a time. Never throws: a field of the wrong type is an unreadable field, not a failed call.
    /// </summary>
    public static DateTimeOffset? ReadTimestamp(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
               DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// A string property, or null where it is missing or of another JSON type. <see cref="JsonElement.GetString"/>
    /// throws on a number or an object, and an exception from a billed call that is not
    /// <see cref="OddsFetchFailedException"/> would record the call as free.
    /// </summary>
    public static string? ReadString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// One event as a game: its id, both sides by name, and its start — the shape <c>/odds</c>,
    /// <c>/events</c> and <c>/scores</c> share. An event with no id, a blank side or no readable start
    /// is not a fixture, and is null rather than an exception, whatever type the provider sent.
    ///
    /// <para>
    /// <paramref name="withRefs"/> attaches what the desk's table (<see cref="TheOddsApiTeams"/>) knows
    /// of each club. Only the schedule adapter asks for it, and deliberately: it is the stats source
    /// that creates teams, so its sides need the desk's abbreviations and other names to land on the
    /// clubs ESPN created. A book's game is resolved against games already held, by name, exactly as
    /// it always has been; the table says nothing to it (see <see cref="TheOddsApiTeams"/>).
    /// </para>
    /// </summary>
    public static CanonicalGame? ReadEvent(JsonElement e, string sportKey, bool withRefs)
    {
        if (ReadString(e, "id") is not { Length: > 0 } id)
            return null;

        var home = ReadString(e, "home_team");
        var away = ReadString(e, "away_team");
        if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away))
            return null;

        // An event with no readable start is skipped rather than stamped "now": a made-up start time
        // is a fixture the resolver cannot place, and once it was allowed to correct the schedule, one
        // malformed payload could move a real game to the present.
        if (ReadTimestamp(e, "commence_time") is not { } startsAt)
            return null;

        return withRefs
            ? new CanonicalGame(id, sportKey, home, away, startsAt,
                Home: TheOddsApiTeams.Ref(sportKey, home),
                Away: TheOddsApiTeams.Ref(sportKey, away))
            : new CanonicalGame(id, sportKey, home, away, startsAt);
    }

    /// <summary>A time as the provider's <c>commenceTimeFrom</c>/<c>To</c> take it: ISO 8601, whole seconds, UTC.</summary>
    public static string IsoSeconds(DateTimeOffset at)
        => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
