using AngleSharp.Dom;
using Bunit;
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Web.Components.Panels;
using LineOps.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// The wager window as the one form a bet is recorded with: the best price is the default, the
/// book is the operator's to change, a price of their own can be typed, and the Journal's door
/// opens it on a game search.
/// </summary>
public class WagerWindowBooksTests : WagerTestContext
{
    private IRenderedComponent<BoardWager> Render(string? market = null, string? outcome = null, bool withGame = true)
        => RenderComponent<BoardWager>(p =>
        {
            if (withGame) p.Add(x => x.GameId, BoardRows.GameId);
            if (market is not null) p.Add(x => x.Market, market);
            if (outcome is not null) p.Add(x => x.Outcome, outcome);
        });

    private static IElement Key(IRenderedFragment cut, string startsWith)
        => cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(startsWith, StringComparison.Ordinal));

    private static IElement Log(IRenderedFragment cut) => cut.Find("button.desk-btn--filled");

    private static string Headline(IRenderedFragment cut) => cut.Find(".wager-offer__price").TextContent.Trim();

    /// <summary>
    /// The books are a menu on the price; their rows exist only while it is open, and the price
    /// step itself is folded once it is done, so this opens both.
    /// </summary>
    private static void OpenBooks(IRenderedFragment cut)
    {
        if (cut.FindAll(".book-picker__trigger").Count == 0)
            Change(cut, "Your bet");
        cut.Find(".book-picker__trigger").Click();
    }

    private static void OpenPrice(IRenderedFragment cut)
    {
        if (cut.FindAll(".wager-manual-key").Count == 0)
            Change(cut, "Your bet");
    }

    // ---- Books ----------------------------------------------------------------------------

    [Fact]
    public void Every_book_quoting_the_side_is_listed_best_first_and_marked()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);
        OpenBooks(cut);

        var rows = cut.FindAll(".book-row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("draftkings", rows[0].TextContent);
        Assert.Contains("Best", rows[0].TextContent);
        Assert.Contains("fanduel", rows[1].TextContent);
        Assert.Equal("true", rows[0].GetAttribute("aria-selected"));
        Assert.Contains("2 quoting", cut.Find(".book-picker__aside").TextContent);
    }

    [Fact]
    public void Choosing_a_book_reprices_the_window_and_logs_at_that_book()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);
        OpenBooks(cut);

        cut.FindAll(".book-row").First(r => r.TextContent.Contains("fanduel")).Click();

        Assert.Equal("-135", Headline(cut));
        Assert.Equal("fanduel", cut.Find(".wager-offer__book").TextContent.Trim());
        Assert.Contains("Price captured 40 min ago", cut.Markup);

        Key(cut, 50m.ToString("C0")).Click();
        Log(cut).Click();

        var bet = Assert.Single(Data.Straights);
        Assert.Equal("fanduel", bet.Book);
        Assert.Equal(-135, bet.PriceTaken);
        Assert.False(bet.ManualPrice);
    }

    [Fact]
    public void Changing_the_book_clears_the_stake_and_the_note()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);
        Key(cut, 50m.ToString("C0")).Click();
        cut.Find("input[aria-label='Note']").Input("fade the public");
        Assert.False(Log(cut).HasAttribute("disabled"));

        OpenBooks(cut);
        cut.FindAll(".book-row").First(r => r.TextContent.Contains("fanduel")).Click();

        // A bet at another book is another bet: the amount and the note were the old one's.
        Assert.True(Log(cut).HasAttribute("disabled"));
        Assert.Equal("Needs a stake above zero.", cut.Find(".wager-why").TextContent.Trim());
        Assert.Empty(cut.Find("input[aria-label='Note']").GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public void Choosing_the_book_already_chosen_keeps_the_stake()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);
        Key(cut, 50m.ToString("C0")).Click();

        OpenBooks(cut);
        cut.FindAll(".book-row").First(r => r.TextContent.Contains("draftkings")).Click();

        Assert.False(Log(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void The_chosen_book_is_kept_when_the_side_changes()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);
        OpenBooks(cut);
        cut.FindAll(".book-row").First(r => r.TextContent.Contains("fanduel")).Click();

        Change(cut, "Your bet");
        cut.FindAll(".selection-chip").First(b => b.GetAttribute("aria-label") == "ML · Bills").Click();

        Assert.Equal("fanduel", cut.Find(".wager-offer__book").TextContent.Trim());
        Assert.Equal("+110", Headline(cut));
    }

    [Fact]
    public void A_side_the_chosen_book_does_not_quote_falls_back_to_the_best_price()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);
        OpenBooks(cut);
        cut.FindAll(".book-row").First(r => r.TextContent.Contains("fanduel")).Click();

        Change(cut, "Your bet");
        cut.FindAll(".selection-chip").First(b => b.GetAttribute("aria-label") == "Spread · Chiefs -2.5").Click();

        Assert.Equal("draftkings", cut.Find(".wager-offer__book").TextContent.Trim());
        Assert.Equal("-110", Headline(cut));
    }

    // ---- A price of your own --------------------------------------------------------------

    [Fact]
    public void Enter_my_own_price_starts_from_the_number_on_screen_and_logs_it_as_manual()
    {
        var cut = Render(Markets.Spread, BoardRows.Home);
        OpenPrice(cut);

        cut.Find(".wager-manual-key").Click();

        var price = cut.Find("input[placeholder='-110']");
        Assert.Equal("-110", price.GetAttribute("value"));
        Assert.Equal("-2.5", cut.Find("input[placeholder='-3']").GetAttribute("value"));

        price.Input("-105");
        cut.Find("input[placeholder='-3']").Input("-3");
        Key(cut, 50m.ToString("C0")).Click();
        Log(cut).Click();

        var bet = Assert.Single(Data.Straights);
        Assert.Equal(-105, bet.PriceTaken);
        Assert.Equal(-3m, bet.LineTaken);
        Assert.Equal("draftkings", bet.Book);
        Assert.True(bet.ManualPrice);
        Assert.Contains("your own price", cut.Find(".wager-logged").TextContent);
    }

    [Fact]
    public void A_typed_price_is_read_by_the_checks_and_not_overwritten_by_a_refresh()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);
        OpenPrice(cut);
        cut.Find(".wager-manual-key").Click();
        cut.Find("input[placeholder='-110']").Input("-120");

        Assert.Contains("Your own price", cut.Markup);
        Assert.DoesNotContain("Price captured", cut.Markup);

        Data.Row = BoardRows.Row(homeMl: -140);
        cut.InvokeAsync(() => Services.GetRequiredService<LineOps.Desk.Windowing.DeskSignals>()
            .Publish(new HashSet<string> { LineOps.Data.Changes.DataTopics.Odds }));

        cut.WaitForAssertion(() => Assert.True(Data.RowReads >= 2), TimeSpan.FromSeconds(5));
        Assert.Equal("-120", cut.Find("input[placeholder='-110']").GetAttribute("value"));
        Assert.Empty(cut.FindAll(".wager-moved"));
    }

    [Fact]
    public void A_typed_price_lifts_the_closing_line_block()
    {
        Data.Row = BoardRows.Row(closing: true, startsAt: DateTimeOffset.UtcNow.AddMinutes(-42));
        var cut = Render(Markets.Moneyline, BoardRows.Home);
        Key(cut, 50m.ToString("C0")).Click();
        Assert.True(Log(cut).HasAttribute("disabled"));

        OpenPrice(cut);
        cut.Find(".wager-manual-key").Click();

        Assert.False(Log(cut).HasAttribute("disabled"));
        Assert.Contains("the game started 42 min ago, so this records the price you took", cut.Markup);
    }

    [Fact]
    public void A_side_no_book_prices_can_be_chosen_and_opens_on_a_typed_price()
    {
        var row = BoardRows.Row();
        Data.Row = row with { Spread = new MarketPair(Markets.Spread, null, null) };
        var cut = Render();
        Change(cut, "Your bet");

        var chip = cut.FindAll(".selection-chip").First(b => b.GetAttribute("aria-label") == "Spread · Chiefs");
        chip.Click();

        Assert.Contains("No book the desk scans prices this side", cut.Markup);
        Assert.Empty(cut.FindAll(".wager-manual-key"));
        Assert.NotNull(cut.Find("input[placeholder='-110']"));
    }

    // ---- The Journal's door ---------------------------------------------------------------

    private static Game Saints(int id, DateTimeOffset startsAt) => new()
    {
        Id = id,
        Sport = new Sport { Key = "nfl", Name = "NFL" },
        HomeTeam = new Team { Name = "New Orleans Saints", Abbrev = "NO" },
        AwayTeam = new Team { Name = "Pittsburgh Steelers", Abbrev = "PIT" },
        StartsAt = startsAt,
        Status = GameStatus.Scheduled
    };

    [Fact]
    public void Opened_without_a_game_it_is_a_search_with_one_list_beneath_it()
    {
        Data.Games = [Saints(BoardRows.GameId, DateTimeOffset.UtcNow.AddDays(1)), Saints(99, DateTimeOffset.UtcNow.AddDays(-6))];
        var cut = Render(withGame: false);

        Assert.Empty(cut.FindAll("select"));
        Assert.Contains("Find the game the bet was on", cut.Markup);

        cut.Find("input[aria-label='Find a game']").Input("steelers");

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".game-search__opt").Count));
        var options = cut.FindAll(".game-search__opt");
        Assert.Contains("Pittsburgh Steelers at New Orleans Saints", options[0].TextContent);
        Assert.Contains("No game", options[2].TextContent);
        Assert.Equal(new[] { "steelers" }, Data.Searches);
    }

    [Fact]
    public void Picking_a_game_shows_its_sides_and_assumes_none()
    {
        Data.Games = [BoardRows.Game()];
        var cut = Render(withGame: false);

        cut.Find("input[aria-label='Find a game']").Input("chiefs");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".game-search__opt")));
        cut.FindAll(".game-search__opt")[0].Click();

        cut.WaitForAssertion(() => Assert.Contains("Buffalo Bills at Kansas City Chiefs", cut.Find(".whead .game-search__chosen").TextContent));
        Assert.Equal(6, cut.FindAll(".selection-chip").Count);
        Assert.Empty(cut.FindAll(".selection-chip--on"));
        Assert.Contains("Pick the side you bet", cut.Markup);

        cut.FindAll(".selection-chip").First(b => b.GetAttribute("aria-label") == "ML · Chiefs").Click();
        Assert.Equal("-128", Headline(cut));
    }

    [Fact]
    public void Arrow_keys_and_enter_pick_from_the_list()
    {
        Data.Games = [Saints(98, DateTimeOffset.UtcNow.AddDays(1)), BoardRows.Game()];
        var cut = Render(withGame: false);

        var field = cut.Find("input[aria-label='Find a game']");
        field.Input("a");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".game-search__opt")));

        cut.Find("input[aria-label='Find a game']").KeyDown("ArrowDown");
        cut.Find("input[aria-label='Find a game']").KeyDown("Enter");

        cut.WaitForAssertion(() => Assert.Contains("Buffalo Bills at Kansas City Chiefs", cut.Find(".game-search__chosen").TextContent));
    }

    [Fact]
    public void No_game_records_a_described_bet_at_a_typed_price()
    {
        var cut = Render(withGame: false);

        cut.Find("input[aria-label='Find a game']").Input("afc north");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".game-search__opt--none")));
        cut.Find(".game-search__opt--none").Click();
        cut.WaitForElement("input[aria-label='The bet']");

        cut.Find("input[aria-label='The bet']").Input("Steelers to win the AFC North");
        cut.Find("input[placeholder='-110']").Input("250");
        cut.Find("input[placeholder='draftkings']").Input("Caesars");
        Key(cut, 25m.ToString("C0")).Click();

        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Add to slip"));
        Log(cut).Click();

        var bet = Assert.Single(Data.Straights);
        Assert.Null(bet.GameId);
        Assert.Equal("Steelers to win the AFC North", bet.FreeTextMarket);
        Assert.Equal(250, bet.PriceTaken);
        Assert.Equal("caesars", bet.Book);
        Assert.True(bet.ManualPrice);
    }
}
