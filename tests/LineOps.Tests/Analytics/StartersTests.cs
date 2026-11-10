using LineOps.Core.Entities;
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

    // The Vikings on 9 November 2026: Murray started every game since week three and is ruled
    // out of Monday's; Wentz started weeks one and two without him.
    private static readonly PassingLine[] Vikings =
    [
        Line(16, 10, 63, "Carson Wentz", "21/30", season: 2026),
        Line(16, 11, 56, "Carson Wentz", "18/27", season: 2026),
        Line(16, 12, 49, "Kyler Murray", "22/31", season: 2026),
        Line(16, 12, 49, "Carson Wentz", "2/3", season: 2026),
        Line(16, 13, 7, "Kyler Murray", "17/29", season: 2026),
        // Last season's backup, who is not this season's.
        Line(16, 9, 300, "Old Backup", "20/30", season: 2025)
    ];

    private static PassingLine Line(int team, int game, int daysBefore, string player, string attempts, int season)
        => Line(team, game, daysBefore, player, attempts) with { SeasonYear = season };

    private static Dictionary<string, (string Status, string? Note)> Report(params (string Player, string Status, string? Note)[] entries)
        => entries.ToDictionary(e => e.Player, e => (e.Status, e.Note));

    [Fact]
    public void A_starter_the_club_has_not_ruled_out_keeps_the_job()
    {
        var qb = Starters.Quarterback(Vikings, Report(("Kyler Murray", "Questionable", null)), 16, Sunday);

        Assert.Equal("Kyler Murray", qb.Name);
        Assert.Equal(Starters.LastGameBasis, qb.Basis);
    }

    [Fact]
    public void A_starter_ruled_out_gives_way_to_the_quarterback_who_started_without_him()
    {
        var qb = Starters.Quarterback(Vikings,
            Report(("Kyler Murray", "Out", "Murray (hamstring) has been ruled out for Monday's game versus the Bills."),
                ("Carson Wentz", "Active", null)),
            16, Sunday);

        Assert.Equal("Carson Wentz", qb.Name);
        Assert.Equal(
            "Kyler Murray is out; Carson Wentz started the last game the club played without him. " +
            "Murray (hamstring) has been ruled out for Monday's game versus the Bills.",
            qb.Basis);
    }

    [Theory]
    [InlineData("Injured Reserve", "Kyler Murray is on injured reserve; ")]
    [InlineData("Doubtful", "Kyler Murray is doubtful; ")]
    [InlineData("out", "Kyler Murray is out; ")]
    public void Injured_reserve_and_doubtful_count_as_out(string status, string opening)
    {
        var qb = Starters.Quarterback(Vikings, Report(("Kyler Murray", status, null)), 16, Sunday);

        Assert.Equal("Carson Wentz", qb.Name);
        Assert.StartsWith(opening, qb.Basis);
    }

    [Fact]
    public void A_replacement_who_is_out_too_is_passed_over()
    {
        var qb = Starters.Quarterback(Vikings,
            Report(("Kyler Murray", "Out", null), ("Carson Wentz", "Out", null)), 16, Sunday);

        Assert.Null(qb.Name);
        Assert.Equal("Kyler Murray is out; no replacement has thrown for the club this season.", qb.Basis);
    }

    [Fact]
    public void Last_seasons_backup_is_not_this_seasons_replacement()
    {
        var lines = Vikings.Where(l => l.Player != "Carson Wentz").ToArray();

        var qb = Starters.Quarterback(lines, Report(("Kyler Murray", "Out", "Hamstring.")), 16, Sunday);

        Assert.Null(qb.Name);
        Assert.Equal("Kyler Murray is out; no replacement has thrown for the club this season. Hamstring.", qb.Basis);
    }

    [Fact]
    public void A_replacement_who_has_only_thrown_in_relief_is_named_as_such()
    {
        // Wentz's only lines this season are a relief appearance in a game Murray started.
        var lines = Vikings.Where(l => l.GameId is not (10 or 11)).ToArray();

        var qb = Starters.Quarterback(lines, Report(("Kyler Murray", "Out", null)), 16, Sunday);

        Assert.Equal("Carson Wentz", qb.Name);
        Assert.Equal("Kyler Murray is out; Carson Wentz is the club's most recent other passer.", qb.Basis);
    }

    [Fact]
    public void A_club_with_no_passing_has_no_quarterback_and_nothing_to_say()
    {
        var qb = Starters.Quarterback(Vikings, Report(), 7, Sunday);

        Assert.Null(qb.Name);
        Assert.Null(qb.Basis);
    }

    // ESPN keeps twenty-five entries a club, so the report stops naming a player without saying
    // he is well; the status he was left with ages out instead.
    [Fact]
    public void An_out_the_report_has_not_repeated_in_two_weeks_is_no_status()
    {
        var injuries = BoardService.CurrentInjuries(
            [("Kyler Murray", "Out", "Hamstring.", Sunday - Player.InjuryStatusLife - TimeSpan.FromHours(1))], Sunday);

        var qb = Starters.Quarterback(Vikings, injuries, 16, Sunday);

        Assert.Empty(injuries);
        Assert.Equal("Kyler Murray", qb.Name);
        Assert.Equal(Starters.LastGameBasis, qb.Basis);
    }

    [Fact]
    public void An_out_the_report_gave_this_week_is_honoured()
    {
        var injuries = BoardService.CurrentInjuries(
            [
                ("Kyler Murray", "Out", "Hamstring.", Sunday.AddDays(-2)),
                ("Carson Wentz", null, null, null)
            ],
            Sunday);

        var qb = Starters.Quarterback(Vikings, injuries, 16, Sunday);

        Assert.Equal("Carson Wentz", qb.Name);
        Assert.StartsWith("Kyler Murray is out; ", qb.Basis);
    }

    [Fact]
    public void A_status_with_no_time_against_it_does_not_hold()
        => Assert.Empty(BoardService.CurrentInjuries([("Kyler Murray", "Out", null, null)], Sunday));

    [Fact]
    public void The_last_quarterback_reads_no_report()
        => Assert.Equal("Kyler Murray", Starters.LastQuarterback(Vikings, 16, Sunday));

    [Theory]
    [InlineData("""{"C/ATT": "20/28"}""", 28)]
    [InlineData("""{"C/ATT": "0/0"}""", 0)]
    [InlineData("""{"CAR": "12"}""", 0)]
    [InlineData("not json", 0)]
    [InlineData("""{"C/ATT": 5}""", 0)]
    public void Attempts_reads_the_second_half_of_comp_att(string statLine, int attempts)
        => Assert.Equal(attempts, Starters.Attempts(statLine));
}
