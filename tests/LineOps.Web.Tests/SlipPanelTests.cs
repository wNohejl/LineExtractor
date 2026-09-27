using AngleSharp.Dom;
using Bunit;
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
        string outcome = BoardRows.Home, int gameId = BoardRows.GameId)
        => new(Guid.Empty, gameId, "Buffalo Bills at Kansas City Chiefs", DateTimeOffset.UtcNow.AddHours(3), "nfl",
            Markets.Moneyline, outcome, null, "draftkings", price, DateTimeOffset.UtcNow, closing, stake, null, null, null);

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

        Assert.Contains("Nothing on the slip. Add to slip from any Place wager window banks the wager here.", cut.Find(".empty").TextContent);
        Assert.Empty(cut.FindAll(".slip-card"));
    }

    [Fact]
    public void Each_card_says_whether_it_can_be_logged()
    {
        Slip.Add(Item(stake: 50m));
        Slip.Add(Item(stake: 0m, outcome: BoardRows.Away));
        Slip.Add(Item(closing: true, gameId: 9));

        var cut = RenderComponent<SlipPanel>();

        var tags = cut.FindAll(".slip-card .tag").Select(t => t.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Ready", "Needs a stake", "Cannot log" }, tags);

        var blocked = Assert.Single(cut.FindAll(".slip-card--blocked"));
        Assert.Contains("Drop it, or log it from the Journal with the price you got.", blocked.TextContent);
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
        Slip.Add(Item(-128, stake: 0m));
        Slip.Add(Item(-105, stake: 0m, outcome: BoardRows.Away, gameId: 8));
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
        Assert.Contains("separate games", blocked.TextContent);
        Assert.True(cut.Find("button.desk-btn--filled").HasAttribute("disabled"));
        Assert.Contains("two legs on separate games", cut.Find(".slip-why").TextContent);
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
        Assert.Contains("Not logged: database is down", cut.Find(".note--warn").TextContent);
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
