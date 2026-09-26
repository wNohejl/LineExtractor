using LineOps.Core.Analytics;
using LineOps.Core.Entities;

namespace LineOps.Tests.Analytics;

public class PerformanceAnalyticsTests
{
    private static JournalEntry Entry(
        EntryResult result, decimal stake = 100m, int price = -110,
        string market = Markets.Spread, string book = "draftkings",
        DateTimeOffset? placedAt = null)
    {
        var entry = new JournalEntry
        {
            Market = market,
            Outcome = "home",
            Book = book,
            PriceTaken = price,
            Stake = stake,
            PlacedAt = placedAt ?? DateTimeOffset.UtcNow
        };

        PerformanceAnalytics.ApplyResult(entry, result);
        return entry;
    }

    [Fact]
    public void ApplyResult_Win_PaysStakePlusProfit()
    {
        var entry = Entry(EntryResult.Win, stake: 110m, price: -110);

        Assert.Equal(210m, entry.Payout!.Value, precision: 2);
        Assert.Equal(100m, entry.NetReturn, precision: 2);
    }

    [Fact]
    public void ApplyResult_Loss_ForfeitsTheStake()
    {
        var entry = Entry(EntryResult.Loss, stake: 100m);

        Assert.Equal(0m, entry.Payout);
        Assert.Equal(-100m, entry.NetReturn);
    }

    [Fact]
    public void ApplyResult_Push_ReturnsStakeAndNetsZero()
    {
        var entry = Entry(EntryResult.Push, stake: 100m);

        Assert.Equal(100m, entry.Payout);
        Assert.Equal(0m, entry.NetReturn);
        Assert.True(entry.IsSettled);
    }

    [Fact]
    public void PendingEntriesAreNotSettledAndDoNotCountTowardRoi()
    {
        var pending = new JournalEntry { Stake = 100m, PriceTaken = -110 };

        Assert.False(pending.IsSettled);
        Assert.Equal(0m, pending.NetReturn);

        var summary = PerformanceAnalytics.Summarise([pending]);
        Assert.Equal(0, summary.SettledCount);
        Assert.Equal(0m, summary.TotalStaked);
    }

    [Fact]
    public void Summarise_ComputesRoiOverSettledEntriesOnly()
    {
        JournalEntry[] entries =
        [
            Entry(EntryResult.Win, stake: 100m, price: 100),   // +100
            Entry(EntryResult.Loss, stake: 100m),              // -100
            Entry(EntryResult.Win, stake: 100m, price: 100),   // +100
            new() { Stake = 500m, PriceTaken = -110 }          // pending: excluded
        ];

        var summary = PerformanceAnalytics.Summarise(entries);

        Assert.Equal(3, summary.SettledCount);
        Assert.Equal(300m, summary.TotalStaked);
        Assert.Equal(100m, summary.NetProfit);
        Assert.Equal(1m / 3m, summary.Roi, precision: 4);
    }

    [Fact]
    public void WinRate_ExcludesPushesFromTheDenominator()
    {
        JournalEntry[] entries =
        [
            Entry(EntryResult.Win),
            Entry(EntryResult.Loss),
            Entry(EntryResult.Push)
        ];

        var summary = PerformanceAnalytics.Summarise(entries);

        // One win from two decided results, not from three settled ones.
        Assert.Equal(0.5, summary.WinRate, precision: 6);
        Assert.Equal(3, summary.SettledCount);
    }

    [Fact]
    public void Summarise_EmptySetIsZeroNotDivideByZero()
    {
        var summary = PerformanceAnalytics.Summarise([]);

        Assert.Equal(0, summary.SettledCount);
        Assert.Equal(0m, summary.Roi);
        Assert.Equal(0d, summary.WinRate);
    }

    [Fact]
    public void ComputeClv_PositiveWhenPriceTakenBeatsTheClose()
    {
        var entry = Entry(EntryResult.Win, price: 110);
        const int closing = -110;

        var clv = PerformanceAnalytics.ComputeClv(entry, closing);

        Assert.NotNull(clv);
        Assert.True(clv!.Value.BeatClose);
        Assert.True(clv.Value.CentsPercent > 0);
    }

    [Fact]
    public void ComputeClv_NegativeWhenTheLineMovedAgainstYou()
    {
        var entry = Entry(EntryResult.Loss, price: -130);
        const int closing = -110;

        var clv = PerformanceAnalytics.ComputeClv(entry, closing);

        Assert.NotNull(clv);
        Assert.False(clv!.Value.BeatClose);
        Assert.True(clv.Value.CentsPercent < 0);
    }

    [Fact]
    public void ComputeClv_IsNullWithoutAClosingSnapshot()
    {
        // Free-text entries — props, futures, parlay legs — have no odds feed to close
        // against, so CLV stays undefined rather than being invented.
        var entry = Entry(EntryResult.Win);

        Assert.Null(PerformanceAnalytics.ComputeClv(entry, (int?)null));
    }

    [Fact]
    public void BankrollCurve_AccumulatesInPlacementOrder()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        JournalEntry[] entries =
        [
            Entry(EntryResult.Win, stake: 100m, price: 100, placedAt: start),
            Entry(EntryResult.Loss, stake: 50m, placedAt: start.AddDays(1)),
            Entry(EntryResult.Win, stake: 100m, price: 100, placedAt: start.AddDays(2))
        ];

        var curve = PerformanceAnalytics.BankrollCurve(entries, startingBankroll: 1000m);

        Assert.Equal(3, curve.Count);
        Assert.Equal(1100m, curve[0].Cumulative);
        Assert.Equal(1050m, curve[1].Cumulative);
        Assert.Equal(1150m, curve[2].Cumulative);
    }

    [Fact]
    public void SummariseBy_BreaksDownByArbitraryKey()
    {
        JournalEntry[] entries =
        [
            Entry(EntryResult.Win, price: 100, market: Markets.Spread),
            Entry(EntryResult.Loss, market: Markets.Spread),
            Entry(EntryResult.Win, price: 100, market: Markets.Total)
        ];

        var byMarket = PerformanceAnalytics.SummariseBy(entries, e => e.Market);

        Assert.Equal(2, byMarket.Count);
        Assert.Equal(0m, byMarket[Markets.Spread].NetProfit);
        Assert.Equal(100m, byMarket[Markets.Total].NetProfit);
    }

    [Fact]
    public void A_void_is_settled_but_not_graded_and_stays_out_of_roi()
    {
        var won = Entry(EntryResult.Win, stake: 110m, price: -110);
        var voided = Entry(EntryResult.Void, stake: 500m);

        // Settled: nothing is owed on it any more, so it is not pending.
        Assert.True(voided.IsSettled);
        Assert.False(voided.IsGraded);
        Assert.Equal(500m, voided.Payout);
        Assert.Equal(0m, voided.NetReturn);

        // Not graded: its stake was never at risk, so it must not dilute the return.
        var summary = PerformanceAnalytics.Summarise([won, voided]);
        Assert.Equal(1, summary.SettledCount);
        Assert.Equal(110m, summary.TotalStaked);
        Assert.Equal(100m / 110m, summary.Roi, precision: 4);
    }

    [Theory]
    [InlineData(Markets.Spread, "home", -1.5, -2.5, 1.0)]   // laid a point and a half less than the close
    [InlineData(Markets.Spread, "away", 3.5, 2.5, 1.0)]     // got the hook the close did not have
    [InlineData(Markets.Spread, "home", -3.0, -2.5, -0.5)]  // laid more than it closed at
    [InlineData(Markets.Total, "over", 8.5, 9.0, 0.5)]      // an over wants the lower number
    [InlineData(Markets.Total, "under", 8.5, 9.0, -0.5)]    // an under wants the higher one
    [InlineData(Markets.Total, "UNDER", 47.5, 45.5, 2.0)]
    public void Points_gained_are_signed_from_the_bettors_side(
        string market, string outcome, double taken, double closed, double expected)
    {
        var entry = new JournalEntry
        {
            Market = market, Outcome = outcome, PriceTaken = -110,
            LineTaken = (decimal)taken, ClosingPoints = (decimal)closed
        };

        Assert.Equal((decimal)expected, PerformanceAnalytics.PointsGained(entry));
    }

    [Fact]
    public void A_moneyline_has_no_points_and_is_read_by_price()
    {
        var entry = new JournalEntry { Market = Markets.Moneyline, Outcome = "home", PriceTaken = 150, ClosingPrice = 120 };

        var clv = PerformanceAnalytics.ComputeClv(entry)!.Value;

        Assert.Null(clv.PointsGained);
        Assert.True(clv.SameNumber);
        Assert.True(clv.BeatClose);
    }

    [Fact]
    public void When_the_line_moved_the_points_decide_whatever_the_juice_did()
    {
        // -1.5 at -120 into a -2.5 -105 close: the price looks 7% worse, the bet is a point and a
        // half better. Scoring it on price alone called this losing to the close.
        var entry = new JournalEntry
        {
            Market = Markets.Spread, Outcome = "home",
            PriceTaken = -120, LineTaken = -1.5m,
            ClosingPrice = -105, ClosingPoints = -2.5m
        };

        var clv = PerformanceAnalytics.ComputeClv(entry)!.Value;

        Assert.False(clv.SameNumber);
        Assert.Equal(1.0m, clv.PointsGained);
        Assert.True(clv.CentsPercent < 0);
        Assert.True(clv.BeatClose);
    }

    [Fact]
    public void At_the_same_number_the_price_decides()
    {
        var entry = new JournalEntry
        {
            Market = Markets.Spread, Outcome = "home",
            PriceTaken = -115, LineTaken = -2.5m,
            ClosingPrice = -105, ClosingPoints = -2.5m
        };

        var clv = PerformanceAnalytics.ComputeClv(entry)!.Value;

        Assert.True(clv.SameNumber);
        Assert.False(clv.BeatClose);
    }

    [Fact]
    public void The_curve_runs_in_the_order_bets_settled_from_the_starting_bankroll()
    {
        var early = DateTimeOffset.Parse("2026-09-01T12:00:00Z");

        // Placed first, settled last: a futures-style bet must not move Tuesday's bankroll.
        var slow = Entry(EntryResult.Win, stake: 100m, price: 100, placedAt: early);
        slow.SettledAt = early.AddDays(10);
        var quick = Entry(EntryResult.Loss, stake: 50m, placedAt: early.AddDays(1));
        quick.SettledAt = early.AddDays(1);

        var curve = PerformanceAnalytics.BankrollCurve([slow, quick], startingBankroll: 1000m);

        Assert.Equal([950m, 1050m], curve.Select(p => p.Cumulative));
        Assert.Equal(quick.SettledAt, curve[0].At);
    }

    [Theory]
    [InlineData(null, null, null, "No close")]
    [InlineData(-110, "draftkings", -2.5, "Same number")]
    [InlineData(-110, "DraftKings", -3.5, "Line moved")]
    [InlineData(-110, "fanduel", -2.5, "Another book's close")]
    public void Each_reading_says_what_it_was_measured_against(int? closePrice, string? closeBook, double? closePoints, string expected)
    {
        var entry = new JournalEntry
        {
            Market = Markets.Spread, Outcome = "home", Book = "draftkings", PriceTaken = -110, LineTaken = -2.5m,
            ClosingPrice = closePrice, ClosingBook = closeBook, ClosingPoints = (decimal?)closePoints
        };

        Assert.Equal(expected, PerformanceAnalytics.ClvBasis(entry));
    }

    [Fact]
    public void A_price_is_worth_what_the_fair_close_says_not_what_one_book_closed_at()
    {
        // -110 into a -110 close compares as nothing, but the fair close was a coin: the bet
        // cost 1/22 of the stake. +110 into the same close was five cents of value.
        var even = new ClvResult(-110, -110, FairAtClose: 0.5);
        var plus = new ClvResult(110, -110, FairAtClose: 0.5);

        Assert.Equal(0.0, even.CentsPercent, precision: 9);
        Assert.Equal(-1.0 / 22, even.EvAtClose!.Value, precision: 9);
        Assert.Equal(0.05, plus.EvAtClose!.Value, precision: 9);
    }

    [Fact]
    public void Without_a_fair_close_there_is_no_value_reading()
        => Assert.Null(new ClvResult(-110, -120).EvAtClose);
}
