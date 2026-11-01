using System.Text.Json.Nodes;
using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LineOps.Tests.Reliability;

/// <summary>
/// The live state on <see cref="Game"/>, written change-only. The live poll runs every few
/// seconds while a game is on and a write is what raises the desk's change notice, so a poll
/// that saw nothing new must write nothing — and one that saw the clock move must write once.
/// </summary>
[Collection(PostgresCollection.Name)]
public class LiveGameStateIntegrationTests(PostgresFixture fixture)
{
    /// <summary>Counts the rows each <c>SaveChanges</c> wrote.</summary>
    private sealed class RowCounter : SaveChangesInterceptor
    {
        public int Rows { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Rows += result;
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>A fresh context, the way each poll gets one, counting what it writes.</summary>
    private LineOpsDbContext Context(RowCounter counter)
        => new(new DbContextOptionsBuilder<LineOpsDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(counter)
            .Options);

    private static async Task<Sport> SeedSportAsync(LineOpsDbContext db)
    {
        var sport = new Sport { Key = $"live-{Guid.NewGuid():N}"[..12], Name = "TEST" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();
        return sport;
    }

    /// <summary>A start on the whole second: Postgres keeps microseconds, a tick would read back as a moved start.</summary>
    private static DateTimeOffset Hour(int offset)
        => new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, DateTime.UtcNow.Hour, 0, 0, TimeSpan.Zero).AddHours(offset);

    private static CanonicalGame LiveGame(string sportKey, DateTimeOffset startsAt, string clock = "8:26", int seconds = 506)
        => new(
            "401873032", sportKey, "Los Angeles Rams", "Los Angeles Chargers", startsAt, "live", 3, 6,
            Home: new CanonicalTeamRef("Los Angeles Rams", "14", "LAR"),
            Away: new CanonicalTeamRef("Los Angeles Chargers", "24", "LAC"),
            Live: new CanonicalLive(
                Period: 2, DisplayClock: clock, ClockSeconds: seconds, StatusDetail: $"{clock} - 2nd",
                HomeLinescore: [3, 0], AwayLinescore: [3, 3],
                PossessionSourceTeamId: "24", Down: 1, Distance: 10, YardLine: 80,
                DownDistanceText: "1st & 10 at LAC 20", PossessionText: "LAC 20", IsRedZone: false,
                HomeTimeouts: 3, AwayTimeouts: 2, LastPlay: "Official Timeout at 08:26.",
                SituationJson: "{\"balls\":2,\"strikes\":1,\"onFirst\":true}"));

    [Fact]
    public async Task TheLiveStateLandsOnTheGameWithPossessionResolvedToOurTeam()
    {
        await using var db = fixture.CreateContext();
        var sport = await SeedSportAsync(db);
        var startsAt = Hour(-1);

        var game = await new EntityResolver(db).ResolveGameAsync(sport, "espn", LiveGame(sport.Key, startsAt), CancellationToken.None);

        await using var read = fixture.CreateContext();
        var row = await read.Games.Include(g => g.AwayTeam).SingleAsync(g => g.Id == game.Id);

        Assert.Equal(2, row.Period);
        Assert.Equal("8:26", row.DisplayClock);
        Assert.Equal(506, row.ClockSeconds);
        Assert.Equal("8:26 - 2nd", row.StatusDetail);
        Assert.Equal(row.AwayTeamId, row.PossessionTeamId);
        Assert.Equal(1, row.Down);
        Assert.Equal(10, row.Distance);
        Assert.Equal(80, row.YardLine);
        Assert.Equal("1st & 10 at LAC 20", row.DownDistanceText);
        Assert.Equal("LAC 20", row.PossessionText);
        Assert.False(row.IsRedZone);
        Assert.Equal(3, row.HomeTimeouts);
        Assert.Equal(2, row.AwayTimeouts);
        Assert.Equal("Official Timeout at 08:26.", row.LastPlay);
        Assert.NotNull(row.LiveUpdatedAt);

        var linescore = JsonNode.Parse(row.Linescore!)!;
        Assert.Equal("[3,0]", linescore["home"]!.ToJsonString());
        Assert.Equal("[3,3]", linescore["away"]!.ToJsonString());
        Assert.Equal(2, (int)JsonNode.Parse(row.Situation!)!["balls"]!);
    }

    [Fact]
    public async Task ASecondPollThatSawNothingNewWritesNothing()
    {
        var startsAt = Hour(-1);
        Sport sport;

        await using (var first = fixture.CreateContext())
        {
            sport = await SeedSportAsync(first);
            await new EntityResolver(first).ResolveGameAsync(sport, "espn", LiveGame(sport.Key, startsAt), CancellationToken.None);
        }

        var counter = new RowCounter();
        await using var second = Context(counter);

        await new EntityResolver(second).ResolveGameAsync(sport, "espn", LiveGame(sport.Key, startsAt), CancellationToken.None);

        // jsonb comes back from Postgres re-spaced and re-ordered, so a string comparison alone
        // would call the linescore changed on every poll.
        Assert.Equal(0, counter.Rows);
        Assert.DoesNotContain(second.ChangeTracker.Entries<Game>(), e => e.State == EntityState.Modified);
    }

    [Fact]
    public async Task AMovedClockWritesTheOneRow()
    {
        var startsAt = Hour(-1);
        Sport sport;

        await using (var first = fixture.CreateContext())
        {
            sport = await SeedSportAsync(first);
            await new EntityResolver(first).ResolveGameAsync(sport, "espn", LiveGame(sport.Key, startsAt), CancellationToken.None);
        }

        var counter = new RowCounter();
        await using var second = Context(counter);

        var game = await new EntityResolver(second).ResolveGameAsync(
            sport, "espn", LiveGame(sport.Key, startsAt, "7:51", 471), CancellationToken.None);

        Assert.Equal(1, counter.Rows);
        Assert.Equal("7:51", game.DisplayClock);
        Assert.Equal(471, game.ClockSeconds);
    }

    [Fact]
    public async Task TheSituationClearsOnceTheGameIsNoLongerInPlay()
    {
        await using var db = fixture.CreateContext();
        var sport = await SeedSportAsync(db);
        var startsAt = Hour(-3);
        var resolver = new EntityResolver(db);

        await resolver.ResolveGameAsync(sport, "espn", LiveGame(sport.Key, startsAt), CancellationToken.None);

        // Even if a provider left a stale situation on a final, the row does not keep one.
        var final = LiveGame(sport.Key, startsAt) with { Status = "final" };
        final = final with { Live = final.Live! with { StatusDetail = "Final", Period = 4, DisplayClock = "0:00", ClockSeconds = 0 } };

        var game = await resolver.ResolveGameAsync(sport, "espn", final, CancellationToken.None);

        Assert.Equal(GameStatus.Final, game.Status);
        Assert.Equal("Final", game.StatusDetail);
        Assert.Equal(4, game.Period);
        Assert.NotNull(game.Linescore);
        Assert.Null(game.PossessionTeamId);
        Assert.Null(game.Down);
        Assert.Null(game.Distance);
        Assert.Null(game.YardLine);
        Assert.Null(game.DownDistanceText);
        Assert.Null(game.PossessionText);
        Assert.Null(game.IsRedZone);
        Assert.Null(game.HomeTimeouts);
        Assert.Null(game.AwayTimeouts);
        Assert.Null(game.LastPlay);
        Assert.Null(game.Situation);
    }

    [Fact]
    public async Task AGameSeenWithoutLiveStateKeepsWhatItHad()
    {
        await using var db = fixture.CreateContext();
        var sport = await SeedSportAsync(db);
        var startsAt = Hour(-1);
        var resolver = new EntityResolver(db);

        await resolver.ResolveGameAsync(sport, "espn", LiveGame(sport.Key, startsAt), CancellationToken.None);

        // A book names the same fixture and says nothing of the clock: silence is not a reset.
        var game = await resolver.ResolveGameAsync(
            sport, "espn", LiveGame(sport.Key, startsAt) with { Live = null }, CancellationToken.None);

        Assert.Equal("8:26", game.DisplayClock);
        Assert.Equal(1, game.Down);
    }

    [Fact]
    public async Task APossessionThatIsNeitherSideCreatesNoTeam()
    {
        await using var db = fixture.CreateContext();
        var sport = await SeedSportAsync(db);
        var startsAt = Hour(-1);

        var canonical = LiveGame(sport.Key, startsAt);
        canonical = canonical with { Live = canonical.Live! with { PossessionSourceTeamId = "999" } };

        var game = await new EntityResolver(db).ResolveGameAsync(sport, "espn", canonical, CancellationToken.None);

        Assert.Null(game.PossessionTeamId);
        Assert.Equal(2, await db.Teams.CountAsync(t => t.SportId == sport.Id));
    }
}
