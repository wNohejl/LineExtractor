namespace LineOps.Web.Components.Panels;

/// <summary>
/// Which stat columns a table shows, and in what order.
///
/// Columns were the keys reported most often, ties broken alphabetically. Every key on an NFL
/// running back's line is reported every game, so the tie-break chose: CAR, receiving.AVG,
/// receiving.LONG, receiving.YDS, rushing.AVG — and rushing yards, the number a back is read by,
/// fell off the end. A key now has a rank: within a group (passing, rushing, receiving …) the
/// volume, the yards and the touchdowns lead, and the averages and longs come after every
/// group's headline numbers. A sport whose keys carry no rank (MLB) keeps the old order.
/// </summary>
public static class StatColumns
{
    /// <summary>The order groups read in, as a box score lists them.</summary>
    private static readonly string[] Groups =
    [
        "passing", "rushing", "receiving", "defensive", "interceptions", "fumbles",
        "kicking", "punting", "kickReturns", "puntReturns"
    ];

    /// <summary>Bare keys that only one NFL group reports, so they belong to it.</summary>
    private static readonly Dictionary<string, string> BareGroup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C/ATT"] = "passing", ["QBR"] = "passing", ["RTG"] = "passing",
        ["CAR"] = "rushing",
        ["REC"] = "receiving", ["TGTS"] = "receiving",
        ["TOT"] = "defensive", ["SOLO"] = "defensive", ["SACKS"] = "defensive", ["TFL"] = "defensive",
        ["PD"] = "defensive", ["QB HTS"] = "defensive",
        ["FG"] = "kicking", ["XP"] = "kicking",
    };

    /// <summary>A group's headline numbers, in the order they are read.</summary>
    private static readonly string[] Lead = ["C/ATT", "CAR", "REC", "TOT", "FG", "YDS", "TD", "INT", "SACKS", "XP"];

    /// <summary>Read after every group's headline numbers.</summary>
    private static readonly HashSet<string> Secondary = new(StringComparer.OrdinalIgnoreCase)
    {
        "AVG", "LONG", "TGTS", "RTG", "QBR", "SOLO", "TFL", "PD", "QB HTS", "FUM", "LOST", "SACK"
    };

    /// <summary>The unit a group is listed under in a roster.</summary>
    private static readonly Dictionary<string, string> UnitNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["passing"] = "Passing",
        ["rushing"] = "Rushing",
        ["receiving"] = "Receiving",
        ["defensive"] = "Defense",
        ["interceptions"] = "Defense",
        ["fumbles"] = "Defense",
        ["kicking"] = "Special teams",
        ["punting"] = "Special teams",
        ["kickReturns"] = "Special teams",
        ["puntReturns"] = "Special teams",
    };

    private static string Segment(string key)
    {
        var dot = key.IndexOf('.');
        return dot < 0 ? key : key[(dot + 1)..];
    }

    /// <summary>The group a key belongs to: its prefix, or the one group that reports it bare.</summary>
    public static string? GroupOf(string key)
    {
        var dot = key.IndexOf('.');
        if (dot > 0)
        {
            var prefix = key[..dot];
            return Array.Exists(Groups, g => string.Equals(g, prefix, StringComparison.OrdinalIgnoreCase)) ? prefix : null;
        }

        return BareGroup.GetValueOrDefault(key);
    }

    /// <summary>Lower reads first. A key the table knows nothing of ranks in the middle, so it keeps its place.</summary>
    public static int Rank(string key)
    {
        var segment = Segment(key);
        var lead = Array.FindIndex(Lead, l => string.Equals(l, segment, StringComparison.OrdinalIgnoreCase));
        var group = GroupOf(key) is { } g ? Array.FindIndex(Groups, x => string.Equals(x, g, StringComparison.OrdinalIgnoreCase)) : -1;

        if (lead < 0 && group < 0 && !Secondary.Contains(segment))
            return 5000;

        // A bare YDS or TD on a one-group line is that player's headline: it reads with group 0.
        var groupRank = Math.Max(group, 0) * 100;
        var leadRank = lead < 0 ? 50 : lead;

        return (Secondary.Contains(segment) ? 10000 : 0) + groupRank + leadRank;
    }

    /// <summary>
    /// The columns for a set of lines: keys filled on at least half of them first, so a column
    /// is mostly numbers rather than dashes; then by rank, by how often they were reported, and
    /// by name.
    /// </summary>
    public static List<string> Pick(IEnumerable<IEnumerable<string>> lines, int max)
    {
        var counts = lines
            .SelectMany(l => l.Distinct(StringComparer.OrdinalIgnoreCase))
            .Where(k => !string.Equals(k, "category", StringComparison.OrdinalIgnoreCase))
            .GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Key: g.Key, Count: g.Count()))
            .ToList();

        if (counts.Count == 0)
            return [];

        var most = counts.Max(c => c.Count);

        return counts
            .OrderByDescending(c => c.Count * 2 >= most)
            .ThenBy(c => Rank(c.Key))
            .ThenByDescending(c => c.Count)
            .ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
            .Select(c => c.Key)
            .Take(max)
            .ToList();
    }

    /// <summary>
    /// The unit a player's totals put them in — the group they gained the most yards in, else
    /// the one most of their keys belong to, ties to the one a box score lists first — or null
    /// where the sport names no groups. Yards first: a back's line carries as many receiving
    /// keys as rushing ones, and counting keys filed the back under receiving.
    /// </summary>
    public static string? UnitOf(IReadOnlyDictionary<string, double> totals)
    {
        var group = totals
            .Select(t => (Group: GroupOf(t.Key), t.Key, t.Value))
            .Where(t => t.Group is not null)
            .GroupBy(t => t.Group!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Where(t => string.Equals(Segment(t.Key), "YDS", StringComparison.OrdinalIgnoreCase)).Sum(t => t.Value))
            .ThenByDescending(g => g.Count())
            .ThenBy(g => Array.FindIndex(Groups, x => string.Equals(x, g.Key, StringComparison.OrdinalIgnoreCase)))
            .Select(g => g.Key)
            .FirstOrDefault();

        return group is null ? null : UnitNames.GetValueOrDefault(group);
    }

    /// <summary>Units in the order a roster lists them.</summary>
    public static int UnitOrder(string? unit) => unit switch
    {
        "Passing" => 0,
        "Rushing" => 1,
        "Receiving" => 2,
        "Defense" => 3,
        "Special teams" => 4,
        _ => 5
    };
}
