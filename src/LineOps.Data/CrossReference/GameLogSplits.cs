namespace LineOps.Data.CrossReference;

/// <summary>
/// Slices a loaded game log by situation.
///
/// <para>
/// These are filters over rows already in memory rather than queries, which is what makes a
/// splits strip worth having: toggling between home and away is a re-render, not a round trip,
/// so an operator can flick through situations at the speed they can read them. Re-querying per
/// toggle would be four queries to answer one question about twenty rows we already hold.
/// </para>
///
/// <para>
/// Pure and static, so every rule is testable without a database — including the ones that are
/// easy to get subtly wrong, like whether "last 5" counts games played or games listed.
/// </para>
/// </summary>
public static class GameLogSplits
{
    /// <summary>The situations a log can be read in. <see cref="All"/> is the unfiltered log.</summary>
    public enum Split
    {
        All,
        Home,
        Away,
        Last5,
        Last10
    }

    public static string Label(Split split) => split switch
    {
        Split.Home => "Home",
        Split.Away => "Away",
        Split.Last5 => "Last 5",
        Split.Last10 => "Last 10",
        _ => "All"
    };

    /// <summary>
    /// Applies a split to a log that is already ordered newest first.
    ///
    /// <para>
    /// "Last 5" counts games <i>played</i>, not rows listed. A window containing scheduled
    /// fixtures would otherwise return a "last 5" made partly of games that have not happened,
    /// which is the one thing a recent-form split must never do.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TeamGameLogRow> Apply(
        IReadOnlyList<TeamGameLogRow> log, Split split) => split switch
        {
            Split.Home => log.Where(r => r.Home).ToList(),
            Split.Away => log.Where(r => !r.Home).ToList(),
            Split.Last5 => Played(log).Take(5).ToList(),
            Split.Last10 => Played(log).Take(10).ToList(),
            _ => log
        };

    /// <summary>Games against one opponent, by name. Separate because it is parameterised.</summary>
    public static IReadOnlyList<TeamGameLogRow> VersusOpponent(
        IReadOnlyList<TeamGameLogRow> log, string opponent)
        => log.Where(r => string.Equals(r.Opponent, opponent, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>
    /// Every opponent faced in the log, in order of how often, for populating a picker.
    /// </summary>
    public static IReadOnlyList<string> Opponents(IReadOnlyList<TeamGameLogRow> log)
        => log.GroupBy(r => r.Opponent, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key)
            .ToList();

    /// <summary>
    /// Record over a set of rows, counting only games that finished.
    ///
    /// Returned as a tuple rather than a type because it has no behaviour and two call sites;
    /// a record here would be ceremony around two integers.
    /// </summary>
    public static (int Wins, int Losses) Record(IReadOnlyList<TeamGameLogRow> rows)
        => (rows.Count(r => r.Won == true), rows.Count(r => r.Won == false));

    /// <summary>
    /// Against-the-spread record. Pushes are counted separately because an ATS record that
    /// folds them into either column misstates it — 8-2-3 is not 8-2 and not 8-5.
    /// </summary>
    public static (int Wins, int Losses, int Pushes) AtsRecord(IReadOnlyList<TeamGameLogRow> rows)
        => (rows.Count(r => r.AtsResult == Core.Entities.EntryResult.Win),
            rows.Count(r => r.AtsResult == Core.Entities.EntryResult.Loss),
            rows.Count(r => r.AtsResult == Core.Entities.EntryResult.Push));

    /// <summary>Over/under record, on the same footing as the ATS one above.</summary>
    public static (int Overs, int Unders, int Pushes) TotalRecord(IReadOnlyList<TeamGameLogRow> rows)
        => (rows.Count(r => r.TotalResult == Core.Entities.EntryResult.Win),
            rows.Count(r => r.TotalResult == Core.Entities.EntryResult.Loss),
            rows.Count(r => r.TotalResult == Core.Entities.EntryResult.Push));

    /// <summary>How many rows carry any market data at all, for a stated coverage note.</summary>
    public static int WithClosingLine(IReadOnlyList<TeamGameLogRow> rows)
        => rows.Count(r => r.HasClosingLine);

    private static IEnumerable<TeamGameLogRow> Played(IReadOnlyList<TeamGameLogRow> log)
        => log.Where(r => r.Status == Core.Entities.GameStatus.Final);
}
