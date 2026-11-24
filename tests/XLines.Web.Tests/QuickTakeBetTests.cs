using Bunit;
using XLines.Core.Entities;
using XLines.Web.Components.Desk;
using XLines.Web.Tests.Support;

namespace XLines.Web.Tests;

/// <summary>
/// The take's bet pop-up. A take opened on one side — the price pressed, or the row's most value
/// — and that was the only side it could log: the Board's Place wager landed on a bet the operator
/// had not chosen, with no way to another but closing the take and finding the cell. The side is
/// now chosen in the take, from every side a book prices, beside the book it is taken at.
/// </summary>
public class QuickTakeBetTests : WagerTestContext
{
    private const string BetKey = ".bet-picker .popup__key";

    private IRenderedComponent<QuickTake> Render(List<(string Market, string Outcome)>? chosen = null)
        => RenderComponent<QuickTake>(p =>
        {
            p.Add(x => x.Row, BoardRows.Row());
            p.Add(x => x.Market, Markets.Moneyline);
            p.Add(x => x.Outcome, BoardRows.Home);
            p.Add(x => x.Matchup, "Bills at Chiefs");

            if (chosen is not null)
                p.Add(x => x.OnChooseSide, ((string Market, string Outcome) side) => chosen.Add(side));
        });

    private static void Choose(IRenderedComponent<QuickTake> cut, string label)
    {
        cut.Find(BetKey).Click();
        cut.FindAll(".bet-picker [role=menuitemradio]").First(r => r.TextContent.Contains(label)).Click();
    }

    [Fact]
    public void A_host_that_does_not_follow_the_side_gets_the_side_in_words_and_no_pop_up()
    {
        var cut = Render();

        Assert.Empty(cut.FindAll(BetKey));
        Assert.Contains("Chiefs", cut.Find(".qtake__sel").TextContent);
    }

    [Fact]
    public void Every_priced_side_of_the_game_is_offered_with_the_one_in_hand_checked()
    {
        var cut = Render(chosen: []);

        cut.Find(BetKey).Click();
        var sides = cut.FindAll(".bet-picker [role=menuitemradio]");

        // Moneyline, spread and total, both sides of each.
        Assert.Equal(6, sides.Count);
        Assert.Contains("ML · Chiefs", Assert.Single(sides, s => s.GetAttribute("aria-checked") == "true").TextContent);
        Assert.Contains(sides, s => s.TextContent.Contains("Spread · Bills +2.5") && s.TextContent.Contains("-105"));
        Assert.Contains(sides, s => s.TextContent.Contains("Total · Under"));
    }

    [Fact]
    public void Choosing_another_side_takes_its_best_price_and_tells_the_host()
    {
        var chosen = new List<(string Market, string Outcome)>();
        var cut = Render(chosen);

        Choose(cut, "Spread · Bills");

        Assert.Equal((Markets.Spread, BoardRows.Away), Assert.Single(chosen));
        Assert.Equal("-105", cut.Find(".qtake__price").TextContent.Trim());
        Assert.Equal("FanDuel", cut.Find(".price-picker .popup__label").TextContent.Trim());
    }

    [Fact]
    public async Task Log_records_the_side_chosen_in_the_take_not_the_one_it_opened_on()
    {
        var cut = Render(chosen: []);

        Choose(cut, "Spread · Bills");
        cut.FindAll(".stake-row button").First(b => b.TextContent.Trim() == "$25").Click();
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Log", StringComparison.Ordinal)).Click();
        await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Trim() == "Log wager").Click());

        var draft = Assert.Single(Data.Straights);
        Assert.Equal((Markets.Spread, BoardRows.Away, 2.5m, "fanduel", -105m, 25m),
            (draft.Market, draft.Outcome, draft.LineTaken, draft.Book, draft.PriceTaken, draft.Stake));
    }

    [Fact]
    public void A_host_that_hears_the_choice_and_does_not_follow_it_keeps_it_through_a_redraw()
    {
        // The callback is bound and the parameters never change: the host's next render hands the
        // opening side back, and the side chosen here must outlast it.
        var cut = Render(chosen: []);

        Choose(cut, "Spread · Bills");
        cut.SetParametersAndRender(p => p.Add(x => x.Row, BoardRows.Row()));

        Assert.Equal("-105", cut.Find(".qtake__price").TextContent.Trim());
        Assert.Contains("Spread · Bills", cut.Find(".bet-picker .popup__label").TextContent);
    }

    [Fact]
    public void The_stake_typed_so_far_is_kept_across_a_change_of_side()
    {
        var cut = Render(chosen: []);

        cut.FindAll(".stake-row button").First(b => b.TextContent.Trim() == "$25").Click();
        Choose(cut, "Total · Over");

        var log = cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Log", StringComparison.Ordinal));
        Assert.False(log.HasAttribute("disabled"));
    }
}
