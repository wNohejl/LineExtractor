using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LineOps.Tests.Reliability;

/// <summary>
/// What the slip writes, against real Postgres: straight bets in one save, a parlay as one row
/// with zero-stake legs in its group, a failure that writes nothing, and the lookup the wager
/// window's duplicate check reads.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SlipLoggingIntegrationTests(PostgresFixture fixture)
{
    private static JournalService Service(LineOpsDbContext db)
        => new(db, new SettlementService(db, NullLogger<SettlementService>.Instance));

    private static async Task<(Game Game, Team Home, Team Away)> ScheduledGameAsync(LineOpsDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sport = new Sport { Key = $"sl-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = $"Home {suffix}", Abbrev = "HOM" };
        var away = new Team { SportId = sport.Id, Name = $"Away {suffix}", Abbrev = "AWY" };
        db.Teams.AddRange(home, away);
        await db.SaveChangesAsync();

        var game = new Game
        {
            SportId = sport.Id, HomeTeamId = home.Id, AwayTeamId = away.Id,
            StartsAt = DateTimeOffset.UtcNow.AddHours(5), Status = GameStatus.Scheduled, SeasonYear = 2026
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return (game, home, away);
    }

    private static EntryDraft Bet(int? gameId, string outcome, decimal stake = 50m, string book = "draftkings", string? note = null)
        => new(gameId, Markets.Moneyline, null, outcome, null, -128, stake, book, note);

    [Fact]
    public async Task Straight_mode_writes_one_entry_per_bet_in_one_save()
    {
        await using var db = fixture.CreateContext();
        var (game, home, away) = await ScheduledGameAsync(db);
        var note = $"slip {Guid.NewGuid():N}";

        var written = await Service(db).SaveEntriesAsync(
        [
            Bet(game.Id, home.Name, 50m, note: note),
            Bet(game.Id, away.Name, 25m, "FanDuel", note)
        ]);

        Assert.Equal(2, written.Count);

        var rows = await db.JournalEntries.AsNoTracking().Where(e => e.Note == note).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(EntryResult.Pending, r.Result));
        Assert.All(rows, r => Assert.Null(r.ParlayGroupId));
        Assert.Single(rows.Select(r => r.PlacedAt).Distinct());
        Assert.Contains(rows, r => r.Book == "fanduel");   // written the way every reader matches it
    }

    [Fact]
    public async Task A_bet_that_cannot_be_written_takes_the_whole_slip_with_it()
    {
        await using var db = fixture.CreateContext();
        var (game, home, _) = await ScheduledGameAsync(db);
        var note = $"slip {Guid.NewGuid():N}";

        // No such game: the foreign key refuses the second row, and the first must not survive it.
        await Assert.ThrowsAsync<DbUpdateException>(() => Service(db).SaveEntriesAsync(
        [
            Bet(game.Id, home.Name, note: note),
            Bet(int.MaxValue, home.Name, note: note)
        ]));

        await using var fresh = fixture.CreateContext();
        Assert.Equal(0, await fresh.JournalEntries.CountAsync(e => e.Note == note));
    }

    [Fact]
    public async Task Parlay_mode_writes_one_parlay_and_zero_stake_legs_in_its_group()
    {
        await using var db = fixture.CreateContext();
        var (first, home1, _) = await ScheduledGameAsync(db);
        var (second, _, away2) = await ScheduledGameAsync(db);
        var note = $"slip {Guid.NewGuid():N}";

        var parlay = await Service(db).SaveParlayAsync(new ParlayDraft("BetMGM", 25m, null, note,
        [
            Bet(first.Id, home1.Name, 0m),
            Bet(second.Id, away2.Name, 0m)
        ]));

        var stored = await db.Parlays.AsNoTracking().Include(p => p.Legs).SingleAsync(p => p.Id == parlay.Id);
        Assert.Equal(25m, stored.Stake);
        Assert.Null(stored.PriceQuoted);
        Assert.Equal("betmgm", stored.Book);
        Assert.Equal(2, stored.Legs.Count);
        Assert.All(stored.Legs, l => Assert.Equal(0m, l.Stake));
        Assert.All(stored.Legs, l => Assert.Equal(parlay.Id, l.ParlayGroupId));
    }

    [Fact]
    public async Task The_newest_straight_bet_on_a_side_is_found_within_the_window()
    {
        await using var db = fixture.CreateContext();
        var (game, home, away) = await ScheduledGameAsync(db);
        var journal = Service(db);

        db.JournalEntries.AddRange(
            new JournalEntry { GameId = game.Id, Market = Markets.Moneyline, Outcome = home.Name, Book = "draftkings",
                               PriceTaken = -130, Stake = 50m, PlacedAt = DateTimeOffset.UtcNow.AddDays(-10) },
            new JournalEntry { GameId = game.Id, Market = Markets.Moneyline, Outcome = home.Name, Book = "draftkings",
                               PriceTaken = -128, Stake = 25m, PlacedAt = DateTimeOffset.UtcNow.AddHours(-2) },
            new JournalEntry { GameId = game.Id, Market = Markets.Moneyline, Outcome = home.Name, Book = "draftkings",
                               PriceTaken = -125, Stake = 40m, PlacedAt = DateTimeOffset.UtcNow.AddHours(-3) },
            new JournalEntry { GameId = game.Id, Market = Markets.Moneyline, Outcome = away.Name, Book = "draftkings",
                               PriceTaken = 110, Stake = 10m, PlacedAt = DateTimeOffset.UtcNow.AddHours(-1) });
        await db.SaveChangesAsync();

        var since = DateTimeOffset.UtcNow.AddDays(-7);

        var found = await journal.LatestOnSideAsync(game.Id, Markets.Moneyline, home.Name.ToUpperInvariant(), since);
        Assert.NotNull(found);
        Assert.Equal(-128, found.PriceTaken);

        Assert.Null(await journal.LatestOnSideAsync(game.Id, Markets.Moneyline, "Nobody", since));
        Assert.Null(await journal.LatestOnSideAsync(game.Id, Markets.Spread, home.Name, since));
    }

    [Fact]
    public async Task A_bet_on_the_same_side_at_another_book_is_still_a_prior_bet()
    {
        await using var db = fixture.CreateContext();
        var (game, home, _) = await ScheduledGameAsync(db);
        var journal = Service(db);

        db.JournalEntries.Add(new JournalEntry
        {
            GameId = game.Id, Market = Markets.Moneyline, Outcome = home.Name, Book = "draftkings",
            PriceTaken = -130, Stake = 50m, PlacedAt = DateTimeOffset.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        // Asked from a Pinnacle price: the same exposure, so the same answer, naming its book.
        var found = await journal.LatestOnSideAsync(game.Id, Markets.Moneyline, home.Name, DateTimeOffset.UtcNow.AddDays(-7));

        Assert.NotNull(found);
        Assert.Equal("draftkings", found.Book);
    }

    [Fact]
    public async Task A_parlay_leg_is_not_a_prior_bet_on_its_side()
    {
        await using var db = fixture.CreateContext();
        var (first, home1, _) = await ScheduledGameAsync(db);
        var (second, home2, _) = await ScheduledGameAsync(db);
        var journal = Service(db);

        await journal.SaveParlayAsync(new ParlayDraft("draftkings", 10m, null, null,
            [Bet(first.Id, home1.Name, 0m), Bet(second.Id, home2.Name, 0m)]));

        Assert.Null(await journal.LatestOnSideAsync(first.Id, Markets.Moneyline, home1.Name,
            DateTimeOffset.UtcNow.AddDays(-7)));
    }
}
