using Bunit;
using XLines.Desk.Windowing;
using XLines.Web.Components.Panels;
using XLines.Web.Tests.Support;
using XLines.Web.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace XLines.Web.Tests;

/// <summary>
/// Door 3: a book's live price in Every book opens its take under the market, on that book — not
/// the Place wager window, which is the take's Full wager form.
/// </summary>
public class BoardAllBetsTests : WagerTestContext
{
    [Fact]
    public void A_live_rung_opens_its_take_in_place_and_no_window()
    {
        var cut = RenderComponent<BoardAllBets>(p => p.Add(x => x.GameId, BoardRows.GameId));
        var manager = Services.GetRequiredService<WindowManager>();

        cut.FindAll("button.take").First(b => b.TextContent.Contains("-135")).Click();

        Assert.Empty(manager.Windows);
        Assert.Equal("-135", cut.Find(".allbets__take .qtake__price").TextContent.Trim());
        Assert.Equal("FanDuel", cut.Find(".allbets__take .popup__label").TextContent.Trim());

        // The full form is the take's escape, on the same side and book.
        cut.Find(".allbets__take .snippet__open").Click();

        var window = Assert.Single(manager.Windows, w => w.Definition.Key == WindowCatalog.Wager);
        Assert.Equal("fanduel", window.Parameters["Book"]);
        Assert.Equal(BoardRows.Home, window.Parameters["Outcome"]);
        Assert.Equal("h2h", window.Parameters["Market"]);
        Assert.Empty(cut.FindAll(".allbets__take"));
    }

    [Fact]
    public void The_same_rung_pressed_again_puts_its_take_away()
    {
        var cut = RenderComponent<BoardAllBets>(p => p.Add(x => x.GameId, BoardRows.GameId));

        cut.FindAll("button.take").First(b => b.TextContent.Contains("-135")).Click();
        cut.FindAll("button.take").First(b => b.TextContent.Contains("-135")).Click();

        Assert.Empty(cut.FindAll(".allbets__take"));
    }

    /// <summary>
    /// A book is a row's label, so it goes by its name. The feed's key ("draftkings") sat in a
    /// dimmed monospace that read as an identifier rather than a book.
    /// </summary>
    [Fact]
    public void Each_book_is_named_not_keyed()
    {
        var cut = RenderComponent<BoardAllBets>(p => p.Add(x => x.GameId, BoardRows.GameId));

        var text = cut.Markup;
        Assert.Contains("DraftKings", text);
        Assert.Contains("FanDuel", text);
        Assert.DoesNotContain(">draftkings<", text);
        Assert.DoesNotContain("margin-left:6px", text);
    }

    /// <summary>
    /// The window re-aimed at another game puts the take away: left open it drew against the new
    /// game's row with the old game's side. A redraw on the same game leaves it open.
    /// </summary>
    [Fact]
    public void Another_game_puts_the_take_away_and_the_same_game_keeps_it()
    {
        var cut = RenderComponent<BoardAllBets>(p => p.Add(x => x.GameId, BoardRows.GameId));
        cut.FindAll("button.take").First(b => b.TextContent.Contains("-135")).Click();

        cut.SetParametersAndRender(p => p.Add(x => x.GameId, BoardRows.GameId));
        Assert.NotEmpty(cut.FindAll(".allbets__take"));

        cut.SetParametersAndRender(p => p.Add(x => x.GameId, BoardRows.GameId + 1));
        Assert.Empty(cut.FindAll(".allbets__take"));
    }

    [Fact]
    public void A_closing_rung_is_not_a_door()
    {
        Data.Row = BoardRows.Row(closing: true);
        var cut = RenderComponent<BoardAllBets>(p => p.Add(x => x.GameId, BoardRows.GameId));

        Assert.Empty(cut.FindAll("button.take"));
    }
}
