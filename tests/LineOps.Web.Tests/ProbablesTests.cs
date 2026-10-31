using LineOps.Core.Entities;
using LineOps.Web.Components.Panels;

namespace LineOps.Web.Tests;

/// <summary>How the Board shortens the announced starters: the surname, which is not always one word.</summary>
public class ProbablesTests
{
    private sealed class Probe : PanelBase
    {
        public static string? Of(Game game) => Probables(game);
    }

    [Theory]
    [InlineData("Max Scherzer", "Chris Bassitt", "Scherzer vs Bassitt")]
    [InlineData("CJ Van Eyk", "Trey Gibson", "Van Eyk vs Gibson")]           // live: was "Eyk"
    [InlineData("Vladimir Guerrero Jr.", null, "Guerrero Jr. vs TBD")]
    [InlineData(null, "Aaron Nola", "TBD vs Nola")]
    public void A_starter_is_named_by_everything_after_the_first_name(string? away, string? home, string expected)
        => Assert.Equal(expected, Probe.Of(new Game { AwayProbablePitcher = away, HomeProbablePitcher = home }));

    [Fact]
    public void Nothing_is_drawn_when_neither_starter_is_known()
        => Assert.Null(Probe.Of(new Game()));
}
