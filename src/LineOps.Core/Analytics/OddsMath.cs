using System.Globalization;

namespace LineOps.Core.Analytics;

/// <summary>
/// American-odds conversions. Pure functions, unit-tested — these underpin every
/// downstream metric (CLV, ROI, break-even rate), so correctness here is load-bearing.
/// </summary>
public static class OddsMath
{
    /// <summary>
    /// Implied probability from American odds, including the book's vig.
    /// -110 => 0.5238, +150 => 0.40.
    /// </summary>
    public static double ImpliedProbability(int american)
    {
        if (american == 0)
            throw new ArgumentOutOfRangeException(nameof(american), "American odds cannot be zero.");

        return american > 0
            ? 100.0 / (american + 100.0)
            : -american / (double)(-american + 100);
    }

    /// <summary>Decimal (European) odds: total return per unit staked, including stake.</summary>
    public static double ToDecimal(int american)
    {
        if (american == 0)
            throw new ArgumentOutOfRangeException(nameof(american), "American odds cannot be zero.");

        return american > 0
            ? 1.0 + american / 100.0
            : 1.0 + 100.0 / -american;
    }

    /// <summary>Inverse of <see cref="ToDecimal"/>, rounded to the nearest whole American price.</summary>
    public static int FromDecimal(double decimalOdds)
    {
        if (decimalOdds <= 1.0)
            throw new ArgumentOutOfRangeException(nameof(decimalOdds), "Decimal odds must exceed 1.0.");

        return decimalOdds >= 2.0
            ? (int)Math.Round((decimalOdds - 1.0) * 100.0)
            : (int)Math.Round(-100.0 / (decimalOdds - 1.0));
    }

    /// <summary>Implied probability of a price taken to the cent: +104.35 is 0.4894.</summary>
    public static double ImpliedProbability(decimal american)
    {
        if (american == 0)
            throw new ArgumentOutOfRangeException(nameof(american), "American odds cannot be zero.");

        return (double)(american > 0
            ? 100m / (american + 100m)
            : -american / (-american + 100m));
    }

    /// <summary>
    /// Decimal odds of a price taken to the cent, in exact decimal arithmetic: +104.35 is 2.0435.
    /// Books quote prices like that (a Fanatics boost), and a payout worked out through a double
    /// drifts off the cent the book pays.
    /// </summary>
    public static decimal ToDecimal(decimal american)
    {
        if (american == 0)
            throw new ArgumentOutOfRangeException(nameof(american), "American odds cannot be zero.");

        return american > 0
            ? 1m + american / 100m
            : 1m + 100m / -american;
    }

    /// <summary>
    /// Decimal odds written as an American price to four places: 2.0435 is +104.35, 2.04348 is
    /// +104.348, 1.5 is -200. What a typed decimal price is read as; four places, because a book
    /// pays on the exact price behind the one it prints (+104.35 on the slip, $1,021.74 on $500).
    /// </summary>
    public static decimal FromDecimal(decimal decimalOdds)
    {
        if (decimalOdds <= 1m)
            throw new ArgumentOutOfRangeException(nameof(decimalOdds), "Decimal odds must exceed 1.0.");

        return decimalOdds >= 2m
            ? Math.Round((decimalOdds - 1m) * 100m, 4, MidpointRounding.AwayFromZero)
            : Math.Round(-100m / (decimalOdds - 1m), 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>Profit (excluding stake) on a winning wager.</summary>
    public static decimal ProfitOnWin(int american, decimal stake) => ProfitOnWin((decimal)american, stake);

    /// <summary>Total returned including stake on a winning wager.</summary>
    public static decimal PayoutOnWin(int american, decimal stake) => PayoutOnWin((decimal)american, stake);

    /// <summary>
    /// Profit (excluding stake) on a winning wager at a price to the cent, unrounded: $500 at
    /// +104.35 is 521.75, -110 risking 110 is exactly 100. Round where it is shown or stored.
    /// </summary>
    public static decimal ProfitOnWin(decimal american, decimal stake)
    {
        if (american == 0)
            throw new ArgumentOutOfRangeException(nameof(american), "American odds cannot be zero.");

        return american > 0 ? stake * american / 100m : stake * 100m / -american;
    }

    /// <summary>Total returned including stake at a price to the cent: $500 at +104.35 is 1,021.75.</summary>
    public static decimal PayoutOnWin(decimal american, decimal stake) => stake + ProfitOnWin(american, stake);

    /// <summary>
    /// The legs' decimal prices multiplied: what one unit returns if every leg wins. Unrounded,
    /// because settlement pays on it (<see cref="ParlayGrading"/>) and a payout must not move
    /// with the rounding of a displayed price.
    /// </summary>
    public static double CombinedDecimal(IEnumerable<int> legsAmerican)
    {
        var product = 1.0;
        var any = false;

        foreach (var american in legsAmerican)
        {
            product *= ToDecimal(american);
            any = true;
        }

        if (!any)
            throw new ArgumentException("A combined price needs at least one leg.", nameof(legsAmerican));

        return product;
    }

    /// <summary>
    /// A parlay's price from its legs, as a whole American price. What the slip quotes when the
    /// book's own parlay price was not given. -128 and -105 make +248.
    /// </summary>
    public static int CombinedAmerican(IEnumerable<int> legsAmerican)
        => FromDecimal(CombinedDecimal(legsAmerican));

    /// <summary>
    /// The legs' decimal prices multiplied, for legs taken to the cent, in exact decimal
    /// arithmetic. Unrounded, for the same reason as the whole-price product: settlement pays on it.
    /// </summary>
    public static decimal CombinedDecimal(IEnumerable<decimal> legsAmerican)
    {
        var product = 1m;
        var any = false;

        foreach (var american in legsAmerican)
        {
            product *= ToDecimal(american);
            any = true;
        }

        if (!any)
            throw new ArgumentException("A combined price needs at least one leg.", nameof(legsAmerican));

        return product;
    }

    /// <summary>A parlay's price from legs taken to the cent, as an American price to the cent.</summary>
    public static decimal CombinedAmerican(IEnumerable<decimal> legsAmerican)
        => FromDecimal(CombinedDecimal(legsAmerican));

    /// <summary>
    /// Removes the vig from a two-way market, normalising the pair of implied
    /// probabilities so they sum to 1. Returns the fair probability of the first side.
    /// </summary>
    public static double NoVigProbability(int sideAmerican, int opposingAmerican)
    {
        var a = ImpliedProbability(sideAmerican);
        var b = ImpliedProbability(opposingAmerican);
        var overround = a + b;

        if (overround <= 0)
            throw new ArgumentException("Degenerate market: implied probabilities sum to zero.");

        return a / overround;
    }

    /// <summary>
    /// Break-even win rate required to profit at this price — the honest bar
    /// any strategy has to clear.
    /// </summary>
    public static double BreakEvenWinRate(int american) => ImpliedProbability(american);

    /// <summary>An American price, always signed. "+150", "-110".</summary>
    public static string FormatPrice(int american) => american > 0 ? $"+{american}" : american.ToString();

    /// <summary>
    /// A price taken, always signed, written as a book prints it — to the cent at most, and with
    /// its cents only when it has them: "+104", "+104.35", "-110". A price stored to four places
    /// (+104.348, read from decimal odds) shows as +104.35 and pays on all four. Invariant, so a
    /// regional decimal comma never reaches a price.
    /// </summary>
    public static string FormatPrice(decimal american)
    {
        var shown = Math.Round(american, 2, MidpointRounding.AwayFromZero);
        var text = shown == decimal.Truncate(shown)
            ? decimal.Truncate(shown).ToString("0", CultureInfo.InvariantCulture)
            : shown.ToString("0.00", CultureInfo.InvariantCulture);
        return american > 0 ? $"+{text}" : text;
    }

    /// <summary>
    /// What a typed price means. A number of 100 or more either way is an American price as typed;
    /// one between 1.01 and 99.99 with a fraction is decimal odds — what a book shows beside the
    /// American price — and is read as the American price it stands for (2.0435 is +104.35). A
    /// whole number under 100 is not a price: a book never prints decimal odds of exactly 15, and
    /// "+15" is "+150" with a digit dropped, which used to be refused and still is. Anything else
    /// is not a price and comes back null.
    /// </summary>
    public static decimal? ReadTypedPrice(decimal typed)
    {
        var size = Math.Abs(typed);

        if (size >= 100m)
            return Math.Round(typed, 4, MidpointRounding.AwayFromZero);

        if (typed is >= 1.01m and <= 99.99m && typed != decimal.Truncate(typed))
            return FromDecimal(typed);

        return null;
    }

    /// <summary>
    /// Money to the cent, with the cents only when it has them: "$520", "$521.75", "$1,021.75".
    /// In the machine's currency format, as the desk's other "C0" money is. "C0" alone rounded
    /// $521.75 to "$522": a payout the book pays to the cent was shown to the dollar.
    /// </summary>
    public static string Money(decimal amount)
    {
        var cents = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        return cents.ToString(cents == decimal.Truncate(cents) ? "C0" : "C2", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// A market's number, written the way that market is written.
    ///
    /// <para>
    /// A handicap is signed and a total is not, but both reach the UI as the same nullable
    /// decimal — so formatting them identically printed an NBA total of 223 as "+223", which
    /// reads as a 223-point handicap rather than a scoreline. It appeared in three places
    /// independently, which is the argument for the rule living here rather than in whichever
    /// component happened to need it.
    /// </para>
    /// </summary>
    public static string FormatLine(decimal line, string outcome) => outcome.ToLowerInvariant() switch
    {
        "over" => $"o{line:0.#}",
        "under" => $"u{line:0.#}",
        _ => line > 0 ? $"+{line:0.#}" : $"{line:0.#}"
    };
}
