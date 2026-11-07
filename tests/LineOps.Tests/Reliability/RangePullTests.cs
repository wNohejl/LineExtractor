using System.Net;
using System.Text.Json;
using LineOps.Core.Analytics;
using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Adapters;
using LineOps.Ingestion.Configuration;
using LineOps.Ingestion.Services;
using LineOps.Reliability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LineOps.Tests.Reliability;

/// <summary>
/// A date range pulled from ESPN (<see cref="IngestionJobs.EspnRange"/>): the calendar names the
/// game days, a past day goes through the box-score path and a day from today on through the
/// schedule path, and a day already complete — every final with its stats and a closing line —
/// is left alone unless a re-pull is asked for. ESPN is a fake handler here, with the real
/// adapter reading what it serves.
/// </summary>
[Collection(PostgresCollection.Name)]
public class RangePullTests(PostgresFixture fixture)
{
    /// <summary>One event the fake scoreboard lists.</summary>
    private sealed record Event(string Id, DateTimeOffset StartsAt, string Status, int? Home, int? Away);

    /// <summary>
    /// ESPN as the adapter reads it: a season calendar naming the days that have events, a
    /// scoreboard per day and a summary per final with a box and a close. Every URL is recorded.
    /// </summary>
    private sealed class FakeEspn : HttpMessageHandler
    {
        public Dictionary<DateOnly, List<Event>> Days { get; } = [];

        public List<string> Urls { get; } = [];

        public string Tag { get; } = Guid.NewGuid().ToString("N")[..8];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);

            var body = url.Contains("/calendar/ondays") ? Calendar()
                : url.Contains("/scoreboard") ? Scoreboard(DateOnly.ParseExact(url[(url.IndexOf("dates=") + 6)..][..8], "yyyyMMdd"))
                : url.Contains("/summary") ? Summary(url[(url.IndexOf("event=") + 6)..])
                : null;

            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }

        private string Calendar()
            => JsonSerializer.Serialize(new
            {
                startDate = "1990-01-01T08:00Z",
                endDate = "2100-01-01T08:00Z",
                eventDate = new
                {
                    type = "ondays",
                    // Midnight Pacific, as ESPN writes them.
                    dates = Days.Keys.Order().Select(d => $"{d:yyyy-MM-dd}T07:00Z").ToArray()
                }
            });

        private string Scoreboard(DateOnly day)
            => JsonSerializer.Serialize(new
            {
                events = Days.GetValueOrDefault(day, []).Select(e => new
                {
                    id = e.Id,
                    date = e.StartsAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mmZ"),
                    season = new { year = e.StartsAt.Year, type = 2 },
                    competitions = new[]
                    {
                        new
                        {
                            type = new { abbreviation = "STD" },
                            status = new { type = new { name = e.Status, state = e.Status == "STATUS_FINAL" ? "post" : "pre", shortDetail = "Final" } },
                            competitors = new[]
                            {
                                new { homeAway = "home", score = e.Home?.ToString() ?? "", team = new { id = $"h{Tag}", displayName = $"Home {Tag}", abbreviation = "HOM" } },
                                new { homeAway = "away", score = e.Away?.ToString() ?? "", team = new { id = $"a{Tag}", displayName = $"Away {Tag}", abbreviation = "AWY" } }
                            }
                        }
                    }
                })
            });

        private string Summary(string eventId)
            => JsonSerializer.Serialize(new
            {
                boxscore = new
                {
                    players = new[] { ("h", "Home"), ("a", "Away") }.Select(side => new
                    {
                        team = new { id = $"{side.Item1}{Tag}", displayName = $"{side.Item2} {Tag}" },
                        statistics = new[]
                        {
                            new
                            {
                                name = "passing",
                                labels = new[] { "YDS", "TD" },
                                athletes = new[]
                                {
                                    new { athlete = new { id = $"{side.Item1}p-{Tag}", displayName = $"{side.Item2} Passer {Tag}" }, stats = new[] { "250", "2" } }
                                }
                            }
                        }
                    })
                },
                pickcenter = new[]
                {
                    new
                    {
                        provider = new { name = "Test Book" },
                        moneyline = new { home = new { close = new { odds = "-150" } }, away = new { close = new { odds = "+130" } } },
                        total = new { over = new { close = new { line = "o44.5", odds = "-110" } }, under = new { close = new { line = "u44.5", odds = "-110" } } }
                    }
                },
                header = new { id = eventId }
            });
    }

    /// <summary>The real adapter under a source key of the test's own, so its rows are its own.</summary>
    private sealed class TaggedEspn(EspnStatsAdapter inner, string key) : IStatsSource, IGameDaySource
    {
        public string Key => key;

        public Task<StatsFetchResult> FetchScheduleAsync(string s, DateOnly d, CancellationToken ct) => inner.FetchScheduleAsync(s, d, ct);

        public Task<StatsFetchResult> FetchRosterAsync(string s, CancellationToken ct) => inner.FetchRosterAsync(s, ct);

        public Task<StatsFetchResult> FetchBoxScoresAsync(string s, DateOnly d, CancellationToken ct) => inner.FetchBoxScoresAsync(s, d, ct);

        public Task<GameDays> FetchGameDaysAsync(string s, DateOnly from, DateOnly to, CancellationToken ct)
            => inner.FetchGameDaysAsync(s, from, to, ct);
    }

    private async Task<(RangePull Pull, FakeEspn Espn, string SourceKey)> ArrangeAsync()
    {
        var espn = new FakeEspn();
        var sourceKey = $"espn-range-{espn.Tag}";

        await using (var db = fixture.CreateContext())
        {
            db.Sources.Add(new Source { Key = sourceKey, Name = "Fake ESPN", Kind = SourceKind.Stats, BaseUrl = "local://" });
            await db.SaveChangesAsync();
        }

        var http = new HttpClient(espn) { BaseAddress = new Uri("https://site.api.espn.com/apis/site/v2/sports/") };
        var source = new TaggedEspn(new EspnStatsAdapter(http, NullLogger<EspnStatsAdapter>.Instance), sourceKey);

        var services = new ServiceCollection();
        services.AddDbContext<LineOpsDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddLogging();
        services.AddScoped<EntityResolver>();
        services.AddScoped(sp => new BudgetCalculator(sp.GetRequiredService<LineOpsDbContext>()));
        services.AddScoped<CreditBudgetGuard>();
        services.AddScoped<StatsIngestionService>();
        services.AddScoped(_ => new SourceRegistry([], [source]));
        var provider = services.BuildServiceProvider();

        var options = new IngestionOptions { Sports = ["nfl"] };
        options.Backfill.RequestDelay = TimeSpan.Zero;

        return (new RangePull(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(options),
            NullLogger<RangePull>.Instance), espn, sourceKey);
    }

    /// <summary>A day of the test's own, years back and apart from every other test's.</summary>
    private static DateOnly DayOf(int year) => new(year, 10, 7 + Random.Shared.Next(0, 14));

    private static DateTimeOffset Evening(DateOnly day) => LeagueClock.StartOf(day).AddHours(20);

    private static string EventId() => Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString();

    private static async Task<List<Game>> GamesAsync(LineOpsDbContext db, string sourceKey)
        => (await db.Games.Include(g => g.Sport).ToListAsync())
            .Where(g => g.ExternalIds.ContainsKey(sourceKey))
            .ToList();

    [Fact]
    public async Task A_past_day_writes_its_finals_with_stats_and_closing_lines()
    {
        var (pull, espn, sourceKey) = await ArrangeAsync();
        var day = DayOf(2003);
        espn.Days[day] = [new Event(EventId(), Evening(day), "STATUS_FINAL", 24, 17), new Event(EventId(), Evening(day).AddHours(1), "STATUS_FINAL", 10, 13)];

        var summary = await pull.RunAsync(new RangePullRequest("nfl", day.AddDays(-2), day.AddDays(2)), null, CancellationToken.None);

        Assert.Null(summary.Refused);
        Assert.Equal(1, summary.DaysWalked);
        Assert.Equal(2, summary.Games);
        Assert.Equal(2, summary.Finals);
        Assert.Equal(4, summary.StatRows);
        Assert.Equal(8, summary.ClosingLines);
        // The calendar, the day's scoreboard and a summary a final.
        Assert.Equal(4, summary.Requests);

        await using var db = fixture.CreateContext();
        var games = await GamesAsync(db, sourceKey);
        Assert.Equal(2, games.Count);
        Assert.All(games, g => Assert.Equal(GameStatus.Final, g.Status));

        var ids = games.Select(g => g.Id).ToList();
        Assert.Equal(4, await db.PlayerGameStats.CountAsync(s => ids.Contains(s.GameId)));
        Assert.Equal(2, await db.ClosingLines.CountAsync(c => ids.Contains(c.GameId) && c.Market == Markets.Total && c.Outcome == "over"));

        var run = await db.IngestionRuns.OrderByDescending(r => r.Id).FirstAsync(r => r.Source!.Key == sourceKey);
        Assert.Equal(IngestionJobs.EspnRange, run.JobKey);
    }

    [Fact]
    public async Task Only_the_calendars_game_days_are_asked_of_the_scoreboard()
    {
        var (pull, espn, _) = await ArrangeAsync();
        var first = DayOf(2004);
        var second = first.AddDays(3);
        espn.Days[first] = [new Event(EventId(), Evening(first), "STATUS_FINAL", 3, 0)];
        espn.Days[second] = [new Event(EventId(), Evening(second), "STATUS_FINAL", 7, 6)];

        var summary = await pull.RunAsync(new RangePullRequest("nfl", first.AddDays(-1), second.AddDays(1)), null, CancellationToken.None);

        Assert.Equal(6, summary.DaysInRange);
        Assert.Equal(2, summary.GameDays);
        Assert.True(summary.FromCalendar);
        Assert.Equal(
            [$"dates={first:yyyyMMdd}", $"dates={second:yyyyMMdd}"],
            espn.Urls.Where(u => u.Contains("/scoreboard")).Select(u => u[u.IndexOf("dates=")..]).ToArray());
    }

    [Fact]
    public async Task A_day_already_complete_is_skipped_unless_a_repull_is_asked_for()
    {
        var (pull, espn, _) = await ArrangeAsync();
        var day = DayOf(2005);
        espn.Days[day] = [new Event(EventId(), Evening(day), "STATUS_FINAL", 21, 20)];
        var range = new RangePullRequest("nfl", day, day);

        await pull.RunAsync(range, null, CancellationToken.None);
        espn.Urls.Clear();

        var again = await pull.RunAsync(range, null, CancellationToken.None);

        Assert.Equal(0, again.DaysWalked);
        Assert.Equal(1, again.DaysSkipped);
        Assert.DoesNotContain(espn.Urls, u => u.Contains("/scoreboard"));

        var repulled = await pull.RunAsync(range with { Refresh = true }, null, CancellationToken.None);

        Assert.Equal(1, repulled.DaysWalked);
        Assert.Equal(0, repulled.DaysSkipped);
        Assert.Contains(espn.Urls, u => u.Contains("/scoreboard"));
    }

    [Fact]
    public async Task A_final_missing_its_close_keeps_its_day_owed()
    {
        var (pull, espn, sourceKey) = await ArrangeAsync();
        var day = DayOf(2006);
        espn.Days[day] = [new Event(EventId(), Evening(day), "STATUS_FINAL", 21, 20)];
        var range = new RangePullRequest("nfl", day, day);

        await pull.RunAsync(range, null, CancellationToken.None);

        await using (var db = fixture.CreateContext())
        {
            var ids = (await GamesAsync(db, sourceKey)).Select(g => g.Id).ToList();
            await db.ClosingLines.Where(c => ids.Contains(c.GameId)).ExecuteDeleteAsync();
        }

        var again = await pull.RunAsync(range, null, CancellationToken.None);

        Assert.Equal(1, again.DaysWalked);
        Assert.Equal(4, again.ClosingLines);
    }

    [Fact]
    public async Task A_held_final_is_not_walked_back_by_a_scoreboard_that_has_lost_it()
    {
        var (pull, espn, sourceKey) = await ArrangeAsync();
        var day = DayOf(2007);
        var id = EventId();
        espn.Days[day] = [new Event(id, Evening(day), "STATUS_FINAL", 27, 24)];
        var range = new RangePullRequest("nfl", day, day, Refresh: true);

        await pull.RunAsync(range, null, CancellationToken.None);

        // ESPN now lists the game as scheduled, with no score.
        espn.Days[day] = [new Event(id, Evening(day), "STATUS_SCHEDULED", null, null)];
        await pull.RunAsync(range, null, CancellationToken.None);

        await using var db = fixture.CreateContext();
        var game = Assert.Single(await GamesAsync(db, sourceKey));
        Assert.Equal(GameStatus.Final, game.Status);
        Assert.Equal(27, game.HomeScore);
        Assert.Equal(24, game.AwayScore);
    }

    [Fact]
    public async Task A_day_from_today_on_reads_only_the_schedule()
    {
        var (pull, espn, sourceKey) = await ArrangeAsync();
        var day = LeagueClock.Today().AddDays(20);
        espn.Days[day] = [new Event(EventId(), Evening(day), "STATUS_SCHEDULED", null, null)];

        var summary = await pull.RunAsync(new RangePullRequest("nfl", day, day), null, CancellationToken.None);

        Assert.Equal(1, summary.DaysWalked);
        Assert.Equal(1, summary.Games);
        Assert.Equal(0, summary.Finals);
        Assert.DoesNotContain(espn.Urls, u => u.Contains("/summary"));

        await using var db = fixture.CreateContext();
        Assert.Equal(GameStatus.Scheduled, Assert.Single(await GamesAsync(db, sourceKey)).Status);
    }

    [Fact]
    public async Task A_range_wider_than_the_cap_is_refused_before_a_request()
    {
        var (pull, espn, _) = await ArrangeAsync();
        var from = new DateOnly(2008, 3, 1);

        var summary = await pull.RunAsync(new RangePullRequest("nfl", from, from.AddDays(RangePull.MaxDays)), null, CancellationToken.None);

        Assert.NotNull(summary.Refused);
        Assert.Contains($"{RangePull.MaxDays}", summary.Refused);
        Assert.Empty(espn.Urls);
    }

    [Fact]
    public async Task A_league_that_is_not_configured_is_refused()
    {
        var (pull, espn, _) = await ArrangeAsync();

        var summary = await pull.RunAsync(new RangePullRequest("mlb", new DateOnly(2008, 4, 1), new DateOnly(2008, 4, 2)), null, CancellationToken.None);

        Assert.Contains("not a configured league", summary.Refused);
        Assert.Empty(espn.Urls);
    }

    [Theory]
    [InlineData("", "2024-09-01", "2024-09-30", "Name a league")]
    [InlineData("nfl", "2024-09-30", "2024-09-01", "after")]
    [InlineData("nfl", "2024-01-01", "2024-12-31", "at most")]
    public void Check_names_what_is_wrong_with_a_range(string sport, string from, string to, string expected)
        => Assert.Contains(expected, RangePull.Check(sport, DateOnly.Parse(from), DateOnly.Parse(to)));

    [Fact]
    public void Check_passes_a_range_of_one_season()
        => Assert.Null(RangePull.Check("nfl", new DateOnly(2024, 9, 1), new DateOnly(2024, 9, 30)));

    [Fact]
    public void A_held_final_keeps_its_status_and_score_against_a_scoreboard_that_says_otherwise()
    {
        var board = new CanonicalGame("1", "nfl", "Home", "Away", DateTimeOffset.UnixEpoch, "scheduled", null, null);
        var corrected = board with { SourceGameId = "2", Status = "final", HomeScore = 10, AwayScore = 3 };

        var kept = StatsIngestionService.KeepHeldFinals([board, corrected],
            new Dictionary<string, GameStatus> { ["1"] = GameStatus.Final, ["2"] = GameStatus.Final });

        Assert.Null(kept[0].Status);
        Assert.Null(kept[0].HomeScore);
        // A final restated with a corrected score is the results sweep's correction, and stands.
        Assert.Equal(corrected, kept[1]);
    }
}
