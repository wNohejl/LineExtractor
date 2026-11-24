using XLines.Core.Contracts;

namespace XLines.Ingestion.Adapters;

/// <summary>
/// The desk's own table of clubs for the leagues The Odds API schedules (ADR 0026): each club's
/// abbreviation, and the other names another provider holds it under. Facts about the leagues, not
/// provider data — The Odds API gives a full name and nothing else, so without this a club it names
/// would get an abbreviation made from its initials ("SF4" for the 49ers), and a club ESPN calls by a
/// shorter name ("LA Clippers") would be created a second time beside ESPN's.
///
/// <para>
/// The abbreviations are the ones ESPN uses, so a team created here reads the same on the board as
/// one ESPN created, and the resolver can recognise ESPN's row by them (only together with the
/// club's last word — see <c>EntityResolver</c>). Only the schedule adapter sends what this table
/// says; ESPN and the books resolve exactly as they always have.
/// </para>
/// </summary>
public static class TheOddsApiTeams
{
    private sealed record Club(string Abbrev, params string[] Names);

    private static readonly Dictionary<string, Club[]> Clubs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mlb"] =
        [
            new("ARI", "Arizona Diamondbacks"), new("ATL", "Atlanta Braves"), new("BAL", "Baltimore Orioles"),
            new("BOS", "Boston Red Sox"), new("CHC", "Chicago Cubs"), new("CHW", "Chicago White Sox"),
            new("CIN", "Cincinnati Reds"), new("CLE", "Cleveland Guardians"), new("COL", "Colorado Rockies"),
            new("DET", "Detroit Tigers"), new("HOU", "Houston Astros"), new("KC", "Kansas City Royals"),
            new("LAA", "Los Angeles Angels"), new("LAD", "Los Angeles Dodgers"), new("MIA", "Miami Marlins"),
            new("MIL", "Milwaukee Brewers"), new("MIN", "Minnesota Twins"), new("NYM", "New York Mets"),
            new("NYY", "New York Yankees"), new("ATH", "Athletics", "Oakland Athletics", "Sacramento Athletics"),
            new("PHI", "Philadelphia Phillies"), new("PIT", "Pittsburgh Pirates"), new("SD", "San Diego Padres"),
            new("SF", "San Francisco Giants"), new("SEA", "Seattle Mariners"),
            new("STL", "St. Louis Cardinals", "St Louis Cardinals"), new("TB", "Tampa Bay Rays"),
            new("TEX", "Texas Rangers"), new("TOR", "Toronto Blue Jays"), new("WSH", "Washington Nationals")
        ],
        ["nba"] =
        [
            new("ATL", "Atlanta Hawks"), new("BOS", "Boston Celtics"), new("BKN", "Brooklyn Nets"),
            new("CHA", "Charlotte Hornets"), new("CHI", "Chicago Bulls"), new("CLE", "Cleveland Cavaliers"),
            new("DAL", "Dallas Mavericks"), new("DEN", "Denver Nuggets"), new("DET", "Detroit Pistons"),
            new("GS", "Golden State Warriors"), new("HOU", "Houston Rockets"), new("IND", "Indiana Pacers"),
            new("LAC", "Los Angeles Clippers", "LA Clippers"), new("LAL", "Los Angeles Lakers"),
            new("MEM", "Memphis Grizzlies"), new("MIA", "Miami Heat"), new("MIL", "Milwaukee Bucks"),
            new("MIN", "Minnesota Timberwolves"), new("NO", "New Orleans Pelicans"), new("NY", "New York Knicks"),
            new("OKC", "Oklahoma City Thunder"), new("ORL", "Orlando Magic"), new("PHI", "Philadelphia 76ers"),
            new("PHX", "Phoenix Suns"), new("POR", "Portland Trail Blazers"), new("SAC", "Sacramento Kings"),
            new("SA", "San Antonio Spurs"), new("TOR", "Toronto Raptors"), new("UTAH", "Utah Jazz"),
            new("WSH", "Washington Wizards")
        ],
        ["nfl"] =
        [
            new("ARI", "Arizona Cardinals"), new("ATL", "Atlanta Falcons"), new("BAL", "Baltimore Ravens"),
            new("BUF", "Buffalo Bills"), new("CAR", "Carolina Panthers"), new("CHI", "Chicago Bears"),
            new("CIN", "Cincinnati Bengals"), new("CLE", "Cleveland Browns"), new("DAL", "Dallas Cowboys"),
            new("DEN", "Denver Broncos"), new("DET", "Detroit Lions"), new("GB", "Green Bay Packers"),
            new("HOU", "Houston Texans"), new("IND", "Indianapolis Colts"), new("JAX", "Jacksonville Jaguars"),
            new("KC", "Kansas City Chiefs"), new("LV", "Las Vegas Raiders"), new("LAC", "Los Angeles Chargers"),
            new("LAR", "Los Angeles Rams"), new("MIA", "Miami Dolphins"), new("MIN", "Minnesota Vikings"),
            new("NE", "New England Patriots"), new("NO", "New Orleans Saints"), new("NYG", "New York Giants"),
            new("NYJ", "New York Jets"), new("PHI", "Philadelphia Eagles"), new("PIT", "Pittsburgh Steelers"),
            new("SF", "San Francisco 49ers"), new("SEA", "Seattle Seahawks"), new("TB", "Tampa Bay Buccaneers"),
            new("TEN", "Tennessee Titans"), new("WSH", "Washington Commanders")
        ]
    };

    /// <summary>The desk's abbreviation for a club by any of its names, or null for one not in the table.</summary>
    public static string? Abbrev(string sportKey, string name) => Find(sportKey, name)?.Abbrev;

    /// <summary>
    /// A side as the schedule adapter hands it to the resolver: the name the provider gave, with the
    /// club's other names and the desk's abbreviation where the table knows the club.
    /// </summary>
    public static CanonicalTeamRef Ref(string sportKey, string name)
        => Find(sportKey, name) is { } club
            ? new CanonicalTeamRef(name)
            {
                KnownAs = club.Names.Where(n => !string.Equals(n, name, StringComparison.OrdinalIgnoreCase)).ToList(),
                DeskAbbrev = club.Abbrev
            }
            : new CanonicalTeamRef(name);

    /// <summary>Every club in a league, for the tests that hold the table to its facts.</summary>
    public static IReadOnlyList<(string Abbrev, IReadOnlyList<string> Names)> League(string sportKey)
        => Clubs.TryGetValue(sportKey, out var clubs)
            ? clubs.Select(c => (c.Abbrev, (IReadOnlyList<string>)c.Names)).ToList()
            : [];

    private static Club? Find(string sportKey, string name)
        => Clubs.TryGetValue(sportKey, out var clubs)
            ? clubs.FirstOrDefault(c => c.Names.Any(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            : null;
}
