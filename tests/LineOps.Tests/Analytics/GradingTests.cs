using LineOps.Core.Analytics;
using LineOps.Core.Entities;

namespace LineOps.Tests.Analytics;

public class GradingTests
{
    private const string Home = "Seattle Seahawks";
    private const string Away = "San Francisco 49ers";

    private static JournalEntry Entry(string market, string outcome, decimal? line = null)
        => new() { Market = market, Outcome = outcome, LineTaken = line, PriceTaken = -110, Stake = 100m };

    [Theory]
    [InlineData(Home, 24, 17, EntryResult.Win)]
    [InlineData(Home, 17, 24, EntryResult.Loss)]
    [InlineData(Away, 17, 24, EntryResult.Win)]
    [InlineData(Away, 24, 17, EntryResult.Loss)]
    public void Moneyline_GradesByWinner(string outcome, int homeScore, int awayScore, EntryResult expected)
    {
        var result = Grading.Grade(Entry(Markets.Moneyline, outcome), Home, Away, homeScore, awayScore);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Moneyline_TieIsAPush()
    {
        var result = Grading.Grade(Entry(Markets.Moneyline, Home), Home, Away, 20, 20);
        Assert.Equal(EntryResult.Push, result);
    }

    [Fact]
    public void Moneyline_AcceptsGenericHomeAwayLabels()
    {
        Assert.Equal(EntryResult.Win, Grading.Grade(Entry(Markets.Moneyline, "home"), Home, Away, 24, 17));
        Assert.Equal(EntryResult.Win, Grading.Grade(Entry(Markets.Moneyline, "away"), Home, Away, 17, 24));
    }

    [Fact]
    public void Moneyline_MatchesTeamNamesIgnoringPunctuationAndCase()
    {
        var entry = Entry(Markets.Moneyline, "seattle seahawks");
        Assert.Equal(EntryResult.Win, Grading.Grade(entry, "Seattle  Seahawks", Away, 24, 17));
    }

    [Theory]
    // Backing the home team at -3.5, winning by 7: covers.
    [InlineData(Home, -3.5, 24, 17, EntryResult.Win)]
    // Same handicap, winning by only 3: does not cover.
    [InlineData(Home, -3.5, 20, 17, EntryResult.Loss)]
    // Backing the underdog at +7.5 and losing by 7: covers.
    [InlineData(Away, 7.5, 24, 17, EntryResult.Win)]
    // Underdog at +3.5 losing by 7: does not cover.
    [InlineData(Away, 3.5, 24, 17, EntryResult.Loss)]
    public void Spread_GradesAgainstTheHandicap(
        string outcome, double line, int homeScore, int awayScore, EntryResult expected)
    {
        var entry = Entry(Markets.Spread, outcome, (decimal)line);
        Assert.Equal(expected, Grading.Grade(entry, Home, Away, homeScore, awayScore));
    }

    [Fact]
    public void Spread_LandingExactlyOnAWholeNumberIsAPush()
    {
        // -3 with a 3-point win is the classic push, and mis-grading it silently
        // corrupts ROI, so it is pinned explicitly.
        var entry = Entry(Markets.Spread, Home, -3m);
        Assert.Equal(EntryResult.Push, Grading.Grade(entry, Home, Away, 20, 17));
    }

    [Fact]
    public void Spread_HalfPointLinesCanNeverPush()
    {
        foreach (var (h, a) in new[] { (20, 17), (21, 17), (17, 20) })
        {
            var entry = Entry(Markets.Spread, Home, -3.5m);
            Assert.NotEqual(EntryResult.Push, Grading.Grade(entry, Home, Away, h, a));
        }
    }

    [Theory]
    [InlineData("over", 44.5, 24, 21, EntryResult.Win)]
    [InlineData("over", 44.5, 20, 17, EntryResult.Loss)]
    [InlineData("under", 44.5, 20, 17, EntryResult.Win)]
    [InlineData("under", 44.5, 24, 21, EntryResult.Loss)]
    public void Total_GradesAgainstCombinedScore(
        string outcome, double line, int homeScore, int awayScore, EntryResult expected)
    {
        var entry = Entry(Markets.Total, outcome, (decimal)line);
        Assert.Equal(expected, Grading.Grade(entry, Home, Away, homeScore, awayScore));
    }

    [Fact]
    public void Total_ExactlyOnTheNumberIsAPush()
    {
        var entry = Entry(Markets.Total, "over", 44m);
        Assert.Equal(EntryResult.Push, Grading.Grade(entry, Home, Away, 24, 20));
    }

    [Fact]
    public void FreeTextEntriesAreNeverAutoGraded()
    {
        // Props, futures and parlay legs are logged before any feed covers them; guessing a
        // result would be worse than leaving them for the user.
        var entry = Entry(Markets.Moneyline, Home);
        entry.FreeTextMarket = "Player X over 1.5 TDs";

        Assert.Null(Grading.Grade(entry, Home, Away, 24, 17));
    }

    [Fact]
    public void SpreadWithoutALineCannotBeGraded()
    {
        Assert.Null(Grading.Grade(Entry(Markets.Spread, Home), Home, Away, 24, 17));
    }

    [Fact]
    public void AnUnrecognisedOutcomeIsLeftUngraded()
    {
        Assert.Null(Grading.Grade(Entry(Markets.Moneyline, "Chicago Bears"), Home, Away, 24, 17));
        Assert.Null(Grading.Grade(Entry(Markets.Total, "middle", 44.5m), Home, Away, 24, 17));
    }

    [Fact]
    public void AnUnknownMarketIsLeftUngraded()
    {
        Assert.Null(Grading.Grade(Entry("player_props", "anything", 1.5m), Home, Away, 24, 17));
    }
}
