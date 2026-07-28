namespace LineOps.Ingestion.Adapters;

/// <summary>
/// The leagues the ESPN port can address, and how to spell them in a URL.
///
/// <para>
/// ESPN paths are <c>{sport}/{league}</c> — "baseball/mlb", "football/nfl" — which does not
/// match the flat sport keys the rest of the platform uses. That mapping used to be a
/// <c>switch</c> in the adapter that threw <see cref="NotSupportedException"/> on anything it
/// did not know, which is the wrong shape twice over: adding a league meant editing parsing
/// code, and an unmapped key took down a whole backfill day rather than skipping one sport.
/// </para>
///
/// <para>
/// Adding a league is now one line here. Everything else — the scheduler, the backfill, the
/// window catalog — already works off sport keys, so a new league is a configuration change
/// plus this entry.
/// </para>
/// </summary>
public static class EspnLeagues
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nfl"] = "football/nfl",
        ["ncaaf"] = "football/college-football",
        ["nba"] = "basketball/nba",
        ["wnba"] = "basketball/wnba",
        ["ncaab"] = "basketball/mens-college-basketball",
        ["mlb"] = "baseball/mlb",
        ["nhl"] = "hockey/nhl",
        ["mls"] = "soccer/usa.1",
        ["epl"] = "soccer/eng.1"
    };

    /// <summary>Every sport key this port can serve.</summary>
    public static IReadOnlyCollection<string> Supported => Paths.Keys;

    /// <summary>
    /// The ESPN path for a sport key, or null when the league is not mapped.
    ///
    /// Null rather than an exception: a sport this port cannot serve is a sport to skip, not a
    /// reason to fail the run that asked for it.
    /// </summary>
    public static string? PathFor(string sportKey)
        => Paths.GetValueOrDefault(sportKey);

    public static bool Supports(string sportKey) => Paths.ContainsKey(sportKey);
}
