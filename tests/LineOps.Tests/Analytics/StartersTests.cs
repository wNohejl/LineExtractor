using LineOps.Data.CrossReference;

namespace LineOps.Tests.Analytics;

/// <summary>
/// The quarterback the Board shows under each NFL club: the passer with the most attempts in the
/// club's last game, read from ESPN's box-score lines, which carry no positions.
/// </summary>
public class StartersTests
{
    private static readonly DateTimeOffset Sunday = new(2026, 10, 25, 17, 0, 0, TimeSpan.Zero);

    private static PassingLine Line(int team, int game, int daysBefore, string player, string attempts)
        => new(team, game, Sunday.AddDays(-daysBefore), player, $$"""{"C/ATT": "{{attempts}}", "category": "passing"}""");

    [Fact]
    public void The_starter_is_the_most_attempts_in_the_last_game()
    {
        var lines = new[]
        {
            Line(7, 1, 14, "Old Starter", "22/35"),
            // Last week: the starter, and a receiver's trick-play pass.
            Line(7, 2, 7, "Sam Darnold", "20/28"),
            Line(7, 2, 7, "Wide Receiver", "1/1"),
            // Another club's game the same week.
            Line(9, 3, 7, "Someone Else", "30/40")
        };

        Assert.Equal("Sam Darnold", Starters.LastQuarterback(lines, 7, Sunday));
    }

    [Fact]
    public void A_game_at_or_after_the_matchup_does_not_count()
    {
        var lines = new[]
        {
            Line(7, 2, 7, "Sam Darnold", "20/28"),
            Line(7, 4, 0, "Tonight's Starter", "25/30")
        };

        Assert.Equal("Sam Darnold", Starters.LastQuarterback(lines, 7, Sunday));
    }

    [Fact]
    public void A_club_with_no_passing_on_record_has_no_starter()
        => Assert.Null(Starters.LastQuarterback([Line(9, 3, 7, "Someone Else", "30/40")], 7, Sunday));

    [Theory]
    [InlineData("""{"C/ATT": "20/28"}""", 28)]
    [InlineData("""{"C/ATT": "0/0"}""", 0)]
    [InlineData("""{"CAR": "12"}""", 0)]
    [InlineData("not json", 0)]
    [InlineData("""{"C/ATT": 5}""", 0)]
    public void Attempts_reads_the_second_half_of_comp_att(string statLine, int attempts)
        => Assert.Equal(attempts, Starters.Attempts(statLine));
}
