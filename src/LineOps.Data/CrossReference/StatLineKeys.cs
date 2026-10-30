namespace LineOps.Data.CrossReference;

/// <summary>
/// One name per measure across a player's lines, whatever groups each game happened to have.
///
/// <para>
/// The box-score parser qualifies a key with its group only when a second group on the same
/// line reported it, so the same measure is stored under two names: a back who only ran the
/// ball one week has a bare <c>YDS</c>, and the next week, with a catch, <c>rushing.YDS</c> and
/// <c>receiving.YDS</c>. Read as they are, those are two columns, each half dashes, and the
/// desk's "Rush yds" summed half a season. Lines stored before the parser stopped merging two
/// groups that agreed hold a bare <c>YDS</c> that stands for both.
/// </para>
///
/// <para>
/// So a bare key that more than one NFL group reports is read as qualified by the groups on
/// its own line that report it: every one of them, because a bare key on a line with two such
/// groups is one they agreed on. A key one group alone reports (<c>CAR</c>, <c>C/ATT</c>) stays
/// bare, as stored. A sport whose groups are not these (NHL's forwards and goalies, MLB's
/// unnamed ones) is untouched.
/// </para>
/// </summary>
public static class StatLineKeys
{
    /// <summary>The labels each ESPN NFL box-score group prints.</summary>
    private static readonly Dictionary<string, string[]> GroupLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["passing"] = ["C/ATT", "YDS", "AVG", "TD", "INT", "SACKS", "QBR", "RTG"],
        ["rushing"] = ["CAR", "YDS", "AVG", "TD", "LONG"],
        ["receiving"] = ["REC", "YDS", "AVG", "TD", "LONG", "TGTS"],
        ["fumbles"] = ["FUM", "LOST", "REC"],
        ["defensive"] = ["TOT", "SOLO", "SACKS", "TFL", "PD", "QB HTS", "TD"],
        ["interceptions"] = ["INT", "YDS", "TD"],
        ["kickReturns"] = ["NO", "YDS", "AVG", "LONG", "TD"],
        ["puntReturns"] = ["NO", "YDS", "AVG", "LONG", "TD"],
        ["kicking"] = ["FG", "PCT", "LONG", "XP", "PTS"],
        ["punting"] = ["NO", "YDS", "AVG", "TB", "In 20", "LONG"],
    };

    /// <summary>Labels more than one group prints: the ones a bare key cannot be trusted for.</summary>
    private static readonly HashSet<string> Shared = GroupLabels.Values
        .SelectMany(labels => labels)
        .GroupBy(label => label, StringComparer.OrdinalIgnoreCase)
        .Where(g => g.Count() > 1)
        .Select(g => g.Key)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The line with its shared bare keys qualified; the line itself where there is nothing to do.</summary>
    public static Dictionary<string, string> Canonical(Dictionary<string, string> line)
    {
        if (!line.TryGetValue("category", out var category) || string.IsNullOrWhiteSpace(category))
            return line;

        var groups = category.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(GroupLabels.ContainsKey)
            .ToList();

        if (groups.Count == 0)
            return line;

        foreach (var key in line.Keys.Where(k => !k.Contains('.') && Shared.Contains(k)).ToList())
        {
            var owners = groups.Where(g => GroupLabels[g].Contains(key, StringComparer.OrdinalIgnoreCase)).ToList();
            if (owners.Count == 0)
                continue;

            var value = line[key];
            line.Remove(key);

            foreach (var owner in owners)
                line.TryAdd($"{owner}.{key}", value);
        }

        return line;
    }
}
