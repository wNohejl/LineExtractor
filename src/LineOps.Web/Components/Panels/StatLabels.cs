namespace LineOps.Web.Components.Panels;

/// <summary>
/// The name a stat column carries on the desk, from the key the provider files it under.
///
/// Stat lines are per-sport jsonb keyed the way ESPN keys them: <c>C/ATT</c>, <c>CAR</c>,
/// <c>RTG</c>. A key is qualified with its group (<c>passing.YDS</c>, <c>rushing.YDS</c>) only
/// when two groups on the same line reported it, so a prefix always means "there is another
/// column with this measure" and is kept as a word rather than dropped. A key with no entry
/// here reads as its last dotted segment in sentence case; the abbreviations a box score
/// prints in capitals stay in capitals.
/// </summary>
public static class StatLabels
{
    /// <summary>Whole keys whose name is not their segment's: a group changes what they measure.</summary>
    private static readonly Dictionary<string, string> Qualified = new(StringComparer.OrdinalIgnoreCase)
    {
        ["passing.AVG"] = "Yds/Att",
        ["rushing.AVG"] = "Yds/Car",
        ["receiving.AVG"] = "Yds/Rec",
        ["receiving.REC"] = "Receptions",
        ["fumbles.REC"] = "Fum rec",
    };

    /// <summary>A bare key's name.</summary>
    private static readonly Dictionary<string, string> Segments = new(StringComparer.OrdinalIgnoreCase)
    {
        // NFL
        ["C/ATT"] = "Comp/Att",
        ["CAR"] = "Carries",
        ["INT"] = "Int",
        ["LONG"] = "Long",
        ["RTG"] = "Rating",
        ["REC"] = "Receptions",
        ["TGTS"] = "Targets",
        ["YDS"] = "Yds",
        ["FUM"] = "Fumbles",
        ["LOST"] = "Lost",
        ["SACKS"] = "Sacks",
        ["TOT"] = "Tackles",
        ["SOLO"] = "Solo",
        ["PD"] = "Passes defended",
        ["PTS"] = "Points",
        // MLB
        ["AB"] = "At bats",
        ["H"] = "Hits",
        ["R"] = "Runs",
        ["AVG"] = "Avg",
        ["PC-ST"] = "Pitches-strikes",
    };

    /// <summary>Printed in capitals, as a box score prints them.</summary>
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "ERA", "RBI", "HR", "QBR", "SO", "BB", "IP", "OBP", "SLG", "OPS", "TD",
        "ER", "K", "WHIP", "SB", "CS", "LOB", "FG", "XP", "TFL", "GP",
    };

    /// <summary>The word a group's prefix keeps in front of its column's name.</summary>
    private static readonly Dictionary<string, string> GroupWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["passing"] = "Pass",
        ["rushing"] = "Rush",
        ["receiving"] = "Rec",
        ["kicking"] = "Kick",
        ["punting"] = "Punt",
        ["kickReturns"] = "KR",
        ["puntReturns"] = "PR",
        ["defensive"] = "Def",
        ["fumbles"] = "Fum",
        ["interceptions"] = "Int",
        ["fielding"] = "Field",
        ["pitching"] = "Pitch",
    };

    /// <summary>
    /// A pitcher's count of something a batter does reads as what he allowed, which is how a
    /// pitching line names it; batting is the plain reading and keeps no word at all.
    /// </summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "H", "R", "HR", "BB",
    };

    /// <summary>The name for a stat key, as a column title or an average's label.</summary>
    public static string StatLabel(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        if (Qualified.TryGetValue(key, out var whole))
            return whole;

        var dot = key.LastIndexOf('.');
        if (dot < 0)
            return Segment(key);

        var group = key[..key.IndexOf('.')];
        var segment = key[(dot + 1)..];
        var name = Segment(segment);

        if (group.Equals("batting", StringComparison.OrdinalIgnoreCase))
            return name;

        if (group.Equals("pitching", StringComparison.OrdinalIgnoreCase) && Allowed.Contains(segment))
            return $"{name} allowed";

        // Only a known group's word is kept. An unknown group says nothing a reader could
        // use, so the column reads as its last segment.
        return GroupWords.TryGetValue(group, out var word)
            ? $"{word} {Lower(segment, name)}"
            : name;
    }

    private static string Segment(string segment)
    {
        if (Segments.TryGetValue(segment, out var name))
            return name;

        // "FG/ATT" reads "FG/Att": each side of a slash is its own word.
        return string.Join('/', segment.Split('/').Select(Word));
    }

    private static string Word(string part)
    {
        if (part.Length == 0 || Abbreviations.Contains(part))
            return part.ToUpperInvariant();

        return char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant();
    }

    /// <summary>The name in mid-sentence: lower case, unless it starts with an abbreviation.</summary>
    private static string Lower(string segment, string name)
    {
        var first = segment.Split('/')[0];
        return Abbreviations.Contains(first) || name.Length == 0
            ? name
            : char.ToLowerInvariant(name[0]) + name[1..];
    }
}
