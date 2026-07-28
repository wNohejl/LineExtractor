using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Configuration;
using LineOps.Ingestion.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LineOps.Tests.Reliability;

/// <summary>
/// The line cadence, which is arithmetic with a cost attached: too slow wastes the free tier,
/// too fast overruns it and every subsequent run is refused by the budget guard.
///
/// The old cadence was the constant "every three hours", which cannot be right for two
/// providers with different ceilings and leaves the guard as the only thing between the
/// schedule and an overrun.
/// </summary>
[Collection(PostgresCollection.Name)]
public class LinePollPlannerTests(PostgresFixture fixture)
{
    private static LinePollPlanner Create(LineOpsDbContext db, IngestionOptions? options = null)
        => new(
            db,
            Options.Create(options ?? new IngestionOptions { Sports = ["mlb"] }),
            NullLogger<LinePollPlanner>.Instance);

    private static async Task<Source> SeedSourceAsync(
        LineOpsDbContext db, int? perDay = null, int? perHour = null, int? monthlyCredits = null)
    {
        var source = new Source
        {
            Key = $"poll-{Guid.NewGuid():N}"[..16],
            Name = "Test odds",
            Kind = SourceKind.Odds,
            BaseUrl = "local://test",
            RateLimitPerDay = perDay,
            RateLimitPerHour = perHour,
            MonthlyCreditBudget = monthlyCredits
        };

        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source;
    }

    /// <summary>Hours between now and the end of the current month, which is what a monthly
    /// allowance is spread across.</summary>
    private static double HoursLeftThisMonth()
    {
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return (monthStart.AddMonths(1) - now).TotalHours;
    }

    [Fact]
    public async Task TheDailyAllotmentIsSpreadAcrossTheDay()
    {
        await using var db = fixture.CreateContext();
        var source = await SeedSourceAsync(db, perDay: 500);

        var plan = await Create(db).PlanAsync([source.Key]);

        Assert.NotNull(plan);

        // One sport, one source, two requests a scan. 500 less the 50 reserve is 450, so 225
        // scans — a little over six minutes apart, which fills the day without exceeding it.
        Assert.Equal(2, plan!.CostPerScan);
        Assert.Equal(225, plan.ScansRemainingToday);
        Assert.Equal(24d / 225, plan.BudgetInterval.TotalHours, precision: 3);
    }

    [Fact]
    public async Task RequestsAlreadySpentTightenTheCadence()
    {
        await using var db = fixture.CreateContext();
        var source = await SeedSourceAsync(db, perDay: 500);

        // A backfill, a manual pull, an earlier scan — whatever spent it, the rest of the day
        // has less to work with and the cadence has to slow down rather than overrun.
        db.IngestionRuns.Add(new IngestionRun
        {
            SourceId = source.Id,
            JobKey = "odds:lines",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
            FinishedAt = DateTimeOffset.UtcNow.AddHours(-2),
            Status = RunStatus.Success,
            RequestsMade = 300
        });
        await db.SaveChangesAsync();

        var plan = await Create(db).PlanAsync([source.Key]);

        // 500 - 50 reserve - 300 spent = 150, so 75 scans rather than 225.
        Assert.Equal(75, plan!.ScansRemainingToday);
    }

    [Fact]
    public async Task TheTighterOfTwoCeilingsGoverns()
    {
        await using var db = fixture.CreateContext();

        // Generous by the day, mean by the hour: 100/h less the 10 reserve is 45 scans an
        // hour, which is 1,080 a day — but the daily ceiling only affords 225.
        var source = await SeedSourceAsync(db, perDay: 500, perHour: 100);

        var plan = await Create(db).PlanAsync([source.Key]);

        Assert.Equal(225, plan!.ScansRemainingToday);
    }

    [Fact]
    public async Task ASpentBudgetBacksOffRatherThanHammeringTheGuard()
    {
        await using var db = fixture.CreateContext();
        var source = await SeedSourceAsync(db, perDay: 100);

        db.IngestionRuns.Add(new IngestionRun
        {
            SourceId = source.Id,
            JobKey = "odds:lines",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            FinishedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Status = RunStatus.Success,
            RequestsMade = 100
        });
        await db.SaveChangesAsync();

        var plan = await Create(db).PlanAsync([source.Key]);

        // Nothing left. Continuing at pace would record a refused run every tick, which is
        // noise in the KPIs and no data.
        Assert.Equal(0, plan!.ScansRemainingToday);
        Assert.Equal(TimeSpan.FromHours(3), plan.BudgetInterval);
    }

    [Fact]
    public async Task AGenerousTierIsStillHeldToTheFloor()
    {
        await using var db = fixture.CreateContext();
        var source = await SeedSourceAsync(db, perDay: 1_000_000);

        var plan = await Create(db).PlanAsync([source.Key]);

        // The maths would ask for a scan every fraction of a second. Books do not reprice that
        // often, so past the floor the requests buy sampling noise rather than movement.
        Assert.Equal(TimeSpan.FromMinutes(5), plan!.BudgetInterval);
    }

    [Fact]
    public async Task AnUnmeteredSourceFallsBackToTheFloor()
    {
        await using var db = fixture.CreateContext();
        var source = await SeedSourceAsync(db);

        var plan = await Create(db).PlanAsync([source.Key]);

        // Nothing declared means nothing to pace against; the floor is then a politeness limit
        // rather than a budget one.
        Assert.Equal(TimeSpan.FromMinutes(5), plan!.BudgetInterval);
        Assert.Null(plan.ScansRemainingToday);
    }

    [Fact]
    public async Task MoreSportsCostMorePerScanAndSlowTheCadence()
    {
        await using var db = fixture.CreateContext();
        var source = await SeedSourceAsync(db, perDay: 500);

        var planner = Create(db, new IngestionOptions { Sports = ["mlb", "nfl", "nba", "nhl"] });
        var plan = await planner.PlanAsync([source.Key]);

        // Four sports at two requests each is eight a scan, so a quarter of the scans.
        Assert.Equal(8, plan!.CostPerScan);
        Assert.Equal(56, plan.ScansRemainingToday);
    }

    [Fact]
    public async Task NoOddsSourceMeansNoPlan()
    {
        await using var db = fixture.CreateContext();

        Assert.Null(await Create(db).PlanAsync([]));
    }

    /// <summary>
    /// A game at a known distance from now, so urgency is decided by the test rather than by
    /// whatever else happens to be on the slate.
    /// </summary>
    private static async Task<Sport> SeedGameAsync(LineOpsDbContext db, double hoursOut)
    {
        var sport = new Sport { Key = $"s{Guid.NewGuid():N}"[..8], Name = "Test" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = "Home", Abbrev = "H" };
        var away = new Team { SportId = sport.Id, Name = "Away", Abbrev = "A" };
        db.Teams.AddRange(home, away);
        await db.SaveChangesAsync();

        var game = new Game
        {
            SportId = sport.Id,
            HomeTeamId = home.Id,
            AwayTeamId = away.Id,
            StartsAt = DateTimeOffset.UtcNow.AddHours(hoursOut)
        };

        db.Games.Add(game);
        await db.SaveChangesAsync();
        return sport;
    }

    [Theory]
    [InlineData(1.0, 0.35)]   // lineups are out — the window worth paying for
    [InlineData(5.0, 0.7)]
    [InlineData(12.0, 1.5)]
    [InlineData(30.0, 4.0)]   // a day out and barely moving
    public async Task ProximityToFirstPitchDecidesWhenTheAllowanceIsSpent(
        double hoursOut, double expected)
    {
        await using var db = fixture.CreateContext();
        var sport = await SeedGameAsync(db, hoursOut);
        var source = await SeedSourceAsync(db, monthlyCredits: 500);

        var plan = await Create(db, new IngestionOptions
        {
            // Scoped to this test's own league, which is also what the planner does in earnest:
            // a scan covers the configured sports, so a game in one nobody asked for is no
            // reason to spend faster.
            Sports = [sport.Key],
            // Wide enough that a game 30 hours out is still inside the window and gets a
            // multiplier rather than being ignored for having nothing close.
            MovementWindow = TimeSpan.FromHours(48)
        }).PlanAsync([source.Key]);

        // An even spread treats a line 30 hours out the same as one an hour from first pitch.
        // They are not the same bet to poll: the distant one barely moves, and the hours after
        // lineups post are where the market finds its number.
        Assert.Equal(expected, plan!.Urgency, precision: 2);
    }

    [Fact]
    public async Task UrgencyRedistributesTheAllowanceRatherThanEnlargingIt()
    {
        await using var db = fixture.CreateContext();
        var sport = await SeedGameAsync(db, 1);
        var source = await SeedSourceAsync(db, monthlyCredits: 500);

        var plan = await Create(db, new IngestionOptions { Sports = [sport.Key] })
            .PlanAsync([source.Key]);

        // Scanning faster near first pitch must not buy more scans — the budget still says how
        // much there is, and only the timing changes. Because the maths recomputes from credits
        // actually spent on every tick, spending sooner slows the rest on its own.
        Assert.Equal(200, plan!.ScansRemainingToday);
        Assert.True(plan.Interval < plan.BudgetInterval);
    }

    [Fact]
    public async Task AMonthlyCreditBudgetIsNotMistakenForNoBudgetAtAll()
    {
        await using var db = fixture.CreateContext();

        // The Odds API's free tier, exactly: 500 credits a month and no per-day or per-hour cap
        // published at all. Read as unmetered, this source falls to the five-minute floor — which
        // spends a month's allowance inside a day and leaves eleven months of nothing.
        var source = await SeedSourceAsync(db, monthlyCredits: 500);

        var plan = await Create(db).PlanAsync([source.Key]);

        Assert.NotNull(plan);
        Assert.True(
            plan!.BudgetInterval > TimeSpan.FromMinutes(5),
            $"A metered source must not be paced at the unmetered floor; got {plan.BudgetInterval}.");

        // 500 less the 100 reserve is 400 credits, at 2 a scan — 200 scans, spread over what is
        // left of the month rather than over the next few hours.
        Assert.Equal(200, plan.ScansRemainingToday);
        Assert.Equal(HoursLeftThisMonth() / 200, plan.BudgetInterval.TotalHours, precision: 2);

        // And it has to say so. "200 scans left in today's allowance" reads as generous; the
        // same number against a month is the opposite, and the operator decides whether to
        // spend a manual pull on that basis.
        Assert.Equal(BudgetWindow.Month, plan.Window);
        Assert.Equal("this month's", plan.WindowLabel);
    }

    [Fact]
    public async Task CreditsAlreadySpentThisMonthSlowTheCadence()
    {
        await using var db = fixture.CreateContext();
        var source = await SeedSourceAsync(db, monthlyCredits: 500);

        // A manual "Pull lines" spends from the same pool the scheduler paces against, so the
        // scheduler has to make room for it rather than race it for the last credits.
        db.IngestionRuns.Add(new IngestionRun
        {
            SourceId = source.Id,
            JobKey = "odds:lines:mlb",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            FinishedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Status = RunStatus.Success,
            CreditsSpent = 300
        });
        await db.SaveChangesAsync();

        var plan = await Create(db).PlanAsync([source.Key]);

        // 500 - 100 reserve - 300 spent = 100 credits, so 50 scans rather than 200.
        Assert.Equal(50, plan!.ScansRemainingToday);
    }

    [Fact]
    public async Task AnExhaustedMonthBacksOffRatherThanRetryingIntoARefusal()
    {
        await using var db = fixture.CreateContext();
        var source = await SeedSourceAsync(db, monthlyCredits: 500);

        db.IngestionRuns.Add(new IngestionRun
        {
            SourceId = source.Id,
            JobKey = "odds:lines:mlb",
            StartedAt = DateTimeOffset.UtcNow.AddDays(-1),
            FinishedAt = DateTimeOffset.UtcNow.AddDays(-1),
            Status = RunStatus.Success,
            CreditsSpent = 500
        });
        await db.SaveChangesAsync();

        var plan = await Create(db).PlanAsync([source.Key]);

        Assert.Equal(0, plan!.ScansRemainingToday);
        Assert.Equal(TimeSpan.FromHours(3), plan.BudgetInterval);
    }

    [Fact]
    public async Task TheSlowerOfADailyAndAMonthlyCeilingGoverns()
    {
        await using var db = fixture.CreateContext();

        // Generous by the day, thin by the month. A daily allowance says nothing about whether
        // the month can afford to keep spending it every day — so the month has to win.
        var source = await SeedSourceAsync(db, perDay: 500, monthlyCredits: 500);

        var plan = await Create(db).PlanAsync([source.Key]);

        Assert.Equal(200, plan!.ScansRemainingToday);

        // Asserted as a relationship rather than a threshold. A monthly allowance is spread over
        // the hours the month has *left*, so the interval it implies shrinks as the month runs
        // out — a hardcoded "> 30 minutes" passes early in the month and fails late in it, which
        // is a test that reports the date rather than the behaviour.
        var daily = TimeSpan.FromHours(24d / 225);   // 500 less the 50 reserve, at 2 a scan
        var monthly = TimeSpan.FromHours(HoursLeftThisMonth() / 200);

        Assert.Equal(monthly.TotalHours, plan.BudgetInterval.TotalHours, precision: 2);
        Assert.True(
            plan.BudgetInterval > daily,
            $"The monthly ceiling must govern; got {plan.BudgetInterval} against a daily {daily}.");
    }

    [Fact]
    public async Task AskingForFewerMarketsBuysMoreScans()
    {
        await using var db = fixture.CreateContext();
        var source = await SeedSourceAsync(db, monthlyCredits: 500);

        // Adding totals to moneyline and spread is a third market billed on every call for the
        // rest of the month — the single largest lever over how long an allowance lasts.
        var planner = Create(db, new IngestionOptions
        {
            Sports = ["mlb"],
            LinePolling = new LinePollingOptions { CreditsPerSportPerScan = 3 }
        });

        var plan = await planner.PlanAsync([source.Key]);

        // 400 usable credits at 3 a scan is 133, against 200 at 2.
        Assert.Equal(133, plan!.ScansRemainingToday);
    }
}
