using Bunit;
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Web.Components.Panels;
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// The slip is a tab of the wager window: what was banked is one click from where it was
/// banked, without a third window. The header's game and its date are links, like every
/// other name on the desk, and a folded step's choice is the link that reopens it.
/// </summary>
public class WagerWindowTabsTests : WagerTestContext
{
    private IRenderedComponent<BoardWager> Render()
        => RenderComponent<BoardWager>(p => p
            .Add(x => x.GameId, BoardRows.GameId)
            .Add(x => x.Market, Markets.Moneyline)
            .Add(x => x.Outcome, BoardRows.Home));

    private static void Bank(IRenderedFragment cut)
    {
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(50m.ToString("C0"), StringComparison.Ordinal)).Click();
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Add to slip", StringComparison.Ordinal)).Click();
    }

    [Fact]
    public void The_window_has_a_wager_tab_and_a_slip_tab_that_counts()
    {
        var cut = Render();

        var tabs = cut.FindAll("[role=tab]").Select(t => t.TextContent.Trim()).ToList();
        Assert.Equal(["Wager", "Slip · 0"], tabs);

        Bank(cut);

        Assert.Equal("Slip · 1", cut.FindAll("[role=tab]")[1].TextContent.Trim());
    }

    [Fact]
    public void The_slip_tab_shows_the_slip_in_place()
    {
        var cut = Render();
        Bank(cut);

        cut.FindAll("[role=tab]")[1].Click();

        Assert.NotNull(cut.Find(".slip-card"));
        Assert.Empty(cut.FindAll(".wsteps"));
    }

    [Fact]
    public void View_slip_after_banking_is_a_link_to_the_slip_tab()
    {
        var cut = Render();
        Bank(cut);

        cut.Find(".wager-logged .desklink").Click();

        Assert.Equal("true", cut.FindAll("[role=tab]")[1].GetAttribute("aria-selected"));
    }

    [Fact]
    public void The_header_names_the_teams_and_the_date_as_links()
    {
        var cut = Render();

        var links = cut.FindAll(".whead .desklink");
        Assert.Equal(3, links.Count);
        Assert.Equal(BoardRows.Away, links[0].QuerySelector(".desklink__label")!.TextContent.Trim());
        Assert.Equal(BoardRows.Home, links[1].QuerySelector(".desklink__label")!.TextContent.Trim());
        Assert.Contains("game", links[2].QuerySelector(".desklink__shortcut")!.TextContent);
    }

    [Fact]
    public void A_folded_steps_choice_is_the_link_that_reopens_it()
    {
        var cut = Render();

        var link = Step(cut, "Your bet").QuerySelector(".wstep__change")!;
        Assert.Contains("desklink", link.ClassName);
        Assert.Equal("ML · Chiefs", link.QuerySelector(".desklink__label")!.TextContent.Trim());
        Assert.Contains("change", link.QuerySelector(".desklink__shortcut")!.TextContent);

        link.Click();
        Assert.Contains("wstep--open", Step(cut, "Your bet").ClassName);
    }

    [Fact]
    public void Add_to_slip_is_disabled_at_zero_stake_and_says_why()
    {
        var cut = Render();

        var add = cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Add to slip", StringComparison.Ordinal));
        Assert.True(add.HasAttribute("disabled"));
        Assert.Equal("Needs a stake above zero.", add.GetAttribute("title"));
        Assert.Empty(Slip.Items);
    }
}
