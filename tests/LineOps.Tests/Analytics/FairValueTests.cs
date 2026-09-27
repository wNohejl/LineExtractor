using LineOps.Core.Analytics;
using LineOps.Core.Entities;

namespace LineOps.Tests.Analytics;

/// <summary>
/// Fair value is what turns "the best price" into "a good price", so it is pinned to
/// hand-worked markets rather than to its own output — and to the cases where the honest answer
/// is that there is no fair price at all.
/// </summary>
public class FairValueTests
{
    private const string Home = "Seattle Mariners";
    private const string Away = "Houston Astros";

    private static FairMarket Moneyline(params SidePrice[] prices)
        => FairMarket.Build(Markets.Moneyline, Home, Away, prices);

    [Fact]
    public void Hold_IsWhatThePairImpliesBeyondCertainty()
    {
        // -110 both ways implies 52.38% twice: 4.76% more than can happen.
        Assert.Equal(0.0476, FairValue.Hold(-110, -110), precision: 4);
    }

    [Theory]
    // A fair coin at even money is worth exactly nothing.
    [InlineData(0.5, 100, 0.0)]
    // 50% at +110 returns 2.10 half the time: five cents on the dollar.
    [InlineData(0.5, 110, 0.05)]
    // The standard vigged price on a fair coin loses the vig: 1/22 of the stake.
    [InlineData(0.5, -110, -0.045454)]
    public void Ev_IsTheExpectedReturnPerUnitStaked(double fair, int price, double expected)
        => Assert.Equal(expected, FairValue.Ev(fair, price), precision: 4);

    [Fact]
    public void Pinnacle_SetsTheFairPrice_WhenItQuotesTheMarket()
    {
        var market = Moneyline(
            new("pinnacle", Home, null, -150), new("pinnacle", Away, null, 140),
            new("draftkings", Home, null, -165), new("draftkings", Away, null, 145),
            new("fanduel", Home, null, -140), new("fanduel", Away, null, 120));

        var fair = market.For(Home, null);

        Assert.NotNull(fair);
        Assert.Equal(OddsMath.NoVigProbability(-150, 140), fair.Probability, precision: 9);
        Assert.Equal(FairValue.SharpBook, fair.Basis);
    }

    [Fact]
    public void Pinnacle_IsRecognisedHoweverItIsCapitalised()
    {
        var market = Moneyline(
            new("Pinnacle", Home, null, -150), new("Pinnacle", Away, null, 140));

        Assert.Equal(FairValue.SharpBook, market.For(Home, null)?.Basis);
    }

    [Fact]
    public void WithoutPinnacle_TheBooksAverageTheirNoVigPrices()
    {
        var market = Moneyline(
            new("draftkings", Home, null, -150), new("draftkings", Away, null, 130),
            new("fanduel", Home, null, -130), new("fanduel", Away, null, 110));

        var fair = market.For(Home, null);

        var expected = (OddsMath.NoVigProbability(-150, 130) + OddsMath.NoVigProbability(-130, 110)) / 2;
        Assert.NotNull(fair);
        Assert.Equal(expected, fair.Probability, precision: 9);
        Assert.Equal(FairValue.Consensus, fair.Basis);
        Assert.Equal(2, fair.Books);
    }

    [Fact]
    public void OneOrdinaryBook_HasNoFairPrice()
    {
        // Judged against itself a book is always exactly its own hold short of fair, which says
        // nothing about whether its price is good.
        var market = Moneyline(new("draftkings", Home, null, -150), new("draftkings", Away, null, 130));

        Assert.Null(market.For(Home, null));
    }

    [Fact]
    public void AOneSidedQuote_IsNotAMarket()
    {
        var market = Moneyline(
            new("pinnacle", Home, null, -150),
            new("draftkings", Home, null, -160), new("fanduel", Away, null, 135));

        Assert.Null(market.For(Home, null));
    }

    [Fact]
    public void TheTwoSidesOfAFairPriceSumToCertainty()
    {
        var market = Moneyline(new("pinnacle", Home, null, -150), new("pinnacle", Away, null, 140));

        Assert.Equal(1.0, market.For(Home, null)!.Probability + market.For(Away, null)!.Probability, precision: 9);
    }

    [Fact]
    public void AFairPriceIsWrittenAsAnAmericanPrice()
    {
        var market = Moneyline(new("pinnacle", Home, null, -110), new("pinnacle", Away, null, -110));

        Assert.Equal(100, market.For(Home, null)!.PriceAmerican);
    }

    [Fact]
    public void ASpreadPairsEachHandicapWithItsMirror()
    {
        var market = FairMarket.Build(Markets.Spread, Home, Away,
        [
            new("pinnacle", Home, -1.5m, 130), new("pinnacle", Away, 1.5m, -150),
        ]);

        var home = market.For(Home, -1.5m);
        var away = market.For(Away, 1.5m);

        Assert.NotNull(home);
        Assert.NotNull(away);
        Assert.Equal(OddsMath.NoVigProbability(130, -150), home.Probability, precision: 9);
        Assert.Equal(1 - home.Probability, away.Probability, precision: 9);
    }

    [Fact]
    public void AnotherNumber_HasNoFairPrice()
    {
        // Pinnacle priced -1.5. What -2.5 is worth needs a model of how often games land on 2,
        // and a guessed one would be a fabricated reading — so there is none.
        var market = FairMarket.Build(Markets.Spread, Home, Away,
        [
            new("pinnacle", Home, -1.5m, 130), new("pinnacle", Away, 1.5m, -150),
            new("draftkings", Home, -2.5m, 160), new("draftkings", Away, 2.5m, -190),
        ]);

        Assert.Null(market.For(Home, -2.5m));
        Assert.Null(market.Ev(Home, -2.5m, 160));
    }

    [Fact]
    public void ATotalPairsOverAndUnderOnTheSameNumber()
    {
        var market = FairMarket.Build(Markets.Total, "over", "under",
        [
            new("pinnacle", "Over", 8.5m, -105), new("pinnacle", "Under", 8.5m, -115),
            new("pinnacle", "Over", 9.0m, 120),
        ]);

        Assert.Equal(OddsMath.NoVigProbability(-105, -115), market.For("over", 8.5m)!.Probability, precision: 9);
        Assert.Null(market.For("over", 9.0m));
    }

    [Fact]
    public void Ev_ScoresAnotherBooksPriceAgainstTheFairOne()
    {
        // Pinnacle -105/-105 is a fair coin. DraftKings hanging +110 on it is five cents of value.
        var market = Moneyline(
            new("pinnacle", Home, null, -105), new("pinnacle", Away, null, -105),
            new("draftkings", Home, null, 110), new("draftkings", Away, null, -130));

        Assert.Equal(0.05, market.Ev(Home, null, 110)!.Value, precision: 4);
        Assert.True(market.Ev(Away, null, -130) < 0);
    }

    [Fact]
    public void Hold_IsReadPerBook()
    {
        var market = Moneyline(
            new("pinnacle", Home, null, -105), new("pinnacle", Away, null, -105),
            new("draftkings", Home, null, -120), new("draftkings", Away, null, 100));

        Assert.Equal(FairValue.Hold(-105, -105), market.Hold("pinnacle", Home, null)!.Value, precision: 9);
        Assert.Equal(FairValue.Hold(-120, 100), market.Hold("draftkings", Away, null)!.Value, precision: 9);
        Assert.Null(market.Hold("fanduel", Home, null));
    }
}
