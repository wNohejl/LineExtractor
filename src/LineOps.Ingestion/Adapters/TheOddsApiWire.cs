using System.Globalization;
using System.Text.Json;
using LineOps.Ingestion.Configuration;
using LineOps.Ingestion.Services;

namespace LineOps.Ingestion.Adapters;

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

/// <summary>The provider's own spellings and headers, written once.</summary>
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

    /// <summary>
    /// What the last call billed (<c>x-requests-last</c>), what is left (<c>x-requests-remaining</c>):
    /// the provider's numbers, preferred over any estimate of ours.
    /// </summary>
    public static int? ReadHeaderInt(HttpResponseMessage response, string header)
        => response.Headers.TryGetValues(header, out var values)
           && int.TryParse(values.FirstOrDefault(), out var parsed)
            ? parsed
            : null;

    public static DateTimeOffset? ReadTimestamp(JsonElement element, string property)
        => element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
               DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    /// <summary>A time as the provider's <c>commenceTimeFrom</c>/<c>To</c> take it: ISO 8601, whole seconds, UTC.</summary>
    public static string IsoSeconds(DateTimeOffset at)
        => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
