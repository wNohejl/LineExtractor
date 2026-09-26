using LineOps.Core.Analytics;
using LineOps.Core.Entities;

namespace LineOps.Tests.Analytics;

/// <summary>
/// The rules that stand in for the provider's season stamp. They fill the column for rows
/// written before it existed and for any source that carries no season block, so they have
/// to agree with what ESPN would have said — a February playoff is the prior year's season.
/// </summary>
public class SeasonCalendarTests
{
    private static DateTimeOffset At(string date) => DateTimeOffset.Parse(date + "T18:00:00Z");

    [Theory]
    [InlineData("nfl", "2025-09-04", 2025)] // kickoff night
    [InlineData("nfl", "2026-01-10", 2025)] // wild-card weekend
    [InlineData("nfl", "2026-02-08", 2025)] // Super Bowl LX belongs to the 2025 season
    [InlineData("nfl", "2026-09-10", 2026)]
    [InlineData("mlb", "2026-03-25", 2026)] // opening day
    [InlineData("mlb", "2026-10-30", 2026)] // the World Series is the same year
    [InlineData("nba", "2025-10-21", 2025)]
    [InlineData("nba", "2026-06-14", 2025)] // June finals belong to the season that started in October
    [InlineData("nhl", "2026-04-20", 2025)]
    [InlineData("NFL", "2025-11-16", 2025)] // keys are case-insensitive
    public void A_season_is_named_by_the_year_its_sport_uses(string sport, string date, int expected)
        => Assert.Equal(expected, SeasonCalendar.YearOf(sport, At(date)));

    [Theory]
    [InlineData("nfl", "2026-01-04", SeasonType.Regular)]    // 2025 season, week 18
    [InlineData("nfl", "2026-01-05", SeasonType.Regular)]    // 2025 season, week 18 Monday
    [InlineData("nfl", "2026-01-10", SeasonType.Postseason)] // 2025 season, wild card
    [InlineData("nfl", "2026-02-08", SeasonType.Postseason)]
    [InlineData("nfl", "2025-09-04", SeasonType.Regular)]
    [InlineData("nfl", "2027-01-10", SeasonType.Regular)]    // 2026 season, week 18 — a week later than 2025's
    [InlineData("nfl", "2027-01-11", SeasonType.Regular)]    // 2026 season, week 18 Monday
    [InlineData("nfl", "2027-01-16", SeasonType.Postseason)] // 2026 season, wild card
    [InlineData("mlb", "2026-09-30", SeasonType.Regular)]
    [InlineData("mlb", "2026-10-01", SeasonType.Postseason)]
    [InlineData("nba", "2026-03-01", SeasonType.Regular)]
    [InlineData("nba", "2026-05-01", SeasonType.Postseason)]
    [InlineData("nhl", "2026-06-10", SeasonType.Postseason)]
    public void The_part_of_the_season_follows_the_boundaries_that_have_held_for_years(
        string sport, string date, SeasonType expected)
        => Assert.Equal(expected, SeasonCalendar.TypeOf(sport, At(date)));

    [Theory]
    [InlineData("nfl", 2025, "2025")]
    [InlineData("mlb", 2026, "2026")]
    [InlineData("nba", 2025, "2025–26")]
    [InlineData("nhl", 2025, "2025–26")]
    public void A_season_is_written_the_way_its_league_writes_it(string sport, int year, string expected)
        => Assert.Equal(expected, SeasonCalendar.Label(sport, year));

    [Theory]
    [InlineData(2025, "2025-09-04")]
    [InlineData(2026, "2026-09-10")]
    [InlineData(2024, "2024-09-05")]
    public void Kickoff_is_the_Thursday_after_Labor_Day(int season, string expected)
        => Assert.Equal(DateOnly.Parse(expected), SeasonCalendar.NflKickoff(season));

    [Fact]
    public void The_current_season_follows_the_same_rule_as_any_other_date()
    {
        Assert.Equal(2025, SeasonCalendar.CurrentYear("nfl", At("2026-01-20")));
        Assert.Equal(2026, SeasonCalendar.CurrentYear("mlb", At("2026-01-20")));
    }

    [Theory]
    [InlineData("nfl", "2027-01-12T01:15:00Z", SeasonType.Regular)]  // week 18 Monday night, 8:15pm Eastern
    [InlineData("mlb", "2026-10-01T01:40:00Z", SeasonType.Regular)]  // 30 September, 9:40pm Eastern
    public void An_evening_game_belongs_to_the_day_it_is_played_in_the_United_States(
        string sport, string startsAt, SeasonType expected)
        => Assert.Equal(expected, SeasonCalendar.TypeOf(sport, DateTimeOffset.Parse(startsAt)));

    [Theory]
    [InlineData("mlb", 2026, SeasonType.Regular, 2430)]
    [InlineData("MLB", 2026, SeasonType.Regular, 2430)]
    [InlineData("nfl", 2025, SeasonType.Regular, 272)]
    [InlineData("nfl", 2020, SeasonType.Regular, 256)]
    [InlineData("nfl", 2025, SeasonType.Postseason, 13)]
    [InlineData("nfl", 2019, SeasonType.Postseason, 11)]
    public void A_league_that_fixes_its_schedule_has_an_expected_count(
        string sport, int year, SeasonType type, int expected)
        => Assert.Equal(expected, SeasonCalendar.ExpectedGames(sport, year, type));

    [Theory]
    // How long MLB's postseason runs depends on how many series go the distance.
    [InlineData("mlb", SeasonType.Postseason)]
    [InlineData("nba", SeasonType.Regular)]
    [InlineData("nfl", SeasonType.Preseason)]
    public void A_schedule_nobody_fixes_has_no_expectation(string sport, SeasonType type)
        => Assert.Null(SeasonCalendar.ExpectedGames(sport, 2026, type));
}
