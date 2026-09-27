using Bunit;
using LineOps.Desk.Windowing;
using LineOps.Web.Components.Panels;
using LineOps.Web.Tests.Support;
using LineOps.Web.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>Door 3: a book's live price in Every book opens the wager window on that book.</summary>
public class BoardAllBetsTests : WagerTestContext
{
    [Fact]
    public void A_live_rung_opens_the_wager_window_on_its_book()
    {
        var cut = RenderComponent<BoardAllBets>(p => p.Add(x => x.GameId, BoardRows.GameId));

        cut.FindAll("button.take").First(b => b.TextContent.Contains("-135")).Click();

        var manager = Services.GetRequiredService<WindowManager>();
        var window = Assert.Single(manager.Windows, w => w.Definition.Key == WindowCatalog.Wager);
        Assert.Equal("fanduel", window.Parameters["Book"]);
        Assert.Equal(BoardRows.Home, window.Parameters["Outcome"]);
        Assert.Equal("h2h", window.Parameters["Market"]);
    }

    [Fact]
    public void A_closing_rung_is_not_a_door()
    {
        Data.Row = BoardRows.Row(closing: true);
        var cut = RenderComponent<BoardAllBets>(p => p.Add(x => x.GameId, BoardRows.GameId));

        Assert.Empty(cut.FindAll("button.take"));
    }
}
