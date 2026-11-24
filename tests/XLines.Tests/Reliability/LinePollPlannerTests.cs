using XLines.Core.Entities;
using XLines.Data;
using XLines.Ingestion.Configuration;
using XLines.Ingestion.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace XLines.Tests.Reliability;

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
    private static LinePollPlanner Create(XLinesDbContext db, IngestionOptions? options = null)
        => new(
            db,
            Options.Create(options ?? new IngestionOptions { Sports = ["mlb"] }),
            NullLogger<LinePollPlanner>.Instance);

    private static async Task<Source> SeedSourceAsync(
        XLinesDbContext db, int? perDay = null, int? perHour = null, int? monthlyCredits = null)
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
        return (MonthlyCredits.NextReset(now) - now).TotalHours;
    }

    /// <summary>
    /// A moment roughly <paramref name="ago"/> in the past, but never earlier than the first of
    /// the month.
    ///
    /// Credit budgets reset monthly, so a run seeded at "a day ago" lands in the previous month
    /// whenever the test runs on the first — and credits spent last month are, correctly, not
    /// counted against this one. The spend then reads as zero and the test fails on a date
    /// rather than on a behaviour.
    /// </summary>
    private static DateTimeOffset EarlierThisMonth(TimeSpan ago)
    {
        var now = DateTimeOffset.UtcNow;
        var monthStart = MonthlyCredits.MonthStart(now);

        return now - ago < monthStart ? monthStart : now - ago;
    }

    /// <summary>
    /// The interval the planner will actually report for a given number of scans left in the
    /// month — the ideal spacing, then clamped to the configured back-off ceiling.
    ///
    /// The clamp is not decoration here. Early in a 31-day month, 500 credits spread evenly
    /// works out slower than the three-hour maximum, so the ceiling binds and the ideal spacing
    /// is never what gets reported. Asserting the unclamped figure passes for most of the month
    /// and fails on the first, which is a worse failure than none: it turns up in CI on a date
    /// nobody changed anything on.
    /// </summary>
    private static double PacedHoursThisMonth(int scans, IngestionOptions? options = null)
    {
        var settings = (options ?? new IngestionOptions { Sports = ["mlb"] }).LinePolling;
        var ideal = HoursLeftThisMonth() / scans;

        return Math.Clamp(
            ideal, settings.MinimumInterval.TotalHours, settings.MaximumInterval.TotalHours);
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

        // Leagues of this test's own, none with a game in the window: the plan then prices a scan of
        // every priced league, and another suite's game in a real league cannot narrow it.
        var planner = Create(db, new IngestionOptions { Sports = [NewKey(), NewKey(), NewKey(), NewKey()] });
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
    private static async Task<Sport> SeedGameAsync(XLinesDbContext db, double hoursOut)
    {
        var sport = await SeedSportAsync(db);
        await SeedGameAsync(db, sport, hoursOut);
        return sport;
    }

    /// <summary>A league key of the test's own, so no other suite's games land in it.</summary>
    private static string NewKey() => $"s{Guid.NewGuid():N}"[..8];

    private static async Task<Sport> SeedSportAsync(XLinesDbContext db)
    {
        var sport = new Sport { Key = NewKey(), Name = "Test" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();
        return sport;
    }

    private static async Task<Game> SeedGameAsync(XLinesDbContext db, Sport sport, double hoursOut)
    {
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
        return game;
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
        Assert.Equal(PacedHoursThisMonth(200), plan.BudgetInterval.TotalHours, precision: 2);

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
            StartedAt = EarlierThisMonth(TimeSpan.FromHours(1)),
            FinishedAt = EarlierThisMonth(TimeSpan.FromHours(1)),
            Status = RunStatus.Success,
            CreditsSpent = 300
        });
        await db.SaveChangesAsync();

        var plan = await Create(db).PlanAsync([source.Key]);

        // 500 - 100 reserve - 300 spent = 100 credits, so 50 scans rather than 200.
        Assert.Equal(50, plan!.ScansRemainingToday);
    }

    /// <summary>
    /// The same key used from the laptop and the desktop: this database recorded one 2-credit
    /// scan, but the provider says only 150 of 500 are left, because the other machine spent the
    /// rest. Pacing against this database alone would plan 199 scans the key cannot afford.
    /// </summary>
    [Fact]
    public async Task AKeySpentFromAnotherMachineIsPacedByWhatTheProviderSaysIsLeft()
    {
        await using var db = fixture.CreateContext();
        var source = await SeedSourceAsync(db, monthlyCredits: 500);

        db.IngestionRuns.Add(new IngestionRun
        {
            SourceId = source.Id,
            JobKey = "odds:lines:mlb",
            StartedAt = EarlierThisMonth(TimeSpan.FromHours(1)),
            FinishedAt = EarlierThisMonth(TimeSpan.FromHours(1)),
            Status = RunStatus.Success,
            CreditsSpent = 2,
            ProviderCreditsRemaining = 150
        });
        await db.SaveChangesAsync();

        var plan = await Create(db).PlanAsync([source.Key]);

        // Spent is 500 - 150 = 350, not 2: 500 - 100 reserve - 350 = 50 credits, so 25 scans.
        Assert.Equal(25, plan!.ScansRemainingToday);
    }

    /// <summary>The allowance resets with the month; last month's remainder says nothing about this one.</summary>
    [Fact]
    public async Task ARemainderReportedLastMonthIsNotCountedAgainstThisOne()
    {
        await using var db = fixture.CreateContext();
        var source = await SeedSourceAsync(db, monthlyCredits: 500);

        var now = DateTimeOffset.UtcNow;
        var lastMonth = MonthlyCredits.MonthStart(now).AddDays(-2);

        db.IngestionRuns.Add(new IngestionRun
        {
            SourceId = source.Id,
            JobKey = "odds:lines:mlb",
            StartedAt = lastMonth,
            FinishedAt = lastMonth,
            Status = RunStatus.Success,
            CreditsSpent = 2,
            ProviderCreditsRemaining = 10
        });
        await db.SaveChangesAsync();

        var plan = await Create(db).PlanAsync([source.Key]);

        Assert.Equal(200, plan!.ScansRemainingToday);
    }

    [Theory]
    [InlineData(40, 500, 150, 350)]   // another machine spent more than this one recorded
    [InlineData(40, 500, 480, 40)]    // this machine's own record is the larger
    [InlineData(40, 500, null, 40)]   // the provider never said
    [InlineData(40, null, 150, 40)]   // no monthly budget to subtract from
    [InlineData(40, 500, 600, 40)]    // a bigger plan than configured: never below what was recorded
    public void TheMonthIsTheLargerOfTheTwoAccounts(int recorded, int? budget, int? remaining, int expected)
        => Assert.Equal(expected, MonthlyCredits.Reconcile(recorded, budget, remaining));

    [Fact]
    public async Task AnExhaustedMonthBacksOffRatherThanRetryingIntoARefusal()
    {
        await using var db = fixture.CreateContext();
        var source = await SeedSourceAsync(db, monthlyCredits: 500);

        db.IngestionRuns.Add(new IngestionRun
        {
            SourceId = source.Id,
            JobKey = "odds:lines:mlb",
            StartedAt = EarlierThisMonth(TimeSpan.FromDays(1)),
            FinishedAt = EarlierThisMonth(TimeSpan.FromDays(1)),
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
        var monthly = TimeSpan.FromHours(PacedHoursThisMonth(200));

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

    /// <summary>A credit-billed source that declares its own price per sport, as the adapters now do.</summary>
    private sealed class PricedSource(string key, Dictionary<string, int> credits) : XLines.Core.Contracts.IOddsSource
    {
        public string Key => key;
        public IReadOnlyList<string> SupportedMarkets => ["h2h", "spread"];
        public int CreditsPerScan(string sportKey) => credits.GetValueOrDefault(sportKey);

        public Task<XLines.Core.Contracts.OddsFetchResult> FetchSlateAsync(
            string sportKey, IReadOnlyList<string> markets, CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_scan_costs_what_the_adapter_says_its_markets_cost_not_a_configured_guess()
    {
        await using var db = fixture.CreateContext();
        var source = await SeedSourceAsync(db, monthlyCredits: 500);

        // Totals added to NFL: three markets there, two for MLB — five credits a scan. The old
        // configured two-per-sport would have said four, and paced a month on the wrong price.
        string mlb = NewKey(), nfl = NewKey();
        var registry = new SourceRegistry([new PricedSource(source.Key, new() { [mlb] = 2, [nfl] = 3 })], []);
        var options = new IngestionOptions { Sports = [mlb, nfl] };

        var planner = new LinePollPlanner(db, Options.Create(options), NullLogger<LinePollPlanner>.Instance, registry);
        var plan = await planner.PlanAsync([source.Key]);

        Assert.Equal(5, plan!.CreditsPerScan);
        Assert.Equal(400 / 5, plan.ScansRemainingToday);

        var markets = planner.MarketsBySport([source.Key]);
        Assert.Equal([2, 3], markets.Select(m => m.Credits));
    }

    /// <summary>
    /// One league with a game two hours out, one with nothing in the window. The scheduler used to
    /// scan both whenever either had a game near, and price the month as if it always would.
    /// </summary>
    [Fact]
    public async Task A_league_with_nothing_in_the_window_is_neither_scanned_nor_costed()
    {
        await using var db = fixture.CreateContext();
        var busy = await SeedGameAsync(db, 2);
        var quiet = NewKey();
        var source = await SeedSourceAsync(db, monthlyCredits: 500);

        var plan = await Create(db, new IngestionOptions { Sports = [busy.Key, quiet] }).PlanAsync([source.Key]);

        Assert.Equal([busy.Key], plan!.Leagues.Select(l => l.Sport));

        // One league at two credits, not two: 400 usable credits buy 200 scans of it, not 100 of both.
        Assert.Equal(2, plan.CreditsPerScan);
        Assert.Equal(2, plan.CostPerScan);
        Assert.Equal(200, plan.ScansRemainingToday);

        Assert.Equal([busy.Key], LinePacing.DueLeagues(plan.Leagues, new Dictionary<string, DateTimeOffset>(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Two_leagues_keep_their_own_clocks_from_their_own_next_starts()
    {
        await using var db = fixture.CreateContext();
        var soon = await SeedGameAsync(db, 1);
        var later = await SeedGameAsync(db, 30);

        // Paced by the day, so the arithmetic does not move with the date: 500 less the 50 reserve,
        // at two leagues of two requests a scan, is 112 scans — one every 24/112 hours.
        var source = await SeedSourceAsync(db, perDay: 500);

        var plan = await Create(db, new IngestionOptions
        {
            Sports = [soon.Key, later.Key],
            MovementWindow = TimeSpan.FromHours(48)
        }).PlanAsync([source.Key]);

        var budget = TimeSpan.FromHours(24d / 112);
        Assert.Equal(budget.TotalHours, plan!.BudgetInterval.TotalHours, precision: 3);

        var first = plan.Leagues.Single(l => l.Sport == soon.Key);
        var second = plan.Leagues.Single(l => l.Sport == later.Key);

        // An hour from first pitch: 0.35 of thirteen minutes, held to the five-minute floor. A day
        // and more out: four times it. One shared multiplier would have scanned both at the floor.
        Assert.Equal(0.35, first.Urgency, precision: 2);
        Assert.Equal(TimeSpan.FromMinutes(5), first.Interval);
        Assert.Equal(4, second.Urgency, precision: 2);
        Assert.Equal((budget * 4).TotalMinutes, second.Interval.TotalMinutes, precision: 2);

        // The headline is the soonest league's, which is the scan that comes next.
        Assert.Equal(soon.Key, plan.Leagues[0].Sport);
        Assert.Equal(first.Interval, plan.Interval);
        Assert.Equal(first.Urgency, plan.Urgency);

        // Each league is due on its own last scan: the one scanned ten minutes ago and an hour from
        // first pitch is due again; the one a day out is not.
        var now = DateTimeOffset.UtcNow;
        var last = new Dictionary<string, DateTimeOffset> { [soon.Key] = now.AddMinutes(-10), [later.Key] = now.AddMinutes(-10) };

        Assert.Equal([soon.Key], LinePacing.DueLeagues(plan.Leagues, last, now));
    }

    private static IngestionOptions Closing(ClosingScanScope scope, params string[] sports) => new()
    {
        Sports = sports,
        LinePolling = new LinePollingOptions
        {
            ClosingScan = new ClosingScanOptions { Scope = scope, Lead = TimeSpan.FromMinutes(10), MaxPerLeaguePerDay = 3 }
        }
    };

    private static readonly IReadOnlyDictionary<string, DateTimeOffset> NeverScanned = new Dictionary<string, DateTimeOffset>();

    [Fact]
    public async Task A_start_inside_the_lead_with_no_scan_since_the_lead_began_is_owed_its_closing_scan()
    {
        await using var db = fixture.CreateContext();
        var sport = await SeedSportAsync(db);
        await SeedGameAsync(db, sport, 5 / 60d);

        var planner = Create(db, Closing(ClosingScanScope.All, sport.Key));
        var now = DateTimeOffset.UtcNow;

        // Never scanned, or last scanned twenty minutes ago — before the lead began: owed.
        Assert.Equal([sport.Key], (await planner.ClosingScansDueAsync(now, NeverScanned)).Select(c => c.Sport));
        Assert.Single(await planner.ClosingScansDueAsync(now, new Dictionary<string, DateTimeOffset> { [sport.Key] = now.AddMinutes(-20) }));

        // Scanned two minutes ago, inside the lead: that scan already priced the close.
        Assert.Empty(await planner.ClosingScansDueAsync(now, new Dictionary<string, DateTimeOffset> { [sport.Key] = now.AddMinutes(-2) }));
    }

    [Fact]
    public async Task A_start_beyond_the_lead_is_not_owed_one_yet()
    {
        await using var db = fixture.CreateContext();
        var sport = await SeedSportAsync(db);
        await SeedGameAsync(db, sport, 0.5);

        var planner = Create(db, Closing(ClosingScanScope.All, sport.Key));

        Assert.Empty(await planner.ClosingScansDueAsync(DateTimeOffset.UtcNow, NeverScanned));
    }

    [Fact]
    public async Task Wagered_closes_only_games_with_an_unsettled_entry_and_All_closes_every_game()
    {
        await using var db = fixture.CreateContext();
        var bet = await SeedSportAsync(db);
        var leg = await SeedSportAsync(db);
        var settled = await SeedSportAsync(db);
        var unbet = await SeedSportAsync(db);

        var betGame = await SeedGameAsync(db, bet, 5 / 60d);
        var legGame = await SeedGameAsync(db, leg, 5 / 60d);
        var settledGame = await SeedGameAsync(db, settled, 5 / 60d);
        await SeedGameAsync(db, unbet, 5 / 60d);

        // A straight bet pending, a parlay leg pending, and a bet already graded (a void, say).
        var parlay = new Parlay { Book = "draftkings", Stake = 10, PlacedAt = DateTimeOffset.UtcNow };
        db.Parlays.Add(parlay);

        db.JournalEntries.AddRange(
            new JournalEntry { GameId = betGame.Id, Market = "h2h", Outcome = "home", Book = "draftkings", PriceTaken = -110, Stake = 10, PlacedAt = DateTimeOffset.UtcNow },
            new JournalEntry { GameId = legGame.Id, Market = "h2h", Outcome = "away", Book = "draftkings", PriceTaken = 120, PlacedAt = DateTimeOffset.UtcNow, ParlayGroupId = parlay.Id },
            new JournalEntry { GameId = settledGame.Id, Market = "h2h", Outcome = "home", Book = "draftkings", PriceTaken = -110, Stake = 10, PlacedAt = DateTimeOffset.UtcNow, Result = EntryResult.Void });
        await db.SaveChangesAsync();

        var sports = new[] { bet.Key, leg.Key, settled.Key, unbet.Key };
        var now = DateTimeOffset.UtcNow;

        var wagered = await Create(db, Closing(ClosingScanScope.Wagered, sports)).ClosingScansDueAsync(now, NeverScanned);
        Assert.Equal(new[] { bet.Key, leg.Key }.Order(), wagered.Select(c => c.Sport).Order());

        var all = await Create(db, Closing(ClosingScanScope.All, sports)).ClosingScansDueAsync(now, NeverScanned);
        Assert.Equal(sports.Order(), all.Select(c => c.Sport).Order());
    }

    [Fact]
    public async Task A_league_past_its_daily_cap_gets_no_more_closing_scans()
    {
        await using var db = fixture.CreateContext();
        var sport = await SeedSportAsync(db);
        var game = await SeedGameAsync(db, sport, 5 / 60d);

        var source = await SeedSourceAsync(db, monthlyCredits: 500);
        var options = Closing(ClosingScanScope.All, sport.Key);
        var day = XLines.Core.Analytics.LeagueClock.DateOf(game.StartsAt);

        db.IngestionRuns.AddRange(ClosingRun(source, sport.Key, day), ClosingRun(source, sport.Key, day));
        await db.SaveChangesAsync();
        Assert.Single(await Create(db, options).ClosingScansDueAsync(DateTimeOffset.UtcNow, NeverScanned));

        // The third is the cap: a slate of staggered starts would otherwise ask all evening.
        db.IngestionRuns.Add(ClosingRun(source, sport.Key, day, RunStatus.Failed));
        await db.SaveChangesAsync();

        // A restart is a fresh host with nothing remembered: the count is the rows, so it holds.
        await using var restarted = fixture.CreateContext();
        var planner = Create(restarted, options);

        Assert.Equal(3, await planner.ClosingScansTakenAsync(sport.Key, day));
        Assert.Empty(await planner.ClosingScansDueAsync(DateTimeOffset.UtcNow, NeverScanned));

        // Another league day is counted on its own.
        Assert.Equal(0, await planner.ClosingScansTakenAsync(sport.Key, day.AddDays(1)));
    }

    /// <summary>
    /// Only a league's own closing scans that day use up its cap: a paced scan or a press the same
    /// day, another league's closing scans, and the day before's are all someone else's count.
    /// </summary>
    [Fact]
    public async Task Only_the_leagues_own_closing_scans_that_day_count_against_its_cap()
    {
        await using var db = fixture.CreateContext();
        var sport = await SeedSportAsync(db);
        var other = await SeedSportAsync(db);
        var game = await SeedGameAsync(db, sport, 5 / 60d);

        var source = await SeedSourceAsync(db, monthlyCredits: 500);
        var day = XLines.Core.Analytics.LeagueClock.DateOf(game.StartsAt);

        IngestionRun Paced()
        {
            var run = ClosingRun(source, sport.Key, day);
            run.ClosingScanDay = null;
            return run;
        }

        db.IngestionRuns.AddRange(
            Paced(), Paced(), Paced(),
            ClosingRun(source, other.Key, day), ClosingRun(source, other.Key, day), ClosingRun(source, other.Key, day),
            ClosingRun(source, sport.Key, day.AddDays(-1)), ClosingRun(source, sport.Key, day.AddDays(-1)), ClosingRun(source, sport.Key, day.AddDays(-1)));
        await db.SaveChangesAsync();

        var planner = Create(db, Closing(ClosingScanScope.All, sport.Key, other.Key));

        Assert.Equal(0, await planner.ClosingScansTakenAsync(sport.Key, day));
        Assert.Equal(3, await planner.ClosingScansTakenAsync(other.Key, day));
        Assert.Equal([sport.Key], (await planner.ClosingScansDueAsync(DateTimeOffset.UtcNow, NeverScanned)).Select(c => c.Sport));
    }

    /// <summary>
    /// One closing scan writes a row a source. Counting rows would let a second odds source halve
    /// the cap, so what counts is the most any one source recorded.
    /// </summary>
    [Fact]
    public async Task One_closing_scan_across_two_sources_counts_once()
    {
        await using var db = fixture.CreateContext();
        var sport = await SeedSportAsync(db);
        var first = await SeedSourceAsync(db, monthlyCredits: 500);
        var second = await SeedSourceAsync(db, monthlyCredits: 500);
        var day = XLines.Core.Analytics.LeagueClock.DateOf(DateTimeOffset.UtcNow);

        db.IngestionRuns.AddRange(
            ClosingRun(first, sport.Key, day), ClosingRun(second, sport.Key, day),
            ClosingRun(first, sport.Key, day));
        await db.SaveChangesAsync();

        Assert.Equal(2, await Create(db, Closing(ClosingScanScope.All, sport.Key)).ClosingScansTakenAsync(sport.Key, day));
    }

    /// <summary>A closing scan's run row, as <see cref="OddsIngestionService"/> leaves one.</summary>
    private static IngestionRun ClosingRun(Source source, string sport, DateOnly day, RunStatus status = RunStatus.Success) => new()
    {
        SourceId = source.Id,
        JobKey = IngestionJobs.OddsLinesPrefix + sport,
        StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
        FinishedAt = DateTimeOffset.UtcNow.AddHours(-1),
        Status = status,
        CreditsSpent = 2,
        ClosingScanDay = day
    };

    [Fact]
    public async Task Closing_scans_switched_off_are_never_owed()
    {
        await using var db = fixture.CreateContext();
        var sport = await SeedSportAsync(db);
        await SeedGameAsync(db, sport, 5 / 60d);

        var options = Closing(ClosingScanScope.All, sport.Key);
        options.LinePolling.ClosingScan.Enabled = false;

        Assert.Empty(await Create(db, options).ClosingScansDueAsync(DateTimeOffset.UtcNow, NeverScanned));
    }

    /// <summary>
    /// A restarted host knows each league's last scan from the runs table, so its first tick does
    /// not buy a scan of every league. Before, the last scan started empty and the first tick paid.
    /// </summary>
    [Fact]
    public async Task A_restart_takes_each_leagues_last_scan_from_the_runs_it_left()
    {
        await using var db = fixture.CreateContext();
        var source = await SeedSourceAsync(db, monthlyCredits: 500);
        string own = NewKey(), shared = NewKey();
        var now = DateTimeOffset.UtcNow;

        IngestionRun Run(string job, DateTimeOffset at) => new()
        {
            SourceId = source.Id,
            JobKey = job,
            StartedAt = at,
            FinishedAt = at,
            Status = RunStatus.Success,
            CreditsSpent = 2
        };

        // A league's own job is its alone. The all-league pull counts for every league, and other
        // suites write those rows too, so what is asserted is a floor rather than an exact time.
        db.IngestionRuns.AddRange(
            Run(IngestionJobs.OddsLinesPrefix + own, now.AddMinutes(-10)),
            Run(IngestionJobs.OddsLinesPrefix + shared, now.AddHours(-5)));
        await db.SaveChangesAsync();

        var last = await Create(db, new IngestionOptions { Sports = [own, shared] }).LastScansAsync();

        Assert.True(last[own] >= now.AddMinutes(-10).AddSeconds(-1), $"Expected the league's own run; got {last[own]}.");
        Assert.True(last[shared] >= now.AddHours(-5).AddSeconds(-1), $"Expected at least the league's own run; got {last[shared]}.");

        // A league scanned ten minutes ago on a thirty-minute cadence is not due after a restart.
        var plans = new[] { new LeaguePollPlan(own, TimeSpan.FromMinutes(30), 0.35, 2, now.AddHours(1)) };
        Assert.Empty(LinePacing.DueLeagues(plans, last, now));
    }

    [Fact]
    public void The_all_league_pull_counts_as_a_scan_of_every_league()
    {
        var now = DateTimeOffset.UtcNow;

        var last = LinePacing.LastScans(["mlb", "nfl", "nba"], new Dictionary<string, DateTimeOffset>
        {
            [IngestionJobs.OddsLines] = now.AddHours(-1),
            [IngestionJobs.OddsLinesPrefix + "mlb"] = now.AddMinutes(-5),
            [IngestionJobs.OddsLinesPrefix + "nfl"] = now.AddHours(-3)
        });

        Assert.Equal(now.AddMinutes(-5), last["mlb"]);
        Assert.Equal(now.AddHours(-1), last["nfl"]);
        Assert.Equal(now.AddHours(-1), last["nba"]);
    }

    [Theory]
    [InlineData(4, true)]      // inside the lead, never scanned
    [InlineData(15, false)]    // not yet inside it
    [InlineData(-1, false)]    // already started: the close is taken, not bought
    public void A_closing_scan_is_owed_only_inside_the_lead(int minutesOut, bool owed)
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(owed, LinePacing.ClosingScanDue(now.AddMinutes(minutesOut), null, TimeSpan.FromMinutes(10), now));
    }
}
