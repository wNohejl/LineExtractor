using LineOps.Ingestion.Services;

namespace LineOps.Tests.Reliability;

/// <summary>
/// A range the operator names in the History window is walked newest day first, as the season
/// walk is, and never reaches past today: a season's last slate is finalized the day it is
/// played, and tomorrow has nothing to fetch.
/// </summary>
public class BackfillRangeTests
{
    private static readonly DateOnly Today = new(2026, 10, 30);

    [Fact]
    public void RangeDates_WalksNewestFirstAndInclusive()
    {
        var dates = HistoryBackfillService.RangeDates(
            new BackfillRange(["mlb"], new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 27)), Today);

        Assert.Equal([new(2026, 10, 27), new(2026, 10, 26), new DateOnly(2026, 10, 25)], dates);
    }

    [Fact]
    public void RangeDates_StopsAtToday()
    {
        var dates = HistoryBackfillService.RangeDates(
            new BackfillRange(["mlb"], new DateOnly(2026, 10, 29), new DateOnly(2026, 11, 5)), Today);

        Assert.Equal([Today, new DateOnly(2026, 10, 29)], dates);
    }

    [Fact]
    public void RangeDates_IsEmptyWhenFromIsAfterTo()
        => Assert.Empty(HistoryBackfillService.RangeDates(
            new BackfillRange(["nfl"], new DateOnly(2026, 10, 28), new DateOnly(2026, 10, 27)), Today));
}
