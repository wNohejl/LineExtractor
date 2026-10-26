using AngleSharp.Dom;
using Bunit;
using LineOps.Desk.Primitives;
using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Desk.Windowing;
using LineOps.Web.Components.Panels;
using LineOps.Web.Services;
using LineOps.Web.Tests.Support;
using LineOps.Web.Windowing;

namespace LineOps.Web.Tests;

/// <summary>
/// The slip: cards that say whether each wager can be logged and why not, a switch between
/// straight bets and one parlay, and a Log that writes what it can and reports what it left.
/// </summary>
public class SlipPanelTests : WagerTestContext
{
    private static SlipItem Item(int price = -110, decimal stake = 50m, bool closing = false,
        string outcome = BoardRows.Home, int gameId = BoardRows.GameId,
        string market = Markets.Moneyline, decimal? line = null)
        => new(Guid.Empty, gameId, "Buffalo Bills at Kansas City Chiefs", DateTimeOffset.UtcNow.AddHours(3), "nfl",
            market, outcome, line, "draftkings", price, DateTimeOffset.UtcNow, closing, stake, null, null, null);

    private static IElement Key(IRenderedFragment cut, string startsWith)
        => cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(startsWith, StringComparison.Ordinal));

    [Fact]
    public void The_slip_is_a_singleton_window_that_needs_no_subject()
    {
        var definition = WindowCatalog.Find(WindowCatalog.Slip)!;

        Assert.Equal("Slip", definition.Title);
        Assert.Equal(typeof(SlipPanel), definition.ComponentType);
        Assert.True(definition.Singleton);
        Assert.False(definition.RequiresSubject);
        Assert.Equal(380, definition.MinWidth);
    }

    [Fact]
    public void An_empty_slip_says_where_wagers_come_from()
    {
        var cut = RenderComponent<SlipPanel>();

        var empty = cut.Find(".empty").TextContent;
        Assert.Contains("Add to slip in a Place wager window banks the wager here", empty);
        Assert.Contains("Shift-click a price on the Board", empty);
        Assert.Empty(cut.FindAll(".slip-card"));
    }

    [Fact]
    public void Each_card_says_whether_it_can_be_logged()
    {
        Slip.Add(Item(stake: 50m));
        Slip.Add(Item(closing: true, gameId: 9));

        var cut = RenderComponent<SlipPanel>();

        var tags = cut.FindAll(".slip-card .tag").Select(t => t.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Ready", "Price needed" }, tags);

        // A started game's card asks for the price that was taken, starting from the close.
        var take = cut.Find(".slip-card__take");
        Assert.Equal("Price you got", take.QuerySelector(".slip-card__take-label")!.TextContent.Trim());
        Assert.Equal("-110", take.QuerySelector("input")!.GetAttribute("value"));
        Assert.Contains("Use this price", take.TextContent);
    }

    /// <summary>
    /// A retroactive parlay: cards on games that have started, each given the price taken, are
    /// legs; the Parlay group lists each leg's price with its decimal multiplier, the Combined row
    /// is their product, and the footer writes the multiplication out.
    /// </summary>
    [Fact]
    public void A_retroactive_parlay_shows_each_legs_odds_and_their_product()
    {
        Slip.Add(Item(price: -132, closing: true, gameId: 1));
        Slip.Add(Item(price: -176, closing: true, gameId: 2));
        Slip.SetMode(SlipMode.Parlay);

        var cut = RenderComponent<SlipPanel>();
        Assert.Empty(cut.FindAll(".slip-leg"));
        Assert.Contains("Enter the price you got", cut.Find(".slip-why").TextContent);

        // The first at the close as printed, the second typed.
        cut.FindAll(".slip-card__take button")[0].Click();
        cut.FindAll(".slip-card__take input")[0].Input("-170");
        cut.FindAll(".slip-card__take button")[0].Click();

        var legs = cut.FindAll(".slip-leg");
        Assert.Equal(2, legs.Count);
        Assert.Equal(["-132", "-170"], legs.Select(l => l.QuerySelector(".slip-leg__price")!.TextContent.Trim()));
        Assert.Equal(["×1.76", "×1.59"], legs.Select(l => l.QuerySelector(".slip-leg__x")!.TextContent.Trim()));

        var combined = OddsMath.CombinedAmerican([-132, -170]);
        Assert.Equal(OddsMath.FormatPrice(combined), cut.Find(".slip-parlay__price").TextContent.Trim());
        Assert.Contains("1.76 × 1.59 =", cut.Find(".slip-parlay__note").TextContent);
        Assert.All(cut.FindAll(".slip-card__meta"), m => Assert.Contains("your price", m.TextContent));
    }

    [Fact]
    public void A_number_that_is_not_a_price_stays_on_the_card_and_says_so()
    {
        Slip.Add(Item(closing: true));
        var cut = RenderComponent<SlipPanel>();

        cut.Find(".slip-card__take input").Input("50");
        cut.Find(".slip-card__take button").Click();

        Assert.True(Slip.Items.Single().IsClosing);
        Assert.Contains("A price is −100 or lower, or +100 or higher.", cut.Find(".slip-card__take").TextContent);
    }

    [Fact]
    public void The_cross_drops_a_card()
    {
        Slip.Add(Item());
        var cut = RenderComponent<SlipPanel>();

        cut.Find(".slip-card button.desk-btn--destructive").Click();

        Assert.Empty(Slip.Items);
        Assert.Empty(cut.FindAll(".slip-card"));
    }

    [Fact]
    public void Typing_a_zero_stake_on_a_card_is_refused_and_said()
    {
        var stored = Slip.Add(Item(stake: 50m));
        var cut = RenderComponent<SlipPanel>();

        cut.Find("input[aria-label='Stake on this wager']").Input("0");

        Assert.Equal(50m, Slip.Items.Single().Stake);
        Assert.Contains($"Needs a stake above zero. The slip keeps {50m:C0} until you type one.", cut.Find(".slip-card__zero").TextContent);

        cut.Find("input[aria-label='Stake on this wager']").Input("75");

        Assert.Equal(75m, Slip.Items.Single().Stake);
        Assert.Empty(cut.FindAll(".slip-card__zero"));
        Assert.Equal(stored.Id, Slip.Items.Single().Id);
    }

    [Fact]
    public void The_parlay_switch_hides_the_stakes_and_shows_the_combined_price()
    {
        Slip.Add(Item(-128));
        Slip.Add(Item(-105, outcome: BoardRows.Away, gameId: 8));
        var cut = RenderComponent<SlipPanel>();
        Assert.Equal(2, cut.FindAll(".slip-card__stake").Count);

        cut.FindAll("button.gate__opt").First(b => b.TextContent.Trim() == "One parlay").Click();

        Assert.Empty(cut.FindAll(".slip-card__stake"));
        Assert.Equal("+248", cut.Find(".slip-parlay__price").TextContent.Trim());
    }

    [Fact]
    public void Log_with_a_closing_item_logs_the_rest_and_reports_what_it_left()
    {
        Slip.Add(Item(stake: 50m));
        Slip.Add(Item(stake: 25m, outcome: BoardRows.Away));
        Slip.Add(Item(closing: true, gameId: 9));
        var cut = RenderComponent<SlipPanel>();

        cut.Find("button.desk-btn--filled").Click();

        Assert.Equal(2, Data.Straights.Count);
        Assert.True(Assert.Single(Slip.Items).IsClosing);
        Assert.Contains("2 logged · 1 left on the slip", cut.Find(".slip-report").TextContent);

        Key(cut, "Show steps").Click();
        Assert.Equal(3, cut.FindAll(".slip-steps li").Count);
    }

    [Fact]
    public void A_parlay_logs_one_parlay_with_zero_stake_legs()
    {
        Slip.Add(Item(-128, stake: 10m));
        Slip.Add(Item(-105, stake: 10m, outcome: BoardRows.Away, gameId: 8));
        Slip.SetMode(SlipMode.Parlay);
        Slip.SetParlay(25m, null, "draftkings");
        var cut = RenderComponent<SlipPanel>();

        cut.Find("button.desk-btn--filled").Click();

        var parlay = Assert.Single(Data.Parlays);
        Assert.Equal(25m, parlay.Stake);
        Assert.Equal(2, parlay.Legs.Count);
        Assert.Empty(Slip.Items);
    }

    [Fact]
    public void In_a_parlay_a_second_item_on_one_game_is_not_a_leg_and_says_why()
    {
        Slip.Add(Item(-332));
        Slip.Add(Item(-345));
        Slip.SetMode(SlipMode.Parlay);
        Slip.SetParlay(25m, null, "draftkings");
        var cut = RenderComponent<SlipPanel>();

        var blocked = Assert.Single(cut.FindAll(".slip-card--blocked"));
        Assert.Contains("Not a leg", blocked.TextContent);
        Assert.Contains("one leg per market on a game", blocked.TextContent);
        Assert.True(cut.Find("button.desk-btn--filled").HasAttribute("disabled"));
        Assert.Contains("needs two legs", cut.Find(".slip-why").TextContent);
    }

    [Fact]
    public void A_same_game_parlay_waits_for_the_books_price_and_logs_at_it()
    {
        Slip.AddLeg(Item(-110, market: Markets.Spread, line: -3m));
        Slip.AddLeg(Item(-110, market: Markets.Total, outcome: "over", line: 47.5m));
        Slip.SetParlay(20m, null, "draftkings");
        var cut = RenderComponent<SlipPanel>();

        Assert.Empty(cut.FindAll(".slip-card--blocked"));
        Assert.Contains("do not multiply", cut.Find(".slip-parlay__note").TextContent);
        Assert.Equal("—", cut.Find(".slip-parlay__price").TextContent.Trim());
        Assert.Contains("book's own parlay price", cut.Find(".slip-why").TextContent);

        Slip.SetParlay(20m, 240, "draftkings");
        cut.Render();
        cut.Find("button.desk-btn--filled").Click();

        var parlay = Assert.Single(Data.Parlays);
        Assert.Equal(240, parlay.PriceQuoted);
        Assert.Equal(2, parlay.Legs.Count);
        Assert.Equal(new[] { Markets.Spread, Markets.Total }, parlay.Legs.Select(l => l.Market));
    }

    [Fact]
    public void A_leg_on_a_straight_slip_says_it_has_no_stake()
    {
        Slip.Add(Item(stake: 50m));
        Slip.AddLeg(Item(outcome: BoardRows.Away, gameId: 8));
        var cut = RenderComponent<SlipPanel>();

        var leg = cut.FindAll(".slip-card")[1];
        Assert.Contains("Parlay leg", leg.TextContent);
        Assert.Contains("Type a stake", leg.TextContent);
        Assert.Contains("Log 1 ready", cut.Find("button.desk-btn--filled").TextContent);
    }

    [Fact]
    public void A_card_banked_over_a_journal_bet_says_so()
    {
        Slip.Add(Item() with { InJournal = "$25 at -128 (draftkings), Sep 26 13:18 ET" });
        var cut = RenderComponent<SlipPanel>();

        var card = cut.Find(".slip-card");
        Assert.Contains("Worth knowing", card.TextContent);
        Assert.Contains("Already in the journal: $25 at -128 (draftkings)", card.TextContent);
    }

    [Fact]
    public void Ready_is_green_only_where_the_price_has_value()
    {
        Slip.Add(Item() with { Ev = -0.043 });
        var cut = RenderComponent<SlipPanel>();

        var tag = cut.Find(".slip-card__state .tag");
        Assert.Contains("Ready", tag.TextContent);
        Assert.DoesNotContain("tag--good", tag.ClassName);
    }

    [Fact]
    public void A_failed_save_leaves_the_slip_intact()
    {
        Slip.Add(Item());
        Slip.Add(Item(outcome: BoardRows.Away));
        Data.FailWith = new InvalidOperationException("database is down");
        var cut = RenderComponent<SlipPanel>();

        cut.Find("button.desk-btn--filled").Click();

        Assert.Equal(2, Slip.Items.Count);
        // A sentence for the operator; the exception's own text goes to the log, not the slip.
        Assert.Contains("The entry was not logged. Try again.", cut.Find(".note--warn").TextContent);
        Assert.DoesNotContain("database is down", cut.Markup);
    }

    [Fact]
    public void Nothing_loggable_disables_Log_and_says_why()
    {
        Slip.Add(Item(closing: true));
        var cut = RenderComponent<SlipPanel>();

        Assert.True(cut.Find("button.desk-btn--filled").HasAttribute("disabled"));
        Assert.Equal("Nothing on the slip can be logged as it stands.", cut.Find(".slip-why").TextContent.Trim());
    }

    [Fact]
    public void Start_over_confirms_when_the_slip_holds_more_than_one()
    {
        Slip.Add(Item());
        Slip.Add(Item(outcome: BoardRows.Away));
        var cut = RenderComponent<SlipPanel>();

        Key(cut, "Start over").Click();

        Assert.Single(Alerts.Asked);
        Assert.Empty(Slip.Items);
    }
}

/// <summary>
/// Additions and removals on the slip are told apart by colour as well as by word: a dropped
/// card is a red toast with Undo, and every ready card wears the same Ready, the value carrying
/// its own colour.
/// </summary>
public class SlipRemovalTests : WagerTestContext
{
    private static SlipItem Item(int price = -110, double? ev = null, int gameId = BoardRows.GameId)
        => new(Guid.Empty, gameId, "Buffalo Bills at Kansas City Chiefs", DateTimeOffset.UtcNow.AddHours(3), "nfl",
            Markets.Moneyline, BoardRows.Home, null, "draftkings", price, DateTimeOffset.UtcNow, false, 50m, null, null, ev);

    [Fact]
    public void Dropping_a_card_is_a_red_toast_whose_Undo_puts_it_back_where_it_was()
    {
        Slip.Add(Item(-110, gameId: 1));
        var middle = Slip.Add(Item(-120, gameId: 2));
        Slip.Add(Item(-130, gameId: 3));
        var cut = RenderComponent<SlipPanel>();

        cut.FindAll(".slip-card button.desk-btn--destructive")[1].Click();

        Assert.Equal(2, Slip.Items.Count);
        Assert.Equal($"Removed from the slip: {middle.Selection}.", LastToast.Message);
        Assert.Equal(DeskState.Negative, LastToast.State);
        Assert.Equal("Undo", LastToast.ActionLabel);

        PressToastKey();

        cut.WaitForAssertion(() => Assert.Equal(3, Slip.Items.Count));
        Assert.Equal(middle.Id, Slip.Items[1].Id);
    }

    [Fact]
    public void Ready_is_one_colour_and_the_value_carries_the_green()
    {
        Slip.Add(Item(-110, ev: 0.003, gameId: 1));
        Slip.Add(Item(-120, ev: -0.022, gameId: 2));
        var cut = RenderComponent<SlipPanel>();

        var tags = cut.FindAll(".slip-card .tag");
        Assert.All(tags, t => Assert.Equal("Ready", t.TextContent.Trim()));
        Assert.Single(tags.Select(t => t.ClassName).Distinct());

        var states = cut.FindAll(".slip-card__state");
        Assert.NotNull(states[0].QuerySelector(".up"));
        Assert.Null(states[1].QuerySelector(".up"));
    }
}
