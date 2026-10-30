using LineOps.Data.CrossReference;

namespace LineOps.Tests.Reliability;

/// <summary>
/// A back's rushing yards were stored as a bare YDS the week the back only ran the ball and as
/// rushing.YDS the week a catch came too, so the desk read two half-empty columns. Read
/// canonically, every week is rushing.YDS.
/// </summary>
public class StatLineKeysTests
{
    [Fact]
    public void Canonical_QualifiesASharedBareKeyByItsOneGroup()
    {
        var line = StatLineKeys.Canonical(new Dictionary<string, string>
        {
            ["CAR"] = "23", ["YDS"] = "105", ["TD"] = "0", ["AVG"] = "4.6", ["LONG"] = "20", ["category"] = "rushing"
        });

        Assert.Equal("105", line["rushing.YDS"]);
        Assert.Equal("4.6", line["rushing.AVG"]);
        Assert.Equal("23", line["CAR"]);
        Assert.False(line.ContainsKey("YDS"));
    }

    [Fact]
    public void Canonical_GivesAMergedBareKeyToEveryGroupThatAgreed()
    {
        var line = StatLineKeys.Canonical(new Dictionary<string, string>
        {
            ["CAR"] = "5", ["REC"] = "2", ["YDS"] = "15", ["rushing.AVG"] = "3.0", ["receiving.AVG"] = "7.5",
            ["category"] = "receiving,rushing"
        });

        Assert.Equal("15", line["rushing.YDS"]);
        Assert.Equal("15", line["receiving.YDS"]);
        Assert.Equal("2", line["receiving.REC"]);
    }

    [Fact]
    public void Canonical_LeavesAKeyOnlyAnotherGroupOnTheLineCouldNotReport()
    {
        // Defensive prints no YDS, so a defender's interception yards are the interceptions group's.
        var line = StatLineKeys.Canonical(new Dictionary<string, string>
        {
            ["TOT"] = "6", ["YDS"] = "24", ["category"] = "defensive,interceptions"
        });

        Assert.Equal("24", line["interceptions.YDS"]);
        Assert.Equal("6", line["TOT"]);
    }

    [Fact]
    public void Canonical_LeavesSportsWithOtherGroupsAlone()
    {
        var line = new Dictionary<string, string> { ["G"] = "1", ["A"] = "2", ["category"] = "forwards" };

        Assert.Equal(["G", "A", "category"], StatLineKeys.Canonical(line).Keys);
    }
}
