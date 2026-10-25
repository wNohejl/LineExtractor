using AngleSharp.Dom;
using Bunit;
using LineOps.Core.Entities;
using LineOps.Web.Components.Panels;
using LineOps.Web.Services;
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// Parlay legs from the wager window: Add as leg beside Add to slip, needing no stake, and the
/// Journal's Build parlay door, where every bet picked is a leg and the game search clears for
/// the next one.
/// </summary>
public class WagerParlayLegTests : WagerTestContext
{
    private static IElement Key(IRenderedFragment cut, string startsWith)
        => cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(startsWith, StringComparison.Ordinal));

    private IRenderedComponent<BoardWager> FromBoard(string market, string outcome)
        => RenderComponent<BoardWager>(p => p
            .Add(x => x.GameId, BoardRows.GameId)
            .Add(x => x.Market, market)
            .Add(x => x.Outcome, outcome));

    private IRenderedComponent<BoardWager> Building()
        => RenderComponent<BoardWager>(p => p.Add(x => x.BuildParlay, true));

    private static void PickGame(IRenderedComponent<BoardWager> cut)
    {
        cut.Find("input[aria-label='Find a game']").Input("chiefs");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".game-search__opt")));
        cut.FindAll(".game-search__opt")[0].Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".selection-chip")));
    }

    [Fact]
    public void Add_as_leg_banks_the_side_with_no_stake_where_Add_to_slip_waits_for_one()
    {
        var cut = FromBoard(Markets.Total, "Over");

        Assert.True(Key(cut, "Add to slip").HasAttribute("disabled"));
        Assert.False(Key(cut, "Add as leg").HasAttribute("disabled"));

        Key(cut, "Add as leg").Click();

        var leg = Assert.Single(Slip.Items);
        Assert.True(leg.AsLeg);
        Assert.Equal(0m, leg.Stake);
        Assert.Equal((Markets.Total, "Over", 47.5m), (leg.Market, leg.Outcome, leg.Line));
        Assert.Equal(SlipMode.Parlay, Slip.Mode);
    }

    [Fact]
    public void A_closing_line_cannot_be_added_as_a_leg()
    {
        Data.Row = BoardRows.Row(closing: true);
        var cut = FromBoard(Markets.Moneyline, BoardRows.Home);

        var key = Key(cut, "Add as leg");
        Assert.True(key.HasAttribute("disabled"));
        Assert.Contains("closing line cannot be a leg", key.GetAttribute("title"));
    }

    [Fact]
    public void Building_a_parlay_has_no_stake_step_and_no_Log()
    {
        Data.Games = [BoardRows.Game()];
        var cut = Building();
        Assert.Contains("Find each leg's game", cut.Find(".whead__hint").TextContent);

        PickGame(cut);

        Assert.Equal(["Your bet", "Price and book", "Review and add"],
            cut.FindAll(".wstep__title").Select(t => t.TextContent.Trim()));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim().StartsWith("Log ", StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("button.gate__opt").Where(b => b.TextContent.Contains("No game")));
    }

    [Fact]
    public void Each_leg_added_clears_the_game_for_the_next_and_the_count_stays_in_view()
    {
        Data.Games = [BoardRows.Game()];
        Slip.SetMode(SlipMode.Parlay);
        var cut = Building();

        PickGame(cut);
        cut.FindAll(".selection-chip").First(c => c.GetAttribute("aria-label") == "Spread · Chiefs -2.5").Click();
        Key(cut, "Add leg").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".selection-chip")));
        Assert.Contains("1 leg on the slip", cut.Find(".wager-legs").TextContent);

        // A same-game second leg: the total on the game just used.
        PickGame(cut);
        cut.FindAll(".selection-chip").First(c => c.GetAttribute("aria-label") == "Total · Over o47.5").Click();
        Key(cut, "Add leg").Click();

        cut.WaitForAssertion(() => Assert.Contains("2 legs on the slip", cut.Find(".wager-legs").TextContent));
        Assert.Equal([Markets.Spread, Markets.Total], Slip.Legs.Select(l => l.Market));
        Assert.True(Slip.IsSameGame);
        Assert.All(Slip.Items, i => Assert.Equal(0m, i.Stake));
    }
}
