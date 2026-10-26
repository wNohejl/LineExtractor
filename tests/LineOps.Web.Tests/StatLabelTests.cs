using LineOps.Web.Components.Panels;

namespace LineOps.Web.Tests;

/// <summary>
/// A stat column reads the way a box score on a sports page reads it, not the way the
/// provider keys it. The Player window used to title its columns <c>C/ATT</c>, <c>CAR</c> and
/// <c>passing.YDS</c>, which is the jsonb talking.
/// </summary>
public class StatLabelTests
{
    [Theory]
    // NFL, the brief's map.
    [InlineData("C/ATT", "Comp/Att")]
    [InlineData("passing.AVG", "Yds/Att")]
    [InlineData("passing.YDS", "Pass yds")]
    [InlineData("CAR", "Carries")]
    [InlineData("INT", "Int")]
    [InlineData("LONG", "Long")]
    [InlineData("QBR", "QBR")]
    [InlineData("RTG", "Rating")]
    // MLB follows the same rule.
    [InlineData("AB", "At bats")]
    [InlineData("H", "Hits")]
    [InlineData("HR", "HR")]
    [InlineData("RBI", "RBI")]
    [InlineData("AVG", "Avg")]
    [InlineData("ERA", "ERA")]
    [InlineData("IP", "IP")]
    [InlineData("SO", "SO")]
    [InlineData("BB", "BB")]
    public void A_known_key_reads_as_its_name(string key, string label)
        => Assert.Equal(label, StatLabels.StatLabel(key));

    [Theory]
    // A group prefix is only written when two groups reported the same key, so it is kept
    // as a word: the two columns must not read the same.
    [InlineData("rushing.YDS", "Rush yds")]
    [InlineData("receiving.YDS", "Rec yds")]
    [InlineData("passing.TD", "Pass TD")]
    [InlineData("rushing.TD", "Rush TD")]
    [InlineData("pitching.H", "Hits allowed")]
    [InlineData("batting.H", "Hits")]
    public void A_qualified_key_keeps_its_group_word(string key, string label)
        => Assert.Equal(label, StatLabels.StatLabel(key));

    [Fact]
    public void Qualified_keys_that_share_a_measure_read_differently()
    {
        var labels = new[] { "passing.YDS", "rushing.YDS", "receiving.YDS" }
            .Select(StatLabels.StatLabel)
            .ToList();

        Assert.Equal(labels.Count, labels.Distinct().Count());
    }

    [Theory]
    [InlineData("YDS", "Yds")]
    [InlineData("PUNTS", "Punts")]
    [InlineData("special.XYZ", "Xyz")]
    [InlineData("misc.TACKLES", "Tackles")]
    [InlineData("OBP", "OBP")]
    [InlineData("misc.OPS", "OPS")]
    [InlineData("FG/ATT", "FG/Att")]
    public void Unknown_key_uses_its_last_segment(string key, string label)
        => Assert.Equal(label, StatLabels.StatLabel(key));
}
