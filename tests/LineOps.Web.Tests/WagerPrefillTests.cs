using Bunit;
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Desk.Windowing;
using LineOps.Web.Components.Panels;
using LineOps.Web.Services;
using LineOps.Web.Tests.Support;
using LineOps.Web.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// Duplicating a bet: the Journal and the wager window both open a new wager window on the same
/// bet through one door, carrying the game, the side, the book, the stake and — for a typed
/// price — the price, so the copy starts where the original ended.
/// </summary>
public class WagerPrefillTests : WagerTestContext
{
    private WindowManager Manager => Services.GetRequiredService<WindowManager>();

    [Fact]
    public void A_journal_entry_at_a_fetched_price_carries_the_side_book_and_stake_but_not_the_price()
    {
        var entry = new JournalEntry
        {
            GameId = BoardRows.GameId, Market = Markets.Spread, Outcome = BoardRows.Home, LineTaken = -2.5m,
            Book = "fanduel", PriceTaken = -112, Stake = 40m, Note = "fade", ManualPrice = false
        };

        var parameters = WagerPrefill.From(entry).ToParameters();

        Assert.Equal(BoardRows.GameId, parameters["GameId"]);
        Assert.Equal(Markets.Spread, parameters["Market"]);
        Assert.Equal(BoardRows.Home, parameters["Outcome"]);
        Assert.Equal("fanduel", parameters["Book"]);
        Assert.Equal(40m, parameters["Stake"]);
        Assert.Equal("fade", parameters["Note"]);
        // Today's price at that book, not the one taken last week.
        Assert.False(parameters.ContainsKey("PriceAmerican"));
    }

    [Fact]
    public void A_typed_or_free_text_entry_carries_its_price_and_description()
    {
        var entry = new JournalEntry
        {
            GameId = null, Market = "other", FreeTextMarket = "Steelers to win the AFC North",
            Book = "caesars", PriceTaken = 250, Stake = 25m, ManualPrice = true
        };

        var parameters = WagerPrefill.From(entry).ToParameters();

        Assert.False(parameters.ContainsKey("GameId"));
        Assert.Equal(250, parameters["PriceAmerican"]);
        Assert.Equal("Steelers to win the AFC North", parameters["Described"]);
        Assert.Equal("caesars", parameters["Book"]);
        Assert.Equal(25m, parameters["Stake"]);
    }

    [Fact]
    public void The_door_opens_a_new_wager_window_on_the_bet()
    {
        var entry = new JournalEntry
        {
            GameId = BoardRows.GameId, Market = Markets.Moneyline, Outcome = BoardRows.Home,
            Book = "fanduel", PriceTaken = -135, Stake = 40m, Game = BoardRows.Game()
        };

        Manager.OpenWagerCopy(WagerPrefill.From(entry), "Buffalo Bills at Kansas City Chiefs");

        var window = Assert.Single(Manager.Windows);
        Assert.Equal(WindowCatalog.Wager, window.Definition.Key);
        Assert.Equal("Buffalo Bills at Kansas City Chiefs", window.Title);
        Assert.Equal(40m, window.Parameters["Stake"]);
    }

    [Fact]
    public void Opened_with_a_stake_the_window_is_ready_to_log_at_that_book()
    {
        var cut = RenderComponent<BoardWager>(p => p
            .Add(x => x.GameId, BoardRows.GameId)
            .Add(x => x.Market, Markets.Moneyline)
            .Add(x => x.Outcome, BoardRows.Home)
            .Add(x => x.Book, "fanduel")
            .Add(x => x.Stake, 40m)
            .Add(x => x.Note, "fade"));

        Assert.Equal("fanduel", cut.Find(".wager-offer__book").TextContent.Trim());
        Assert.Contains($"You are logging {40m:C0}", cut.Find(".wreview").TextContent);
        Assert.False(cut.Find("button.desk-btn--filled").HasAttribute("disabled"));
        Assert.Equal("fade", cut.Find("input[placeholder='Note (optional)']").GetAttribute("value"));
    }

    [Fact]
    public void Opened_with_a_price_the_window_starts_on_that_typed_price()
    {
        var cut = RenderComponent<BoardWager>(p => p
            .Add(x => x.GameId, BoardRows.GameId)
            .Add(x => x.Market, Markets.Moneyline)
            .Add(x => x.Outcome, BoardRows.Home)
            .Add(x => x.Book, "caesars")
            .Add(x => x.PriceAmerican, -120)
            .Add(x => x.Stake, 40m));

        // Ready to log, so the price step is folded to what it carries; opened, the fields hold it.
        Assert.Contains("-120 at caesars, your own price", Step(cut, "Price and book").QuerySelector(".wstep__summary")!.TextContent);
        Assert.Contains("your own price", cut.Find(".wreview").TextContent);

        Change(cut, "Price and book");
        Assert.Equal("-120", cut.Find("input[placeholder='-110']").GetAttribute("value"));
        Assert.Equal("caesars", cut.Find("input[placeholder='draftkings']").GetAttribute("value"));
    }

    [Fact]
    public void Opened_on_a_described_bet_the_window_is_the_no_game_form_filled_in()
    {
        var cut = RenderComponent<BoardWager>(p => p
            .Add(x => x.Described, "Steelers to win the AFC North")
            .Add(x => x.Book, "caesars")
            .Add(x => x.PriceAmerican, 250)
            .Add(x => x.Stake, 25m));

        Assert.Equal("Steelers to win the AFC North", cut.Find("input[aria-label='The bet']").GetAttribute("value"));
        Assert.Equal("250", cut.Find("input[placeholder='-110']").GetAttribute("value"));
        Assert.Contains($"You are logging {25m:C0}", cut.Find(".wreview").TextContent);
    }

    [Fact]
    public void Duplicate_in_the_wager_window_opens_a_second_window_on_the_same_bet()
    {
        var cut = RenderComponent<BoardWager>(p => p
            .Add(x => x.GameId, BoardRows.GameId)
            .Add(x => x.Market, Markets.Moneyline)
            .Add(x => x.Outcome, BoardRows.Home));

        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(50m.ToString("C0"), StringComparison.Ordinal)).Click();
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Duplicate", StringComparison.Ordinal)).Click();

        var window = Assert.Single(Manager.Windows);
        Assert.Equal(WindowCatalog.Wager, window.Definition.Key);
        Assert.Equal(BoardRows.GameId, window.Parameters["GameId"]);
        Assert.Equal(BoardRows.Home, window.Parameters["Outcome"]);
        Assert.Equal("draftkings", window.Parameters["Book"]);
        Assert.Equal(50m, window.Parameters["Stake"]);
    }
}
