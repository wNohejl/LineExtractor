using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Configuration;
using LineOps.Ingestion.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LineOps.Tests.Reliability;

/// <summary>
/// The results sweep has to heal an outage longer than the window it looks back over.
///
/// <para>
/// It used to look back three days. The host was down from 31 August to 3 September 2026,
/// and by the time it returned the 30 August games had aged out of the window: never owed
/// again, never swept, three of them sitting at Live for good. A sweep that promises to be
/// self-healing cannot have a horizon shorter than the outage it is healing from.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class ResultsOwedTests(PostgresFixture fixture)
{
    private IngestionJobs Jobs(IngestionOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<LineOpsDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        var provider = services.BuildServiceProvider();

        return new IngestionJobs(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options ?? new IngestionOptions()),
            NullLogger<IngestionJobs>.Instance);
    }

    private static Task<DateOnly> SeedGameAsync(LineOpsDbContext db, int daysAgo, GameStatus status)
        => SeedGameAsync(db, DateTimeOffset.UtcNow.AddDays(-daysAgo), status);

    private static async Task<DateOnly> SeedGameAsync(LineOpsDbContext db, DateTimeOffset startsAt, GameStatus status)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var sport = new Sport { Key = $"owed-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = $"Home {suffix}", Abbrev = "HOM" };
        var away = new Team { SportId = sport.Id, Name = $"Away {suffix}", Abbrev = "AWY" };
        db.Teams.AddRange(home, away);
        await db.SaveChangesAsync();

        db.Games.Add(new Game
        {
            SportId = sport.Id,
            HomeTeamId = home.Id,
            AwayTeamId = away.Id,
            StartsAt = startsAt,
            Status = status,
            SeasonYear = startsAt.Year
        });
        await db.SaveChangesAsync();

        return LeagueClock.DateOf(startsAt);
    }

    [Fact]
    public async Task A_game_left_unfinished_by_a_five_day_outage_is_still_owed()
    {
        await using var db = fixture.CreateContext();
        var day = await SeedGameAsync(db, daysAgo: 5, GameStatus.Live);

        var owed = await Jobs().DatesAwaitingResultsAsync(TimeSpan.FromHours(4));

        Assert.Contains(day, owed);
    }

    [Fact]
    public async Task A_late_game_is_owed_on_the_day_its_scoreboard_lists_it()
    {
        // 10:10pm Eastern five days ago: already the next day in UTC. ESPN lists it on the
        // evening's scoreboard, so that is the day the sweep has to ask for — asking for the
        // UTC day is how three games sat at Live for a week in September 2026.
        var evening = LeagueClock.StartOf(LeagueClock.Today().AddDays(-5)).AddHours(22).AddMinutes(10);

        await using var db = fixture.CreateContext();
        var day = await SeedGameAsync(db, evening, GameStatus.Live);

        var owed = await Jobs().DatesAwaitingResultsAsync(TimeSpan.FromHours(4));

        Assert.Equal(DateOnly.FromDateTime(evening.UtcDateTime).AddDays(-1), day);
        Assert.Contains(day, owed);
    }

    [Fact]
    public async Task A_game_older_than_the_lookback_is_left_to_the_backfill()
    {
        await using var db = fixture.CreateContext();
        var day = await SeedGameAsync(db, daysAgo: 60, GameStatus.Live);

        var owed = await Jobs().DatesAwaitingResultsAsync(TimeSpan.FromHours(4));

        Assert.DoesNotContain(day, owed);
    }

    [Fact]
    public async Task The_lookback_is_the_operators_to_set()
    {
        await using var db = fixture.CreateContext();
        var day = await SeedGameAsync(db, daysAgo: 60, GameStatus.Live);

        var options = new IngestionOptions();
        options.GamePasses.ResultsLookback = TimeSpan.FromDays(90);

        var owed = await Jobs(options).DatesAwaitingResultsAsync(TimeSpan.FromHours(4));

        Assert.Contains(day, owed);
    }

    [Fact]
    public async Task A_final_with_no_box_score_is_owed_in_a_sport_we_ingest()
    {
        // 22 September 2026: the live poll saw every game go final from the scoreboard, and the
        // box scores were never fetched because a final game was never owed.
        await using var db = fixture.CreateContext();
        var day = await SeedGameAsync(db, daysAgo: 4, GameStatus.Final);
        var key = (await db.Games.Include(g => g.Sport).OrderByDescending(g => g.Id).FirstAsync()).Sport!.Key;

        var options = new IngestionOptions { Sports = [key] };
        var owed = await Jobs(options).DatesAwaitingResultsAsync(TimeSpan.FromHours(4));

        Assert.Contains(day, owed);
    }

    /// <summary>A final in a fresh sport holding one stat line, written under a run of <paramref name="jobKey"/>.</summary>
    private static async Task<Game> FinalWithLineAsync(LineOpsDbContext db, string jobKey)
    {
        await SeedGameAsync(db, daysAgo: 4, GameStatus.Final);
        var game = await db.Games.Include(g => g.Sport).OrderByDescending(g => g.Id).FirstAsync();

        var source = new Source { Key = $"owed-src-{Guid.NewGuid():N}", Name = "Test stats", Kind = SourceKind.Stats, BaseUrl = "local://test" };
        var player = new Player { SportId = game.SportId, FullName = $"Player {Guid.NewGuid():N}" };
        db.Sources.Add(source);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        // Dated now: a source whose last success is days old is a freshness alert in every other test.
        var run = new IngestionRun
        {
            SourceId = source.Id,
            JobKey = jobKey,
            Status = RunStatus.Success,
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow
        };
        db.IngestionRuns.Add(run);
        await db.SaveChangesAsync();

        db.PlayerGameStats.Add(new PlayerGameStat
        {
            GameId = game.Id,
            PlayerId = player.Id,
            SourceId = source.Id,
            CapturedAt = game.StartsAt,
            IngestionRunId = run.Id
        });
        await db.SaveChangesAsync();
        return game;
    }

    [Fact]
    public async Task A_final_holding_only_live_box_lines_is_owed()
    {
        await using var db = fixture.CreateContext();
        var game = await FinalWithLineAsync(db, IngestionJobs.EspnGamecastLive);

        Assert.True(await IngestionJobs.StatsOwed(db, [game.Sport!.Key]).AnyAsync(g => g.Id == game.Id));

        var owed = await Jobs(new IngestionOptions { Sports = [game.Sport!.Key] })
            .DatesAwaitingResultsAsync(TimeSpan.FromHours(4));
        Assert.Contains(LeagueClock.DateOf(game.StartsAt), owed);
    }

    [Fact]
    public async Task A_final_holding_the_results_sweeps_lines_is_not_owed()
    {
        await using var db = fixture.CreateContext();
        var game = await FinalWithLineAsync(db, IngestionJobs.EspnResults);

        Assert.False(await IngestionJobs.StatsOwed(db, [game.Sport!.Key]).AnyAsync(g => g.Id == game.Id));
    }

    [Fact]
    public async Task A_final_game_is_never_owed()
    {
        await using var db = fixture.CreateContext();
        var day = await SeedGameAsync(db, daysAgo: 5, GameStatus.Final);

        var owed = await Jobs().DatesAwaitingResultsAsync(TimeSpan.FromHours(4));

        // Another test may have left an unfinished game on the same calendar day, so the
        // assertion is on this game's status, not on the day's absence outright.
        var unfinishedThatDay = await db.Games.CountAsync(g =>
            g.Status != GameStatus.Final && g.Status != GameStatus.Postponed
            && g.StartsAt >= DateTimeOffset.UtcNow.AddDays(-5).Date
            && g.StartsAt < DateTimeOffset.UtcNow.AddDays(-4).Date);

        if (unfinishedThatDay == 0)
            Assert.DoesNotContain(day, owed);
    }
}
