namespace LineOps.Web.Components.Desk;

/// <summary>
/// Each club's colour, keyed by sport and the abbreviation ESPN gives it. Team colour is
/// content, not chrome: it reaches the page only as a <see cref="TeamMark"/> beside the team's
/// name, never as the colour of text, a control, a fill or a selection, and no hex lives in CSS.
/// The colour is the one a fan names first; a near-black or navy primary is kept, since the
/// mark's ring makes it read on a dark surface. Where ESPN has spelt a club two ways, both are here.
/// </summary>
public static class TeamColors
{
    private static readonly Dictionary<string, string> Nfl = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ARI"] = "#97233F", // Arizona Cardinals: cardinal red
        ["ATL"] = "#A71930", // Atlanta Falcons: red
        ["BAL"] = "#241773", // Baltimore Ravens: purple
        ["BUF"] = "#00338D", // Buffalo Bills: royal blue
        ["CAR"] = "#0085CA", // Carolina Panthers: Panther blue
        ["CHI"] = "#0B162A", // Chicago Bears: navy
        ["CIN"] = "#FB4F14", // Cincinnati Bengals: orange
        ["CLE"] = "#FF3C00", // Cleveland Browns: orange, the helmet
        ["DAL"] = "#003594", // Dallas Cowboys: royal blue, the star
        ["DEN"] = "#FB4F14", // Denver Broncos: orange
        ["DET"] = "#0076B6", // Detroit Lions: Honolulu blue
        ["GB"] = "#203731",  // Green Bay Packers: dark green
        ["HOU"] = "#03202F", // Houston Texans: deep steel blue
        ["IND"] = "#002C5F", // Indianapolis Colts: speed blue
        ["JAX"] = "#006778", // Jacksonville Jaguars: teal
        ["JAC"] = "#006778", // Jacksonville Jaguars: older spelling
        ["KC"] = "#E31837",  // Kansas City Chiefs: red
        ["LAC"] = "#0080C6", // Los Angeles Chargers: powder blue
        ["LAR"] = "#003594", // Los Angeles Rams: royal blue
        ["LA"] = "#003594",  // Los Angeles Rams: ESPN's spelling 2016-2019
        ["LV"] = "#000000",  // Las Vegas Raiders: black
        ["OAK"] = "#000000", // Oakland Raiders: black, before the move
        ["MIA"] = "#008E97", // Miami Dolphins: aqua
        ["MIN"] = "#4F2683", // Minnesota Vikings: purple
        ["NE"] = "#002244",  // New England Patriots: nautical blue
        ["NO"] = "#D3BC8D",  // New Orleans Saints: old gold
        ["NYG"] = "#0B2265", // New York Giants: dark blue
        ["NYJ"] = "#125740", // New York Jets: gotham green
        ["PHI"] = "#004C54", // Philadelphia Eagles: midnight green
        ["PIT"] = "#FFB612", // Pittsburgh Steelers: gold
        ["SEA"] = "#69BE28", // Seattle Seahawks: action green, the one that is theirs alone
        ["SF"] = "#AA0000",  // San Francisco 49ers: scarlet
        ["TB"] = "#D50A0A",  // Tampa Bay Buccaneers: buccaneer red
        ["TEN"] = "#4B92DB", // Tennessee Titans: titans blue
        ["WSH"] = "#5A1414", // Washington Commanders: burgundy
        ["WAS"] = "#5A1414", // Washington Commanders: the other spelling
    };

    private static readonly Dictionary<string, string> Mlb = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ARI"] = "#A71930", // Arizona Diamondbacks: Sedona red
        ["AZ"] = "#A71930",  // Arizona Diamondbacks: MLB's own spelling
        ["ATH"] = "#003831", // Athletics: green
        ["OAK"] = "#003831", // Oakland Athletics: before the move
        ["ATL"] = "#CE1141", // Atlanta Braves: scarlet
        ["BAL"] = "#DF4601", // Baltimore Orioles: orange
        ["BOS"] = "#BD3039", // Boston Red Sox: red
        ["CHC"] = "#0E3386", // Chicago Cubs: Cubbie blue
        ["CHW"] = "#27251F", // Chicago White Sox: black
        ["CWS"] = "#27251F", // Chicago White Sox: MLB's own spelling
        ["CIN"] = "#C6011F", // Cincinnati Reds: red
        ["CLE"] = "#00385D", // Cleveland Guardians: navy
        ["COL"] = "#333366", // Colorado Rockies: purple
        ["DET"] = "#0C2340", // Detroit Tigers: navy
        ["HOU"] = "#EB6E1F", // Houston Astros: orange
        ["KC"] = "#004687",  // Kansas City Royals: royal blue
        ["LAA"] = "#BA0021", // Los Angeles Angels: red
        ["LAD"] = "#005A9C", // Los Angeles Dodgers: Dodger blue
        ["MIA"] = "#00A3E0", // Miami Marlins: Miami blue
        ["MIL"] = "#12284B", // Milwaukee Brewers: navy
        ["MIN"] = "#002B5C", // Minnesota Twins: navy
        ["NYM"] = "#002D72", // New York Mets: blue
        ["NYY"] = "#0C2340", // New York Yankees: navy
        ["PHI"] = "#E81828", // Philadelphia Phillies: red
        ["PIT"] = "#FDB827", // Pittsburgh Pirates: gold
        ["SD"] = "#2F241D",  // San Diego Padres: brown
        ["SEA"] = "#005C5C", // Seattle Mariners: northwest green
        ["SF"] = "#FD5A1E",  // San Francisco Giants: orange
        ["STL"] = "#C41E3A", // St. Louis Cardinals: red
        ["TB"] = "#092C5C",  // Tampa Bay Rays: navy
        ["TEX"] = "#003278", // Texas Rangers: blue
        ["TOR"] = "#134A8E", // Toronto Blue Jays: blue
        ["WSH"] = "#AB0003", // Washington Nationals: red
        ["WAS"] = "#AB0003", // Washington Nationals: the other spelling
    };

    /// <summary>The club's colour as <c>#RRGGBB</c>, or null when the sport or club is not mapped.</summary>
    public static string? For(string sportKey, string abbrev)
    {
        if (string.IsNullOrWhiteSpace(sportKey) || string.IsNullOrWhiteSpace(abbrev))
            return null;

        var map = sportKey.Trim().ToLowerInvariant() switch
        {
            "nfl" => Nfl,
            "mlb" => Mlb,
            _ => null,
        };

        return map is not null && map.TryGetValue(abbrev.Trim(), out var hex) ? hex : null;
    }
}

/// <summary>A team mark's size: an 8px disc beside body text, a 12px one beside a title.</summary>
public enum TeamMarkSize
{
    Small,
    Large,
}
