using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XLines.Core.Analytics;

/// <summary>
/// What a period is called, by sport: a quarter, an inning, a round. One place, because a title's
/// tag, a play's stamp, a line score's column and a drive log's heading all name the same period
/// and had each begun to word it for themselves.
///
/// <para>
/// Football and basketball play four quarters and then overtimes; baseball plays innings, as many
/// as it takes; a fight is rounds. A sport this does not know is numbered and called a period,
/// which is true of every one of them.
/// </para>
/// </summary>
public static class Periods
{
    /// <summary>The short mark a clock is read beside: "Q3", "OT", "2OT", a fight's "R2". Null where a sport has none (an inning is said in words).</summary>
    public static string? Mark(string? sport, int period)
        => FightSports.IsFight(sport) ? $"R{period}"
            : Quarters(sport) ? period switch
            {
                <= 4 => $"Q{period}",
                5 => "OT",
                _ => $"{period - 4}OT"
            }
            : null;

    /// <summary>A line score's column heading: 1 to 4 then "OT", "2OT" for a game of quarters; the number otherwise.</summary>
    public static string Column(string? sport, int period)
        => Quarters(sport) && period > 4
            ? period == 5 ? "OT" : $"{period - 4}OT"
            : period.ToString(CultureInfo.InvariantCulture);

    /// <summary>The period in words, as a heading: "1st quarter", "Overtime", "2nd overtime", "7th inning", "Round 2".</summary>
    public static string Name(string? sport, int period)
    {
        if (FightSports.IsFight(sport))
            return $"Round {period}";

        if (sport == "mlb")
            return $"{Ordinal(period)} inning";

        if (!Quarters(sport))
            return $"{Ordinal(period)} period";

        return period switch
        {
            <= 4 => $"{Ordinal(period)} quarter",
            5 => "Overtime",
            _ => $"{Ordinal(period - 4)} overtime"
        };
    }

    /// <summary>What a line score calls its last column: runs in baseball, the total anywhere else.</summary>
    public static string TotalColumn(string? sport) => sport == "mlb" ? "R" : "T";

    /// <summary>1st, 2nd, 3rd, 4th … 11th, 12th, 13th … 21st.</summary>
    public static string Ordinal(int n)
    {
        var suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th"
        };

        return n.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    private static bool Quarters(string? sport) => sport is "nfl" or "ncaaf" or "nba";
}

/// <summary>
/// A game's points by period, read from the jsonb the feed stores (<c>{"home":[..],"away":[..]}</c>,
/// <see cref="Entities.Game.Linescore"/>). One reader, so the Gamecast and the Game window cannot
/// come to disagree about a line score with a hole in it.
/// </summary>
public static class Linescores
{
    /// <summary>
    /// Points by period for each side, first period first. A side whose list is missing or holds
    /// anything but whole numbers is empty: a linescore with a hole in it would put the third
    /// quarter's points under the second, which is the adapter's rule too.
    /// </summary>
    public static (IReadOnlyList<int> Home, IReadOnlyList<int> Away) Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ([], []);

        try
        {
            return JsonNode.Parse(json) is JsonObject o ? (Side(o["home"]), Side(o["away"])) : ([], []);
        }
        catch (JsonException)
        {
            return ([], []);
        }

        static IReadOnlyList<int> Side(JsonNode? node)
        {
            if (node is not JsonArray periods)
                return [];

            var points = new List<int>(periods.Count);
            foreach (var period in periods)
            {
                if (period is not JsonValue value || !value.TryGetValue<int>(out var n))
                    return [];
                points.Add(n);
            }

            return points;
        }
    }
}
