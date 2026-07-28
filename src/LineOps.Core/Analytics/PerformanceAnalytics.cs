using LineOps.Core.Entities;

namespace LineOps.Core.Analytics;

/// <summary>Closing line value for a single settled entry.</summary>
/// <param name="PriceTaken">The price recorded when the wager was placed.</param>
/// <param name="ClosingPrice">The last pre-start price for the same book/market/outcome.</param>
public readonly record struct ClvResult(int PriceTaken, int ClosingPrice)
{
    /// <summary>Probability implied by the price taken.</summary>
    public double TakenProbability => OddsMath.ImpliedProbability(PriceTaken);

    /// <summary>Probability implied by the closing price.</summary>
    public double ClosingProbability => OddsMath.ImpliedProbability(ClosingPrice);

    /// <summary>
    /// Percentage edge over the close: how much more a unit stake returns at the taken
    /// price than at the close. Positive means the line moved in your favour.
    /// </summary>
    public double CentsPercent
        => (OddsMath.ToDecimal(PriceTaken) / OddsMath.ToDecimal(ClosingPrice) - 1.0) * 100.0;

    /// <summary>Beating the close is the standard proxy for skill.</summary>
    public bool BeatClose => CentsPercent > 0;
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

        return new ClvResult(entry.PriceTaken, closingPrice.Value);
    }

    /// <summary>CLV from the entry's own denormalised close, which is the usual case.</summary>
    public static ClvResult? ComputeClv(JournalEntry entry)
        => ComputeClv(entry, entry.ClosingPrice);

    public static PerformanceSummary Summarise(IEnumerable<JournalEntry> entries)
    {
        var settled = entries.Where(e => e.IsSettled).ToList();

        return new PerformanceSummary(
            SettledCount: settled.Count,
            Wins: settled.Count(e => e.Result == EntryResult.Win),
            Losses: settled.Count(e => e.Result == EntryResult.Loss),
            Pushes: settled.Count(e => e.Result == EntryResult.Push),
            TotalStaked: settled.Sum(e => e.Stake),
            NetProfit: settled.Sum(e => e.NetReturn));
    }

    /// <summary>
    /// Running bankroll over time, ordered by settlement. Each point is the cumulative
    /// net profit after that entry — this is what the bankroll curve plots.
    /// </summary>
    public static IReadOnlyList<(DateTimeOffset At, decimal Cumulative)> BankrollCurve(
        IEnumerable<JournalEntry> entries,
        decimal startingBankroll = 0m)
    {
        var points = new List<(DateTimeOffset, decimal)>();
        var running = startingBankroll;

        foreach (var entry in entries.Where(e => e.IsSettled).OrderBy(e => e.PlacedAt))
        {
            running += entry.NetReturn;
            points.Add((entry.PlacedAt, running));
        }

        return points;
    }

    /// <summary>Groups settled entries by an arbitrary key (sport, market, book) for breakdown tables.</summary>
    public static IReadOnlyDictionary<TKey, PerformanceSummary> SummariseBy<TKey>(
        IEnumerable<JournalEntry> entries,
        Func<JournalEntry, TKey> keySelector)
        where TKey : notnull
        => entries
            .Where(e => e.IsSettled)
            .GroupBy(keySelector)
            .ToDictionary(g => g.Key, Summarise);

    /// <summary>
    /// Settles an entry against a final score-derived outcome, computing the payout.
    /// Grading itself (did the pick cover?) lives in the settlement service; this
    /// applies the result consistently.
    /// </summary>
    public static void ApplyResult(JournalEntry entry, EntryResult result)
    {
        entry.Result = result;
        entry.Payout = result switch
        {
            EntryResult.Win => OddsMath.PayoutOnWin(entry.PriceTaken, entry.Stake),
            EntryResult.Push or EntryResult.Void => entry.Stake,
            EntryResult.Loss => 0m,
            _ => null
        };
    }
}
