using LineOps.Core.Entities;

namespace LineOps.Core.Analytics;

/// <summary>What a parlay's legs add up to: a result and, once there is one, a payout.</summary>
public readonly record struct ParlayOutcome(EntryResult Result, decimal? Payout, string? Reason = null);

/// <summary>
/// Grades a parlay from its legs. Pure, like the rest of the analytics.
///
/// <para>
/// The rules are the ones books use. One losing leg loses the parlay, whatever else happened. A
/// leg that pushes or is voided drops out and the parlay is repriced on what is left; if nothing
/// is left, the stake comes back. Every remaining leg winning is a win.
/// </para>
///
/// <para>
/// The price of a win is the one thing that can be unknowable. A clean sweep pays the book's
/// quoted price where it was logged — books price correlated legs their own way — and otherwise
/// the product of the legs' prices. After a push the quote no longer applies and only the legs'
/// prices can reprice it; where one of those was never logged the parlay is left pending with
/// the reason, for the operator to settle by hand, rather than paid at an invented number.
/// </para>
/// </summary>
public static class ParlayGrading
{
    public static ParlayOutcome Grade(Parlay parlay, IReadOnlyCollection<JournalEntry> legs)
    {
        if (legs.Count == 0)
            return new(EntryResult.Pending, null, "No legs.");

        if (legs.Any(l => l.Result == EntryResult.Loss))
            return new(EntryResult.Loss, 0m);

        if (legs.Any(l => l.Result == EntryResult.Pending))
            return new(EntryResult.Pending, null);

        var winners = legs.Where(l => l.Result == EntryResult.Win).ToList();

        // Every leg pushed or voided: no action, the stake comes back.
        if (winners.Count == 0)
            return new(EntryResult.Void, parlay.Stake);

        var clean = winners.Count == legs.Count;

        if (clean && parlay.PriceQuoted is { } quoted && quoted != 0)
            return new(EntryResult.Win, OddsMath.PayoutOnWin(quoted, parlay.Stake));

        if (winners.Any(l => l.PriceTaken == 0))
            return new(EntryResult.Pending, null,
                clean
                    ? "Won, but neither the parlay's price nor every leg's price was logged."
                    : "Won after a push, and a winning leg's price was not logged to reprice it.");

        var multiplier = winners.Aggregate(1.0, (m, l) => m * OddsMath.ToDecimal(l.PriceTaken));
        return new(EntryResult.Win, Math.Round(parlay.Stake * (decimal)multiplier, 2));
    }

    /// <summary>Applies an outcome to the parlay, stamping when it settled.</summary>
    public static void Apply(Parlay parlay, ParlayOutcome outcome, DateTimeOffset? at = null)
    {
        parlay.Result = outcome.Result;
        parlay.Payout = outcome.Result == EntryResult.Pending ? null : outcome.Payout;
        parlay.SettledAt = outcome.Result == EntryResult.Pending ? null : at ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Every bet once: straight entries as they are, each parlay as one row carrying its stake
    /// and payout, and legs not at all — a leg has no stake, and counting it would count a
    /// parlay's wins several times over. This is what ROI, the bankroll and the breakdowns read.
    /// </summary>
    public static IReadOnlyList<JournalEntry> Ledger(IEnumerable<JournalEntry> entries, IEnumerable<Parlay> parlays)
        => entries
            .Where(e => !e.IsParlayLeg)
            .Concat(parlays.Select(p => new JournalEntry
            {
                Market = "parlay",
                Outcome = $"{p.Legs.Count}-leg parlay",
                Book = p.Book,
                Stake = p.Stake,
                PriceTaken = p.PriceQuoted ?? 0,
                PlacedAt = p.PlacedAt,
                Result = p.Result,
                Payout = p.Payout,
                SettledAt = p.SettledAt,
                // A parlay's sport is its legs' when they agree; a cross-sport parlay has none.
                Game = SharedGame(p)
            }))
            .ToList();

    /// <summary>A leg's game to stand for the parlay's sport, when every leg is in the same sport.</summary>
    private static Game? SharedGame(Parlay parlay)
    {
        var games = parlay.Legs.Select(l => l.Game).OfType<Game>().ToList();
        return games.Count > 0 && games.Select(g => g.SportId).Distinct().Count() == 1 ? games[0] : null;
    }
}
