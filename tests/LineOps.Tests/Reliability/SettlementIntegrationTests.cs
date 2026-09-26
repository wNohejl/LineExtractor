using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Configuration;
using LineOps.Ingestion.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LineOps.Tests.Reliability;

/// <summary>
/// End-to-end settlement: an entry is logged, the game finishes, and the entry is graded
/// with closing-line value resolved against the stored snapshots. This is the payoff of the
/// append-only odds table, so it is verified against real Postgres.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SettlementIntegrationTests(PostgresFixture fixture)
{
    private async Task<(Game Game, Source Source)> SeedGameAsync(
        LineOpsDbContext db, DateTimeOffset startsAt)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var sport = new Sport { Key = $"test-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = $"Home {suffix}", Abbrev = "HOM" };
        var away = new Team { SportId = sport.Id, Name = $"Away {suffix}", Abbrev = "AWY" };
        db.Teams.AddRange(home, away);

        var source = new Source
        {
            Key = $"src-{suffix}",
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

    private static OddsSnapshot Snapshot(
        Game game, Source source, string outcome, int price, DateTimeOffset at,
        string market = Markets.Moneyline, string book = "draftkings")
        => new()
        {
            GameId = game.Id,
            SourceId = source.Id,
            Book = book,
            Market = market,
            Outcome = outcome,
            PriceAmerican = price,
            CapturedAt = at,
            IngestionRunId = 0
        };

    private SettlementService CreateService(LineOpsDbContext db)
        => new(db, NullLogger<SettlementService>.Instance);

    /// <summary>
    /// Settlement reads <c>ClosingLines</c>, which retention writes when a game starts. Tests
    /// that care about CLV therefore promote first, exactly as the scheduler does.
    /// </summary>
    private static OddsRetentionService CreateRetention(LineOpsDbContext db)
        => new(
            db,
            Options.Create(new IngestionOptions()),
            NullLogger<OddsRetentionService>.Instance);

    [Fact]
    public async Task AWinningEntryIsGradedAndPaidOut()
    {
        await using var db = fixture.CreateContext();
        var startsAt = DateTimeOffset.UtcNow.AddHours(-3);
        var (game, _) = await SeedGameAsync(db, startsAt);

        var home = await db.Teams.FirstAsync(t => t.Id == game.HomeTeamId);

        db.JournalEntries.Add(new JournalEntry
        {
            GameId = game.Id,
            Market = Markets.Moneyline,
            Outcome = home.Name,
            Book = "draftkings",
            PriceTaken = 150,
            Stake = 100m,
            PlacedAt = startsAt.AddHours(-2),
            Result = EntryResult.Pending
        });
        await db.SaveChangesAsync();

        await CreateService(db).MarkFinalAsync(game.Id, homeScore: 27, awayScore: 20);

        var entry = await db.JournalEntries.FirstAsync(e => e.GameId == game.Id);

        Assert.Equal(EntryResult.Win, entry.Result);
        Assert.Equal(250m, entry.Payout!.Value, precision: 2);
        Assert.Equal(150m, entry.NetReturn, precision: 2);
    }

    [Fact]
    public async Task ClvIsResolvedFromTheLastPreStartSnapshot()
    {
        await using var db = fixture.CreateContext();
        var startsAt = DateTimeOffset.UtcNow.AddHours(-3);
        var (game, source) = await SeedGameAsync(db, startsAt);
        var home = await db.Teams.FirstAsync(t => t.Id == game.HomeTeamId);

        // A line that drifts, then one observation *after* kick-off which must be ignored.
        db.OddsSnapshots.AddRange(
            Snapshot(game, source, home.Name, 150, startsAt.AddHours(-5)),
            Snapshot(game, source, home.Name, 135, startsAt.AddHours(-2)),
            Snapshot(game, source, home.Name, 120, startsAt.AddMinutes(-5)),
            Snapshot(game, source, home.Name, -110, startsAt.AddMinutes(30)));

        db.JournalEntries.Add(new JournalEntry
        {
            GameId = game.Id,
            Market = Markets.Moneyline,
            Outcome = home.Name,
            Book = "draftkings",
            PriceTaken = 150,
            Stake = 100m,
            PlacedAt = startsAt.AddHours(-5),
            Result = EntryResult.Pending
        });
        await db.SaveChangesAsync();

        // Settlement reads the promoted close, not the scan stream, so retention runs first —
        // which is the order the scheduler uses too.
        await CreateRetention(db).PromoteAsync();

        await CreateService(db).MarkFinalAsync(game.Id, 27, 20);

        var entry = await db.JournalEntries.FirstAsync(e => e.GameId == game.Id);

        // The close is the last price before kick-off: +120, not the post-start -110.
        Assert.Equal(120, entry.ClosingPrice);
        Assert.NotNull(entry.ClosingCapturedAt);

        var clv = PerformanceAnalytics.ComputeClv(entry);
        Assert.True(clv!.Value.BeatClose, "Taking +150 into a +120 close must be positive CLV.");
    }

    [Fact]
    public async Task ClvFallsBackToAnotherBookWhenTheOwnBookWasNotTracked()
    {
        await using var db = fixture.CreateContext();
        var startsAt = DateTimeOffset.UtcNow.AddHours(-3);
        var (game, source) = await SeedGameAsync(db, startsAt);
        var home = await db.Teams.FirstAsync(t => t.Id == game.HomeTeamId);

        db.OddsSnapshots.Add(Snapshot(game, source, home.Name, 130, startsAt.AddMinutes(-10), book: "fanduel"));

        db.JournalEntries.Add(new JournalEntry
        {
            GameId = game.Id,
            Market = Markets.Moneyline,
            Outcome = home.Name,
            Book = "betmgm", // never ingested on the free tier
            PriceTaken = 150,
            Stake = 100m,
            PlacedAt = startsAt.AddHours(-4),
            Result = EntryResult.Pending
        });
        await db.SaveChangesAsync();

        await CreateRetention(db).PromoteAsync();

        await CreateService(db).MarkFinalAsync(game.Id, 27, 20);

        var entry = await db.JournalEntries.FirstAsync(e => e.GameId == game.Id);

        // A cross-book close is weaker than a same-book one, but far better than no CLV.
        Assert.Equal(130, entry.ClosingPrice);
    }

    [Fact]
    public async Task EntriesForUnfinishedGamesAreLeftPending()
    {
        await using var db = fixture.CreateContext();
        var (game, _) = await SeedGameAsync(db, DateTimeOffset.UtcNow.AddHours(3));
        var home = await db.Teams.FirstAsync(t => t.Id == game.HomeTeamId);

        db.JournalEntries.Add(new JournalEntry
        {
            GameId = game.Id,
            Market = Markets.Moneyline,
            Outcome = home.Name,
            Book = "draftkings",
            PriceTaken = -110,
            Stake = 100m,
            PlacedAt = DateTimeOffset.UtcNow,
            Result = EntryResult.Pending
        });
        await db.SaveChangesAsync();

        var summary = await CreateService(db).SettleAsync();

        Assert.Equal(0, summary.Graded);
        Assert.True(summary.LeftPending >= 1);

        var entry = await db.JournalEntries.FirstAsync(e => e.GameId == game.Id);
        Assert.Equal(EntryResult.Pending, entry.Result);
    }

    [Fact]
    public async Task FreeTextEntriesSurviveSettlementUngraded()
    {
        await using var db = fixture.CreateContext();
        var (game, _) = await SeedGameAsync(db, DateTimeOffset.UtcNow.AddHours(-3));

        db.JournalEntries.Add(new JournalEntry
        {
            GameId = game.Id,
            Market = "other",
            FreeTextMarket = "Player X over 1.5 receiving TDs",
            Outcome = "over",
            Book = "draftkings",
            PriceTaken = 220,
            Stake = 25m,
            PlacedAt = DateTimeOffset.UtcNow.AddHours(-5),
            Result = EntryResult.Pending
        });
        await db.SaveChangesAsync();

        await CreateService(db).MarkFinalAsync(game.Id, 27, 20);

        var entry = await db.JournalEntries.FirstAsync(e => e.GameId == game.Id);

        // No feed covers this market yet, so the platform must not invent a result.
        Assert.Equal(EntryResult.Pending, entry.Result);
    }

    [Fact]
    public async Task SettlementIsIdempotent()
    {
        await using var db = fixture.CreateContext();
        var startsAt = DateTimeOffset.UtcNow.AddHours(-3);
        var (game, source) = await SeedGameAsync(db, startsAt);
        var home = await db.Teams.FirstAsync(t => t.Id == game.HomeTeamId);

        db.OddsSnapshots.Add(Snapshot(game, source, home.Name, 120, startsAt.AddMinutes(-5)));
        db.JournalEntries.Add(new JournalEntry
        {
            GameId = game.Id,
            Market = Markets.Moneyline,
            Outcome = home.Name,
            Book = "draftkings",
            PriceTaken = 150,
            Stake = 100m,
            PlacedAt = startsAt.AddHours(-4),
            Result = EntryResult.Pending
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.MarkFinalAsync(game.Id, 27, 20);

        var first = await db.JournalEntries.AsNoTracking().FirstAsync(e => e.GameId == game.Id);

        // Re-running must not re-pay or re-resolve anything.
        var second = await service.SettleAsync();

        var after = await db.JournalEntries.AsNoTracking().FirstAsync(e => e.GameId == game.Id);

        Assert.Equal(0, second.Graded);
        Assert.Equal(first.Payout, after.Payout);
        Assert.Equal(first.ClosingSnapshotId, after.ClosingSnapshotId);
    }

    private static JournalEntry Pending(Game game, string outcome, string market = Markets.Moneyline,
        decimal? line = null, string book = "draftkings", int price = -110)
        => new()
        {
            GameId = game.Id,
            Market = market,
            Outcome = outcome,
            LineTaken = line,
            Book = book,
            PriceTaken = price,
            Stake = 100m,
            PlacedAt = game.StartsAt.AddHours(-2),
            Result = EntryResult.Pending
        };

    [Fact]
    public async Task A_bet_on_a_game_postponed_past_the_window_is_voided()
    {
        await using var db = fixture.CreateContext();
        var (game, _) = await SeedGameAsync(db, DateTimeOffset.UtcNow.AddDays(-3));
        game.Status = GameStatus.Postponed;
        var home = await db.Teams.FirstAsync(t => t.Id == game.HomeTeamId);

        db.JournalEntries.Add(Pending(game, home.Name));
        await db.SaveChangesAsync();

        var summary = await CreateService(db).SettleAsync();

        var entry = await db.JournalEntries.AsNoTracking().FirstAsync(e => e.GameId == game.Id);
        Assert.Equal(EntryResult.Void, entry.Result);
        Assert.Equal(100m, entry.Payout);
        Assert.True(summary.Voided >= 1);
    }

    [Fact]
    public async Task A_bet_on_a_game_postponed_today_waits_for_the_make_up()
    {
        await using var db = fixture.CreateContext();
        var (game, _) = await SeedGameAsync(db, DateTimeOffset.UtcNow.AddHours(-5));
        game.Status = GameStatus.Postponed;
        var home = await db.Teams.FirstAsync(t => t.Id == game.HomeTeamId);

        db.JournalEntries.Add(Pending(game, home.Name));
        await db.SaveChangesAsync();

        await CreateService(db).SettleAsync();

        var entry = await db.JournalEntries.AsNoTracking().FirstAsync(e => e.GameId == game.Id);
        Assert.Equal(EntryResult.Pending, entry.Result);
    }

    [Fact]
    public async Task A_spread_records_the_number_it_closed_at_and_scores_the_points()
    {
        await using var db = fixture.CreateContext();
        var startsAt = DateTimeOffset.UtcNow.AddHours(-4);
        var (game, source) = await SeedGameAsync(db, startsAt);
        var home = await db.Teams.FirstAsync(t => t.Id == game.HomeTeamId);

        // The book's own close, written with the capital the reference feed uses: the journal
        // says "draftkings", and a case-sensitive match used to miss its own book.
        db.ClosingLines.Add(new ClosingLine
        {
            GameId = game.Id, SourceId = source.Id, Book = "DraftKings",
            Market = Markets.Spread, Outcome = home.Name, Line = -2.5m, PriceAmerican = -110,
            CapturedAt = startsAt.AddMinutes(-3), PromotedAt = startsAt.AddMinutes(10)
        });
        db.ClosingLines.Add(new ClosingLine
        {
            GameId = game.Id, SourceId = source.Id, Book = "fanduel",
            Market = Markets.Spread, Outcome = home.Name, Line = -1.5m, PriceAmerican = -130,
            CapturedAt = startsAt.AddMinutes(-1), PromotedAt = startsAt.AddMinutes(10)
        });
        db.JournalEntries.Add(Pending(game, home.Name, Markets.Spread, line: -1.5m, price: -110));
        await db.SaveChangesAsync();

        await CreateService(db).MarkFinalAsync(game.Id, homeScore: 24, awayScore: 20);

        var entry = await db.JournalEntries.AsNoTracking().FirstAsync(e => e.GameId == game.Id);

        Assert.Equal(EntryResult.Win, entry.Result);
        Assert.Equal("DraftKings", entry.ClosingBook);   // its own book, not the later FanDuel close
        Assert.Equal(-2.5m, entry.ClosingPoints);

        var clv = PerformanceAnalytics.ComputeClv(entry)!.Value;
        Assert.Equal(1.0m, clv.PointsGained);
        Assert.True(clv.BeatClose);
    }

    private static ClosingLine Close(Game game, Source source, string book, string outcome, int price,
        string market = Markets.Moneyline, decimal? line = null)
        => new()
        {
            GameId = game.Id, SourceId = source.Id, Book = book, Market = market, Outcome = outcome,
            Line = line, PriceAmerican = price,
            CapturedAt = game.StartsAt.AddMinutes(-2), PromotedAt = game.StartsAt.AddMinutes(10)
        };

    [Fact]
    public async Task A_price_is_valued_at_the_fair_close_not_at_one_books_close()
    {
        await using var db = fixture.CreateContext();
        var (game, source) = await SeedGameAsync(db, DateTimeOffset.UtcNow.AddHours(-4));
        var home = await db.Teams.FirstAsync(t => t.Id == game.HomeTeamId);
        var away = await db.Teams.FirstAsync(t => t.Id == game.AwayTeamId);

        // Pinnacle closed a fair coin. DraftKings closed the home side at -110, the price taken,
        // so the price comparison reads zero — but -110 on a coin costs 1/22 of the stake.
        db.ClosingLines.AddRange(
            Close(game, source, "pinnacle", home.Name, -105),
            Close(game, source, "pinnacle", away.Name, -105),
            Close(game, source, "draftkings", home.Name, -110),
            Close(game, source, "draftkings", away.Name, -110));
        db.JournalEntries.Add(Pending(game, home.Name, price: -110));
        await db.SaveChangesAsync();

        await CreateService(db).MarkFinalAsync(game.Id, homeScore: 5, awayScore: 3);

        var entry = await db.JournalEntries.AsNoTracking().FirstAsync(e => e.GameId == game.Id);
        var clv = PerformanceAnalytics.ComputeClv(entry)!.Value;

        Assert.Equal(0.5, entry.ClosingFairProbability!.Value, precision: 9);
        Assert.Equal(FairValue.SharpBook, entry.ClosingFairBasis);
        Assert.Equal(0.0, clv.CentsPercent, precision: 9);
        Assert.Equal(-1.0 / 22, clv.EvAtClose!.Value, precision: 6);
    }

    [Fact]
    public async Task A_number_the_market_closed_off_has_no_fair_close()
    {
        await using var db = fixture.CreateContext();
        var (game, source) = await SeedGameAsync(db, DateTimeOffset.UtcNow.AddHours(-4));
        var home = await db.Teams.FirstAsync(t => t.Id == game.HomeTeamId);
        var away = await db.Teams.FirstAsync(t => t.Id == game.AwayTeamId);

        // Taken at -1.5; everyone closed -2.5. The points say how the bet did against the move;
        // what -1.5 was worth at the close is not something the close priced.
        db.ClosingLines.AddRange(
            Close(game, source, "pinnacle", home.Name, -105, Markets.Spread, -2.5m),
            Close(game, source, "pinnacle", away.Name, -105, Markets.Spread, 2.5m),
            Close(game, source, "draftkings", home.Name, -110, Markets.Spread, -2.5m));
        db.JournalEntries.Add(Pending(game, home.Name, Markets.Spread, line: -1.5m, price: -110));
        await db.SaveChangesAsync();

        await CreateService(db).MarkFinalAsync(game.Id, homeScore: 5, awayScore: 3);

        var entry = await db.JournalEntries.AsNoTracking().FirstAsync(e => e.GameId == game.Id);

        Assert.NotNull(entry.ClosingPrice);
        Assert.Null(entry.ClosingFairProbability);
        Assert.Null(PerformanceAnalytics.ComputeClv(entry)!.Value.EvAtClose);
    }
}
