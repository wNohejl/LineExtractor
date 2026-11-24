using XLines.Core.Analytics;
using XLines.Core.Entities;
using XLines.Data;
using XLines.Ingestion.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace XLines.Tests.Reliability;

/// <summary>
/// The book list is cached per database for a short while, because the Journal, the wager window
/// and the take's price fields all ask for it on opening; a save through the journal drops it, so
/// a book just typed is offered at once.
/// </summary>
[Collection(PostgresCollection.Name)]
public class JournalBooksCacheTests(PostgresFixture fixture)
{
    private static JournalService Service(XLinesDbContext db)
        => new(db, new SettlementService(db, NullLogger<SettlementService>.Instance));

    private static EntryDraft Untracked(string book)
        => new(null, Markets.Moneyline, "a described bet", "Someone", null, -110, 10m, book, null);

    [Fact]
    public async Task A_second_read_is_the_cached_list_and_a_book_written_around_the_service_waits_for_it()
    {
        await using var db = fixture.CreateContext();
        var journal = Service(db);

        // A save first, so the cache starts empty whatever another test in the collection read.
        await journal.SaveEntryAsync(Untracked("draftkings"));
        var first = await journal.KnownBooksAsync();

        // Written past the service, as a journal import or an ingested close is: not seen yet.
        var stranger = $"side-{Guid.NewGuid():N}"[..13];
        db.JournalEntries.Add(new JournalEntry
        {
            Market = Markets.Moneyline, FreeTextMarket = "a described bet", Outcome = "Someone",
            PriceTaken = -110, Stake = 1m, Book = stranger,
            PlacedAt = DateTimeOffset.UtcNow, Result = EntryResult.Pending
        });
        await db.SaveChangesAsync();

        var second = await Service(db).KnownBooksAsync();

        Assert.Same(first, second);
        Assert.DoesNotContain(stranger, second);
    }

    [Fact]
    public async Task A_book_saved_through_the_journal_is_offered_at_once()
    {
        await using var db = fixture.CreateContext();
        var journal = Service(db);
        var before = await journal.KnownBooksAsync();

        var typed = $"book-{Guid.NewGuid():N}"[..13];
        await journal.SaveEntryAsync(Untracked(typed));

        var after = await Service(db).KnownBooksAsync();

        Assert.NotSame(before, after);
        Assert.Contains(typed, after);
    }

    [Fact]
    public async Task A_parlay_saved_through_the_journal_drops_the_cache_too()
    {
        await using var db = fixture.CreateContext();
        var journal = Service(db);
        await journal.KnownBooksAsync();

        var typed = $"parl-{Guid.NewGuid():N}"[..13];
        await journal.SaveParlayAsync(new ParlayDraft(typed, 5m, 300, null, [Untracked(typed), Untracked(typed)]));

        Assert.Contains(typed, await Service(db).KnownBooksAsync());
    }
}
