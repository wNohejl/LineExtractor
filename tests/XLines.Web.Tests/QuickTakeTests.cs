using AngleSharp.Dom;
using Bunit;
using XLines.Core.Entities;
using XLines.Data.CrossReference;
using XLines.Desk.Primitives;
using XLines.Web.Components.Desk;
using XLines.Web.Tests.Support;

namespace XLines.Web.Tests;

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
        Assert.Equal("DraftKings", cut.Find(".qtake__line .popup__label").TextContent.Trim());

        PricePickers.Open(cut);
        var books = PricePickers.Rows(cut);
        Assert.Equal(2, books.Count);
        Assert.Contains("DraftKings", books[0].TextContent);
        Assert.Contains("-128", books[0].TextContent);
        Assert.Contains("FanDuel", books[1].TextContent);
        Assert.Contains("-135", books[1].TextContent);
        Assert.Equal("true", books[0].GetAttribute("aria-checked"));
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
    /// The books were a segment each, which stopped at five and broke into monograms over prices
    /// in a narrow take. They are one key now, whatever the count: nothing in the take is a radio
    /// per book, and the list holds them all.
    /// </summary>
    [Fact]
    public void The_books_are_one_key_however_many_quote_the_side()
    {
        var cut = Render();

        Assert.Empty(cut.FindAll(".qtake__line [role=radio]"));
        Assert.Single(cut.FindAll(".qtake__line " + PricePickers.Key));

        PricePickers.Open(cut);
        Assert.Equal(2, PricePickers.Rows(cut).Count);
    }

    // ---- Custom price…, in the row ---------------------------------------------------------
    // It opened the whole Place wager window in another column, for one number. The take's own
    // price line becomes the typed fields now — the window's fields and the window's reading of
    // them (TypedPriceFields, TypedPrice) — and the row is never left.

    private IRenderedComponent<QuickTake> RenderTyped(string market = Markets.Moneyline, string outcome = BoardRows.Home,
        string? book = null, Action<(string Book, decimal Stake)>? onOpenForm = null, Action<TypedTake>? onOpenTyped = null)
        => RenderComponent<QuickTake>(p =>
        {
            p.Add(x => x.Row, BoardRows.Row());
            p.Add(x => x.Market, market);
            p.Add(x => x.Outcome, outcome);
            p.Add(x => x.Matchup, "Bills at Chiefs");
            if (book is not null) p.Add(x => x.Book, book);
            p.Add(x => x.OnOpenForm, onOpenForm ?? (_ => { }));
            p.Add(x => x.OnOpenTyped, onOpenTyped ?? (_ => { }));
        });

    private static IElement Field(IRenderedFragment cut, string name) => cut.Find($"input[aria-label='{name}']");

    private static string Squash(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void Custom_price_turns_the_price_line_into_the_typed_fields_and_opens_no_window()
    {
        (string, decimal)? form = null;
        TypedTake? typed = null;
        var cut = RenderTyped(onOpenForm: f => form = f, onOpenTyped: t => typed = t);

        Stake25(cut);
        PricePickers.TypePrice(cut);

        // In the row: the price starts from the number shown, the book from the book it was at,
        // and a moneyline asks for no line.
        Assert.Equal("-128", Field(cut, "Price").GetAttribute("value"));
        Assert.Equal("DraftKings", Field(cut, "Book").GetAttribute("value"));
        Assert.Empty(cut.FindAll("input[aria-label='Line']"));
        Assert.Contains("qtake--typed", cut.Find(".qtake").ClassList);

        // The picker has given its place to the fields, and the way back is beside the price.
        Assert.False(PricePickers.Shown(cut));
        Assert.NotNull(Key(cut, TypedPriceFields.UseFetchedLabel));

        // Nothing was asked of the host: no window, no form.
        Assert.Null(form);
        Assert.Null(typed);
        Assert.Empty(Data.Straights);

        // The stake typed before stays, and the advisory says the price is the operator's own.
        Assert.Equal("25", cut.Find("input[aria-label='Stake']").GetAttribute("value"));
        Assert.Contains(cut.FindAll(".check__text"),
            c => c.TextContent.Trim() == "Your own price, entered by hand rather than read from a scan.");
    }

    /// <summary>One edge: the take is the card, so its typed rows are rows of it, never a group inside it.</summary>
    /// <summary>
    /// The fields drew about 85px tall and the card's width: a flex basis for width sat on the input
    /// inside its column .field-set and became its height. The take's sheet sizes each field by what
    /// it holds and puts no flex basis on a field.
    /// </summary>
    [Fact]
    public void The_take_s_typed_fields_are_sized_by_what_they_hold_not_stretched()
    {
        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Web", "wwwroot", "css", "xlines.css"));

        Assert.DoesNotContain(".qtake__typed .wfield--short", css);
        Assert.DoesNotContain(".wager-typed__cell > .field-set { flex: 1 1", css);
        Assert.Contains(".typed-inline > .field-set { flex: 0 0 auto;", css);

        // Each field's width must out-rank every width the Desk's sheets give a .field: the one-class
        // rule lost to `.field-set > .field { width: 100% }` and the fields drew as equal thirds.
        var desk = string.Join("\n", XLines.Desk.Testing.DeskConventions.DeskSheetNames.Select(XLines.Desk.Testing.DeskConventions.DeskSheet));
        var deskWidest = WidthRules(desk).Where(r => r.Selector.Contains(".field", StringComparison.Ordinal)
                                                     && !r.Selector.Contains("select", StringComparison.Ordinal))
            .Max(r => Classes(r.Selector));

        foreach (var (field, width) in new[] { ("typed-inline__price", "9ch"), ("typed-inline__line", "7ch"), ("typed-inline__book", "15ch") })
        {
            var rule = Assert.Single(WidthRules(css), r => r.Selector.TrimEnd().EndsWith("." + field, StringComparison.Ordinal));
            Assert.Equal(width, rule.Width);
            Assert.True(Classes(rule.Selector) > deskWidest,
                $"{rule.Selector} ({Classes(rule.Selector)} classes) does not out-rank the Desk's widest .field width ({deskWidest} classes)");
        }
    }

    /// <summary>Every rule that sets a width, by its selectors one at a time, comments stripped.</summary>
    private static IEnumerable<(string Selector, string Width)> WidthRules(string css)
    {
        var bare = System.Text.RegularExpressions.Regex.Replace(css, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        foreach (System.Text.RegularExpressions.Match rule in System.Text.RegularExpressions.Regex.Matches(bare, @"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}"))
        {
            var width = System.Text.RegularExpressions.Regex.Match(rule.Groups["body"].Value, @"(?<![-\w])width\s*:\s*(?<w>[^;]+)");
            if (!width.Success)
                continue;

            foreach (var selector in rule.Groups["sel"].Value.Split(','))
                yield return (selector.Trim(), width.Groups["w"].Value.Trim());
        }
    }

    /// <summary>A selector's class, attribute and pseudo-class count: its specificity where no id is used.</summary>
    private static int Classes(string selector)
        => System.Text.RegularExpressions.Regex.Matches(selector, @"\.[\w-]+|\[[^\]]+\]|:(?!:)[\w-]+").Count;

    [Fact]
    public void The_typed_fields_sit_on_one_line_each_its_content_s_width()
    {
        var cut = RenderTyped(Markets.Spread, BoardRows.Away);
        PricePickers.TypePrice(cut);

        // Price, line and book on one line, each labelled above its field, the way back at its end.
        var line = cut.Find(".qtake__typed .typed-inline");
        Assert.Equal("Your own price", line.GetAttribute("aria-label"));
        Assert.Equal(["Price", "Line", "Book"], line.QuerySelectorAll(".field-lbl").Select(l => l.TextContent.Trim()));
        Assert.Equal(["typed-inline__price", "typed-inline__line", "typed-inline__book"],
            line.QuerySelectorAll("input").Select(i => i.ClassList.First(c => c.StartsWith("typed-inline__", StringComparison.Ordinal))));
        Assert.EndsWith(TypedPriceFields.UseFetchedLabel, line.Children.Last().TextContent.Trim());

        // Not a form inside the take: no label column, no group frame.
        Assert.Empty(cut.FindAll(".qtake__typed .wgroup__row--form, .qtake__typed .wgroup"));
        Assert.Equal("Stake", cut.Find(".qtake__stake-lbl").TextContent.Trim());

        // A spread's line starts from the number shown, as a price does.
        Assert.Equal("2.5", Field(cut, "Line").GetAttribute("value"));
    }

    [Fact]
    public void Custom_price_moves_focus_to_the_price_and_the_way_back_returns_it_to_the_picker()
    {
        var focus = JSInterop.SetupModule(XLines.Web.Services.DeskFocus.Module);
        focus.SetupVoid(XLines.Web.Services.DeskFocus.Function, _ => true);
        var cut = RenderTyped();

        PricePickers.TypePrice(cut);

        var calls = focus.Invocations[XLines.Web.Services.DeskFocus.Function];
        var toPrice = Assert.Single(calls);
        var selector = (string)toPrice.Arguments[0]!;
        Assert.Equal("Price", cut.Find(selector).GetAttribute("aria-label"));

        Key(cut, TypedPriceFields.UseFetchedLabel).Click();

        cut.WaitForAssertion(() => Assert.Equal(2, calls.Count), Settle);
        Assert.EndsWith(".price-picker .popup__key", (string)calls[1].Arguments[0]!);
        Assert.NotNull(cut.Find((string)calls[1].Arguments[0]!));
    }

    [Fact]
    public void Use_the_fetched_price_restores_the_picker_on_the_best_book()
    {
        var cut = RenderTyped(book: "fanduel");
        Assert.Equal("-135", cut.Find(".qtake__price").TextContent.Trim());

        PricePickers.TypePrice(cut);
        Field(cut, "Price").Input("-140");
        Key(cut, TypedPriceFields.UseFetchedLabel).Click();

        Assert.True(PricePickers.Shown(cut));
        Assert.Empty(cut.FindAll("input[aria-label='Price']"));
        Assert.Equal("-128", cut.Find(".qtake__price").TextContent.Trim());
        Assert.Equal("DraftKings", cut.Find(".qtake__line .popup__label").TextContent.Trim());
        Assert.DoesNotContain("qtake--typed", cut.Find(".qtake").ClassList);
    }

    [Fact]
    public void Escape_from_a_typed_price_goes_back_to_the_fetched_one_before_closing()
    {
        var closed = 0;
        var cut = RenderComponent<QuickTake>(p =>
        {
            p.Add(x => x.Row, BoardRows.Row());
            p.Add(x => x.Market, Markets.Moneyline);
            p.Add(x => x.Outcome, BoardRows.Home);
            p.Add(x => x.OnClose, () => closed++);
        });

        PricePickers.TypePrice(cut);
        cut.Find(".qtake").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        Assert.True(PricePickers.Shown(cut));
        Assert.Equal(0, closed);
    }

    /// <summary>
    /// The same input, typed in the row and in the Place wager window, records the same entry —
    /// every field of it, tagged manual. One reading (TypedPrice) serves both, so they cannot drift.
    /// </summary>
    [Theory]
    [InlineData(Markets.Moneyline, BoardRows.Home, "104.35", null)]
    [InlineData(Markets.Moneyline, BoardRows.Home, "2.0435", null)]
    [InlineData(Markets.Spread, BoardRows.Away, "-110", "3.5")]
    [InlineData(Markets.Total, "Over", "-115", "48")]
    public async Task A_typed_take_logs_the_entry_the_full_form_logs_for_the_same_input(
        string market, string outcome, string price, string? line)
    {
        using var dollars = new UsDollars();

        // In the row.
        var take = RenderTyped(market, outcome);
        PricePickers.TypePrice(take);
        Field(take, "Price").Input(price);
        if (line is not null) Field(take, "Line").Input(line);
        Field(take, "Book").Input("Fanatics ");
        take.Find("input[aria-label='Stake']").Input("500");
        take.WaitForAssertion(() => Assert.False(Key(take, "Log").HasAttribute("disabled")), Settle);
        Key(take, "Log").Click();
        await take.InvokeAsync(() => Key(take, "Log wager").Click());

        // In the Place wager window, on the same side.
        var form = RenderComponent<XLines.Web.Components.Panels.BoardWager>(p => p
            .Add(x => x.GameId, BoardRows.GameId).Add(x => x.Market, market).Add(x => x.Outcome, outcome));
        if (!PricePickers.Shown(form))
            Change(form, "Bet");
        PricePickers.TypePrice(form);
        form.WaitForElement("input[aria-label='Price']", Settle);
        Field(form, "Price").Input(price);
        if (line is not null) Field(form, "Line").Input(line);
        Field(form, "Book").Input("Fanatics ");
        form.Find("input[aria-label='Stake']").Input("500");
        form.WaitForAssertion(() => Assert.False(form.Find("button.desk-btn--filled").HasAttribute("disabled")), Settle);
        form.Find("button.desk-btn--filled").Click();

        form.WaitForAssertion(() => Assert.Equal(2, Data.Straights.Count), Settle);
        var (inRow, inForm) = (Data.Straights[0], Data.Straights[1]);

        Assert.Equal(inForm, inRow);
        Assert.True(inRow.ManualPrice);
        Assert.Equal("fanatics", inRow.Book);
        Assert.Equal(line is null ? null : decimal.Parse(line, System.Globalization.CultureInfo.InvariantCulture), inRow.LineTaken);

        // The receipt says it as the window does.
        Assert.EndsWith(", your own price.", Squash(take.Find(".qtake__receipt-text").TextContent));
    }

    [Fact]
    public void A_typed_take_banks_on_the_slip_as_a_manual_price()
    {
        using var dollars = new UsDollars();
        var cut = RenderTyped();

        PricePickers.TypePrice(cut);
        Field(cut, "Price").Input("-120");
        Stake25(cut);
        Key(cut, "Add to slip").Click();

        var card = Assert.Single(Slip.Items);
        Assert.Equal(("draftkings", -120m, 25m, true), (card.Book, card.PriceAmerican, card.Stake, card.ManualPrice));
    }

    /// <summary>What a field says is wrong is said under it, in the Journal's words, and the key row waits on it.</summary>
    [Fact]
    public void A_typed_price_that_is_not_a_price_says_so_under_the_field_and_Log_waits()
    {
        var cut = RenderTyped();
        PricePickers.TypePrice(cut);
        Stake25(cut);

        Field(cut, "Price").Input("50");
        Field(cut, "Price").Blur();

        var error = Field(cut, "Price").ParentElement!.QuerySelector(".field-err")!;
        Assert.Equal(XLines.Web.Components.Panels.JournalPanel.PriceError(50), error.TextContent.Trim());
        Assert.Equal(error.Id, Field(cut, "Price").GetAttribute("aria-describedby"));
        Assert.Equal(TypedPrice.NeedsPrice, cut.Find(".keyrow__reason").TextContent.Trim());
        Assert.True(Key(cut, "Log").HasAttribute("disabled"));
        Assert.True(Key(cut, "Add to slip").HasAttribute("disabled"));
        Assert.Equal("—", cut.Find(".qtake__price").TextContent.Trim());

        // A book cleared says so too; a price fixed lets Log go once the book is back.
        Field(cut, "Price").Input("-118");
        Field(cut, "Book").Input("");
        Field(cut, "Book").Blur();
        Assert.Equal(TypedPrice.BookError, Field(cut, "Book").ParentElement!.QuerySelector(".field-err")!.TextContent.Trim());
        Assert.True(Key(cut, "Log").HasAttribute("disabled"));

        Field(cut, "Book").Input("caesars");
        Assert.False(Key(cut, "Log").HasAttribute("disabled"));
        Assert.Equal("-118", cut.Find(".qtake__price").TextContent.Trim());
    }

    /// <summary>"-" and "-1" on the way to "-115" are not errors: a field speaks once it is left.</summary>
    [Fact]
    public void No_error_is_drawn_while_a_price_is_being_typed()
    {
        var cut = RenderTyped();
        PricePickers.TypePrice(cut);

        foreach (var keystroke in new[] { "-", "-1", "-11" })
        {
            Field(cut, "Price").Input(keystroke);
            Assert.Empty(cut.FindAll(".field-err"));
            Assert.Empty(cut.FindAll("[role=alert]"));
        }

        Field(cut, "Price").Blur();
        Assert.Single(cut.FindAll(".field-err"));

        // Typed on to a price, the error goes, and the field it is under keeps its place.
        Field(cut, "Price").Input("-115");
        Assert.Empty(cut.FindAll(".field-err"));
        Assert.Equal("-115", cut.Find(".qtake__price").TextContent.Trim());
    }

    [Fact]
    public void Escape_keeps_a_price_that_has_been_typed()
    {
        var closed = 0;
        var cut = RenderComponent<QuickTake>(p =>
        {
            p.Add(x => x.Row, BoardRows.Row());
            p.Add(x => x.Market, Markets.Moneyline);
            p.Add(x => x.Outcome, BoardRows.Home);
            p.Add(x => x.OnClose, () => closed++);
        });

        PricePickers.TypePrice(cut);
        Field(cut, "Book").Input("Fanatics");
        cut.Find(".qtake").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        Assert.Equal("Fanatics", Field(cut, "Book").GetAttribute("value"));
        Assert.Equal(0, closed);
    }

    [Fact]
    public void Custom_price_chosen_again_brings_back_what_was_typed()
    {
        var cut = RenderTyped();

        PricePickers.TypePrice(cut);
        Field(cut, "Price").Input("-121");
        Key(cut, TypedPriceFields.UseFetchedLabel).Click();
        PricePickers.TypePrice(cut);

        Assert.Equal("-121", Field(cut, "Price").GetAttribute("value"));
    }

    /// <summary>
    /// The book reads by its name and is recorded by the key the full form has always recorded:
    /// the started book's own key while the field still names it, else what was typed, lower-cased.
    /// </summary>
    [Theory]
    [InlineData(null, "draftkings")]
    [InlineData("FanDuel", "fanduel")]
    [InlineData("Pinnacle", "pinnacle")]
    [InlineData(" Caesars ", "caesars")]
    public async Task The_book_is_named_on_screen_and_recorded_by_its_key(string? typed, string recorded)
    {
        using var dollars = new UsDollars();
        var take = RenderTyped();
        PricePickers.TypePrice(take);
        Assert.Equal("DraftKings", Field(take, "Book").GetAttribute("value"));

        if (typed is not null) Field(take, "Book").Input(typed);
        Stake25(take);
        Key(take, "Log").Click();
        Assert.Contains($"({XLines.Web.Components.Panels.BookNames.Display(recorded)})", take.Find(".confirm__ask").TextContent);
        await take.InvokeAsync(() => Key(take, "Log wager").Click());

        Assert.Equal(recorded, Assert.Single(Data.Straights).Book);
        Assert.Contains($"({XLines.Web.Components.Panels.BookNames.Display(recorded)})", take.Find(".qtake__receipt-text").TextContent);
    }

    /// <summary>A key whose name several keys share is recorded as itself while its name stands in the field.</summary>
    [Fact]
    public void A_book_started_from_a_key_that_shares_its_name_records_that_key()
    {
        var model = new TypedPrice();
        model.StartFrom(XLines.Web.Services.WagerOffer.Best(Markets.Moneyline,
            BoardRows.Offer(BoardRows.Home, "williamhill_us", -125)));

        Assert.Equal("Caesars", model.Book);
        Assert.Equal("williamhill_us", model.BookKey);

        model.SetBook("caesars");
        Assert.Equal("williamhill_us", model.BookKey);

        model.SetBook("BetMGM");
        Assert.Equal("betmgm", model.BookKey);
    }

    [Fact]
    public void A_spread_typed_without_its_line_cannot_be_logged_and_says_why()
    {
        var cut = RenderTyped(Markets.Spread, BoardRows.Away);
        PricePickers.TypePrice(cut);
        Stake25(cut);

        Field(cut, "Line").Input("");
        Field(cut, "Line").Blur();

        Assert.Equal("Enter the line.", Field(cut, "Line").ParentElement!.QuerySelector(".field-err")!.TextContent.Trim());
        Assert.Equal(TypedPrice.NeedsLine, cut.Find(".keyrow__reason").TextContent.Trim());
        Assert.True(Key(cut, "Log").HasAttribute("disabled"));
    }

    /// <summary>The same messages in the Place wager window: one set of fields, one set of words.</summary>
    [Fact]
    public void The_full_form_says_the_same_under_the_same_fields()
    {
        var form = RenderComponent<XLines.Web.Components.Panels.BoardWager>(p => p
            .Add(x => x.GameId, BoardRows.GameId).Add(x => x.Market, Markets.Moneyline).Add(x => x.Outcome, BoardRows.Home));
        if (!PricePickers.Shown(form))
            Change(form, "Bet");
        PricePickers.TypePrice(form);
        form.WaitForElement("input[aria-label='Price']", Settle);

        Field(form, "Price").Input("50");
        Field(form, "Price").Blur();

        Assert.Equal(XLines.Web.Components.Panels.JournalPanel.PriceError(50),
            Field(form, "Price").ParentElement!.QuerySelector(".field-err")!.TextContent.Trim());
        // The review waits, folded, on the price: the window says so on its step, not on a key row.
        Assert.Contains("The bet and its price come first.", Step(form, "Review and log").TextContent);
    }

    /// <summary>Full wager form, pressed with a price typed: the form opens typed as it is, not on the fetched number.</summary>
    [Fact]
    public void The_full_form_from_a_typed_take_carries_the_typed_price()
    {
        using var dollars = new UsDollars();
        TypedTake? typed = null;
        var cut = RenderTyped(Markets.Spread, BoardRows.Away, onOpenTyped: t => typed = t);

        PricePickers.TypePrice(cut);
        Field(cut, "Price").Input("-112");
        Field(cut, "Line").Input("3");
        Stake25(cut);
        cut.Find(".snippet__open").Click();

        Assert.Equal(new TypedTake("fanduel", 25m, -112m, 3m), typed);
    }

    /// <summary>
    /// A price typed only in part goes to the full form as typed — the form's fields say there what
    /// is still wrong — rather than the form opening on the fetched number with the typing dropped.
    /// </summary>
    [Fact]
    public void The_full_form_from_a_partly_typed_take_carries_what_was_typed()
    {
        using var dollars = new UsDollars();
        TypedTake? typed = null;
        (string, decimal)? plain = null;
        var cut = RenderTyped(Markets.Spread, BoardRows.Away, onOpenForm: f => plain = f, onOpenTyped: t => typed = t);

        PricePickers.TypePrice(cut);
        Field(cut, "Price").Input("50");
        Field(cut, "Line").Input("3");
        Stake25(cut);
        cut.Find(".snippet__open").Click();

        Assert.Null(plain);
        Assert.Equal(new TypedTake("fanduel", 25m, 50m, 3m), typed);

        // The form opens on it, the number in its field and its message under it once left.
        var form = RenderComponent<XLines.Web.Components.Panels.BoardWager>(
            TypedTake.Door(BoardRows.GameId, Markets.Spread, BoardRows.Away, typed!.Book, typed.Stake, typed)
                .Select(d => ComponentParameter.CreateParameter(d.Key, d.Value)).ToArray());
        form.WaitForAssertion(() => Assert.NotEmpty(form.FindAll(".wager-step, input")), Settle);
        if (form.FindAll("input[aria-label='Price']").Count == 0)
            Change(form, "Bet");
        form.WaitForElement("input[aria-label='Price']", Settle);
        Assert.Equal("50", Field(form, "Price").GetAttribute("value"));
        Assert.Equal("3", Field(form, "Line").GetAttribute("value"));
    }

    /// <summary>A refresh that drops the side leaves a typed price as it was: it needs nothing fetched.</summary>
    [Fact]
    public void A_typed_price_stays_usable_when_the_side_stops_being_quoted()
    {
        using var dollars = new UsDollars();
        var cut = RenderTyped();
        PricePickers.TypePrice(cut);
        Field(cut, "Price").Input("-121");
        Field(cut, "Book").Input("Fanatics");
        Stake25(cut);

        var row = BoardRows.Row();
        cut.SetParametersAndRender(p => p.Add(x => x.Row, row with { Moneyline = row.Moneyline with { First = null } }));

        Assert.Empty(cut.FindAll(".qtake__gone"));
        Assert.Equal("-121", Field(cut, "Price").GetAttribute("value"));
        Assert.Equal("-121", cut.Find(".qtake__price").TextContent.Trim());
        // Nothing fetched to go back to.
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == TypedPriceFields.UseFetchedLabel);

        Key(cut, "Add to slip").Click();
        Assert.Equal(("fanatics", -121m, true), (Slip.Items[0].Book, Slip.Items[0].PriceAmerican, Slip.Items[0].ManualPrice));
    }

    /// <summary>The side gone with nothing typed is said as before.</summary>
    [Fact]
    public void A_side_no_longer_quoted_says_so_when_nothing_is_typed()
    {
        var cut = Render();
        var row = BoardRows.Row();

        cut.SetParametersAndRender(p => p.Add(x => x.Row, row with { Moneyline = row.Moneyline with { First = null } }));

        Assert.Contains("no longer quoted", cut.Find(".qtake__gone").TextContent);
    }

    /// <summary>
    /// A typed number past any price a book prints is refused as an unreadable one is — under the
    /// field, and Log waiting — instead of passing as a price whose return overflows mid-render.
    /// </summary>
    [Fact]
    public void A_typed_price_past_any_book_is_refused_and_draws_without_overflowing()
    {
        var cut = RenderTyped();
        PricePickers.TypePrice(cut);
        Stake25(cut);

        Field(cut, "Price").Input("1000000000000000000000000000");
        Field(cut, "Price").Blur();

        Assert.Equal(TypedPrice.TooLarge, Field(cut, "Price").ParentElement!.QuerySelector(".field-err")!.TextContent.Trim());
        Assert.Equal(TypedPrice.NeedsPrice, cut.Find(".keyrow__reason").TextContent.Trim());
        Assert.True(Key(cut, "Log").HasAttribute("disabled"));
        Assert.Equal("—", cut.Find(".qtake__price").TextContent.Trim());
    }

    [Theory]
    [InlineData("100000", true)]
    [InlineData("-100000", true)]
    [InlineData("100000.01", false)]
    [InlineData("-100001", false)]
    [InlineData("1000000000000000000000000000", false)]
    [InlineData("-1000000000000000000000000000", false)]
    [InlineData("100", true)]
    [InlineData("-100", true)]
    [InlineData("99", false)]
    public void A_typed_american_price_is_read_up_to_its_ceiling(string typed, bool reads)
    {
        var model = Typed(typed);

        Assert.Equal(reads, model.American is not null);
        Assert.Equal(reads, model.Offer(Markets.Moneyline, BoardRows.Home, false, null, DateTimeOffset.UtcNow) is not null);
        model.TouchPrice();
        Assert.Equal(reads, model.PriceError is null);
        if (!reads && Math.Abs(decimal.Parse(typed, System.Globalization.CultureInfo.InvariantCulture)) > TypedPrice.Largest)
            Assert.Equal(TypedPrice.TooLarge, model.PriceError);
    }

    /// <summary>Decimal odds are read as the American price they stand for, the longest of them well inside the ceiling.</summary>
    [Theory]
    [InlineData("2.0435", "104.35")]
    [InlineData("1.5", "-200")]
    [InlineData("99.99", "9899")]
    [InlineData("1.01", "-10000")]
    public void A_typed_decimal_price_is_read_as_its_american_price(string typed, string american)
    {
        var model = Typed(typed);

        Assert.Equal(decimal.Parse(american, System.Globalization.CultureInfo.InvariantCulture), model.American);
        Assert.NotNull(model.Offer(Markets.Moneyline, BoardRows.Home, false, null, DateTimeOffset.UtcNow));
        model.TouchPrice();
        Assert.Null(model.PriceError);
    }

    private static TypedPrice Typed(string price)
    {
        var model = new TypedPrice();
        model.StartFrom(null, "fanatics");
        model.SetPrice(decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture));
        return model;
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
