using LineOps.Core.Analytics;

namespace LineOps.Tests.Analytics;

/// <summary>
/// The day the leagues keep. ESPN's scoreboard is addressed by it, so a game that starts at
/// 10:10pm Eastern is on that evening's board, not on the next day's — which is what UTC says.
/// </summary>
public class LeagueClockTests
{
    [Theory]
    [InlineData("2026-09-05T02:10:00Z", "2026-09-04")] // 10:10pm EDT: the evening before, in UTC's next day
    [InlineData("2026-09-15T00:15:00Z", "2026-09-14")] // Monday Night Football
    [InlineData("2026-09-14T03:59:00Z", "2026-09-13")] // one minute before Eastern midnight
    [InlineData("2026-09-14T04:00:00Z", "2026-09-14")] // Eastern midnight, daylight time
    [InlineData("2026-12-01T04:59:00Z", "2026-11-30")] // standard time: midnight is 05:00 UTC
    [InlineData("2026-12-01T05:00:00Z", "2026-12-01")]
    [InlineData("2026-09-13T17:00:00Z", "2026-09-13")] // a 1pm kickoff is the same day either way
    public void A_moment_falls_on_the_scoreboard_day_of_the_United_States(string instant, string expected)
        => Assert.Equal(DateOnly.Parse(expected), LeagueClock.DateOf(DateTimeOffset.Parse(instant)));

    [Fact]
    public void Today_is_the_Eastern_date_even_after_UTC_has_moved_on()
    {
        var clock = new At(DateTimeOffset.Parse("2026-09-22T01:30:00Z")); // 9:30pm on the 21st

        Assert.Equal(new DateOnly(2026, 9, 21), LeagueClock.Today(clock));
    }

    [Theory]
    [InlineData("2026-09-14", "2026-09-14T04:00:00Z")]
    [InlineData("2026-12-01", "2026-12-01T05:00:00Z")]
    [InlineData("2026-03-08", "2026-03-08T05:00:00Z")] // the spring-forward day still starts in standard time
    [InlineData("2026-11-01", "2026-11-01T04:00:00Z")] // the fall-back day still starts in daylight time
    public void A_day_starts_at_Eastern_midnight(string date, string expected)
        => Assert.Equal(DateTimeOffset.Parse(expected), LeagueClock.StartOf(DateOnly.Parse(date)));

    private sealed class At(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
