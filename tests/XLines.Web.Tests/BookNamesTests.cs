using System.Text.Json;
using XLines.Web.Components.Panels;
using XLines.Web.Tests.Support;

namespace XLines.Web.Tests;

/// <summary>
/// Every book the desk polls goes by a name and a two-letter mark of its own. A mark is the only
/// thing telling two books apart in a dense cell (colour means state, ADR 0008), so two books
/// sharing one — "BE" for three keys, "PO" beside "PB" for one book under two keys — is the cell
/// naming the wrong book.
/// </summary>
public class BookNamesTests
{
    /// <summary>The books appsettings.json names for The Odds API to poll.</summary>
    private static IEnumerable<string> Polled()
    {
        var json = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Web", "appsettings.json"));
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

        return doc.RootElement.GetProperty("Ingestion").GetProperty("TheOddsApi").GetProperty("Bookmakers")
            .EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    [Fact]
    public void Every_polled_book_is_named_and_marked()
    {
        var polled = Polled().ToList();

        Assert.Equal(10, polled.Count);
        Assert.All(polled, book =>
        {
            Assert.Contains(book, BookNames.Known);
            Assert.NotEqual(book, BookNames.Display(book));
            Assert.Equal(2, BookNames.Code(book).Length);
        });
    }

    [Fact]
    public void No_two_books_share_a_mark()
    {
        // Two keys for one book (pointsbet/pointsbetus, caesars/williamhill_us) share a name and
        // so may share a mark; two names may not.
        var clashes = BookNames.Known
            .GroupBy(BookNames.Code)
            .Where(g => g.Select(BookNames.Display).Distinct().Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g)}")
            .ToList();

        Assert.True(clashes.Count == 0, "Books sharing a mark:\n" + string.Join("\n", clashes));
    }

    [Theory]
    [InlineData("espnbet", "theScore Bet", "TS")]
    [InlineData("betonlineag", "BetOnline.ag", "BO")]
    [InlineData("bovada", "Bovada", "BV")]
    [InlineData("pointsbetus", "PointsBet", "PB")]
    [InlineData("williamhill_us", "Caesars", "CZ")]
    [InlineData("novelbook", "Novelbook", "NO")]
    public void A_book_reads_by_its_name_and_its_mark(string key, string name, string code)
    {
        Assert.Equal(name, BookNames.Display(key));
        Assert.Equal(code, BookNames.Code(key));
    }
}
