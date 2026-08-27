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

        return GradeOutcome(
            entry.Market, entry.Outcome, entry.LineTaken,
            homeTeamName, awayTeamName, homeScore, awayScore);
    }

    /// <summary>
    /// The rules themselves, applied to a priced outcome rather than to a recorded entry.
    ///
    /// <para>
    /// A journal entry is one thing that can be graded; a closing line is another. "Did this
    /// team cover" on a game log is the same arithmetic as "did my spread bet win" — the only
    /// difference is where the handicap came from. Keeping the rules here and taking the four
    /// values they actually need means the game log and the journal cannot drift apart, which a
    /// second implementation of the push cases certainly would.
    /// </para>
    ///
    /// <para>
    /// Returns null when the outcome cannot be graded automatically: an unknown market, a side
    /// that maps to neither team, or a handicap market with no line. Null is "no reading", which
    /// is distinct from <see cref="EntryResult.Push"/> — a game with no stored line did not tie
    /// against the number, it never had one.
    /// </para>
    /// </summary>
    public static EntryResult? GradeOutcome(
        string market,
        string outcome,
        decimal? line,
        string homeTeamName,
        string awayTeamName,
        int homeScore,
        int awayScore)
        => market switch
        {
            Markets.Moneyline => GradeMoneyline(outcome, homeTeamName, awayTeamName, homeScore, awayScore),
            Markets.Spread => GradeSpread(outcome, line, homeTeamName, awayTeamName, homeScore, awayScore),
            Markets.Total => GradeTotal(outcome, line, homeScore, awayScore),
            _ => null
        };

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
        string outcome, decimal? lineTaken, string home, string away, int homeScore, int awayScore)
    {
        if (lineTaken is not { } line)
            return null;

        var side = ResolveSide(outcome, home, away);
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

    private static EntryResult? GradeTotal(
        string outcome, decimal? lineTaken, int homeScore, int awayScore)
    {
        if (lineTaken is not { } line)
            return null;

        var total = homeScore + awayScore;
        var isOver = outcome.Equals("over", StringComparison.OrdinalIgnoreCase);
        var isUnder = outcome.Equals("under", StringComparison.OrdinalIgnoreCase);

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
