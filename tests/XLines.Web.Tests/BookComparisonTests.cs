using Bunit;
using XLines.Core.Analytics;
using XLines.Core.Entities;
using XLines.Data.CrossReference;
using XLines.Web.Components.Panels;
using XLines.Web.Components.Snippets;
using XLines.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace XLines.Web.Tests;

/// <summary>
/// A market laid out book by book shows its top five books and folds the rest behind a key that
/// names how many (BookComparison, MoreBooks, BookRanking) — in More bets, the Game window's market
/// in place, and Line movement alike. "Top" is the books holding the best price on either side
/// first, then the lowest hold, so the marked prices are always among the five showing.
/// </summary>
public class BookComparisonTests : WagerTestContext
{
    public BookComparisonTests()
    {
        Services.AddSingleton<Microsoft.EntityFrameworkCore.IDbContextFactory<XLines.Data.XLinesDbContext>>(new JournalParlayTests.NoDatabase());
    }

    // Ten books on a moneyline. The best home price is at WynnBET and the best away price at
    // Bovada — both late in the alphabet and both with the widest holds, so neither a name order
    // nor a hold order alone would show them in the first five.
    private static readonly string[] Ten =
        ["betmgm", "betonlineag", "betrivers", "bovada", "caesars", "draftkings", "fanduel", "pointsbetus", "unibet_us", "wynnbet"];

    private static double HoldOf(string book) => book switch
    {
        "wynnbet" => 0.09,
        "bovada" => 0.08,
        _ => 0.02 + Array.IndexOf(Ten, book) * 0.001
    };

    private static BookPrice Rung(string book, int price, double? ev = null)
        => new(book, price, null, OddsMath.ImpliedProbability(price)) { Hold = HoldOf(book), Ev = ev };

    private static BestOffer Side(string outcome, string best, int bestPrice, int others, bool closing = false)
    {
        var rungs = Ten.Select(b => Rung(b, b == best ? bestPrice : others, b == best ? 0.031 : -0.01))
            .OrderBy(r => r.Book == best ? 0 : 1)
            .ToList();

        return new BestOffer(outcome, best, bestPrice, null, DateTimeOffset.UtcNow, null, false, closing, rungs)
        {
            Fair = new FairPrice(0.55, -122, FairValue.SharpBook, 1)
        };
    }

    private static BestOffer Home(bool closing = false) => Side(BoardRows.Home, "wynnbet", -110, -125, closing);

    private static BestOffer Away(bool closing = false) => Side(BoardRows.Away, "bovada", 120, 104, closing);

    private static BoardRow TenBookRow()
    {
        var row = BoardRows.Row();
        return row with { Moneyline = new MarketPair(Markets.Moneyline, Home(), Away()) };
    }

    private IRenderedComponent<BookComparison> Comparison(bool closing = false)
        => RenderComponent<BookComparison>(p => p
            .Add(c => c.Market, Markets.Moneyline)
            .Add(c => c.First, Away(closing))
            .Add(c => c.Second, Home(closing))
            .Add(c => c.FirstSide, "Bills")
            .Add(c => c.SecondSide, "Chiefs"));

    private static IReadOnlyList<string> BookColumn(IRenderedFragment cut)
        => cut.FindAll(".mud-table-body .mud-table-row")
            .Select(r => r.QuerySelector(".mud-table-cell")!.TextContent.Trim())
            .ToList();

    // ---- The order --------------------------------------------------------------------------

    [Fact]
    public void Top_is_the_best_prices_first_then_the_lowest_hold()
    {
        var ranked = BookRanking.Priced(Away(), Home());

        Assert.Equal(10, ranked.Count);
        // Both best books lead, the narrower hold of the two first; then the lowest holds.
        Assert.Equal(["bovada", "wynnbet", "betmgm", "betonlineag", "betrivers"], ranked.Take(5));
    }

    [Fact]
    public void A_book_with_no_price_is_neither_a_row_nor_counted()
    {
        var away = Away() with { Rungs = Away().Rungs.Where(r => r.Book != "caesars").ToList() };
        var home = Home() with { Rungs = Home().Rungs.Where(r => r.Book != "caesars").ToList() };

        Assert.Equal(9, BookRanking.Priced(away, home).Count);
        Assert.DoesNotContain("caesars", BookRanking.Priced(away, home));
    }

    [Fact]
    public void The_key_names_how_many_are_folded()
    {
        Assert.Equal("View 5 more books", BookRanking.KeyLabel(5, expanded: false));
        Assert.Equal("View 1 more book", BookRanking.KeyLabel(1, expanded: false));
        Assert.Equal("View fewer books", BookRanking.KeyLabel(5, expanded: true));
        Assert.Equal("Top 5 of 10 books priced", BookRanking.CountText(10, expanded: false));
        Assert.Equal("All 10 books priced", BookRanking.CountText(10, expanded: true));
    }

    [Fact]
    public void The_fair_line_is_spelled_one_way()
        => Assert.Equal("Fair Kansas City Chiefs -122 (Pinnacle, no-vig)", BookRanking.FairText(null, Home()));

    // ---- The table -------------------------------------------------------------------------

    [Fact]
    public void Ten_books_show_five_and_a_key_that_says_five_more()
    {
        var cut = Comparison();

        Assert.Equal(5, BookColumn(cut).Count);

        var key = cut.Find(".bookmore button");
        Assert.Equal("View 5 more books", key.TextContent.Trim());
        Assert.Equal("false", key.GetAttribute("aria-expanded"));
        Assert.Contains("Top 5 of 10 books priced", cut.Find(".bookmore__count").TextContent);
        Assert.Contains("best price on either side first, then the lowest hold", cut.Find(".bookmore__count").TextContent);
    }

    [Fact]
    public void The_best_prices_are_within_the_visible_five()
    {
        var cut = Comparison();
        var books = BookColumn(cut);

        Assert.Contains(BookNames.Display("wynnbet"), books);
        Assert.Contains(BookNames.Display("bovada"), books);
        Assert.Equal(2, cut.FindAll(".bookcmp__best").Count);
        Assert.Equal(2, cut.FindAll(".bookcmp__dot").Count);
    }

    [Fact]
    public void The_key_reveals_every_book_by_name_and_folds_them_back()
    {
        var cut = Comparison();

        cut.Find(".bookmore button").Click();

        var books = BookColumn(cut);
        Assert.Equal(10, books.Count);
        Assert.All(Ten, b => Assert.Contains(BookNames.Display(b), books));

        var key = cut.Find(".bookmore button");
        Assert.Equal("View fewer books", key.TextContent.Trim());
        Assert.Equal("true", key.GetAttribute("aria-expanded"));

        key.Click();
        Assert.Equal(5, BookColumn(cut).Count);
        Assert.Equal("false", cut.Find(".bookmore button").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Five_books_or_fewer_draw_no_key()
    {
        var cut = RenderComponent<BookComparison>(p => p
            .Add(c => c.Market, Markets.Moneyline)
            .Add(c => c.First, BoardRows.Row().Moneyline.Second)
            .Add(c => c.Second, BoardRows.Row().Moneyline.First)
            .Add(c => c.FirstSide, "Bills")
            .Add(c => c.SecondSide, "Chiefs"));

        Assert.Equal(2, BookColumn(cut).Count);
        Assert.Empty(cut.FindAll(".bookmore"));
    }

    [Fact]
    public void A_price_reads_as_line_movement_quotes_it_with_its_edge_after()
    {
        var cut = Comparison();
        var best = cut.FindAll(".bookcmp__best").Select(e => e.TextContent.Trim()).ToList();

        Assert.Contains("+120", best);
        Assert.Contains("-110", best);
        Assert.Contains(cut.FindAll(".bookcmp__ev"), e => e.TextContent.Trim() == "+3.1%");
        Assert.DoesNotContain("% EV", cut.Markup);
    }

    // ---- More bets ---------------------------------------------------------------------------

    [Fact]
    public void More_bets_lays_each_market_out_as_a_card_in_a_flow()
    {
        Data.Row = TenBookRow();
        var cut = RenderComponent<BoardAllBets>(p => p.Add(x => x.GameId, BoardRows.GameId));

        Assert.NotNull(cut.Find(".wflow"));
        var titles = cut.FindAll(".wflow > .wsection .wgroup__bar .wgroup__title").Select(t => t.TextContent.Trim()).ToList();
        Assert.Equal(3, titles.Count);
        // The grid is the card's own child: the card frames it, and nothing frames it twice.
        Assert.NotEmpty(cut.FindAll(".wgroup > .deskgrid"));
        Assert.Contains("(Pinnacle, no-vig)", cut.Find(".wsection .wgroup__foot").TextContent);
        // The two price columns read as the game does: away, then home.
        var heads = cut.Find(".wsection").QuerySelectorAll(".mud-table-head .mud-table-cell").Select(h => h.TextContent.Trim()).ToList();
        Assert.Equal(4, heads.Count);
        Assert.Equal("Book", heads[0]);
        Assert.EndsWith(BoardRows.Away, heads[1]);
        Assert.EndsWith(BoardRows.Home, heads[2]);
        Assert.Equal("Hold", heads[3]);
    }

    [Fact]
    public void A_market_s_view_more_survives_a_redraw_and_is_its_own()
    {
        Data.Row = TenBookRow();
        var cut = RenderComponent<BoardAllBets>(p => p.Add(x => x.GameId, BoardRows.GameId));

        var moneyline = cut.FindAll(".wsection")[0];
        Assert.Equal(5, moneyline.QuerySelectorAll(".mud-table-body .mud-table-row").Length);

        cut.Find(".wsection .bookmore button").Click();
        cut.SetParametersAndRender(p => p.Add(x => x.GameId, BoardRows.GameId));

        moneyline = cut.FindAll(".wsection")[0];
        Assert.Equal(10, moneyline.QuerySelectorAll(".mud-table-body .mud-table-row").Length);
        Assert.Equal("true", moneyline.QuerySelector(".bookmore button")!.GetAttribute("aria-expanded"));
        Assert.Equal(moneyline.Id, moneyline.QuerySelector(".bookmore button")!.GetAttribute("aria-controls"));

        // Another game folds every market back to its top five.
        cut.SetParametersAndRender(p => p.Add(x => x.GameId, BoardRows.GameId + 1));
        cut.WaitForAssertion(() =>
            Assert.Equal(5, cut.FindAll(".wsection")[0].QuerySelectorAll(".mud-table-body .mud-table-row").Length));
    }

    // ---- The Game window's market in place --------------------------------------------------

    [Fact]
    public void The_game_window_s_market_caps_its_closes_too()
    {
        var cut = RenderComponent<MarketMovement>(p => p
            .Add(c => c.GameId, BoardRows.GameId)
            .Add(c => c.Market, Markets.Moneyline)
            .Add(c => c.MarketName, "Moneyline")
            .Add(c => c.First, Away(closing: true))
            .Add(c => c.Second, Home(closing: true))
            .Add(c => c.FirstSide, "Bills")
            .Add(c => c.SecondSide, "Chiefs")
            .Add(c => c.Id, "gamemove-test")
            .Add(c => c.Closing, true));

        Assert.Equal(5, BookColumn(cut).Count);
        Assert.Equal("gamemove-test", cut.Find(".bookmore button").GetAttribute("aria-controls"));
        Assert.Empty(cut.FindAll("button.take"));

        cut.Find(".bookmore button").Click();
        Assert.Equal(10, BookColumn(cut).Count);

        // A new price for the same market re-renders the row and keeps it open.
        cut.SetParametersAndRender(p => p.Add(c => c.PricedAt, DateTimeOffset.UtcNow));
        Assert.Equal(10, BookColumn(cut).Count);
    }

    // ---- Line movement -----------------------------------------------------------------------

    /// <summary>
    /// A side nobody has moved shows each book's standing in Line movement: the same five books
    /// lead as in More bets, ranked on the whole market, and the key shows the rest.
    /// </summary>
    [Fact]
    public void Line_movement_s_standing_caps_its_books_and_keeps_the_best_in_view()
    {
        var at = DateTimeOffset.UtcNow;
        var snaps = Ten.Select(b => new OddsSnapshot
        {
            Book = b, Market = Markets.Moneyline, Outcome = BoardRows.Home,
            PriceAmerican = b == "wynnbet" ? -110 : -125, CapturedAt = at.AddHours(-1)
        });
        var rows = OddsPanel.MovementRows(snaps, BoardRows.Home);

        var cut = RenderComponent<PriceStanding>(p => p
            .Add(x => x.Rows, rows)
            .Add(x => x.Side, Home())
            .Add(x => x.Other, Away())
            .Add(x => x.SideName, "Chiefs")
            .Add(x => x.OtherName, "Bills")
            .Add(x => x.ShowBooks, true)
            .Add(x => x.Now, at));

        var books = BookColumn(cut);
        Assert.Equal(5, books.Count);
        Assert.StartsWith(BookNames.Display("bovada"), books[0]);
        Assert.StartsWith(BookNames.Display("wynnbet"), books[1]);

        var key = cut.Find(".bookmore button");
        Assert.Equal("View 5 more books", key.TextContent.Trim());
        Assert.Equal(cut.Find(".standing__books").Id, key.GetAttribute("aria-controls"));

        key.Click();
        Assert.Equal(10, BookColumn(cut).Count);
    }
}
