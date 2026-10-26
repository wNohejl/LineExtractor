using System.Globalization;
using System.Text;
using LineOps.Core.Entities;

namespace LineOps.Core.Analytics;

/// <summary>
/// What is different about a sport that is fought rather than played (ADR 0021).
///
/// <para>
/// A fight is stored as a game between two fighters, so almost nothing needs to know it is one.
/// The few things that do — how long a fight lasted, how its result is written, how a rounds total
/// is graded — ask here, so the rule lives in one place rather than as a string compared in each.
/// </para>
/// </summary>
public static class FightSports
{
    /// <summary>
    /// The fought sports the desk holds, by sport key. The UFC only; other promotions are not
    /// stored. An array, so a query can ask "is this game's sport one of them" in SQL.
    /// </summary>
    public static readonly string[] Keys = ["ufc"];

    public static bool IsFight(string? sportKey)
        => sportKey is not null && Keys.Contains(sportKey.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>A professional MMA round: five minutes.</summary>
    public const int RoundSeconds = 300;

    /// <summary>
    /// How many rounds the fight lasted, as a rounds total is graded: a stoppage at 3:14 of the
    /// first is 0.65, one at 2:30 of the second is 1.5, and a fight that went three full rounds is
    /// 3. Null until the fight has ended with a round and a clock.
    /// </summary>
    public static decimal? ElapsedRounds(Bout bout)
        => bout is { EndRound: { } round, EndSeconds: { } seconds } && round > 0
            ? round - 1 + Math.Clamp(seconds, 0, RoundSeconds) / (decimal)RoundSeconds
            : null;

    /// <summary>Whether the fight went the scheduled distance — every round, to the bell.</summary>
    public static bool WentTheDistance(Bout bout)
        => bout.EndRound == bout.ScheduledRounds && bout.EndSeconds >= RoundSeconds;

    /// <summary>
    /// A card as a reader tells it from the rest: "UFC 300", "Noche UFC" — but a Fight Night by its
    /// billing, "UFC Fight Night: Allen vs. Curtis 2", since every one shares the short name.
    /// </summary>
    public static string CardName(Bout bout)
        => bout.EventShortName is { Length: > 0 } shortName
           && !shortName.Equals("UFC Fight Night", StringComparison.OrdinalIgnoreCase)
            ? shortName
            : bout.EventName;

    /// <summary>A clock as it is written in a fight: 194 seconds is 3:14.</summary>
    public static string Clock(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

    /// <summary>
    /// The method in the few letters a dense row has room for: KO/TKO, SUB, U-DEC, S-DEC, M-DEC,
    /// DQ, DRAW, NC. Null when the fight has no result yet.
    /// </summary>
    public static string? MethodShort(Bout bout)
    {
        if (bout.Outcome is null)
            return null;

        if (bout.Outcome == BoutOutcome.NoContest)
            return "NC";

        var method = bout.Method ?? string.Empty;

        if (bout.Outcome == BoutOutcome.Draw)
            return "DRAW";

        if (method.Contains("KO", StringComparison.OrdinalIgnoreCase))
            return "KO/TKO";

        if (method.Contains("Sub", StringComparison.OrdinalIgnoreCase))
            return "SUB";

        if (method.Contains("Disq", StringComparison.OrdinalIgnoreCase) || method.Equals("DQ", StringComparison.OrdinalIgnoreCase))
            return "DQ";

        if (method.Contains("Decision", StringComparison.OrdinalIgnoreCase))
        {
            if (method.Contains("Unanimous", StringComparison.OrdinalIgnoreCase)) return "U-DEC";
            if (method.Contains("Split", StringComparison.OrdinalIgnoreCase)) return "S-DEC";
            if (method.Contains("Majority", StringComparison.OrdinalIgnoreCase)) return "M-DEC";
            return "DEC";
        }

        return method.Length == 0 ? null : method.ToUpperInvariant();
    }

    /// <summary>
    /// Where the fight ended, "R1 3:14", or null without a round. A decision says the round it
    /// went to, which is the distance.
    /// </summary>
    public static string? Ended(Bout bout)
        => bout.EndRound is { } round
            ? bout.EndSeconds is { } seconds ? $"R{round} {Clock(seconds)}" : $"R{round}"
            : null;

    /// <summary>
    /// The result as a sentence: "Pereira by KO/TKO (Punch), R1 3:14", "Draw (Majority), R3 5:00",
    /// "No contest, R1 2:10". Null until there is one.
    /// </summary>
    public static string? ResultLine(Game game, Bout bout)
    {
        if (bout.Outcome is not { } outcome)
            return null;

        var ended = Ended(bout) is { } at ? $", {at}" : string.Empty;

        if (outcome == BoutOutcome.NoContest)
            return $"No contest{Detail(bout)}{ended}";

        if (outcome == BoutOutcome.Draw)
            return $"Draw{(bout.Method is { Length: > 0 } m && m.Contains('-') ? $" ({m[(m.IndexOf('-') + 1)..].Trim()})" : string.Empty)}{ended}";

        var winner = Winner(game);
        var name = winner is null ? "Won" : TeamNames.Short(winner, winner.Name);

        return $"{name} by {bout.Method ?? "decision"}{Detail(bout)}{ended}";
    }

    private static string Detail(Bout bout)
        => bout.MethodDetail is { Length: > 0 } detail ? $" ({detail})" : string.Empty;

    /// <summary>The fighter who won, or null for a draw, a no contest or an unfinished fight.</summary>
    public static Team? Winner(Game game)
        => game.Status == GameStatus.Final && game.HomeScore is { } home && game.AwayScore is { } away && home != away
            ? home > away ? game.HomeTeam : game.AwayTeam
            : null;
}

/// <summary>
/// Whether two spellings of a fighter's name are the same person.
///
/// <para>
/// A book and ESPN write fighters differently far more often than they write clubs differently:
/// accents ("Jiří Procházka" against "Jiri Prochazka"), suffixes ("Khalil Rountree Jr."), and
/// fighters who changed what they are called. Folding accents and suffixes settles most; the
/// surname with the first initial settles a dropped middle name. What it cannot settle — a
/// fighter billed under a new first name — is left to the bout matcher, which needs only one
/// corner to agree in full when the other agrees on the surname.
/// </para>
/// </summary>
public static class FighterNames
{
    private static readonly HashSet<string> Suffixes = new(StringComparer.Ordinal) { "jr", "sr", "ii", "iii", "iv" };

    /// <summary>Letters that do not decompose into a base letter and a mark.</summary>
    private static readonly Dictionary<char, string> Unfolded = new()
    {
        ['ł'] = "l", ['ø'] = "o", ['đ'] = "d", ['ß'] = "ss", ['æ'] = "ae", ['œ'] = "oe", ['ı'] = "i", ['þ'] = "th"
    };

    /// <summary>
    /// The name reduced to what identifies it: lower case, accents and punctuation gone, a
    /// trailing qualifier in brackets dropped ("Bruno Silva (Flyweight)" is "bruno silva"), and
    /// generational suffixes removed.
    /// </summary>
    public static string Fold(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var text = name.Trim();

        var bracket = text.IndexOf('(');
        if (bracket > 0)
            text = text[..bracket];

        var builder = new StringBuilder(text.Length);

        foreach (var c in text.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            if (Unfolded.TryGetValue(c, out var replacement))
                builder.Append(replacement);
            else if (char.IsLetterOrDigit(c))
                builder.Append(c);
            else if (char.IsWhiteSpace(c) || c is '-' or '\'')
                builder.Append(c == '\'' ? "" : " ");
        }

        var words = builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Suffixes.Contains(w))
            .ToList();

        return string.Join(' ', words);
    }

    /// <summary>The same person, by full folded name or by surname and first initial.</summary>
    public static bool Same(string? a, string? b)
    {
        var x = Fold(a);
        var y = Fold(b);

        if (x.Length == 0 || y.Length == 0)
            return false;

        if (x == y || x.Replace(" ", "") == y.Replace(" ", ""))
            return true;

        return Surname(x) == Surname(y) && x[0] == y[0];
    }

    /// <summary>Only the surname agrees — the weaker reading the bout matcher accepts for one corner.</summary>
    public static bool SameSurname(string? a, string? b)
    {
        var x = Fold(a);
        var y = Fold(b);

        return x.Length > 0 && y.Length > 0 && Surname(x) == Surname(y);
    }

    private static string Surname(string folded)
    {
        var space = folded.LastIndexOf(' ');
        return space < 0 ? folded : folded[(space + 1)..];
    }
}
