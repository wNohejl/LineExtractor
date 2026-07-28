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
}
