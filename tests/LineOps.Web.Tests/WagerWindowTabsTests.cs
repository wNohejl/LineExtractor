using AngleSharp.Dom;
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

    private static IElement SlipTab(IRenderedFragment cut)
        => cut.FindAll("[role=tab]").Single(t => t.TextContent.Trim().StartsWith("Slip", StringComparison.Ordinal));

    private static void Bank(IRenderedFragment cut)
    {
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(50m.ToString("C0"), StringComparison.Ordinal)).Click();
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Add to slip", StringComparison.Ordinal)).Click();
    }

    [Fact]
    public void The_window_has_a_wager_tab_and_a_slip_tab_that_counts()
    {
        var cut = Render();

        // The count is a badge beside the name, not part of it: at zero there is none.
        var tabs = cut.FindAll("[role=tab]").Select(t => t.TextContent.Trim()).ToList();
        Assert.Equal(["Wager", "Slip"], tabs);
        Assert.Null(SlipTab(cut).QuerySelector(".badge"));

        Bank(cut);

        Assert.Equal("1", SlipTab(cut).QuerySelector(".badge")!.TextContent.Trim());
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

        // The green toast's key turns the window to its Slip tab.
        Assert.Equal("View slip", LastToast.ActionLabel);
        PressToastKey();

        cut.WaitForAssertion(() => Assert.Equal("true", cut.FindAll("[role=tab]")[1].GetAttribute("aria-selected")));
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

        var link = Step(cut, "Bet").QuerySelector(".wstep__change")!;
        Assert.Contains("desklink", link.ClassName);
        Assert.StartsWith("ML · Chiefs · -128 at draftkings", System.Text.RegularExpressions.Regex.Replace(link.QuerySelector(".desklink__label")!.TextContent, @"\s+", " ").Trim());
        Assert.Contains("change", link.QuerySelector(".desklink__shortcut")!.TextContent);

        link.Click();
        Assert.Contains("wstep--open", Step(cut, "Bet").ClassName);
    }

    [Fact]
    public void Add_to_slip_waits_out_of_sight_at_zero_stake_and_Log_says_why()
    {
        var cut = Render();

        // A key that cannot be used yet is not on screen; the Filled key's reason says what to do.
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim().StartsWith("Add to slip", StringComparison.Ordinal));
        Assert.Equal("Needs a stake above zero.", cut.Find(".keyrow__reason").TextContent.Trim());
        Assert.Empty(Slip.Items);
    }
}
