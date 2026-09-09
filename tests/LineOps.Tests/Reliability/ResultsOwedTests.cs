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

    private static async Task<DateOnly> SeedGameAsync(LineOpsDbContext db, int daysAgo, GameStatus status)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var sport = new Sport { Key = $"owed-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = $"Home {suffix}", Abbrev = "HOM" };
        var away = new Team { SportId = sport.Id, Name = $"Away {suffix}", Abbrev = "AWY" };
        db.Teams.AddRange(home, away);
        await db.SaveChangesAsync();

        var startsAt = DateTimeOffset.UtcNow.AddDays(-daysAgo);

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

        return DateOnly.FromDateTime(startsAt.UtcDateTime);
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
