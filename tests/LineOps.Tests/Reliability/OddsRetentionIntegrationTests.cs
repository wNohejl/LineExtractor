using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Configuration;
using LineOps.Ingestion.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LineOps.Tests.Reliability;

/// <summary>
/// The two-tier odds policy: scans are working state until first pitch, a single closing line
/// survives afterwards, and everything before the policy's floor date goes.
///
/// Verified against real Postgres because the parts worth checking — that promotion picks the
/// right row out of a partitioned table, and that pruning does not take a close with it — are
/// exactly the parts an in-memory provider would not model.
/// </summary>
[Collection(PostgresCollection.Name)]
public class OddsRetentionIntegrationTests(PostgresFixture fixture)
{
    private static OddsRetentionService CreateService(
        LineOpsDbContext db, OddsRetentionOptions? settings = null)
        => new(
            db,
            Options.Create(new IngestionOptions { OddsRetention = settings ?? new OddsRetentionOptions() }),
            NullLogger<OddsRetentionService>.Instance);

    private async Task<(Game Game, Source Source)> SeedGameAsync(
        LineOpsDbContext db, DateTimeOffset startsAt)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var sport = new Sport { Key = $"ret-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = $"Home {suffix}", Abbrev = "HOM" };
        var away = new Team { SportId = sport.Id, Name = $"Away {suffix}", Abbrev = "AWY" };
        db.Teams.AddRange(home, away);

        var source = new Source
        {
            Key = $"ret-src-{suffix}",
            Name = "Test source",
            Kind = SourceKind.Odds,
            BaseUrl = "local://test"
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();

        var game = new Game
        {
            SportId = sport.Id,
            HomeTeamId = home.Id,
            AwayTeamId = away.Id,
            StartsAt = startsAt,
            Status = GameStatus.Scheduled
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();

        return (game, source);
    }

    private static OddsSnapshot Scan(
        Game game, Source source, int price, DateTimeOffset at,
        string outcome = "home", string book = "draftkings")
        => new()
        {
            GameId = game.Id,
            SourceId = source.Id,
            Book = book,
            Market = Markets.Moneyline,
            Outcome = outcome,
            PriceAmerican = price,
            CapturedAt = at,
            IngestionRunId = 0
        };

    [Fact]
    public async Task PromotionTakesTheLastPriceBeforeFirstPitchNotTheInPlayOne()
    {
        await using var db = fixture.CreateContext();
        var startsAt = DateTimeOffset.UtcNow.AddHours(-2);
        var (game, source) = await SeedGameAsync(db, startsAt);

        db.OddsSnapshots.AddRange(
            Scan(game, source, 100, startsAt.AddHours(-4)),
            Scan(game, source, 120, startsAt.AddMinutes(-3)),   // the close
            Scan(game, source, -110, startsAt.AddMinutes(30))); // in-play, not the close
        await db.SaveChangesAsync();

        var promoted = await CreateService(db).PromoteAsync();

        Assert.Equal(1, promoted);

        var close = await db.ClosingLines.SingleAsync(c => c.GameId == game.Id);
        Assert.Equal(120, close.PriceAmerican);
        Assert.Equal("draftkings", close.Book);
        Assert.True(close.PromotedAt >= startsAt);
    }

    [Fact]
    public async Task PromotionRecordsOneCloseForEachBookAndOutcome()
    {
        await using var db = fixture.CreateContext();
        var startsAt = DateTimeOffset.UtcNow.AddHours(-2);
        var (game, source) = await SeedGameAsync(db, startsAt);

        db.OddsSnapshots.AddRange(
            Scan(game, source, 120, startsAt.AddMinutes(-5), "home", "draftkings"),
            Scan(game, source, -140, startsAt.AddMinutes(-5), "away", "draftkings"),
            Scan(game, source, 115, startsAt.AddMinutes(-5), "home", "fanduel"));
        await db.SaveChangesAsync();

        await CreateService(db).PromoteAsync();

        var closes = await db.ClosingLines.Where(c => c.GameId == game.Id).ToListAsync();

        Assert.Equal(3, closes.Count);
        Assert.Equal(115, closes.Single(c => c.Book == "fanduel" && c.Outcome == "home").PriceAmerican);
        Assert.Equal(-140, closes.Single(c => c.Outcome == "away").PriceAmerican);
    }

    [Fact]
    public async Task AGameStillAheadOfUsKeepsItsScansAndGetsNoClose()
    {
        await using var db = fixture.CreateContext();
        var startsAt = DateTimeOffset.UtcNow.AddHours(6);
        var (game, source) = await SeedGameAsync(db, startsAt);

        db.OddsSnapshots.AddRange(
            Scan(game, source, 100, DateTimeOffset.UtcNow.AddHours(-2)),
            Scan(game, source, 110, DateTimeOffset.UtcNow.AddMinutes(-5)));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.PromoteAsync();
        await service.PruneAsync();

        // The live market is exactly what the scan tier is for.
        Assert.False(await db.ClosingLines.AnyAsync(c => c.GameId == game.Id));
        Assert.Equal(2, await db.OddsSnapshots.CountAsync(s => s.GameId == game.Id));
    }

    [Fact]
    public async Task PruningDropsScansOnlyOnceTheCloseIsRecorded()
    {
        await using var db = fixture.CreateContext();
        var startsAt = DateTimeOffset.UtcNow.AddHours(-2);
        var (game, source) = await SeedGameAsync(db, startsAt);

        db.OddsSnapshots.AddRange(
            Scan(game, source, 100, startsAt.AddHours(-3)),
            Scan(game, source, 120, startsAt.AddMinutes(-2)));
        await db.SaveChangesAsync();

        var service = CreateService(db);

        // Prune before promotion must not touch a game whose close has not been taken —
        // that ordering is the whole safety property.
        await service.PruneAsync();
        Assert.Equal(2, await db.OddsSnapshots.CountAsync(s => s.GameId == game.Id));

        await service.RunAsync();

        Assert.Equal(0, await db.OddsSnapshots.CountAsync(s => s.GameId == game.Id));
        Assert.Equal(120, (await db.ClosingLines.SingleAsync(c => c.GameId == game.Id)).PriceAmerican);
    }

    [Fact]
    public async Task ScansFromBeforeTheFloorAreDroppedWhetherOrNotTheyWerePromoted()
    {
        await using var db = fixture.CreateContext();
        var floor = new DateOnly(2026, 7, 25);

        // A game far enough in the past that its scans predate the policy entirely.
        var startsAt = new DateTimeOffset(2026, 6, 10, 18, 0, 0, TimeSpan.Zero);
        var (game, source) = await SeedGameAsync(db, startsAt);

        db.OddsSnapshots.Add(Scan(game, source, 105, startsAt.AddHours(-1)));
        await db.SaveChangesAsync();

        var service = CreateService(db, new OddsRetentionOptions { HistoryFloor = floor });
        await service.PruneAsync();

        Assert.Equal(0, await db.OddsSnapshots.CountAsync(s => s.GameId == game.Id));
    }

    [Fact]
    public async Task PromotionIsIdempotent()
    {
        await using var db = fixture.CreateContext();
        var startsAt = DateTimeOffset.UtcNow.AddHours(-2);
        var (game, source) = await SeedGameAsync(db, startsAt);

        db.OddsSnapshots.Add(Scan(game, source, 120, startsAt.AddMinutes(-1)));
        await db.SaveChangesAsync();

        var service = CreateService(db);

        Assert.Equal(1, await service.PromoteAsync());

        // Second pass finds the close already recorded and does nothing. Without that the
        // unique index would turn a routine re-run into a failed batch.
        Assert.Equal(0, await service.PromoteAsync());
        Assert.Equal(1, await db.ClosingLines.CountAsync(c => c.GameId == game.Id));
    }

    [Fact]
    public async Task AGameThatJustStartedWaitsForTheGracePeriod()
    {
        await using var db = fixture.CreateContext();
        var startsAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var (game, source) = await SeedGameAsync(db, startsAt);

        db.OddsSnapshots.Add(Scan(game, source, 120, startsAt.AddMinutes(-2)));
        await db.SaveChangesAsync();

        var service = CreateService(db, new OddsRetentionOptions
        {
            PromoteAfterStart = TimeSpan.FromMinutes(10)
        });

        // A book still being scanned as the game begins gets to land its last price.
        Assert.Equal(0, await service.PromoteAsync());
        Assert.False(await db.ClosingLines.AnyAsync(c => c.GameId == game.Id));
    }

    [Fact]
    public async Task DroppingEmptyPartitionsActuallyReachesPostgres()
    {
        await using var db = fixture.CreateContext();

        // Deliberately thin: it only has to execute. The statement previously carried a regex
        // written as [0-9]{4}_[0-9]{2}, and ExecuteSqlRawAsync reads {4} and {2} as parameter
        // placeholders — so with no parameters supplied it threw before Postgres saw any of it,
        // every scheduler pass, swallowed by the catch that keeps housekeeping from stopping
        // ingestion. Nothing called this method, so nothing noticed.
        var exception = await Record.ExceptionAsync(() => CreateService(db).DropEmptyPartitionsAsync());

        Assert.Null(exception);

        // Asserted as -1 rather than as a count, because that is what it genuinely returns: a
        // Postgres DO block reports no rows affected, and ExecuteSqlRawAsync passes that through.
        // The value is therefore not a number of partitions and must not be logged as one.
        Assert.Equal(-1, await CreateService(db).DropEmptyPartitionsAsync());
    }
}
