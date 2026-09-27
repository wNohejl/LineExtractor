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

    /// <summary>Profit (excluding stake) on a winning wager.</summary>
    public static decimal ProfitOnWin(int american, decimal stake)
        => stake * (decimal)(ToDecimal(american) - 1.0);

    /// <summary>Total returned including stake on a winning wager.</summary>
    public static decimal PayoutOnWin(int american, decimal stake)
        => stake * (decimal)ToDecimal(american);

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
