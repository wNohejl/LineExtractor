using System.Globalization;
using Bunit;
using LineOps.Core.Analytics;
using LineOps.Desk;
using LineOps.Desk.Windowing;
using LineOps.Web.Components;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// The desk tells time in the league's zone, wherever the server runs.
///
/// Blazor Server formats on the server. With <c>ToLocalTime()</c> that meant the host's zone: a
/// 6:40pm Eastern first pitch read 18:40 under <c>dotnet run</c> on the desktop and 22:40 in the
/// web container, whose zone is UTC. Nothing here depends on the zone of the machine running
/// the tests, which is the point — these pass identically in the SDK container and on Windows.
/// </summary>
public class DisplayTimeTests : DeskTestContext
{
    private static readonly DateTimeOffset FirstPitch = DateTimeOffset.Parse("2026-09-23T22:40:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void A_22_40Z_start_reads_18_40_ET()
        => Assert.Equal("Sep 23 18:40 ET", DisplayTime.Stamp(FirstPitch));

    [Fact]
    public void Winter_starts_read_in_standard_time()
        // 23:40Z in January is 18:40 EST: the offset follows the date, not the season it is now.
        => Assert.Equal("Jan 10 18:40 ET", DisplayTime.Stamp(DateTimeOffset.Parse("2026-01-10T23:40:00Z", CultureInfo.InvariantCulture)));

    [Fact]
    public void An_evening_game_is_on_the_league_day_not_the_UTC_day()
        // 8:10pm Eastern on the 4th is 00:10Z on the 5th; the scoreboard lists it on the 4th.
        => Assert.Equal("Sep 4", DisplayTime.Day(DateTimeOffset.Parse("2026-09-05T00:10:00Z", CultureInfo.InvariantCulture)));

    [Fact]
    public void The_footer_clock_shows_the_desk_zone_and_names_it()
    {
        Services.AddSingleton(new DeskClock(LeagueClock.Zone, DisplayTime.Label) { Time = new At(FirstPitch) });

        var cut = RenderComponent<DeskFooter>();

        Assert.Contains("18:40 ET", cut.Find(".ftr__stat.num[title^='Desk time']").TextContent);
    }

    [Fact]
    public void A_desk_told_no_zone_shows_UTC_and_says_so()
    {
        Services.AddSingleton(DeskClock.Utc with { Time = new At(FirstPitch) });

        var cut = RenderComponent<DeskFooter>();

        Assert.Contains("22:40 UTC", cut.Find(".ftr__stat.num[title^='Desk time']").TextContent);
    }

    private sealed class At(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
