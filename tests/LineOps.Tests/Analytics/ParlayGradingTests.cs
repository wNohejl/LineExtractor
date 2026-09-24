using LineOps.Core.Analytics;
using LineOps.Core.Entities;

namespace LineOps.Tests.Analytics;

/// <summary>
/// A parlay is one bet: one loss loses it, a push drops a leg and reprices, and it pays once.
/// Its legs used to be graded and summed as straight bets, so a two-leg win counted as two.
/// </summary>
public class ParlayGradingTests
{
    private static JournalEntry Leg(EntryResult result, int price = -110, int sportId = 1)
        => new() { Result = result, PriceTaken = price, Stake = 0m, Game = new Game { SportId = sportId } };

    private static Parlay Parlay(decimal stake = 10m, int? quoted = null) => new() { Stake = stake, PriceQuoted = quoted, Book = "fanduel" };

    [Fact]
    public void One_losing_leg_loses_it_whatever_else_is_undecided()
    {
        var outcome = ParlayGrading.Grade(Parlay(), [Leg(EntryResult.Loss), Leg(EntryResult.Pending), Leg(EntryResult.Win)]);

        Assert.Equal(EntryResult.Loss, outcome.Result);
        Assert.Equal(0m, outcome.Payout);
    }

    [Fact]
    public void An_undecided_leg_keeps_it_pending()
        => Assert.Equal(EntryResult.Pending,
            ParlayGrading.Grade(Parlay(), [Leg(EntryResult.Win), Leg(EntryResult.Pending)]).Result);

    [Fact]
    public void A_clean_sweep_pays_the_books_quoted_price_where_there_is_one()
    {
        // Two -110 legs multiply to +264; the book quoted +250 — the book's number is what paid.
        var outcome = ParlayGrading.Grade(Parlay(10m, quoted: 250), [Leg(EntryResult.Win), Leg(EntryResult.Win)]);

        Assert.Equal(EntryResult.Win, outcome.Result);
        Assert.Equal(35m, outcome.Payout);
    }

    [Fact]
    public void Without_a_quote_a_sweep_pays_the_product_of_the_legs()
    {
        var outcome = ParlayGrading.Grade(Parlay(10m), [Leg(EntryResult.Win, 100), Leg(EntryResult.Win, 100)]);

        Assert.Equal(40m, outcome.Payout);   // 2.0 × 2.0
    }

    [Fact]
    public void A_push_drops_its_leg_and_the_rest_is_repriced_on_the_legs_not_the_quote()
    {
        var outcome = ParlayGrading.Grade(Parlay(10m, quoted: 600),
            [Leg(EntryResult.Win, 100), Leg(EntryResult.Push, 100), Leg(EntryResult.Win, 100)]);

        Assert.Equal(EntryResult.Win, outcome.Result);
        Assert.Equal(40m, outcome.Payout);   // the quote was for three legs; two are left
    }

    [Fact]
    public void Every_leg_pushed_or_voided_returns_the_stake()
    {
        var outcome = ParlayGrading.Grade(Parlay(10m), [Leg(EntryResult.Push), Leg(EntryResult.Void)]);

        Assert.Equal(EntryResult.Void, outcome.Result);
        Assert.Equal(10m, outcome.Payout);
    }

    [Fact]
    public void A_win_with_no_price_to_pay_it_at_is_left_for_a_person_not_invented()
    {
        var outcome = ParlayGrading.Grade(Parlay(10m), [Leg(EntryResult.Win, 0), Leg(EntryResult.Win, -110)]);

        Assert.Equal(EntryResult.Pending, outcome.Result);
        Assert.NotNull(outcome.Reason);
    }

    [Fact]
    public void The_ledger_counts_a_parlay_once_and_its_legs_not_at_all()
    {
        var parlay = Parlay(10m, quoted: 250);
        var legs = new[] { Leg(EntryResult.Win), Leg(EntryResult.Win) };
        foreach (var leg in legs)
        {
            leg.ParlayGroupId = parlay.Id;
            parlay.Legs.Add(leg);
        }
        ParlayGrading.Apply(parlay, ParlayGrading.Grade(parlay, parlay.Legs));

        var straight = new JournalEntry { Stake = 100m, PriceTaken = -110 };
        PerformanceAnalytics.ApplyResult(straight, EntryResult.Loss);

        var ledger = ParlayGrading.Ledger([straight, .. legs], [parlay]);
        var summary = PerformanceAnalytics.Summarise(ledger);

        Assert.Equal(2, summary.SettledCount);
        Assert.Equal(110m, summary.TotalStaked);
        Assert.Equal(-100m + 25m, summary.NetProfit);
    }

    [Fact]
    public void A_parlay_across_sports_has_no_one_sport()
    {
        var parlay = Parlay();
        parlay.Legs.Add(Leg(EntryResult.Pending, sportId: 1));
        parlay.Legs.Add(Leg(EntryResult.Pending, sportId: 2));

        Assert.Null(Assert.Single(ParlayGrading.Ledger([], [parlay])).Game);
    }
}
