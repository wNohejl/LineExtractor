using LineOps.Core.Entities;

namespace LineOps.Core.Analytics;

/// <summary>Closing line value for a single settled entry.</summary>
/// <param name="PriceTaken">The price recorded when the wager was placed.</param>
/// <param name="ClosingPrice">The last pre-start price for the same book/market/outcome.</param>
/// <param name="PointsGained">
/// On a spread or a total, how many points of line the entry got over the close, signed so that
/// positive is in the bettor's favour: taking -1.5 into a -2.5 close is +1, an over at 8.5 into a
/// 9 close is +0.5. Null for a moneyline, or where either number is unknown.
/// </param>
public readonly record struct ClvResult(int PriceTaken, int ClosingPrice, decimal? PointsGained = null)
{
    /// <summary>
    /// The close was quoted at the number the entry was taken at, so the two prices are a fair
    /// comparison. When the line moved, they are prices for different bets, and the points are
    /// the measure.
    /// </summary>
    public bool SameNumber => PointsGained is null or 0m;

    /// <summary>Probability implied by the price taken.</summary>
    public double TakenProbability => OddsMath.ImpliedProbability(PriceTaken);

    /// <summary>Probability implied by the closing price.</summary>
    public double ClosingProbability => OddsMath.ImpliedProbability(ClosingPrice);

    /// <summary>
    /// Percentage edge over the close: how much more a unit stake returns at the taken
    /// price than at the close. Positive means the price moved in your favour. Only a
    /// like-for-like reading when <see cref="SameNumber"/>; averages should skip the rest.
    /// </summary>
    public double CentsPercent
        => (OddsMath.ToDecimal(PriceTaken) / OddsMath.ToDecimal(ClosingPrice) - 1.0) * 100.0;

    /// <summary>
    /// Beating the close is the standard proxy for skill. Points decide it when the line moved —
    /// a point and a half of spread outweighs any difference in juice on the way — and the price
    /// decides it when it did not.
    /// </summary>
    public bool BeatClose => PointsGained switch
    {
        > 0m => true,
        < 0m => false,
        _ => CentsPercent > 0
    };
}

/// <summary>Aggregate performance across a set of settled entries.</summary>
public readonly record struct PerformanceSummary(
    int SettledCount,
    int Wins,
    int Losses,
    int Pushes,
    decimal TotalStaked,
    decimal NetProfit)
{
    /// <summary>Profit per unit staked. The headline number.</summary>
    public decimal Roi => TotalStaked == 0 ? 0m : NetProfit / TotalStaked;

    /// <summary>Pushes are excluded — they are neither won nor lost.</summary>
    public double WinRate
    {
        get
        {
            var decided = Wins + Losses;
            return decided == 0 ? 0d : Wins / (double)decided;
        }
    }
}

/// <summary>
/// Computes journal performance metrics. Kept pure and dependency-free so the
/// maths is unit-testable without a database.
/// </summary>
public static class PerformanceAnalytics
{
    /// <summary>
    /// CLV for an entry whose close has resolved. Returns null while the entry is unresolved —
    /// for free-text markets with no odds feed, this stays null.
    ///
    /// <para>
    /// Takes the price rather than a row. It used to take an <c>OddsSnapshot</c> and read one
    /// property off it, which meant computing CLV required materialising an odds record —
    /// awkward once scanned odds are deleted after promotion, and unnecessary in the first
    /// place because <see cref="JournalEntry.ClosingPrice"/> is denormalised precisely so the
    /// number outlives the row it came from.
    /// </para>
    /// </summary>
    public static ClvResult? ComputeClv(JournalEntry entry, int? closingPrice)
    {
        if (closingPrice is null || entry.PriceTaken == 0)
            return null;

        return new ClvResult(entry.PriceTaken, closingPrice.Value, PointsGained(entry));
    }

    /// <summary>
    /// Points of line over the close, from the bettor's side. A spread is quoted from the backed
    /// side, so a bigger number is better whichever side it is: +3.5 beats +2.5, -1.5 beats -2.5.
    /// An over wants the total lower than it closed, an under higher.
    /// </summary>
    public static decimal? PointsGained(JournalEntry entry)
    {
        if (entry.LineTaken is not { } taken || entry.ClosingPoints is not { } close)
            return null;

        return entry.Market switch
        {
            Markets.Spread => taken - close,
            Markets.Total when entry.Outcome.Equals("over", StringComparison.OrdinalIgnoreCase) => close - taken,
            Markets.Total when entry.Outcome.Equals("under", StringComparison.OrdinalIgnoreCase) => taken - close,
            _ => null
        };
    }

    /// <summary>CLV from the entry's own denormalised close, which is the usual case.</summary>
    public static ClvResult? ComputeClv(JournalEntry entry)
        => ComputeClv(entry, entry.ClosingPrice);

    /// <summary>
    /// What an entry's CLV was measured against — the honesty of the reading. A close at the same
    /// number at the entry's own book is the clean comparison; a moved line is read in points; a
    /// close borrowed from another book is weaker; and some entries have none.
    /// </summary>
    public static string ClvBasis(JournalEntry entry)
    {
        if (entry.ClosingPrice is null)
            return "No close";

        if (entry.ClosingBook is { } book && !string.Equals(book, entry.Book, StringComparison.OrdinalIgnoreCase))
            return "Another book's close";

        return PointsGained(entry) is { } points && points != 0m ? "Line moved" : "Same number";
    }

    public static PerformanceSummary Summarise(IEnumerable<JournalEntry> entries)
    {
        // Graded, not merely settled: a void returned its stake and is left out of ROI.
        var settled = entries.Where(e => e.IsGraded).ToList();

        return new PerformanceSummary(
            SettledCount: settled.Count,
            Wins: settled.Count(e => e.Result == EntryResult.Win),
            Losses: settled.Count(e => e.Result == EntryResult.Loss),
            Pushes: settled.Count(e => e.Result == EntryResult.Push),
            TotalStaked: settled.Sum(e => e.Stake),
            NetProfit: settled.Sum(e => e.NetReturn));
    }

    /// <summary>
    /// Running bankroll over time, ordered by settlement. Each point is the starting bankroll
    /// plus the cumulative net after that entry — this is what the bankroll curve plots.
    /// </summary>
    public static IReadOnlyList<(DateTimeOffset At, decimal Cumulative)> BankrollCurve(
        IEnumerable<JournalEntry> entries,
        decimal startingBankroll = 0m)
    {
        var points = new List<(DateTimeOffset, decimal)>();
        var running = startingBankroll;

        // By settlement: the bankroll moves when a bet pays, and a Sunday bet placed on Tuesday
        // did not change it on Tuesday. Entries settled before SettledAt existed fall back to
        // when they were placed.
        foreach (var entry in entries.Where(e => e.IsGraded).OrderBy(e => e.SettledAt ?? e.PlacedAt))
        {
            running += entry.NetReturn;
            points.Add((entry.SettledAt ?? entry.PlacedAt, running));
        }

        return points;
    }

    /// <summary>Groups settled entries by an arbitrary key (sport, market, book) for breakdown tables.</summary>
    public static IReadOnlyDictionary<TKey, PerformanceSummary> SummariseBy<TKey>(
        IEnumerable<JournalEntry> entries,
        Func<JournalEntry, TKey> keySelector)
        where TKey : notnull
        => entries
            .Where(e => e.IsGraded)
            .GroupBy(keySelector)
            .ToDictionary(g => g.Key, Summarise);

    /// <summary>
    /// Settles an entry against a final score-derived outcome, computing the payout.
    /// Grading itself (did the pick cover?) lives in the settlement service; this
    /// applies the result consistently.
    /// </summary>
    public static void ApplyResult(JournalEntry entry, EntryResult result, DateTimeOffset? at = null)
    {
        entry.Result = result;
        entry.SettledAt = result == EntryResult.Pending ? null : at ?? DateTimeOffset.UtcNow;
        entry.Payout = result switch
        {
            EntryResult.Win => OddsMath.PayoutOnWin(entry.PriceTaken, entry.Stake),
            EntryResult.Push or EntryResult.Void => entry.Stake,
            EntryResult.Loss => 0m,
            _ => null
        };
    }
}
