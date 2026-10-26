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
            SportId = sport.Id,
            HomeTeamId = h.Id,
            AwayTeamId = a.Id,
            StartsAt = DateTimeOffset.UtcNow.AddHours(-5),
            Status = GameStatus.Final,
            HomeScore = home,
            AwayScore = away,
            SeasonYear = 2026
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
    public async Task A_typed_price_is_stored_as_manual()
    {
        await using var db = fixture.CreateContext();
        var (game, home, _) = await FinalGameAsync(db, home: 24, away: 20);
        var journal = Service(db);

        var saved = await journal.SaveEntriesAsync([
            new EntryDraft(game.Id, Markets.Spread, null, home.Name, -3m, -105, 50m, "draftkings", null, ManualPrice: true),
            new EntryDraft(game.Id, Markets.Moneyline, null, home.Name, null, -150, 50m, "draftkings", null)
        ]);

        var rows = await db.JournalEntries.AsNoTracking().Where(e => saved.Select(s => s.Id).Contains(e.Id)).ToListAsync();
        Assert.True(rows.Single(e => e.Market == Markets.Spread).ManualPrice);
        Assert.False(rows.Single(e => e.Market == Markets.Moneyline).ManualPrice);
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

    /// <summary>Two legs that both won, on two final games; settled before it is returned.</summary>
    private async Task<Parlay> WonParlayAsync(LineOpsDbContext db, int? quoted, string note = "Sunday double")
    {
        var (first, firstHome, _) = await FinalGameAsync(db, home: 5, away: 1);
        var (second, _, secondAway) = await FinalGameAsync(db, home: 2, away: 7);

        var parlay = await Service(db).SaveParlayAsync(new ParlayDraft("draftkings", 10m, quoted, note, [
            new EntryDraft(first.Id, Markets.Moneyline, null, firstHome.Name, null, -110, 0m, "", null),
            new EntryDraft(second.Id, Markets.Moneyline, null, secondAway.Name, null, -110, 0m, "", null)
        ]));

        await new SettlementService(db, NullLogger<SettlementService>.Instance).SettleAsync();
        return parlay;
    }

    private static Task<Parlay> ReadParlayAsync(LineOpsDbContext db, Guid id)
        => db.Parlays.AsNoTracking().Include(p => p.Legs).FirstAsync(p => p.Id == id);

    [Fact]
    public async Task A_new_stake_on_a_won_parlay_is_paid_again_at_that_stake()
    {
        await using var db = fixture.CreateContext();
        var parlay = await WonParlayAsync(db, quoted: 264);
        Assert.Equal(36.40m, (await ReadParlayAsync(db, parlay.Id)).Payout);

        // The win stood at $10; at $20 the same price pays double. Leaving 36.40 would be a lie.
        await Service(db).UpdateParlayAsync(parlay.Id, new ParlayEdit("draftkings", 20m, 264, "Sunday double"));

        var after = await ReadParlayAsync(db, parlay.Id);
        Assert.Equal(EntryResult.Win, after.Result);
        Assert.Equal(20m, after.Stake);
        Assert.Equal(72.80m, after.Payout);
        Assert.NotNull(after.SettledAt);
    }

    [Fact]
    public async Task A_corrected_leg_price_reprices_a_parlay_the_book_gave_no_price_for()
    {
        await using var db = fixture.CreateContext();
        var parlay = await WonParlayAsync(db, quoted: null);
        var legs = parlay.Legs.OrderBy(l => l.Id).ToList();

        await Service(db).UpdateParlayAsync(parlay.Id, new ParlayEdit("draftkings", 10m, null, "Sunday double",
            new Dictionary<int, int> { [legs[1].Id] = 150 }));

        var after = await ReadParlayAsync(db, parlay.Id);
        var corrected = after.Legs.Single(l => l.Id == legs[1].Id);

        Assert.Equal(EntryResult.Win, after.Result);
        Assert.Equal(Math.Round(10m * (decimal)OddsMath.CombinedDecimal([-110, 150]), 2), after.Payout);
        Assert.Equal(150, corrected.PriceTaken);
        Assert.Equal(EntryResult.Win, corrected.Result);
        Assert.Equal(0m, corrected.Stake);

        // The typed number is the operator's; the leg left alone keeps the price the desk fetched.
        Assert.True(corrected.ManualPrice);
        Assert.False(after.Legs.Single(l => l.Id == legs[0].Id).ManualPrice);
    }

    [Fact]
    public async Task A_note_alone_leaves_a_settled_parlay_as_it_stood()
    {
        await using var db = fixture.CreateContext();
        var parlay = await WonParlayAsync(db, quoted: 264);
        var before = await ReadParlayAsync(db, parlay.Id);

        await Service(db).UpdateParlayAsync(parlay.Id, new ParlayEdit("DraftKings ", 10m, 264, "late note"));

        var after = await ReadParlayAsync(db, parlay.Id);
        Assert.Equal(EntryResult.Win, after.Result);
        Assert.Equal(before.Payout, after.Payout);
        Assert.Equal(before.SettledAt, after.SettledAt);
        Assert.Equal("late note", after.Note);
        Assert.Equal("draftkings", after.Book);   // the same book, written the way every reader matches it
    }

    [Fact]
    public async Task A_new_book_moves_the_legs_with_it_and_the_parlay_is_graded_again()
    {
        await using var db = fixture.CreateContext();
        var parlay = await WonParlayAsync(db, quoted: 264);

        await Service(db).UpdateParlayAsync(parlay.Id, new ParlayEdit("FanDuel", 10m, 264, null));

        var after = await ReadParlayAsync(db, parlay.Id);
        Assert.Equal("fanduel", after.Book);
        Assert.All(after.Legs, l => Assert.Equal("fanduel", l.Book));
        Assert.All(after.Legs, l => Assert.Equal(EntryResult.Win, l.Result));
        Assert.Equal(EntryResult.Win, after.Result);
        Assert.Equal(36.40m, after.Payout);
        Assert.Null(after.Note);
    }

    /// <summary>
    /// A leg that is a described bet is never graded, so a parlay holding one is settled by
    /// hand. Its result is the operator's and survives an edit, paid at the new money — and a
    /// win with no book's price left to pay it at goes back to pending.
    /// </summary>
    [Fact]
    public async Task A_hand_result_on_a_parlay_settlement_cannot_grade_is_kept_at_the_new_stake()
    {
        await using var db = fixture.CreateContext();
        var (game, home, _) = await FinalGameAsync(db, 3, 1);
        var journal = Service(db);

        var parlay = await journal.SaveParlayAsync(new ParlayDraft("fanduel", 5m, 200, null, [
            new EntryDraft(game.Id, Markets.Moneyline, null, home.Name, null, -150, 0m, "", null),
            new EntryDraft(null, "other", "Player X 2+ hits", "—", null, 180, 0m, "", null)
        ]));
        await new SettlementService(db, NullLogger<SettlementService>.Instance).SettleAsync();
        await journal.SetParlayResultAsync(parlay.Id, EntryResult.Win, OddsMath.PayoutOnWin(200, 5m));
        var settledAt = (await ReadParlayAsync(db, parlay.Id)).SettledAt;

        await journal.UpdateParlayAsync(parlay.Id, new ParlayEdit("fanduel", 10m, 200, null));

        var kept = await ReadParlayAsync(db, parlay.Id);
        Assert.Equal(EntryResult.Win, kept.Result);
        Assert.Equal(OddsMath.PayoutOnWin(200, 10m), kept.Payout);
        Assert.Equal(settledAt, kept.SettledAt);

        await journal.UpdateParlayAsync(parlay.Id, new ParlayEdit("fanduel", 10m, null, null));

        var unpriced = await ReadParlayAsync(db, parlay.Id);
        Assert.Equal(EntryResult.Pending, unpriced.Result);
        Assert.Null(unpriced.Payout);
        Assert.Null(unpriced.SettledAt);
    }

    [Fact]
    public async Task An_open_parlay_takes_the_edit_and_stays_pending()
    {
        await using var db = fixture.CreateContext();
        var journal = Service(db);

        var parlay = await journal.SaveParlayAsync(new ParlayDraft("betmgm", 5m, null, null, [
            new EntryDraft(null, "other", "Prop one", "—", null, 120, 0m, "", null),
            new EntryDraft(null, "other", "Prop two", "—", null, 0, 0m, "", null)
        ]));
        var unpriced = parlay.Legs.Single(l => l.PriceTaken == 0);

        await journal.UpdateParlayAsync(parlay.Id, new ParlayEdit("betmgm", 15m, 450, "both props",
            new Dictionary<int, int> { [unpriced.Id] = 140 }));

        var after = await ReadParlayAsync(db, parlay.Id);
        Assert.Equal(EntryResult.Pending, after.Result);
        Assert.Equal(15m, after.Stake);
        Assert.Equal(450, after.PriceQuoted);
        Assert.Equal(140, after.Legs.Single(l => l.Id == unpriced.Id).PriceTaken);
        Assert.Equal("Prop two", after.Legs.Single(l => l.Id == unpriced.Id).FreeTextMarket);
    }

    [Fact]
    public async Task A_leg_of_another_parlay_a_stake_of_nothing_and_a_price_that_is_not_one_are_refused()
    {
        await using var db = fixture.CreateContext();
        var journal = Service(db);

        ParlayDraft Draft() => new("caesars", 5m, null, null, [
            new EntryDraft(null, "other", "Prop one", "—", null, 120, 0m, "", null),
            new EntryDraft(null, "other", "Prop two", "—", null, 130, 0m, "", null)
        ]);

        var parlay = await journal.SaveParlayAsync(Draft());
        var other = await journal.SaveParlayAsync(Draft());

        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.UpdateParlayAsync(parlay.Id,
            new ParlayEdit("caesars", 5m, null, null, new Dictionary<int, int> { [other.Legs[0].Id] = 150 })));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => journal.UpdateParlayAsync(parlay.Id,
            new ParlayEdit("caesars", 0m, null, null)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => journal.UpdateParlayAsync(parlay.Id,
            new ParlayEdit("caesars", 5m, null, null, new Dictionary<int, int> { [parlay.Legs[0].Id] = 50 })));

        var untouched = await ReadParlayAsync(db, parlay.Id);
        Assert.Equal(5m, untouched.Stake);
        Assert.All(untouched.Legs, l => Assert.NotEqual(150, l.PriceTaken));
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
