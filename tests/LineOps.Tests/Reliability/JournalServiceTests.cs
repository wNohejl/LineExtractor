using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LineOps.Tests.Reliability;

/// <summary>
/// The journal's writes against real Postgres: an edited result is re-graded rather than left
/// standing, a parlay settles as one bet when its legs do, and the game search finds a matchup.
/// </summary>
[Collection(PostgresCollection.Name)]
public class JournalServiceTests(PostgresFixture fixture)
{
    private static JournalService Service(LineOpsDbContext db)
        => new(db, new SettlementService(db, NullLogger<SettlementService>.Instance));

    private static async Task<(Game Game, Team Home, Team Away)> FinalGameAsync(
        LineOpsDbContext db, int home, int away, string? homeName = null, string? awayName = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sport = new Sport { Key = $"js-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();

        var h = new Team { SportId = sport.Id, Name = homeName ?? $"Home {suffix}", Abbrev = "HOM" };
        var a = new Team { SportId = sport.Id, Name = awayName ?? $"Away {suffix}", Abbrev = "AWY" };
        db.Teams.AddRange(h, a);
        await db.SaveChangesAsync();

        var game = new Game
        {
            SportId = sport.Id, HomeTeamId = h.Id, AwayTeamId = a.Id,
            StartsAt = DateTimeOffset.UtcNow.AddHours(-5), Status = GameStatus.Final,
            HomeScore = home, AwayScore = away, SeasonYear = 2026
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return (game, h, a);
    }

    [Fact]
    public async Task An_edited_line_on_a_graded_spread_is_graded_again()
    {
        await using var db = fixture.CreateContext();
        var (game, home, _) = await FinalGameAsync(db, home: 24, away: 20);
        var journal = Service(db);

        var entry = await journal.SaveEntryAsync(new EntryDraft(
            game.Id, Markets.Spread, null, home.Name, -1.5m, -110, 100m, "draftkings", null));
        await new SettlementService(db, NullLogger<SettlementService>.Instance).SettleAsync();
        Assert.Equal(EntryResult.Win, (await db.JournalEntries.AsNoTracking().FirstAsync(e => e.Id == entry.Id)).Result);

        // Won by four: at -5.5 it did not cover. Leaving "Win" standing would be a lie.
        await journal.SaveEntryAsync(new EntryDraft(
            game.Id, Markets.Spread, null, home.Name, -5.5m, -110, 100m, "draftkings", "was a typo"), entry.Id);

        var after = await db.JournalEntries.AsNoTracking().FirstAsync(e => e.Id == entry.Id);
        Assert.Equal(EntryResult.Loss, after.Result);
        Assert.Equal("was a typo", after.Note);
        Assert.NotNull(after.SettledAt);
    }

    [Fact]
    public async Task A_note_alone_leaves_a_graded_result_as_it_is()
    {
        await using var db = fixture.CreateContext();
        var (game, home, _) = await FinalGameAsync(db, home: 3, away: 2);
        var journal = Service(db);

        var entry = await journal.SaveEntryAsync(new EntryDraft(
            game.Id, Markets.Moneyline, null, home.Name, null, 120, 50m, "fanduel", null));
        await new SettlementService(db, NullLogger<SettlementService>.Instance).SettleAsync();
        var settledAt = (await db.JournalEntries.AsNoTracking().FirstAsync(e => e.Id == entry.Id)).SettledAt;

        await journal.SaveEntryAsync(new EntryDraft(
            game.Id, Markets.Moneyline, null, home.Name, null, 120, 50m, "FanDuel", "late note"), entry.Id);

        var after = await db.JournalEntries.AsNoTracking().FirstAsync(e => e.Id == entry.Id);
        Assert.Equal(EntryResult.Win, after.Result);
        Assert.Equal(settledAt, after.SettledAt);
        Assert.Equal("fanduel", after.Book);   // same book, written the way every reader matches it
    }

    [Fact]
    public async Task A_parlay_settles_as_one_bet_when_its_legs_do()
    {
        await using var db = fixture.CreateContext();
        var (first, firstHome, _) = await FinalGameAsync(db, home: 5, away: 1);
        var (second, _, secondAway) = await FinalGameAsync(db, home: 2, away: 7);
        var journal = Service(db);

        var parlay = await journal.SaveParlayAsync(new ParlayDraft("draftkings", 10m, 264, "Sunday double", [
            new EntryDraft(first.Id, Markets.Moneyline, null, firstHome.Name, null, -110, 0m, "", null),
            new EntryDraft(second.Id, Markets.Moneyline, null, secondAway.Name, null, -110, 0m, "", null)
        ]));

        var summary = await new SettlementService(db, NullLogger<SettlementService>.Instance).SettleAsync();

        var settled = await db.Parlays.AsNoTracking().Include(p => p.Legs).FirstAsync(p => p.Id == parlay.Id);
        Assert.True(summary.ParlaysSettled >= 1);
        Assert.Equal(EntryResult.Win, settled.Result);
        Assert.Equal(36.40m, settled.Payout);
        Assert.All(settled.Legs, l => Assert.Equal(0m, l.Stake));
        Assert.All(settled.Legs, l => Assert.Equal(EntryResult.Win, l.Result));
    }

    [Fact]
    public async Task A_leg_is_deleted_with_its_parlay_not_on_its_own()
    {
        await using var db = fixture.CreateContext();
        var (game, home, away) = await FinalGameAsync(db, 1, 0);
        var journal = Service(db);

        var parlay = await journal.SaveParlayAsync(new ParlayDraft("fanduel", 5m, null, null, [
            new EntryDraft(game.Id, Markets.Moneyline, null, home.Name, null, -150, 0m, "", null),
            new EntryDraft(null, "other", "Some prop", "—", null, 200, 0m, "", null)
        ]));
        var legId = parlay.Legs[0].Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.DeleteEntryAsync(legId));

        await journal.DeleteParlayAsync(parlay.Id);
        Assert.False(await db.JournalEntries.AnyAsync(e => e.Id == legId));
        _ = away;
    }

    [Fact]
    public async Task A_matchup_is_found_by_both_teams_and_a_typed_wildcard_is_just_a_character()
    {
        await using var db = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var (game, _, _) = await FinalGameAsync(db, 1, 2, $"Harbor {suffix} Gulls", $"Summit {suffix} Rams");

        // FinalGameAsync makes a sport that is enabled by default; the search reaches it.
        var journal = Service(db);

        var found = await journal.SearchGamesAsync($"gulls {suffix} rams");
        Assert.Contains(found, g => g.Id == game.Id);

        Assert.DoesNotContain(await journal.SearchGamesAsync($"{suffix}%"), g => g.Id == game.Id);
    }

    [Fact]
    public async Task The_book_list_holds_the_common_books_and_any_the_data_has_seen()
    {
        await using var db = fixture.CreateContext();
        var (game, home, _) = await FinalGameAsync(db, 1, 0);
        await Service(db).SaveEntryAsync(new EntryDraft(game.Id, Markets.Moneyline, null, home.Name, null, -110, 10m, "Hard Rock", null));

        var books = await Service(db).KnownBooksAsync();

        Assert.Contains("hard rock", books);
        Assert.Contains("draftkings", books);
        Assert.Equal(books.Count, books.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task The_starting_bankroll_is_kept_with_the_data()
    {
        await using var db = fixture.CreateContext();
        var journal = Service(db);

        await journal.SetStartingBankrollAsync(1500m);
        await journal.SetStartingBankrollAsync(2000m);

        Assert.Equal(2000m, await Service(fixture.CreateContext()).StartingBankrollAsync());
    }
}
