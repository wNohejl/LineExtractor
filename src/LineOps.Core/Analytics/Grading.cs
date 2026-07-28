using LineOps.Core.Entities;

namespace LineOps.Core.Analytics;

/// <summary>
/// Decides whether a recorded entry won, lost or pushed against a final score.
///
/// Pure and side-effect free so every rule — including the push cases that are easy to get
/// wrong — is unit-testable without a database or a live game.
/// </summary>
public static class Grading
{
    /// <summary>
    /// Grades an entry against a final score. Returns null when the entry cannot be graded
    /// automatically: free-text markets, missing scores, or an outcome we cannot map to a side.
    /// Those stay pending for the user to settle by hand rather than being guessed at.
    /// </summary>
    public static EntryResult? Grade(
        JournalEntry entry,
        string homeTeamName,
        string awayTeamName,
        int homeScore,
        int awayScore)
    {
        if (!string.IsNullOrWhiteSpace(entry.FreeTextMarket))
            return null;

        return entry.Market switch
        {
            Markets.Moneyline => GradeMoneyline(entry.Outcome, homeTeamName, awayTeamName, homeScore, awayScore),
            Markets.Spread => GradeSpread(entry, homeTeamName, awayTeamName, homeScore, awayScore),
            Markets.Total => GradeTotal(entry, homeScore, awayScore),
            _ => null
        };
    }

    private static EntryResult? GradeMoneyline(
        string outcome, string home, string away, int homeScore, int awayScore)
    {
        var side = ResolveSide(outcome, home, away);
        if (side is null)
            return null;

        if (homeScore == awayScore)
            return EntryResult.Push;

        var homeWon = homeScore > awayScore;
        return side.Value == Side.Home
            ? (homeWon ? EntryResult.Win : EntryResult.Loss)
            : (homeWon ? EntryResult.Loss : EntryResult.Win);
    }

    private static EntryResult? GradeSpread(
        JournalEntry entry, string home, string away, int homeScore, int awayScore)
    {
        if (entry.LineTaken is not { } line)
            return null;

        var side = ResolveSide(entry.Outcome, home, away);
        if (side is null)
            return null;

        // Margin from the perspective of the side backed, plus the handicap taken.
        var margin = side.Value == Side.Home ? homeScore - awayScore : awayScore - homeScore;
        var adjusted = margin + line;

        return adjusted switch
        {
            > 0 => EntryResult.Win,
            < 0 => EntryResult.Loss,
            // Only whole-number lines can land exactly on the number.
            _ => EntryResult.Push
        };
    }

    private static EntryResult? GradeTotal(JournalEntry entry, int homeScore, int awayScore)
    {
        if (entry.LineTaken is not { } line)
            return null;

        var total = homeScore + awayScore;
        var isOver = entry.Outcome.Equals("over", StringComparison.OrdinalIgnoreCase);
        var isUnder = entry.Outcome.Equals("under", StringComparison.OrdinalIgnoreCase);

        if (!isOver && !isUnder)
            return null;

        if (total == line)
            return EntryResult.Push;

        var wentOver = total > line;
        return isOver
            ? (wentOver ? EntryResult.Win : EntryResult.Loss)
            : (wentOver ? EntryResult.Loss : EntryResult.Win);
    }

    private enum Side { Home, Away }

    /// <summary>
    /// Maps a recorded outcome label onto a side. Books and feeds spell team names
    /// inconsistently, so matching is case- and punctuation-insensitive.
    /// </summary>
    private static Side? ResolveSide(string outcome, string home, string away)
    {
        if (Normalise(outcome) == Normalise(home))
            return Side.Home;

        if (Normalise(outcome) == Normalise(away))
            return Side.Away;

        return outcome.ToLowerInvariant() switch
        {
            "home" => Side.Home,
            "away" => Side.Away,
            _ => null
        };
    }

    private static string Normalise(string value)
        => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
