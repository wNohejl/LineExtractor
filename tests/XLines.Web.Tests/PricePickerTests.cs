using Bunit;
using XLines.Core.Analytics;
using XLines.Core.Entities;
using XLines.Desk.Primitives;
using XLines.Web.Components.Desk;
using XLines.Web.Services;
using XLines.Web.Tests.Support;
using Microsoft.AspNetCore.Components;

namespace XLines.Web.Tests;

/// <summary>
/// The one control a wager's price is chosen with: every book quoting the side, best first, by
/// name, each price in a column, the edge against fair where the side has one at that number,
/// a stale price's age, and Custom price… leading to the window's own typed price.
/// </summary>
public class PricePickerTests : DeskTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 18, 0, 0, TimeSpan.Zero);
    private static readonly FairPrice Pinnacle = new(0.49, 104, FairValue.SharpBook, 1);

    private static WagerOffer Offer(string book, decimal price, bool best = false, decimal? line = 3m,
        double? ev = -0.018, bool fair = true, TimeSpan? age = null, bool closing = false)
        => new(Markets.Spread, "Minnesota Vikings", line, book, price, Now - (age ?? TimeSpan.FromMinutes(5)),
            closing, fair ? Pinnacle : null, ev, 4, best);

    // Handed over unordered, as a feed's rungs arrive: the picker orders them.
    private static readonly IReadOnlyList<WagerOffer> Books =
    [
        Offer("draftkings", -108, ev: -0.06),
        Offer("betmgm", -105, line: 3.5m, fair: false, ev: null),
        Offer("fanduel", 100, best: true, ev: 0.021, age: TimeSpan.FromHours(71) + TimeSpan.FromMinutes(47)),
        Offer("pinnacle", -102, ev: -0.01)
    ];

    private IRenderedComponent<PricePicker> Picker(IReadOnlyList<WagerOffer>? books = null, string? chosen = "fanduel",
        Action<string>? onChoose = null, Action? onCustom = null)
        => RenderComponent<PricePicker>(p =>
        {
            p.Add(x => x.Books, books ?? Books);
            p.Add(x => x.Chosen, chosen);
            p.Add(x => x.Now, Now);
            if (onChoose is not null) p.Add(x => x.OnChoose, onChoose);
            if (onCustom is not null) p.Add(x => x.OnCustom, EventCallback.Factory.Create(this, onCustom));
        });

    [Fact]
    public void The_key_names_the_book_and_marks_the_best_and_leaves_the_price_to_the_row()
    {
        var cut = Picker();

        var key = cut.Find(PricePickers.Key);
        Assert.Equal("FanDuel", key.QuerySelector(".popup__label")!.TextContent);
        Assert.Equal("Best", key.QuerySelector(".popup__mark")!.TextContent);
        Assert.Null(key.QuerySelector(".popup__figure"));
        Assert.StartsWith("Book, FanDuel, +3 +100, best price", key.GetAttribute("aria-label"));
    }

    [Fact]
    public void Books_are_listed_best_first_then_the_best_number_by_price_then_any_other_number()
    {
        var cut = Picker();
        PricePickers.Open(cut);

        var names = PricePickers.Rows(cut).Select(r => r.QuerySelector(".menu__label")!.TextContent).ToList();
        Assert.Equal(["FanDuel", "Pinnacle", "DraftKings", "BetMGM"], names);

        var figures = PricePickers.Rows(cut).Select(r => r.QuerySelector(".menu__figure")!.TextContent).ToList();
        Assert.Equal(["+3 +100", "+3 -102", "+3 -108", "+3.5 -105"], figures);
    }

    [Fact]
    public void Only_the_best_carries_the_mark_and_is_heard_as_the_best_price()
    {
        var cut = Picker();
        PricePickers.Open(cut);

        var rows = PricePickers.Rows(cut);
        Assert.Equal("Best", rows[0].QuerySelector(".menu__mark")!.TextContent.Trim());
        Assert.All(rows.Skip(1), r => Assert.Null(r.QuerySelector(".menu__mark")));
        Assert.Contains("best price", rows[0].GetAttribute("aria-label"));
        Assert.DoesNotContain("best price", rows[1].GetAttribute("aria-label"));
        Assert.Equal("true", rows[0].GetAttribute("aria-checked"));
    }

    [Fact]
    public void The_edge_is_the_value_checks_own_and_only_where_there_is_a_fair_price()
    {
        var cut = Picker();
        PricePickers.Open(cut);

        var rows = PricePickers.Rows(cut);
        var best = rows[0].QuerySelector(".menu__subfigure")!;
        Assert.Equal(Checks.Percent(0.021), best.TextContent);
        Assert.Contains("menu__subfigure--positive", best.ClassList);

        var behind = rows[2].QuerySelector(".menu__subfigure")!;
        Assert.Equal(Checks.Percent(-0.06), behind.TextContent);
        Assert.Contains("menu__subfigure--warning", behind.ClassList);

        // BetMGM is on another number: no fair price there, so no edge is drawn or heard.
        Assert.Null(rows[3].QuerySelector(".menu__subfigure"));
        Assert.DoesNotContain("edge", rows[3].GetAttribute("aria-label"));

        Assert.Equal("Edge against fair +104 (Pinnacle, no-vig). A book on another number has no fair price to read.",
            cut.Find(".popup__foot").TextContent);
    }

    [Fact]
    public void A_stale_price_says_how_old_it_is_and_a_fresh_one_says_nothing()
    {
        var cut = Picker();
        PricePickers.Open(cut);

        var rows = PricePickers.Rows(cut);
        Assert.Equal("Captured 71h 47m ago", rows[0].QuerySelector(".menu__desc")!.TextContent);
        Assert.True(string.IsNullOrEmpty(rows[1].QuerySelector(".menu__desc")?.TextContent));
    }

    [Fact]
    public void A_closing_line_says_so_and_reads_no_edge()
    {
        var cut = Picker([Offer("fanduel", 100, best: true, closing: true, ev: 0.02), Offer("pinnacle", -102, closing: true)]);
        PricePickers.Open(cut);

        var row = PricePickers.Rows(cut)[0];
        Assert.Equal("Closing line", row.QuerySelector(".menu__desc")!.TextContent);
        Assert.Null(row.QuerySelector(".menu__subfigure"));
    }

    [Fact]
    public void Choosing_a_book_reports_its_key()
    {
        string? chosen = null;
        var cut = Picker(onChoose: b => chosen = b);
        PricePickers.Open(cut);

        PricePickers.Rows(cut).First(r => r.TextContent.Contains("Pinnacle")).Click();

        Assert.Equal("pinnacle", chosen);
    }

    [Fact]
    public void Custom_price_ends_the_list_and_runs_the_windows_typed_path()
    {
        var typed = 0;
        string? chosen = null;
        var cut = Picker(onChoose: b => chosen = b, onCustom: () => typed++);

        PricePickers.TypePrice(cut);

        Assert.Equal(1, typed);
        Assert.Null(chosen);
    }

    [Fact]
    public void Without_a_typed_path_there_is_no_custom_price()
    {
        var cut = Picker();
        PricePickers.Open(cut);

        Assert.Empty(cut.FindAll(".price-picker [role=menuitem]"));
    }

    [Fact]
    public void One_book_and_no_typed_path_reads_as_a_statement()
    {
        var cut = Picker([Offer("fanduel", 100, best: true)]);

        Assert.Empty(cut.FindAll("button"));
        Assert.Contains("FanDuel", cut.Find(".popup--statement").TextContent);
    }

    [Fact]
    public void One_book_with_a_typed_path_is_still_a_choice()
    {
        var cut = Picker([Offer("fanduel", 100, best: true)], onCustom: () => { });

        Assert.NotNull(cut.Find(PricePickers.Key));
    }

    [Fact]
    public void Nine_books_are_one_key_and_nine_rows()
    {
        var books = Enumerable.Range(0, 9)
            .Select(i => Offer($"book{i}", -100 - i, best: i == 0))
            .ToList();

        var cut = Picker(books, chosen: "book0");
        PricePickers.Open(cut);

        Assert.Single(cut.FindAll(PricePickers.Key));
        Assert.Equal(9, PricePickers.Rows(cut).Count);
    }
}
