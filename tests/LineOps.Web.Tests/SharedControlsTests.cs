using Bunit;
using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Web.Components.Desk;
using LineOps.Web.Services;
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>The selection strip and the stake row: one definition each, shared by every window that logs.</summary>
public class SharedControlsTests : DeskTestContext
{
    [Fact]
    public void The_strip_lists_every_side_and_tints_the_chosen_one()
    {
        var cut = RenderComponent<SelectionChips>(p => p
            .Add(x => x.Row, BoardRows.Row())
            .Add(x => x.Market, Markets.Spread)
            .Add(x => x.Outcome, BoardRows.Away));

        var chips = cut.FindAll(".selection-chips button");
        Assert.Equal(6, chips.Count);

        var chosen = Assert.Single(cut.FindAll(".selection-chip--on"));
        Assert.Equal("Spread · Bills +2.5", chosen.GetAttribute("aria-label"));
        Assert.Equal("+2.5", chosen.QuerySelector(".selection-chip__side")!.TextContent.Trim());
        Assert.Equal("true", chosen.GetAttribute("aria-checked"));
    }

    [Fact]
    public void Pressing_a_chip_reports_the_side()
    {
        SelectionChoice? picked = null;
        var cut = RenderComponent<SelectionChips>(p => p
            .Add(x => x.Row, BoardRows.Row())
            .Add(x => x.OnChoose, (SelectionChoice c) => picked = c));

        var over = cut.FindAll(".selection-chip").First(b => b.GetAttribute("aria-label") == "Total · Over o47.5");
        Assert.Equal("Over 47.5", over.QuerySelector(".selection-chip__side")!.TextContent.Trim());
        over.Click();

        Assert.NotNull(picked);
        Assert.Equal(Markets.Total, picked.Market);
        Assert.Equal("Over", picked.Outcome);
    }

    [Fact]
    public void A_quick_key_sets_the_stake()
    {
        decimal? stake = null;
        var cut = RenderComponent<StakeRow>(p => p
            .Add(x => x.Stake, 0m)
            .Add(x => x.StakeChanged, (decimal s) => stake = s)
            .Add(x => x.PriceAmerican, -110));

        cut.FindAll("button").First(b => b.TextContent.Trim() == 50m.ToString("C0")).Click();

        Assert.Equal(50m, stake);
    }

    [Fact]
    public void The_quick_amount_matching_the_stake_shows_as_chosen()
    {
        var cut = RenderComponent<StakeRow>(p => p.Add(x => x.Stake, 50m));

        var keys = cut.FindAll("button").Where(b => b.TextContent.Trim().StartsWith('$')).ToList();
        Assert.Equal(["false", "true", "false"], keys.Select(k => k.GetAttribute("aria-pressed")));
        Assert.Contains("desk-btn--tinted", keys[1].ClassName);

        // A typed amount that is none of them leaves all three unchosen.
        var typed = RenderComponent<StakeRow>(p => p.Add(x => x.Stake, 40m));
        Assert.All(typed.FindAll("button").Where(b => b.TextContent.Trim().StartsWith('$')),
            k => Assert.Equal("false", k.GetAttribute("aria-pressed")));
    }

    [Fact]
    public void Returns_sit_beside_the_stake_once_there_is_one()
    {
        var none = RenderComponent<StakeRow>(p => p.Add(x => x.Stake, 0m).Add(x => x.PriceAmerican, -110));
        Assert.Empty(none.FindAll(".stake-row__returns"));

        var some = RenderComponent<StakeRow>(p => p.Add(x => x.Stake, 100m).Add(x => x.PriceAmerican, -110));
        Assert.Contains(OddsMath.PayoutOnWin(-110, 100m).ToString("C0"), some.Find(".stake-row__returns").TextContent);
    }

    [Fact]
    public void The_stake_field_is_immediate_numeric_and_named()
    {
        var cut = RenderComponent<StakeRow>(p => p.Add(x => x.Stake, 0m));
        var input = cut.Find("input[placeholder='Stake']");

        Assert.Equal("number", input.GetAttribute("type"));
        Assert.Equal("5", input.GetAttribute("step"));
    }

    [Fact]
    public void A_door_lands_on_its_side_and_its_book()
    {
        var row = BoardRows.Row();

        Assert.Equal(-128, SelectionChoice.Land(row, null, null, null)!.PriceAmerican);
        Assert.Equal(-105, SelectionChoice.Land(row, Markets.Spread, BoardRows.Away, null)!.PriceAmerican);
        Assert.Equal(-135, SelectionChoice.Land(row, Markets.Moneyline, BoardRows.Home, "fanduel")!.PriceAmerican);
        // A book that does not quote the side falls back to the best, not to nothing.
        Assert.Equal(-128, SelectionChoice.Land(row, Markets.Moneyline, BoardRows.Home, "caesars")!.PriceAmerican);
    }
}
