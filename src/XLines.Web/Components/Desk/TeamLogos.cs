namespace XLines.Web.Components.Desk;

/// <summary>
/// Where a club's crest is served from: ESPN's image host, which keys a crest by league and the
/// same abbreviation ESPN's scoreboard gives the club, so the Teams table's own spelling is the
/// address. The crest is never stored or committed; the browser fetches it, at the size it is
/// drawn, from the one origin the edge's <c>img-src</c> allows beyond the desk's own.
///
/// <para>
/// Only clubs listed here get a crest. A league the desk does not follow, a fighter, or a
/// spelling ESPN never served draws the colour disc or nothing, rather than a request that
/// would come back 404. Every entry was read back from the host in both variants.
/// </para>
/// </summary>
public static class TeamLogos
{
    /// <summary>The origin the crests come from, as the Content-Security-Policy names it.</summary>
    public const string Origin = "https://a.espncdn.com";

    private static readonly HashSet<string> Nfl = new(StringComparer.OrdinalIgnoreCase)
    {
        "ARI", "ATL", "BAL", "BUF", "CAR", "CHI", "CIN", "CLE", "DAL", "DEN", "DET", "GB",
        "HOU", "IND", "JAX", "KC", "LAC", "LAR", "LV", "MIA", "MIN", "NE", "NO", "NYG",
        "NYJ", "PHI", "PIT", "SEA", "SF", "TB", "TEN", "WSH",
        // Older spellings ESPN still serves a crest for.
        "OAK", "LA", "JAC", "WAS",
    };

    private static readonly HashSet<string> Mlb = new(StringComparer.OrdinalIgnoreCase)
    {
        "ARI", "ATH", "ATL", "BAL", "BOS", "CHC", "CHW", "CIN", "CLE", "COL", "DET", "HOU",
        "KC", "LAA", "LAD", "MIA", "MIL", "MIN", "NYM", "NYY", "PHI", "PIT", "SD", "SEA",
        "SF", "STL", "TB", "TEX", "TOR", "WSH",
        "OAK", "CWS", "WAS",
    };

    private static readonly HashSet<string> Nba = new(StringComparer.OrdinalIgnoreCase)
    {
        "ATL", "BKN", "BOS", "CHA", "CHI", "CLE", "DAL", "DEN", "DET", "GS", "HOU", "IND",
        "LAC", "LAL", "MEM", "MIA", "MIL", "MIN", "NO", "NY", "OKC", "ORL", "PHI", "PHX",
        "POR", "SA", "SAC", "TOR", "UTAH", "WSH",
    };

    /// <summary>Whether the club has a crest on file.</summary>
    public static bool Has(string? sportKey, string? abbrev) => Slug(sportKey, abbrev) is not null;

    /// <summary>
    /// The crest's address, or null for a club with none. <paramref name="onDark"/> picks the
    /// variant ESPN draws for a dark ground (a black or navy crest keeps a light keyline there);
    /// <paramref name="pixels"/> is the square the host scales it to, so a 16px mark does not
    /// pull a 500px file.
    /// </summary>
    public static string? For(string? sportKey, string? abbrev, bool onDark, int pixels)
    {
        if (Slug(sportKey, abbrev) is not var (sport, club))
            return null;

        var variant = onDark ? "500-dark" : "500";
        return $"{Origin}/combiner/i?img=/i/teamlogos/{sport}/{variant}/{club}.png&h={pixels}&w={pixels}";
    }

    private static (string Sport, string Club)? Slug(string? sportKey, string? abbrev)
    {
        if (string.IsNullOrWhiteSpace(sportKey) || string.IsNullOrWhiteSpace(abbrev))
            return null;

        var sport = sportKey.Trim().ToLowerInvariant();
        var known = sport switch
        {
            "nfl" => Nfl,
            "mlb" => Mlb,
            "nba" => Nba,
            _ => null,
        };

        var club = abbrev.Trim();
        return known is not null && known.Contains(club) ? (sport, club.ToLowerInvariant()) : null;
    }
}
