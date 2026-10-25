using LineOps.Core.Entities;
using LineOps.Web.Services;

namespace LineOps.Web.Tests;

/// <summary>
/// The slip's rules, owned by one class so no window has to remember them: a closing price is
/// never logged, a straight bet needs a stake, a parlay's legs are what the market still offers,
/// and the combined price is the legs multiplied unless the book quoted its own.
/// </summary>
public class WagerSlipTests
{
    private static SlipItem Item(int price = -110, bool closing = false, decimal stake = 50m,
        string outcome = "Kansas City Chiefs", string book = "draftkings", int gameId = 7,
        string market = Markets.Moneyline, decimal? line = null)
        => new(Guid.Empty, gameId, "Buffalo Bills at Kansas City Chiefs", DateTimeOffset.UtcNow.AddHours(3), "nfl",
            market, outcome, line, book, price, DateTimeOffset.UtcNow, closing, stake, null, null, null);

    [Fact]
    public void Add_remove_and_clear_each_raise_Changed_once()
    {
        var slip = new WagerSlip();
        var changes = 0;
        slip.Changed += () => changes++;

        var stored = slip.Add(Item());
        Assert.Equal(1, changes);

        slip.Remove(stored.Id);
        Assert.Equal(2, changes);

        slip.Add(Item());
        slip.Clear();
        Assert.Equal(4, changes);
        Assert.Empty(slip.Items);
    }

    [Fact]
    public void Add_assigns_an_id()
    {
        var stored = new WagerSlip().Add(Item());
        Assert.NotEqual(Guid.Empty, stored.Id);
    }

    [Fact]
    public void Removing_an_unknown_id_is_a_no_op()
    {
        var slip = new WagerSlip();
        slip.Add(Item());
        var changes = 0;
        slip.Changed += () => changes++;

        slip.Remove(Guid.NewGuid());

        Assert.Single(slip.Items);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void A_closing_item_cannot_be_logged_and_is_not_a_leg()
    {
        var slip = new WagerSlip();
        var closing = slip.Add(Item(closing: true));
        slip.Add(Item());

        Assert.False(slip.CanLog(closing));
        Assert.DoesNotContain(closing, slip.Legs);
        Assert.Single(slip.Ready);
    }

    [Fact]
    public void A_zero_stake_never_reaches_the_slip()
    {
        var slip = new WagerSlip();
        var changes = 0;
        slip.Changed += () => changes++;

        Assert.Throws<ArgumentOutOfRangeException>(() => slip.Add(Item(stake: 0m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => slip.Add(Item(stake: -5m)));
        Assert.Empty(slip.Items);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void An_edit_to_a_zero_stake_is_refused_and_the_card_keeps_its_stake()
    {
        var slip = new WagerSlip();
        var stored = slip.Add(Item(stake: 50m));
        var changes = 0;
        slip.Changed += () => changes++;

        slip.Touch(stored.Id, i => i with { Stake = 0m });

        Assert.Equal(50m, slip.Items.Single().Stake);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void A_straight_bet_needs_a_stake_and_a_leg_does_not()
    {
        var slip = new WagerSlip();
        // A leg carries no stake once it is on a parlay; the slip never accepts one at zero.
        var unstaked = slip.Add(Item(stake: 1m)) with { Stake = 0m };

        Assert.False(slip.CanLog(unstaked));

        slip.SetMode(SlipMode.Parlay);
        Assert.True(slip.CanLog(unstaked));
    }

    [Fact]
    public void The_combined_price_multiplies_the_legs()
    {
        var slip = new WagerSlip();
        slip.Add(Item(-128));
        slip.Add(Item(-105, outcome: "Buffalo Bills", gameId: 8));
        slip.Add(Item(-200, closing: true));   // not a leg: it does not count

        Assert.Equal(248, slip.CombinedPrice);
    }

    [Fact]
    public void A_parlay_takes_one_leg_per_market_on_a_game()
    {
        var slip = new WagerSlip();
        var first = slip.Add(Item(-332, book: "pinnacle"));
        var twin = slip.Add(Item(-345, book: "draftkings"));   // the same side, the same game
        var other = slip.Add(Item(-105, outcome: "Buffalo Bills", gameId: 8));

        slip.SetMode(SlipMode.Parlay);

        Assert.Equal(new[] { first.Id, other.Id }, slip.Legs.Select(l => l.Id));
        Assert.False(slip.CanLog(twin));
        Assert.True(slip.SharesMarketWithLeg(twin));

        // Dropping the first promotes the twin: the rule is one per market, not "never this one".
        slip.Remove(first.Id);
        Assert.True(slip.IsLeg(twin));
    }

    /// <summary>
    /// A side and a total on one game is a same-game parlay. Both are legs; the over beside the
    /// under is not, since only one can win.
    /// </summary>
    [Fact]
    public void A_side_and_a_total_on_one_game_are_both_legs()
    {
        var slip = new WagerSlip();
        var side = slip.Add(Item(-110, market: Markets.Spread, line: -3m));
        var over = slip.Add(Item(-110, market: Markets.Total, outcome: "over", line: 47.5m));
        var under = slip.Add(Item(-110, market: Markets.Total, outcome: "under", line: 47.5m));

        slip.SetMode(SlipMode.Parlay);

        Assert.Equal(new[] { side.Id, over.Id }, slip.Legs.Select(l => l.Id));
        Assert.True(slip.SharesMarketWithLeg(under));
        Assert.True(slip.IsSameGame);
    }

    /// <summary>
    /// Legs on one game win and lose together, so their prices multiplied overstate the payout:
    /// a same-game parlay has no combined price until the book's own is given.
    /// </summary>
    [Fact]
    public void A_same_game_parlay_is_priced_only_by_the_book()
    {
        var slip = new WagerSlip();
        slip.Add(Item(-110, market: Markets.Spread, line: -3m));
        slip.Add(Item(-110, market: Markets.Total, outcome: "over", line: 47.5m));
        slip.SetMode(SlipMode.Parlay);

        Assert.Null(slip.CombinedPrice);
        Assert.Equal(264, slip.MultipliedPrice);

        slip.SetParlay(20m, 240, "draftkings");
        Assert.Equal(240, slip.CombinedPrice);
    }

    [Fact]
    public void Legs_on_separate_games_are_not_a_same_game_parlay()
    {
        var slip = new WagerSlip();
        slip.Add(Item(-128));
        slip.Add(Item(-110, market: Markets.Total, outcome: "over", line: 8.5m, gameId: 8));

        Assert.False(slip.IsSameGame);
        Assert.Equal(slip.MultipliedPrice, slip.CombinedPrice);
    }

    /// <summary>A leg banked from the Board carries no stake; it is the one card the slip takes at zero.</summary>
    [Fact]
    public void A_leg_is_banked_without_a_stake_and_turns_an_empty_slip_into_a_parlay()
    {
        var slip = new WagerSlip();
        var changes = 0;
        slip.Changed += () => changes++;

        var leg = slip.AddLeg(Item(stake: 25m));

        Assert.True(leg.AsLeg);
        Assert.Equal(0m, leg.Stake);
        Assert.Equal(SlipMode.Parlay, slip.Mode);
        Assert.Equal("draftkings", slip.ParlayBook);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_leg_beside_straight_bets_keeps_the_mode_and_is_not_logged_straight()
    {
        var slip = new WagerSlip();
        var straight = slip.Add(Item(stake: 50m));
        var leg = slip.AddLeg(Item(outcome: "Buffalo Bills", gameId: 8));

        Assert.Equal(SlipMode.Straight, slip.Mode);
        Assert.False(slip.CanLog(leg));
        Assert.Equal(new[] { straight.Id }, slip.Ready.Select(i => i.Id));

        slip.SetMode(SlipMode.Parlay);
        Assert.True(slip.CanLog(leg));
    }

    [Fact]
    public void A_stake_typed_on_a_leg_makes_it_a_wager_and_an_edit_at_zero_keeps_it_a_leg()
    {
        var slip = new WagerSlip();
        var leg = slip.AddLeg(Item());

        slip.Touch(leg.Id, i => i with { Note = "same-game" });
        Assert.True(slip.Items.Single().AsLeg);
        Assert.Equal("same-game", slip.Items.Single().Note);

        slip.Touch(leg.Id, i => i with { Stake = 20m });
        Assert.False(slip.Items.Single().AsLeg);
        Assert.Equal(20m, slip.Items.Single().Stake);

        // Once a wager, the $0 rule is its rule.
        slip.Touch(leg.Id, i => i with { Stake = 0m });
        Assert.Equal(20m, slip.Items.Single().Stake);
    }

    [Fact]
    public void Two_items_on_one_game_are_not_a_parlay()
    {
        var slip = new WagerSlip();
        slip.Add(Item(-332));
        slip.Add(Item(-345));

        Assert.Single(slip.Legs);
        Assert.Null(slip.CombinedPrice);
    }

    [Fact]
    public void A_quoted_parlay_price_replaces_the_product()
    {
        var slip = new WagerSlip();
        slip.Add(Item(-128));
        slip.Add(Item(-105, outcome: "Buffalo Bills", gameId: 8));

        slip.SetParlay(25m, 260, "draftkings");

        Assert.Equal(260, slip.CombinedPrice);
    }

    [Fact]
    public void One_leg_has_no_combined_price()
    {
        var slip = new WagerSlip();
        slip.Add(Item(-128));
        Assert.Null(slip.CombinedPrice);
    }

    [Fact]
    public void Switching_mode_keeps_the_items()
    {
        var slip = new WagerSlip();
        slip.Add(Item());
        slip.Add(Item(outcome: "Buffalo Bills"));

        slip.SetMode(SlipMode.Parlay);
        slip.SetMode(SlipMode.Straight);

        Assert.Equal(2, slip.Items.Count);
    }

    [Fact]
    public void Switching_to_a_parlay_defaults_the_book_to_the_legs_book()
    {
        var slip = new WagerSlip();
        slip.Add(Item(book: "fanduel"));
        slip.Add(Item(book: "fanduel", outcome: "Buffalo Bills", gameId: 9));
        slip.Add(Item(book: "draftkings", gameId: 8));

        slip.SetMode(SlipMode.Parlay);

        Assert.Equal("fanduel", slip.ParlayBook);
    }

    [Fact]
    public void Touch_edits_in_place_and_keeps_the_id()
    {
        var slip = new WagerSlip();
        var stored = slip.Add(Item(stake: 10m));
        var changes = 0;
        slip.Changed += () => changes++;

        slip.Touch(stored.Id, i => i with { Stake = 75m, Id = Guid.NewGuid() });

        Assert.Equal(75m, slip.Items.Single().Stake);
        Assert.Equal(stored.Id, slip.Items.Single().Id);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Two_items_on_one_side_are_twins_and_both_stay()
    {
        var slip = new WagerSlip();
        var a = slip.Add(Item());
        slip.Add(Item(book: "fanduel"));
        var other = slip.Add(Item(outcome: "Buffalo Bills"));

        Assert.True(slip.HasTwin(a));
        Assert.False(slip.HasTwin(other));
        Assert.Equal(3, slip.Items.Count);
    }

    [Fact]
    public void Logged_items_leave_the_slip_in_one_change()
    {
        var slip = new WagerSlip();
        var a = slip.Add(Item());
        var b = slip.Add(Item(outcome: "Buffalo Bills", gameId: 8));
        var closing = slip.Add(Item(closing: true));
        var changes = 0;
        slip.Changed += () => changes++;

        slip.Logged([a.Id, b.Id]);

        Assert.Equal(new[] { closing.Id }, slip.Items.Select(i => i.Id));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void The_drafts_carry_the_stake_straight_and_none_on_a_leg()
    {
        var slip = new WagerSlip();
        slip.Add(Item(-128, stake: 50m));
        slip.Add(Item(-105, stake: 10m, outcome: "Buffalo Bills", gameId: 8));

        var straights = slip.StraightDrafts();
        Assert.Equal([50m, 10m], straights.Select(s => s.Stake));

        slip.SetMode(SlipMode.Parlay);
        slip.SetParlay(25m, null, "draftkings");
        var parlay = slip.ToParlayDraft();

        Assert.Equal(25m, parlay.Stake);
        Assert.Null(parlay.PriceQuoted);
        Assert.Equal(2, parlay.Legs.Count);
        Assert.All(parlay.Legs, l => Assert.Equal(0m, l.Stake));
    }

    [Theory]
    [InlineData("h2h", "Kansas City Chiefs", null, "ML · Chiefs")]
    [InlineData("spread", "Buffalo Bills", 2.5, "Spread · Bills +2.5")]
    [InlineData("total", "Over", 47.5, "Total · Over o47.5")]
    public void A_selection_reads_the_same_everywhere(string market, string outcome, double? line, string expected)
        => Assert.Equal(expected, SelectionText.Of(market, outcome, (decimal?)line));
}
