using LineOps.Core.Entities;

namespace LineOps.Core.Analytics;

/// <summary>
/// What a price is worth, as against what it pays.
///
/// <para>
/// The board already finds the best price, but the best of four bad prices is still a bad
/// price. Whether a number is <i>good</i> needs a fair price to hold it against: the market's
/// probability with the book's margin taken out. Pinnacle is the reference where it quotes the
/// market — the sharpest number and the lowest margin, the book the others are judged against
/// (see the note on <c>TheOddsApiAdapter</c>'s book list). Where it does not, the books that
/// do quote both sides are averaged, and it takes two of them: a single book judged against
/// itself is always exactly its own margin short of fair, which says nothing.
/// </para>
///
/// <para>
/// A fair price belongs to a number. Pinnacle's -1.5 says nothing about what -2.5 is worth —
/// that needs a model of how often games land on 2, and a guessed one would be a fabricated
/// reading (the same line Phase 2 drew for closing-line value). So an offer on a number the
/// reference did not price has no fair price and no expected value, rather than an invented one.
/// </para>
/// </summary>
public static class FairValue
{
    /// <summary>The book whose de-margined price is the fair price, where it quotes the market.</summary>
    public const string SharpBook = "pinnacle";

    /// <summary>The basis of a fair price averaged over the books quoting the market.</summary>
    public const string Consensus = "consensus";

    /// <summary>How many books an average needs before it means more than one book's margin.</summary>
    public const int ConsensusMinimumBooks = 2;

    /// <summary>
    /// A book's margin on a two-way market: how far its pair of implied probabilities sums past
    /// certainty. -110 both ways is 4.76%.
    /// </summary>
    public static double Hold(int firstAmerican, int secondAmerican)
        => OddsMath.ImpliedProbability(firstAmerican) + OddsMath.ImpliedProbability(secondAmerican) - 1;

    /// <summary>
    /// Expected return per unit staked at this price, if the fair probability is right.
    /// 0.05 is five cents on the dollar; a negative number is what the bet costs.
    /// </summary>
    public static double Ev(double fairProbability, int american)
        => fairProbability * OddsMath.ToDecimal(american) - 1;
}

/// <summary>One book's price on one side of a market.</summary>
public readonly record struct SidePrice(string Book, string Outcome, decimal? Line, int PriceAmerican);

/// <summary>
/// A side's fair price.
/// </summary>
/// <param name="Probability">The de-margined chance of the side.</param>
/// <param name="PriceAmerican">That chance written as a price: what an honest book would pay.</param>
/// <param name="Basis"><see cref="FairValue.SharpBook"/> or <see cref="FairValue.Consensus"/>.</param>
/// <param name="Books">How many books' pairs it was read from — one where it is Pinnacle's.</param>
public sealed record FairPrice(double Probability, int PriceAmerican, string Basis, int Books);

/// <summary>
/// One market, both sides, every book — paired up so each book's two prices on the same bet
/// can be read together.
///
/// <para>
/// Pairing is where markets differ. A moneyline pairs the two teams; a total pairs over and
/// under on the same number; a handicap pairs a side's line with its mirror, home -1.5 with
/// away +1.5. Every pair is keyed by the first side's line, so a question about either side on
/// any number lands on the same pair.
/// </para>
/// </summary>
public sealed class FairMarket
{
    private readonly string _market;
    private readonly string _first;
    private readonly string _second;

    /// <summary>Every book's complete pair, keyed by the first side's line.</summary>
    private readonly Dictionary<LineKey, List<Pair>> _pairs;

    private readonly record struct LineKey(decimal? Line);

    private readonly record struct Pair(string Book, int First, int Second);

    private FairMarket(string market, string first, string second, Dictionary<LineKey, List<Pair>> pairs)
    {
        _market = market;
        _first = first;
        _second = second;
        _pairs = pairs;
    }

    /// <summary>Pairs up a market's prices.</summary>
    /// <param name="market">See <see cref="Markets"/>.</param>
    /// <param name="firstOutcome">Home on a team market, "over" on a total.</param>
    /// <param name="secondOutcome">Away, or "under".</param>
    /// <param name="prices">Each book's current price on either side; other outcomes are ignored.</param>
    public static FairMarket Build(string market, string firstOutcome, string secondOutcome, IEnumerable<SidePrice> prices)
    {
        var all = prices.ToList();
        var pairs = new Dictionary<LineKey, List<Pair>>();

        foreach (var first in all.Where(p => Same(p.Outcome, firstOutcome)))
        {
            var partner = Mirror(market, first.Line);
            var second = all.FirstOrDefault(p =>
                Same(p.Book, first.Book) && Same(p.Outcome, secondOutcome) && p.Line == partner);

            if (second.Book is null)
                continue;

            var key = new LineKey(first.Line);
            if (!pairs.TryGetValue(key, out var list))
                pairs[key] = list = [];

            // One pair per book per number: a feed that repeats a book keeps its first.
            if (!list.Any(p => Same(p.Book, first.Book)))
                list.Add(new Pair(first.Book, first.PriceAmerican, second.PriceAmerican));
        }

        return new FairMarket(market, firstOutcome, secondOutcome, pairs);
    }

    /// <summary>The fair price of one side on one number, or null when there is none to be had.</summary>
    public FairPrice? For(string outcome, decimal? line)
    {
        if (!TryKey(outcome, line, out var key, out var isFirst) || !_pairs.TryGetValue(key, out var pairs))
            return null;

        double firstProbability;
        string basis;
        int books;

        var sharp = pairs.FirstOrDefault(p => Same(p.Book, FairValue.SharpBook));
        if (sharp.Book is not null)
        {
            firstProbability = OddsMath.NoVigProbability(sharp.First, sharp.Second);
            basis = FairValue.SharpBook;
            books = 1;
        }
        else if (pairs.Count >= FairValue.ConsensusMinimumBooks)
        {
            firstProbability = pairs.Average(p => OddsMath.NoVigProbability(p.First, p.Second));
            basis = FairValue.Consensus;
            books = pairs.Count;
        }
        else
        {
            return null;
        }

        var probability = isFirst ? firstProbability : 1 - firstProbability;

        // A certainty has no price. No real two-way market reaches it, but a malformed quote
        // should read as "no fair price" rather than throw on the way to the board.
        if (probability is <= 0 or >= 1)
            return null;

        return new FairPrice(probability, OddsMath.FromDecimal(1 / probability), basis, books);
    }

    /// <summary>Expected value of taking this price on this side and number, or null with no fair price.</summary>
    public double? Ev(string outcome, decimal? line, int american)
        => For(outcome, line) is { } fair ? FairValue.Ev(fair.Probability, american) : null;

    /// <summary>One book's margin on the pair a side belongs to, or null when it does not quote both sides.</summary>
    public double? Hold(string book, string outcome, decimal? line)
    {
        if (!TryKey(outcome, line, out var key, out _) || !_pairs.TryGetValue(key, out var pairs))
            return null;

        var pair = pairs.FirstOrDefault(p => Same(p.Book, book));
        return pair.Book is null ? null : FairValue.Hold(pair.First, pair.Second);
    }

    private bool TryKey(string outcome, decimal? line, out LineKey key, out bool isFirst)
    {
        if (Same(outcome, _first))
        {
            (key, isFirst) = (new LineKey(line), true);
            return true;
        }

        if (Same(outcome, _second))
        {
            // The mirror is its own inverse — a handicap negates, a total and a moneyline keep
            // their number — so the same call maps the second side back to the first's key.
            (key, isFirst) = (new LineKey(Mirror(_market, line)), false);
            return true;
        }

        (key, isFirst) = (default, false);
        return false;
    }

    /// <summary>The other side's number on the same bet.</summary>
    private static decimal? Mirror(string market, decimal? line)
        => market == Markets.Spread && line is { } l ? -l : line;

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
