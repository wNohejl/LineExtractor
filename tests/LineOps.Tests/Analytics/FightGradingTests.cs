using LineOps.Core.Analytics;
using LineOps.Core.Entities;

namespace LineOps.Tests.Analytics;

public class FightGradingTests
{
    private const string Red = "Alex Pereira";
    private const string Blue = "Jamahal Hill";

    private static JournalEntry Entry(string market, string outcome, decimal? line = null)
        => new() { Market = market, Outcome = outcome, LineTaken = line, PriceTaken = -138, Stake = 100m };

    private static Bout Finished(BoutOutcome outcome, int round, int seconds, int scheduled = 5, string method = "KO/TKO")
        => new()
        {
            Outcome = outcome,
            EndRound = round,
            EndSeconds = seconds,
            ScheduledRounds = scheduled,
            Method = method
        };

    // UFC 300: Pereira (red) stopped Hill at 3:14 of the first.
    private static readonly Bout Ufc300 = Finished(BoutOutcome.Win, 1, 194);

    [Theory]
    [InlineData(Red, EntryResult.Win)]
    [InlineData(Blue, EntryResult.Loss)]
    public void Moneyline_GradesTheWinner(string outcome, EntryResult expected)
        => Assert.Equal(expected, Grading.GradeFight(Entry(Markets.Moneyline, outcome), Red, Blue, 1, 0, Ufc300));

    [Fact]
    public void Moneyline_MatchesAFighterWrittenWithoutAccents()
    {
        var bout = Finished(BoutOutcome.Win, 2, 197, scheduled: 3);

        Assert.Equal(EntryResult.Win,
            Grading.GradeFight(Entry(Markets.Moneyline, "Jiri Prochazka"), "Jiří Procházka", "Aleksandar Rakic", 1, 0, bout));
    }

    [Fact]
    public void Moneyline_MatchesASurnameTypedAlone()
        => Assert.Equal(EntryResult.Loss,
            Grading.GradeFight(Entry(Markets.Moneyline, "Hill"), Red, Blue, 1, 0, Ufc300));

    [Fact]
    public void Moneyline_ADrawPushes()
    {
        var draw = Finished(BoutOutcome.Draw, 3, 300, scheduled: 3, method: "Decision - Majority");

        Assert.Equal(EntryResult.Push, Grading.GradeFight(Entry(Markets.Moneyline, Red), Red, Blue, 0, 0, draw));
    }

    [Fact]
    public void ANoContestVoidsEveryMarket()
    {
        var noContest = Finished(BoutOutcome.NoContest, 1, 130);

        Assert.Equal(EntryResult.Void, Grading.GradeFight(Entry(Markets.Moneyline, Red), Red, Blue, null, null, noContest));
        Assert.Equal(EntryResult.Void, Grading.GradeFight(Entry(Markets.Total, "under", 1.5m), Red, Blue, null, null, noContest));
    }

    [Fact]
    public void AFightWithNoResultIsNotGraded()
        => Assert.Null(Grading.GradeFight(Entry(Markets.Moneyline, Red), Red, Blue, null, null, new Bout()));

    [Fact]
    public void AnUnknownFighterIsNotGraded()
        => Assert.Null(Grading.GradeFight(Entry(Markets.Moneyline, "Israel Adesanya"), Red, Blue, 1, 0, Ufc300));

    [Fact]
    public void FreeTextPropsAreLeftForTheOperator()
    {
        var entry = Entry(Markets.Moneyline, Red);
        entry.FreeTextMarket = "Pereira by KO/TKO";

        Assert.Null(Grading.GradeFight(entry, Red, Blue, 1, 0, Ufc300));
    }

    [Theory]
    // 3:14 of the first is 0.65 rounds: under 1.5 wins.
    [InlineData("under", 1.5, 1, 194, EntryResult.Win)]
    [InlineData("over", 1.5, 1, 194, EntryResult.Loss)]
    // 4:59 of the fifth, Holloway–Gaethje: over 4.5.
    [InlineData("over", 4.5, 5, 299, EntryResult.Win)]
    // Exactly 2:30 of the second reaches 1.5: the over has it.
    [InlineData("over", 1.5, 2, 150, EntryResult.Win)]
    [InlineData("under", 1.5, 2, 149, EntryResult.Win)]
    // A whole-round line reached exactly is a push.
    [InlineData("over", 2.0, 3, 0, EntryResult.Push)]
    public void Total_IsGradedInRounds(string side, double line, int round, int seconds, EntryResult expected)
    {
        var bout = Finished(BoutOutcome.Win, round, seconds);

        Assert.Equal(expected, Grading.GradeFight(Entry(Markets.Total, side, (decimal)line), Red, Blue, 1, 0, bout));
    }

    [Fact]
    public void Total_AFightThatWentTheDistanceLastedEveryRound()
    {
        var decision = Finished(BoutOutcome.Win, 3, 300, scheduled: 3, method: "Decision - Unanimous");

        Assert.Equal(3m, FightSports.ElapsedRounds(decision));
        Assert.True(FightSports.WentTheDistance(decision));
        Assert.Equal(EntryResult.Win, Grading.GradeFight(Entry(Markets.Total, "over", 2.5m), Red, Blue, 1, 0, decision));
    }

    [Fact]
    public void Total_ADrawIsStillGradedOnTime()
    {
        var draw = Finished(BoutOutcome.Draw, 3, 300, scheduled: 3, method: "Decision - Split");

        Assert.Equal(EntryResult.Win, Grading.GradeFight(Entry(Markets.Total, "over", 2.5m), Red, Blue, 0, 0, draw));
    }

    [Fact]
    public void ThereIsNoFightSpread()
        => Assert.Null(Grading.GradeFight(Entry(Markets.Spread, Red, -1.5m), Red, Blue, 1, 0, Ufc300));
}

public class FightPresentationTests
{
    private static Game Game(int? home, int? away) => new()
    {
        Status = GameStatus.Final,
        HomeScore = home,
        AwayScore = away,
        HomeTeam = new Team { Name = "Alex Pereira" },
        AwayTeam = new Team { Name = "Khalil Rountree Jr." }
    };

    [Theory]
    [InlineData("KO/TKO", BoutOutcome.Win, "KO/TKO")]
    [InlineData("Submission", BoutOutcome.Win, "SUB")]
    [InlineData("Decision - Unanimous", BoutOutcome.Win, "U-DEC")]
    [InlineData("Decision - Split", BoutOutcome.Win, "S-DEC")]
    [InlineData("Decision - Majority", BoutOutcome.Draw, "DRAW")]
    [InlineData("Decision - Unanimous", BoutOutcome.NoContest, "NC")]
    public void MethodShort(string method, BoutOutcome outcome, string expected)
        => Assert.Equal(expected, FightSports.MethodShort(new Bout { Method = method, Outcome = outcome }));

    [Fact]
    public void ResultLine_NamesTheWinnerTheMethodAndWhereItEnded()
    {
        var bout = new Bout { Outcome = BoutOutcome.Win, Method = "KO/TKO", MethodDetail = "Punch", EndRound = 1, EndSeconds = 194 };

        Assert.Equal("Pereira by KO/TKO (Punch), R1 3:14", FightSports.ResultLine(Game(1, 0), bout));
    }

    [Fact]
    public void ResultLine_KeepsAGenerationalSuffixWithTheSurname()
    {
        var bout = new Bout { Outcome = BoutOutcome.Win, Method = "KO/TKO", EndRound = 4, EndSeconds = 272 };

        Assert.Equal("Rountree Jr. by KO/TKO, R4 4:32", FightSports.ResultLine(Game(0, 1), bout));
    }

    [Fact]
    public void AFightIsWrittenByWhoBeatWhomNotByAScore()
    {
        var game = Game(1, 0);
        game.Sport = new Sport { Key = "ufc" };
        game.HomeTeam!.Abbrev = "Pereira";
        game.AwayTeam!.Abbrev = "Rountree Jr.";

        Assert.Equal("PEREIRA def. ROUNTREE JR.", Scoreline.Format(game));
    }

    [Fact]
    public void ResultLine_ADrawSaysWhichKind()
    {
        var bout = new Bout { Outcome = BoutOutcome.Draw, Method = "Decision - Majority", EndRound = 3, EndSeconds = 300 };

        Assert.Equal("Draw (Majority), R3 5:00", FightSports.ResultLine(Game(0, 0), bout));
    }
}

public class FighterNamesTests
{
    [Theory]
    [InlineData("Jiří Procházka", "Jiri Prochazka")]
    [InlineData("Jan Błachowicz", "Jan Blachowicz")]
    [InlineData("Khalil Rountree Jr.", "Khalil Rountree")]
    [InlineData("Bruno Silva (Flyweight)", "Bruno Silva")]
    [InlineData("Alex Pereira", "A. Pereira")]
    [InlineData("Weili Zhang", "Weili  Zhang")]
    [InlineData("Ode' Osbourne", "Ode Osbourne")]
    public void Same(string a, string b) => Assert.True(FighterNames.Same(a, b));

    [Theory]
    [InlineData("Alex Pereira", "Glover Teixeira")]
    [InlineData("Bobby Green", "King Green")]
    [InlineData("", "Alex Pereira")]
    public void NotSame(string a, string b) => Assert.False(FighterNames.Same(a, b));

    [Fact]
    public void SurnameAloneIsTheWeakerReading()
    {
        Assert.True(FighterNames.SameSurname("Bobby Green", "King Green"));
        Assert.False(FighterNames.SameSurname("Bobby Green", "King Silva"));
    }
}
