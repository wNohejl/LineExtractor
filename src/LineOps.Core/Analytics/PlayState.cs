using System.Text.Json.Nodes;
using LineOps.Core.Entities;

namespace LineOps.Core.Analytics;

/// <summary>
/// Where a game in play stands, in the words a title uses: the period and the clock
/// (<c>Q3 4:12</c>), the situation (<c>CHI ball, 2nd &amp; 6 at NE 34</c>, or baseball's
/// <c>2 out · 1-2 · runners on 1st, 3rd</c>) and the last play.
///
/// <para>
/// With only a score to move, a live game's window could only jump — 0, 7, 7, 10 — and read as
/// "blocky" between the jumps (spec 2026-10-24 §3). The state of play changes every play even
/// when the score does not, and this is how it is said. Every reading is null rather than a
/// guess when ESPN did not send the field, and null for a game that is not in play: once it is
/// over the scoreline is the whole story.
/// </para>
/// </summary>
public static class PlayState
{
    /// <summary>
    /// The period and clock as one tag's worth: <c>Q3 4:12</c>, <c>OT 7:41</c>, <c>R2 3:14</c>.
    ///
    /// <para>
    /// ESPN's <c>shortDetail</c> is the fallback rather than the answer for football and fights,
    /// because while the clock runs it reads <c>8:26 - 2nd</c> — the time before the quarter,
    /// and the quarter as an ordinal — where a scoreboard reads the quarter first. When the detail
    /// does not carry the clock it is saying something the clock cannot — <c>Halftime</c>,
    /// <c>End of 3rd</c> — and is used as written. Baseball has no clock, and ESPN's
    /// <c>Top 7th</c> is already how a box score says it.
    /// </para>
    /// </summary>
    public static string? Clock(Game game)
    {
        if (game.Status != GameStatus.Live)
            return null;

        var detail = Blank(game.StatusDetail);
        var clock = Blank(game.DisplayClock);
        var sport = game.Sport?.Key;

        if (sport == "mlb")
            return detail ?? (game.Period is { } inning ? $"Inning {inning}" : null);

        var marked = game.Period is { } period && clock is not null && PeriodMark(sport, period) is { } mark
            ? $"{mark} {clock}"
            : null;

        if (detail is not null && (clock is null || !detail.Contains(clock, StringComparison.Ordinal)))
            return detail;

        return marked ?? detail;
    }

    /// <summary>"Q3", "OT", "2OT"; a fight's "R2". Null for a sport with no mark of its own.</summary>
    private static string? PeriodMark(string? sport, int period)
        => FightSports.IsFight(sport) ? $"R{period}"
            : sport is "nfl" or "ncaaf" ? period switch
            {
                <= 4 => $"Q{period}",
                5 => "OT",
                _ => $"{period - 4}OT"
            }
            : null;

    /// <summary>
    /// The situation as one line. Football: the side with the ball by its mark, then the down and
    /// the spot as ESPN words it — <c>CHI ball, 2nd &amp; 6 at NE 34</c>; just <c>CHI ball</c>
    /// between plays that have no down (a kickoff). Baseball: outs, the count, the runners. A
    /// fight has no situation. Null when there is nothing to say.
    /// </summary>
    public static string? Situation(Game game)
    {
        if (game.Status != GameStatus.Live || FightSports.IsFight(game.Sport?.Key))
            return null;

        return game.Sport?.Key == "mlb" ? Diamond(game.Situation) : Possession(game);
    }

    private static string? Possession(Game game)
    {
        var side = game.PossessionTeamId is not { } id ? null
            : id == game.HomeTeamId ? Scoreline.Abbrev(game.HomeTeam, "HOME")
            : id == game.AwayTeamId ? Scoreline.Abbrev(game.AwayTeam, "AWAY")
            : null;

        if (side is null)
            return null;

        return Blank(game.DownDistanceText) is { } spot ? $"{side} ball, {spot}" : $"{side} ball";
    }

    /// <summary>"2 out · 1-2 · runners on 1st, 3rd" from the situation bag; null when unreadable or empty.</summary>
    private static string? Diamond(string? json)
    {
        if (Blank(json) is not { } text)
            return null;

        JsonObject? bag;
        try
        {
            bag = JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        if (bag is null)
            return null;

        var parts = new List<string>(3);

        if (Int(bag, "outs") is { } outs)
            parts.Add($"{outs} out");

        if (Int(bag, "balls") is { } balls && Int(bag, "strikes") is { } strikes)
            parts.Add($"{balls}-{strikes}");

        var bases = new[] { ("onFirst", "1st"), ("onSecond", "2nd"), ("onThird", "3rd") };
        if (bases.Any(b => bag.ContainsKey(b.Item1)))
        {
            var on = bases.Where(b => Bool(bag, b.Item1) == true).Select(b => b.Item2).ToList();
            parts.Add(on.Count switch
            {
                0 => "bases empty",
                1 => $"runner on {on[0]}",
                3 => "bases loaded",
                _ => $"runners on {string.Join(", ", on)}"
            });
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>
    /// ESPN's text for the last play, cut at a word with an ellipsis past <paramref name="max"/>
    /// characters: a title line holds one sentence, and a penalty's full enforcement is three.
    /// </summary>
    public static string? LastPlay(Game game, int max = 90)
    {
        if (game.Status != GameStatus.Live || Blank(game.LastPlay) is not { } play)
            return null;

        if (play.Length <= max)
            return play;

        var cut = play.LastIndexOf(' ', max);
        return play[..(cut > max / 2 ? cut : max)].TrimEnd(' ', ',', ';', '-') + "…";
    }

    private static int? Int(JsonObject bag, string key)
        => bag[key] is JsonValue v && v.TryGetValue<int>(out var n) ? n : null;

    private static bool? Bool(JsonObject bag, string key)
        => bag[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
