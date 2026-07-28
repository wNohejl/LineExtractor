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
}
