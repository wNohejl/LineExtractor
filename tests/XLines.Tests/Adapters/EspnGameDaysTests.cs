using System.Net;
using System.Text.Json;
using XLines.Core.Contracts;
using XLines.Ingestion.Adapters;
using XLines.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace XLines.Tests.Adapters;

/// <summary>
/// The league days a date range holds, from ESPN's core calendar (<c>calendar/ondays</c>): one
/// small request a season year, so a range pull asks the scoreboard only for days with games.
/// The site scoreboard refuses a range (<c>dates=20251001-20251031</c> is a 400, measured
/// 7 November 2026), which is why the calendar is read at all. Its dates are midnight Pacific —
/// <c>2024-09-05T07:00Z</c> — and are read as the league day the scoreboard is addressed by.
/// </summary>
public class EspnGameDaysTests
{
    private static (IGameDaySource Source, StubHttp Handler) Adapter(Func<string, (HttpStatusCode, string)> respond)
    {
        var handler = new StubHttp(respond);
        var http = handler.Client("https://site.api.espn.com/apis/site/v2/sports/");
        return (new EspnStatsAdapter(http, NullLogger<EspnStatsAdapter>.Instance), handler);
    }

    /// <summary>The two NFL seasons the fixtures hold, by the year asked for.</summary>
    private static (HttpStatusCode, string) Nfl(string url)
        => url.Contains("dates=2024") ? (HttpStatusCode.OK, Fixtures.Text("espn.nfl.calendar.2024.json"))
            : url.Contains("dates=2025") ? (HttpStatusCode.OK, Fixtures.Text("espn.nfl.calendar.2025.json"))
            : (HttpStatusCode.NotFound, "{}");

    [Fact]
    public void ParseCalendar_reads_each_instant_as_the_league_day()
    {
        using var doc = JsonDocument.Parse(Fixtures.Text("espn.nfl.calendar.2024.json"));

        var calendar = EspnStatsAdapter.ParseCalendar(doc.RootElement);

        Assert.NotNull(calendar);
        Assert.Equal(new DateOnly(2024, 8, 1), calendar.Value.Start);
        Assert.Equal(new DateOnly(2025, 2, 15), calendar.Value.End);
        Assert.Contains(new DateOnly(2024, 9, 5), calendar.Value.Days);
        Assert.Contains(new DateOnly(2025, 1, 5), calendar.Value.Days);
        Assert.DoesNotContain(new DateOnly(2024, 9, 7), calendar.Value.Days);
    }

    [Theory]
    [InlineData("""{ "code": 400 }""")]
    [InlineData("""{ "eventDate": { "type": "ondays" } }""")]
    [InlineData("""{ "eventDate": { "dates": "2024-09-05T07:00Z" } }""")]
    [InlineData("""{ "eventDate": { "dates": [ "not a date", 7 ] } }""")]
    public void ParseCalendar_is_null_for_a_shape_it_does_not_know(string json)
    {
        using var doc = JsonDocument.Parse(json);

        Assert.Null(EspnStatsAdapter.ParseCalendar(doc.RootElement));
    }

    [Fact]
    public async Task A_range_inside_one_season_is_one_request_and_only_its_game_days()
    {
        var (source, handler) = Adapter(Nfl);

        var days = await source.FetchGameDaysAsync("nfl", new DateOnly(2024, 9, 5), new DateOnly(2024, 9, 10), CancellationToken.None);

        Assert.True(days.FromCalendar);
        Assert.Equal([new(2024, 9, 5), new(2024, 9, 6), new(2024, 9, 8), new DateOnly(2024, 9, 9)], days.Days);
        Assert.Equal(1, days.Cost.Requests);

        var url = Assert.Single(handler.Urls);
        Assert.Equal("https://sports.core.api.espn.com/v2/sports/football/leagues/nfl/calendar/ondays?dates=2024", url);
    }

    [Fact]
    public async Task January_reads_the_season_that_began_the_year_before()
    {
        // The 2025 calendar starts in July 2025; the January playoffs are the 2024 season's.
        var (source, handler) = Adapter(Nfl);

        var days = await source.FetchGameDaysAsync("nfl", new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), CancellationToken.None);

        Assert.True(days.FromCalendar);
        Assert.Equal([new(2025, 1, 4), new DateOnly(2025, 1, 5)], days.Days);
        Assert.Equal(2, days.Cost.Requests);
        Assert.Contains(handler.Urls, u => u.EndsWith("dates=2024"));
        Assert.Contains(handler.Urls, u => u.EndsWith("dates=2025"));
    }

    /// <summary>
    /// The NBA's seasons as ESPN names them, by the year they end: <c>dates=2026</c> is 2025–26,
    /// to June 2026, and the 2026–27 season opening 20 October 2026 is <c>dates=2027</c>.
    /// </summary>
    private static (HttpStatusCode, string) Nba(string url)
        => url.Contains("dates=2026")
            ? (HttpStatusCode.OK, """{ "startDate": "2025-10-01T07:00Z", "endDate": "2026-06-27T06:59Z", "eventDate": { "dates": [ "2025-10-21T07:00Z", "2026-06-13T07:00Z" ] } }""")
            : url.Contains("dates=2027")
                ? (HttpStatusCode.OK, """{ "startDate": "2026-09-30T07:00Z", "endDate": "2027-06-26T06:59Z", "eventDate": { "dates": [ "2026-10-20T07:00Z", "2026-10-21T07:00Z", "2026-10-23T07:00Z" ] } }""")
                : (HttpStatusCode.NotFound, "{}");

    [Fact]
    public async Task An_autumn_range_reads_the_season_named_for_the_year_it_ends()
    {
        // The NBA's 2026 calendar closes in June 2026; October 2026 is only in the 2027 one.
        var (source, handler) = Adapter(Nba);

        var days = await source.FetchGameDaysAsync("nba", new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 24), CancellationToken.None);

        Assert.True(days.FromCalendar);
        Assert.Equal([new(2026, 10, 20), new(2026, 10, 21), new DateOnly(2026, 10, 23)], days.Days);
        Assert.Equal(2, days.Cost.Requests);
        Assert.Contains(handler.Urls, u => u.EndsWith("dates=2026"));
        Assert.Contains(handler.Urls, u => u.EndsWith("dates=2027"));
    }

    [Fact]
    public async Task A_calendar_that_will_not_answer_falls_back_to_every_day()
    {
        var (source, _) = Adapter(_ => (HttpStatusCode.InternalServerError, "{}"));

        var days = await source.FetchGameDaysAsync("nfl", new DateOnly(2024, 9, 5), new DateOnly(2024, 9, 7), CancellationToken.None);

        Assert.False(days.FromCalendar);
        Assert.Equal([new(2024, 9, 5), new(2024, 9, 6), new DateOnly(2024, 9, 7)], days.Days);
        Assert.Equal(1, days.Cost.Requests);
    }

    [Fact]
    public async Task An_odd_shape_falls_back_to_every_day()
    {
        var (source, _) = Adapter(_ => (HttpStatusCode.OK, """{ "items": [] }"""));

        var days = await source.FetchGameDaysAsync("mlb", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 3), CancellationToken.None);

        Assert.False(days.FromCalendar);
        Assert.Equal(3, days.Days.Count);
    }

    [Fact]
    public async Task An_unmapped_league_asks_nothing_and_has_no_days()
    {
        var (source, handler) = Adapter(Nfl);

        var days = await source.FetchGameDaysAsync("cricket", new DateOnly(2024, 9, 5), new DateOnly(2024, 9, 7), CancellationToken.None);

        Assert.Empty(days.Days);
        Assert.Empty(handler.Urls);
    }
}
