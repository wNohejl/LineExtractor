using LineOps.Web.Components.Panels;

namespace LineOps.Web.Tests;

/// <summary>
/// A running back's Last 5 showed Carries, Yds/Rec, Rec long, Rec yds and Yds/Car: every key on
/// the line is reported every game, the tie went alphabetical, and rushing yards fell off the
/// end. A table's columns now lead with each group's headline numbers.
/// </summary>
public class StatColumnsTests
{
    private static readonly string[] BackLine =
    [
        "CAR", "REC", "TGTS", "category", "rushing.TD", "rushing.AVG", "rushing.YDS", "receiving.TD",
        "rushing.LONG", "receiving.AVG", "receiving.YDS", "receiving.LONG"
    ];

    [Fact]
    public void Pick_LeadsABacksLineWithItsRushingNumbers()
    {
        var columns = StatColumns.Pick(Enumerable.Repeat(BackLine, 5), 5);

        Assert.Equal(["CAR", "rushing.YDS", "rushing.TD", "REC", "receiving.YDS"], columns);
    }

    [Fact]
    public void Pick_PrefersAKeyFilledInMostGamesOverAHeadlineSeenOnce()
    {
        string[][] lines = [["CAR", "rushing.AVG"], ["CAR", "rushing.AVG"], ["CAR", "rushing.AVG", "rushing.YDS"]];

        Assert.Equal(["CAR", "rushing.AVG"], StatColumns.Pick(lines, 2));
    }

    [Fact]
    public void Pick_KeepsTheOldOrderForKeysWithNoRank()
    {
        // MLB: no groups are named, so most-reported then alphabetical, as before.
        string[][] lines = [["AB", "H", "R"], ["AB", "H"], ["AB", "R", "RBI"]];

        Assert.Equal(["AB", "H", "R"], StatColumns.Pick(lines, 3));
    }

    [Fact]
    public void UnitOf_FilesABackUnderRushingByYards()
    {
        var totals = new Dictionary<string, double>
        {
            ["CAR"] = 93, ["REC"] = 10, ["TGTS"] = 14, ["rushing.YDS"] = 420, ["rushing.AVG"] = 4.5,
            ["receiving.YDS"] = 60, ["receiving.AVG"] = 6, ["receiving.LONG"] = 15,
        };

        Assert.Equal("Rushing", StatColumns.UnitOf(totals));
    }

    [Fact]
    public void UnitOf_IsNullWhereTheSportNamesNoGroups()
        => Assert.Null(StatColumns.UnitOf(new Dictionary<string, double> { ["AB"] = 4, ["H"] = 2 }));
}
