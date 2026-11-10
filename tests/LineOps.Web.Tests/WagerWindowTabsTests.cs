using AngleSharp.Dom;
using Bunit;
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Web.Components.Panels;
using LineOps.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

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

        // An empty slip keeps its tab, disabled: in the bar, in its place, and not a key until
        // something is banked. The count is a badge beside the name, not part of it: at zero
        // there is none, and a click on the disabled tab turns nothing.
        var tabs = cut.FindAll("[role=tab]").Select(t => t.TextContent.Trim()).ToList();
        Assert.Equal(["Wager", "Slip"], tabs);
        Assert.True(SlipTab(cut).HasAttribute("disabled"));
        Assert.Equal("true", SlipTab(cut).GetAttribute("aria-disabled"));
        Assert.Null(SlipTab(cut).QuerySelector(".badge"));

        SlipTab(cut).Click();
        Assert.Equal("true", cut.FindAll("[role=tab]")[0].GetAttribute("aria-selected"));
        Assert.NotEmpty(cut.FindAll(".wsteps"));

        Bank(cut);

        Assert.False(SlipTab(cut).HasAttribute("disabled"));
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

    /// <summary>
    /// A look at the slip after banking does not cost the bet: back on the Wager tab the side,
    /// its price and book, and the stake are as they were left, and Add to slip can be pressed
    /// again.
    /// </summary>
    [Fact]
    public void Wager_then_slip_then_wager_finds_the_bet_as_it_was_left()
    {
        var cut = RenderComponent<BoardWager>(p => p.Add(x => x.GameId, BoardRows.GameId));

        cut.FindAll(".selection-chip").First(c => c.GetAttribute("aria-label") == "ML · Bills").Click();
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(50m.ToString("C0"), StringComparison.Ordinal)).Click();

        var bet = Squash(Step(cut, "Bet").QuerySelector(".wstep__summary")!.TextContent);
        var review = Squash(cut.Find(".wreview").TextContent);
        Assert.StartsWith("ML · Bills", bet);
        Assert.Contains($"{50m:C0} on ML · Bills", review);

        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Add to slip", StringComparison.Ordinal)).Click();
        cut.FindAll("[role=tab]")[1].Click();
        Assert.NotNull(cut.Find(".slip-card"));
        cut.FindAll("[role=tab]")[0].Click();

        Assert.Equal(bet, Squash(Step(cut, "Bet").QuerySelector(".wstep__summary")!.TextContent));
        Assert.Equal(review, Squash(cut.Find(".wreview").TextContent));
        Assert.Contains("wstep--done", Step(cut, "Stake").ClassList);
        Assert.Equal("On the slip", cut.Find(".wbanked .tag").TextContent.Trim());
        var again = cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Add to slip", StringComparison.Ordinal));
        Assert.False(again.HasAttribute("disabled"));
    }

    /// <summary>
    /// The Slip tab stays live while the operator is on it, so a slip emptied there (its last
    /// card dropped) is not pulled from under them; what it shows then offers the way back to
    /// this window's form, not a second window, and once they leave the tab is disabled again.
    /// </summary>
    [Fact]
    public void An_emptied_slip_tab_offers_pick_a_bet_and_turns_back_to_the_wager_tab()
    {
        var cut = Render();
        Bank(cut);
        cut.FindAll("[role=tab]")[1].Click();
        cut.InvokeAsync(() => Slip.Clear());

        var key = cut.WaitForElement(".empty__action button");
        Assert.Equal("Pick a bet", key.TextContent.Trim());
        Assert.False(SlipTab(cut).HasAttribute("disabled"));

        key.Click();

        Assert.Equal("true", cut.FindAll("[role=tab]")[0].GetAttribute("aria-selected"));
        Assert.True(SlipTab(cut).HasAttribute("disabled"));
        Assert.NotEmpty(cut.FindAll(".wsteps"));
        Assert.Empty(Services.GetRequiredService<LineOps.Desk.Windowing.WindowManager>().Windows);
    }

    private static string Squash(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
