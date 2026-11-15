using AngleSharp.Dom;
using Bunit;
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Desk.Primitives;
using LineOps.Web.Components.Desk;
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// A Board price taken in its row. Pressing a price floated a wager window over the board for
/// what is usually one number and one stake; the take answers under the row instead — the book,
/// the stake, what is worth knowing, and Log, which asks in place before it records anything.
/// </summary>
public class QuickTakeTests : WagerTestContext
{
    private IRenderedComponent<QuickTake> Render(BoardRow? row = null, string market = Markets.Moneyline,
        string outcome = BoardRows.Home, string? book = null, Action? onClose = null,
        Action<(string Book, decimal Stake)>? onOpenForm = null)
        => RenderComponent<QuickTake>(p =>
        {
            p.Add(x => x.Row, row ?? BoardRows.Row());
            p.Add(x => x.Market, market);
            p.Add(x => x.Outcome, outcome);
            p.Add(x => x.Matchup, "Bills at Chiefs");
            if (book is not null) p.Add(x => x.Book, book);
            if (onClose is not null) p.Add(x => x.OnClose, onClose);
            if (onOpenForm is not null) p.Add(x => x.OnOpenForm, onOpenForm);
        });

    private static IElement Key(IRenderedFragment cut, string startsWith)
        => cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(startsWith, StringComparison.Ordinal));

    private static void Stake25(IRenderedFragment cut)
        => cut.FindAll(".stake-row button").First(b => b.TextContent.Trim() == "$25").Click();

    [Fact]
    public void It_opens_on_the_pressed_price_with_every_book_best_first()
    {
        using var dollars = new UsDollars();
        var cut = Render();

        Assert.Equal("-128", cut.Find(".qtake__price").TextContent.Trim());
        Assert.Equal("at draftkings", cut.Find(".qtake__at").TextContent.Trim());

        var books = cut.FindAll(".qtake__line [role=radio]").Select(b => b.TextContent.Trim()).ToList();
        Assert.Equal(["DK -128", "FD -135"], books);
        Assert.Equal("true", cut.FindAll(".qtake__line [role=radio]")[0].GetAttribute("aria-checked"));
    }

    [Fact]
    public void A_book_named_by_the_press_is_the_one_shown()
    {
        var cut = Render(book: "fanduel");

        Assert.Equal("-135", cut.Find(".qtake__price").TextContent.Trim());
    }

    [Fact]
    public void Log_waits_for_a_stake_and_says_so()
    {
        var cut = Render();

        Assert.True(Key(cut, "Log").HasAttribute("disabled"));
        Assert.Equal("A stake comes first.", cut.Find(".keyrow__reason").TextContent.Trim());
    }

    [Fact]
    public async Task Log_asks_in_the_row_and_records_only_on_the_answer()
    {
        using var dollars = new UsDollars();
        var cut = Render();

        Stake25(cut);
        Key(cut, "Log $25").Click();

        Assert.Contains("Log $25 on", cut.Find(".confirm__ask").TextContent);
        Assert.Empty(Data.Straights);

        await cut.InvokeAsync(() => Key(cut, "Log wager").Click());

        var draft = Assert.Single(Data.Straights);
        Assert.Equal(("draftkings", -128m, 25m), (draft.Book, draft.PriceTaken, draft.Stake));
        Assert.StartsWith("Logged $25 on", cut.Find(".qtake__receipt-text").TextContent.Trim());
        Assert.Equal(DeskState.Positive, LastToast.State);
    }

    [Fact]
    public void Cancel_puts_the_question_away_and_logs_nothing()
    {
        using var dollars = new UsDollars();
        var cut = Render();

        Stake25(cut);
        Key(cut, "Log $25").Click();
        Key(cut, "Cancel").Click();

        Assert.Empty(cut.FindAll(".confirm"));
        Assert.Empty(Data.Straights);
        Assert.NotNull(Key(cut, "Log $25"));
    }

    [Fact]
    public void Escape_steps_back_one_state_at_a_time()
    {
        using var dollars = new UsDollars();
        var closed = 0;
        var cut = Render(onClose: () => closed++);

        Stake25(cut);
        Key(cut, "Log $25").Click();
        cut.Find(".qtake").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".confirm"));
        Assert.Equal(0, closed);

        cut.Find(".qtake").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Add_to_slip_banks_the_stake_at_the_chosen_book()
    {
        using var dollars = new UsDollars();
        var cut = Render(book: "fanduel");

        Stake25(cut);
        Key(cut, "Add to slip").Click();

        var card = Assert.Single(Slip.Items);
        Assert.Equal(("fanduel", -135m, 25m), (card.Book, card.PriceAmerican, card.Stake));
    }

    [Fact]
    public void A_side_already_in_the_journal_is_said_before_logging()
    {
        Data.Latest = new JournalEntry
        {
            GameId = BoardRows.GameId,
            Market = Markets.Moneyline,
            Outcome = BoardRows.Home,
            Book = "draftkings",
            PriceTaken = -130,
            Stake = 25,
            PlacedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };

        var cut = Render();

        Assert.Contains(cut.FindAll(".check__text"), c => c.TextContent.StartsWith("Already in the journal", StringComparison.Ordinal));
    }

    [Fact]
    public void A_clean_bill_says_only_the_value()
    {
        var cut = Render();

        var check = Assert.Single(cut.FindAll(".check__text"));
        Assert.StartsWith("Value:", check.TextContent);
    }

    [Fact]
    public void A_price_gone_to_its_close_cannot_be_logged()
    {
        var cut = Render(BoardRows.Row(closing: true));

        Assert.Contains("closed", cut.Find(".qtake__gone").TextContent);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim().StartsWith("Log", StringComparison.Ordinal));
    }

    [Fact]
    public void The_full_form_carries_the_book_and_the_stake()
    {
        using var dollars = new UsDollars();
        (string Book, decimal Stake)? opened = null;
        var cut = Render(book: "fanduel", onOpenForm: f => opened = f);

        Stake25(cut);
        cut.Find(".snippet__open").Click();

        Assert.Equal(("fanduel", 25m), opened);
    }

    /// <summary>
    /// A narrow take stacks each book segment's monogram over its price by breaking the label at
    /// its spaces (lineops.css, @container qtake). That breaks every segment in the same place
    /// only while each label is a monogram and a price with one space between them.
    /// </summary>
    [Fact]
    public void Every_book_segment_breaks_in_one_place_between_its_book_and_price()
    {
        var cut = Render();

        var books = cut.FindAll(".qtake__line [role=radio]").Select(b => b.TextContent.Trim()).ToList();

        Assert.True(books.Count > 1);
        Assert.All(books, label =>
        {
            var parts = label.Split(' ');
            Assert.Equal(2, parts.Length);
            Assert.Matches("^[+-]?\\d+(\\.\\d+)?$", parts[1].Replace('−', '-'));
        });
    }

    // ---- The cell that opened it -----------------------------------------------------

    private IRenderedComponent<PriceCell> Cell(bool taking = false, bool banked = false)
        => RenderComponent<PriceCell>(p => p
            .Add(x => x.Offer, BoardRows.Row().Moneyline.First)
            .Add(x => x.OnTake, () => { })
            .Add(x => x.Taking, taking)
            .Add(x => x.Banked, banked));

    [Fact]
    public void The_pressed_cell_stays_lit_and_says_it_is_open()
    {
        var cell = Cell(taking: true).Find("button.price");

        Assert.Contains("price--taking", cell.ClassList);
        Assert.Equal("true", cell.GetAttribute("aria-expanded"));
    }

    [Fact]
    public void A_cell_with_nothing_open_reports_collapsed()
    {
        var cell = Cell().Find("button.price");

        Assert.DoesNotContain("price--taking", cell.ClassList);
        Assert.Equal("false", cell.GetAttribute("aria-expanded"));
    }

    [Fact]
    public void A_banked_price_says_it_is_on_the_slip()
    {
        Assert.Equal("on slip", Cell(banked: true).Find(".price__slip").TextContent.Trim());
        Assert.Empty(Cell().FindAll(".price__slip"));
    }
}
