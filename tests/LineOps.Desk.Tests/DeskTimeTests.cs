using System.Globalization;

namespace LineOps.Desk.Tests;

/// <summary>
/// The formats the desk shows a moment in, read in the clock's zone and never the host's. A
/// reading that names a clock time carries the clock's label; a bare day does not.
/// </summary>
public class DeskTimeTests
{
    private static readonly DeskClock Fixed = new(
        TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(-5), "Test", "TST"), "TST");

    private static readonly DateTimeOffset Moment = DateTimeOffset.Parse("2026-09-23T22:40:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void A_stamp_names_its_zone()
        => Assert.Equal("Sep 23 17:40 TST", Fixed.Stamp(Moment));

    [Fact]
    public void A_day_is_bare()
        // 03:10Z on the 24th is still the 23rd five hours west, and the day carries no label.
        => Assert.Equal("Sep 23", Fixed.Day(DateTimeOffset.Parse("2026-09-24T03:10:00Z", CultureInfo.InvariantCulture)));

    [Fact]
    public void Format_uses_the_clock_s_zone()
        => Assert.Equal("17:40", Fixed.Format(Moment, "HH:mm"));

    [Fact]
    public void Utc_says_so()
    {
        var stamp = DeskClock.Utc.Stamp(Moment);

        Assert.Equal("Sep 23 22:40 UTC", stamp);
        Assert.EndsWith(" UTC", stamp, StringComparison.Ordinal);
    }
}
