using XLines.Data;
using XLines.Ingestion.Configuration;
using XLines.Ingestion.Services;

namespace XLines.Tests.Reliability;

/// <summary>
/// The budget arithmetic behind the line cadence, asked directly with fixed numbers and a fixed
/// instant (<see cref="LinePacing.Budget"/>), and the one definition of the credit month every reader
/// counts from (<see cref="MonthlyCredits.MonthStart"/>). The planner's own tests read the same rule
/// through the database and the clock; these pin it to the minute.
/// </summary>
public class LinePacingBudgetTests
{
    private static readonly LinePollingOptions Settings = new()
    {
        DailyReserve = 50,
        HourlyReserve = 10,
        MonthlyCreditReserve = 100,
        MinimumInterval = TimeSpan.FromMinutes(5),
        MaximumInterval = TimeSpan.FromHours(3)
    };

    /// <summary>Midnight on 11 November 2026, UTC: twenty days, 480 hours, before the month resets.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);

    private static BudgetFacts Facts(
        int? daily = null, int requestsLastDay = 0, int? hourly = null, int requestsPerScan = 2,
        int? monthly = null, int spent = 0, int scores = 0, int creditsPerScan = 1)
        => new(daily, requestsLastDay, hourly, requestsPerScan, monthly, spent, scores, creditsPerScan);

    [Fact]
    public void A_source_with_no_ceiling_has_no_budget()
        => Assert.Null(LinePacing.Budget(Facts(), Settings, Now));

    [Fact]
    public void A_daily_allowance_is_spread_over_the_day_after_the_reserve_and_the_days_spend()
    {
        // 500 a day, 50 held back, 210 spent: 240 requests, 120 scans of two, one every twelve minutes.
        var budget = LinePacing.Budget(Facts(daily: 500, requestsLastDay: 210), Settings, Now);

        Assert.Equal(new SourceBudget(TimeSpan.FromHours(24d / 120), 120, false), budget);
    }

    [Fact]
    public void An_hourly_allowance_is_counted_as_scans_a_day()
    {
        // 100 an hour less 10 is 45 scans of two an hour, 1,080 a day: one every 80 seconds.
        var budget = LinePacing.Budget(Facts(hourly: 100), Settings, Now);

        Assert.Equal(new SourceBudget(TimeSpan.FromHours(24d / 1080), 1080, false), budget);
    }

    [Fact]
    public void A_monthly_allowance_is_spread_over_the_hours_the_month_has_left()
    {
        // 500 less the 100 reserve, 80 spent and 20 set aside for scores: 300 credits, 100 scans of
        // three across 480 hours, one every 4.8 hours. The planner clamps it; the arithmetic does not.
        var budget = LinePacing.Budget(Facts(monthly: 500, spent: 80, scores: 20, creditsPerScan: 3), Settings, Now);

        Assert.Equal(new SourceBudget(TimeSpan.FromHours(480d / 100), 100, true), budget);
    }

    [Fact]
    public void Where_a_day_and_a_month_both_apply_the_slower_governs()
    {
        // The day affords a scan every twelve minutes; the month only every 4.8 hours.
        var budget = LinePacing.Budget(
            Facts(daily: 500, requestsLastDay: 210, monthly: 500, spent: 80, scores: 20, creditsPerScan: 3), Settings, Now);

        Assert.Equal(new SourceBudget(TimeSpan.FromHours(480d / 100), 100, true), budget);
    }

    [Fact]
    public void Where_the_day_is_slower_than_the_month_the_day_governs()
    {
        // The day: 10 requests, 5 scans, one every 4.8 hours. The month affords far more.
        var budget = LinePacing.Budget(
            Facts(daily: 60, monthly: 10_000, creditsPerScan: 1), Settings, Now);

        Assert.Equal(new SourceBudget(TimeSpan.FromHours(24d / 5), 5, false), budget);
    }

    [Theory]
    [InlineData(400)]   // exactly the reserve's edge: nothing usable
    [InlineData(600)]   // overspent: never negative
    public void A_spent_month_backs_off_to_the_maximum(int spent)
    {
        var budget = LinePacing.Budget(Facts(monthly: 500, spent: spent), Settings, Now);

        Assert.Equal(new SourceBudget(Settings.MaximumInterval, 0, true), budget);
    }

    [Fact]
    public void A_spent_day_backs_off_to_the_maximum_and_says_it_is_the_days()
    {
        var budget = LinePacing.Budget(Facts(daily: 500, requestsLastDay: 499), Settings, Now);

        Assert.Equal(new SourceBudget(Settings.MaximumInterval, 0, false), budget);
    }

    [Fact]
    public void Late_in_the_month_the_scans_left_are_still_spread_over_a_day()
    {
        // Thirty minutes from the reset, 10 scans left: the month alone would space them six minutes
        // apart, but the day's spread is the floor, so they come 2.4 hours apart and the month governs.
        var late = new DateTimeOffset(2026, 11, 30, 23, 30, 0, TimeSpan.Zero);
        var budget = LinePacing.Budget(Facts(monthly: 110, creditsPerScan: 1), Settings, late);

        Assert.Equal(new SourceBudget(TimeSpan.FromHours(24d / 10), 10, true), budget);
    }

    [Fact]
    public void The_same_facts_at_a_later_instant_spread_over_fewer_hours()
    {
        // The instant is the caller's: one plan passes one, so its sources cannot disagree about it.
        var early = LinePacing.Budget(Facts(monthly: 500, creditsPerScan: 4), Settings, Now)!.Value;
        var later = LinePacing.Budget(Facts(monthly: 500, creditsPerScan: 4), Settings, Now.AddDays(10))!.Value;

        Assert.Equal(TimeSpan.FromHours(480d / 100), early.Interval);
        Assert.Equal(TimeSpan.FromHours(240d / 100), later.Interval);
    }

    [Theory]
    [InlineData("2026-11-21T15:42:00+00:00", "2026-11-01T00:00:00+00:00", "2026-12-01T00:00:00+00:00")]
    [InlineData("2026-12-31T23:59:59+00:00", "2026-12-01T00:00:00+00:00", "2027-01-01T00:00:00+00:00")]
    [InlineData("2026-11-01T00:00:00+00:00", "2026-11-01T00:00:00+00:00", "2026-12-01T00:00:00+00:00")]
    // The month is the provider's, in UTC: 8 p.m. on 30 November in New York is already December.
    [InlineData("2026-11-30T20:00:00-05:00", "2026-12-01T00:00:00+00:00", "2027-01-01T00:00:00+00:00")]
    public void The_credit_month_is_the_UTC_calendar_month(string now, string start, string reset)
    {
        var at = DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(DateTimeOffset.Parse(start, System.Globalization.CultureInfo.InvariantCulture), MonthlyCredits.MonthStart(at));
        Assert.Equal(DateTimeOffset.Parse(reset, System.Globalization.CultureInfo.InvariantCulture), MonthlyCredits.NextReset(at));
        Assert.Equal(TimeSpan.Zero, MonthlyCredits.MonthStart(at).Offset);
    }
}
