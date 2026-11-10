using System.Globalization;
using LineOps.Core.Analytics;

namespace LineOps.Tests.Analytics;

/// <summary>
/// The odds conversions underpin every downstream metric, so they are pinned to
/// hand-checked values rather than to the implementation's own output.
/// </summary>
public class OddsMathTests
{
    [Theory]
    [InlineData(150, "+150")]
    [InlineData(-110, "-110")]
    [InlineData(100, "+100")]
    public void FormatPrice_AlwaysSignsAnAmericanPrice(int american, string expected)
        => Assert.Equal(expected, OddsMath.FormatPrice(american));

    [Theory]
    // A handicap is signed, because the sign is the whole meaning.
    [InlineData(-1.5, "Seattle Mariners", "-1.5")]
    [InlineData(1.5, "Houston Astros", "+1.5")]
    // A total is not. Signing it printed an NBA total of 223 as "+223", which reads as a
    // 223-point handicap rather than a scoreline.
    [InlineData(223, "over", "o223")]
    [InlineData(8.5, "under", "u8.5")]
    [InlineData(44.5, "Over", "o44.5")]
    public void FormatLine_WritesEachMarketTheWayThatMarketIsWritten(
        decimal line, string outcome, string expected)
        => Assert.Equal(expected, OddsMath.FormatLine(line, outcome));

    [Theory]
    // -110 is the standard vigged price: 11 to win 10, so 11/21.
    [InlineData(-110, 0.5238)]
    [InlineData(-200, 0.6667)]
    [InlineData(100, 0.5000)]
    [InlineData(150, 0.4000)]
    [InlineData(-1000, 0.9091)]
    public void ImpliedProbability_MatchesKnownValues(int american, double expected)
    {
        Assert.Equal(expected, OddsMath.ImpliedProbability(american), precision: 4);
    }

    [Theory]
    [InlineData(-110, 1.9091)]
    [InlineData(100, 2.0)]
    [InlineData(150, 2.5)]
    [InlineData(-200, 1.5)]
    public void ToDecimal_MatchesKnownValues(int american, double expected)
    {
        Assert.Equal(expected, OddsMath.ToDecimal(american), precision: 4);
    }

    [Theory]
    [InlineData(-110)]
    [InlineData(-200)]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(2500)]
    public void FromDecimal_RoundTripsToDecimal(int american)
    {
        var decimalOdds = OddsMath.ToDecimal(american);
        Assert.Equal(american, OddsMath.FromDecimal(decimalOdds));
    }

    [Fact]
    public void PayoutOnWin_ReturnsStakePlusProfit()
    {
        // -110 risking 110 wins 100, returning 210.
        Assert.Equal(210m, OddsMath.PayoutOnWin(-110, 110m), precision: 2);
        Assert.Equal(100m, OddsMath.ProfitOnWin(-110, 110m), precision: 2);
    }

    [Fact]
    public void PayoutOnWin_HandlesUnderdogPrice()
    {
        // +150 risking 100 wins 150, returning 250.
        Assert.Equal(250m, OddsMath.PayoutOnWin(150, 100m), precision: 2);
    }

    [Fact]
    public void NoVigProbability_RemovesTheOverround()
    {
        // A symmetric -110/-110 market implies 104.8%; fair value is exactly 50%.
        Assert.Equal(0.5, OddsMath.NoVigProbability(-110, -110), precision: 6);
    }

    [Fact]
    public void NoVigProbability_FavoursTheShorterPrice()
    {
        var fair = OddsMath.NoVigProbability(-200, 170);

        Assert.True(fair > 0.5, "The favourite must carry more than half the fair probability.");
        Assert.True(fair < OddsMath.ImpliedProbability(-200),
            "Removing vig must reduce the raw implied probability.");
    }

    [Fact]
    public void BreakEvenWinRate_EqualsImpliedProbability()
    {
        Assert.Equal(OddsMath.ImpliedProbability(-110), OddsMath.BreakEvenWinRate(-110));
    }

    [Fact]
    public void ZeroIsNotAValidAmericanPrice()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OddsMath.ImpliedProbability(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => OddsMath.ToDecimal(0));
    }

    [Fact]
    public void FromDecimal_RejectsNonReturningOdds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OddsMath.FromDecimal(1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => OddsMath.FromDecimal(0.5));
    }

    // ---- A parlay's combined price ------------------------------------------------------
    //
    // The product of the legs' decimal prices. The slip quotes it as an American price; the
    // settlement reprices after a push on the unrounded product, so a payout is never paid on
    // a number rounded to the nearest cent of American odds.

    [Fact]
    public void CombinedAmerican_MultipliesTheLegsDecimalPrices()
        // 1.78125 × 1.95238 = 3.4777 → +248
        => Assert.Equal(248, OddsMath.CombinedAmerican([-128, -105]));

    [Fact]
    public void CombinedAmerican_TwoLegsAtMinus110IsPlus264()
        => Assert.Equal(264, OddsMath.CombinedAmerican([-110, -110]));

    [Fact]
    public void CombinedDecimal_IsTheUnroundedProduct()
        => Assert.Equal(1.78125 * (1.0 + 100.0 / 105.0), OddsMath.CombinedDecimal([-128, -105]), 10);

    [Fact]
    public void CombinedPrice_NeedsALeg()
        => Assert.Throws<ArgumentException>(() => OddsMath.CombinedAmerican(Array.Empty<int>()));

    // ---- Prices to the cent. Fanatics quoted +104.35 on $500; the desk could only take +104,
    // and paid $1,020 where the book pays $1,021.75.

    [Fact]
    public void ToDecimal_OfAPriceToTheCent_IsExact()
    {
        Assert.Equal(2.0435m, OddsMath.ToDecimal(104.35m));
        Assert.Equal(1.5m, OddsMath.ToDecimal(-200m));
    }

    [Fact]
    public void PayoutOnWin_AtAPriceToTheCent_PaysToTheCent()
    {
        Assert.Equal(521.75m, OddsMath.ProfitOnWin(104.35m, 500m));
        Assert.Equal(1021.75m, OddsMath.PayoutOnWin(104.35m, 500m));
        // The price behind it, from the book's decimal odds 2.04348, pays what the book pays.
        Assert.Equal(1021.74m, OddsMath.PayoutOnWin(OddsMath.FromDecimal(2.04348m), 500m));
        // A favourite's profit is exact too: risking 110 at -110 wins 100, not 99.9999.
        Assert.Equal(100m, OddsMath.ProfitOnWin(-110m, 110m));
    }

    [Fact]
    public void TheWholePriceOverloads_PayWhatTheDecimalOnesPay()
    {
        Assert.Equal(OddsMath.PayoutOnWin(-110m, 25m), OddsMath.PayoutOnWin(-110, 25m));
        Assert.Equal(OddsMath.ProfitOnWin(150m, 25m), OddsMath.ProfitOnWin(150, 25m));
    }

    [Theory]
    [InlineData("2.0435", "104.35")]
    [InlineData("1.5", "-200")]
    [InlineData("1.9", "-111.1111")]
    // Four places: the price behind the +104.35 a book prints, which it pays on.
    [InlineData("2.04348", "104.348")]
    [InlineData("2", "100")]
    public void FromDecimal_ReadsDecimalOddsToFourPlaces(string decimalOdds, string american)
        => Assert.Equal(decimal.Parse(american, CultureInfo.InvariantCulture),
            OddsMath.FromDecimal(decimal.Parse(decimalOdds, CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("104.35", "+104.35")]
    [InlineData("104", "+104")]
    [InlineData("104.00", "+104")]
    [InlineData("-110", "-110")]
    [InlineData("-104.5", "-104.50")]
    // Stored to four places, shown as a book prints it.
    [InlineData("104.348", "+104.35")]
    [InlineData("-110.0049", "-110")]
    public void FormatPrice_ShowsAtMostCentsAndOnlyWhenThePriceHasThem(string american, string expected)
        => Assert.Equal(expected, OddsMath.FormatPrice(decimal.Parse(american, CultureInfo.InvariantCulture)));

    [Theory]
    // American as typed, either way of the dead band.
    [InlineData("104.35", "104.35")]
    [InlineData("-104.35", "-104.35")]
    [InlineData("-110", "-110")]
    // Between 1.01 and 99.99: decimal odds, the number a book shows beside the American price.
    [InlineData("2.0435", "104.35")]
    [InlineData("1.5", "-200")]
    public void ReadTypedPrice_TakesAmericanOrDecimalOdds(string typed, string american)
        => Assert.Equal(decimal.Parse(american, CultureInfo.InvariantCulture),
            OddsMath.ReadTypedPrice(decimal.Parse(typed, CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-50")]
    public void ReadTypedPrice_RefusesWhatIsNotAPrice(string typed)
        => Assert.Null(OddsMath.ReadTypedPrice(decimal.Parse(typed, CultureInfo.InvariantCulture)));

    [Fact]
    public void CombinedDecimal_OverPricesToTheCent_IsTheExactProduct()
    {
        Assert.Equal(2.0435m * 2.0435m, OddsMath.CombinedDecimal([104.35m, 104.35m]));
        Assert.Equal(317.5892m, OddsMath.CombinedAmerican([104.35m, 104.35m]));
    }

    [Fact]
    public void Money_ShowsCentsOnlyWhenTheAmountHasThem()
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            Assert.Equal("$520", OddsMath.Money(520m));
            Assert.Equal("$521.75", OddsMath.Money(521.75m));
            Assert.Equal("$1,021.75", OddsMath.Money(1021.75m));
            Assert.Equal("$190.91", OddsMath.Money(190.909090m));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
