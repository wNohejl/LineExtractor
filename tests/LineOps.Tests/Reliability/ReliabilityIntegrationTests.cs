using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Reliability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LineOps.Tests.Reliability;

[Collection(PostgresCollection.Name)]
public class ReliabilityIntegrationTests(PostgresFixture fixture)
{
    private static readonly ReliabilityOptions Options = new()
    {
        FreshnessSlo = TimeSpan.FromHours(26),
        SuccessRateSlo = 0.95,
        SuccessRateWindow = TimeSpan.FromDays(7),
        VolumeAnomalyThreshold = 0.5,
        VolumeBaselineDays = 7
    };

    /// <summary>Each test gets its own source so runs never bleed across cases.</summary>
    private async Task<Source> NewSourceAsync(
        LineOpsDbContext db,
        string? failureMode = null,
        int? dailyRequestLimit = null,
        int? monthlyCreditLimit = null)
    {
        var source = new Source
        {
            Key = $"test-{Guid.NewGuid():N}",
            Name = "Test source",
            Kind = SourceKind.Odds,
            BaseUrl = "local://test",
            Enabled = true,
            FailureMode = failureMode,
            RateLimitPerDay = dailyRequestLimit,
            MonthlyCreditBudget = monthlyCreditLimit
        };

        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source;
    }

    private static IngestionRun Run(int sourceId, RunStatus status, DateTimeOffset at, int rows = 10)
        => new()
        {
            SourceId = sourceId,
            JobKey = "test",
            StartedAt = at,
            FinishedAt = at.AddSeconds(1),
            Status = status,
            RowsIngested = rows
        };

    private static AlertEngine CreateEngine(LineOpsDbContext db)
        => new(db, new KpiCalculator(db), new BudgetCalculator(db),
            new OptionsWrapper<ReliabilityOptions>(Options), NullLogger<AlertEngine>.Instance);

    [Fact]
    public async Task FreshnessIsMeasuredFromTheLastSuccessfulRun()
    {
        await using var db = fixture.CreateContext();
        var source = await NewSourceAsync(db);

        db.IngestionRuns.Add(Run(source.Id, RunStatus.Success, DateTimeOffset.UtcNow.AddHours(-3)));
        // A later failure must not reset freshness — only success counts as fresh data.
        db.IngestionRuns.Add(Run(source.Id, RunStatus.Failed, DateTimeOffset.UtcNow.AddMinutes(-5)));
        await db.SaveChangesAsync();

        var kpi = new KpiCalculator(db);
        var freshness = await kpi.GetFreshnessMinutesAsync(source.Id);

        Assert.NotNull(freshness);
        Assert.InRange(freshness!.Value, 179, 181);
    }

    [Fact]
    public async Task SuccessRateCountsPartialRunsAsFailures()
    {
        await using var db = fixture.CreateContext();
        var source = await NewSourceAsync(db);

        var now = DateTimeOffset.UtcNow;
        db.IngestionRuns.Add(Run(source.Id, RunStatus.Success, now.AddHours(-4)));
        db.IngestionRuns.Add(Run(source.Id, RunStatus.Success, now.AddHours(-3)));
        db.IngestionRuns.Add(Run(source.Id, RunStatus.Partial, now.AddHours(-2)));
        db.IngestionRuns.Add(Run(source.Id, RunStatus.Failed, now.AddHours(-1)));
        await db.SaveChangesAsync();

        var (rate, count) = await new KpiCalculator(db)
            .GetSuccessRateAsync(source.Id, TimeSpan.FromDays(7));

        Assert.Equal(4, count);
        Assert.Equal(0.5, rate, precision: 6);
    }

    [Fact]
    public async Task RunningRunsAreExcludedFromTheSuccessRate()
    {
        await using var db = fixture.CreateContext();
        var source = await NewSourceAsync(db);

        db.IngestionRuns.Add(Run(source.Id, RunStatus.Success, DateTimeOffset.UtcNow.AddMinutes(-10)));
        db.IngestionRuns.Add(new IngestionRun
        {
            SourceId = source.Id,
            JobKey = "test",
            StartedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Running
        });
        await db.SaveChangesAsync();

        var (rate, count) = await new KpiCalculator(db)
            .GetSuccessRateAsync(source.Id, TimeSpan.FromDays(7));

        Assert.Equal(1, count);
        Assert.Equal(1.0, rate);
    }

    [Fact]
    public async Task ASourceThatHasNeverRunDoesNotRaiseAnAlert()
    {
        await using var db = fixture.CreateContext();
        await NewSourceAsync(db);

        var candidates = await CreateEngine(db).EvaluateAsync();

        // An unconfigured source is not an outage. Alerting on it is how alert fatigue starts.
        Assert.DoesNotContain(candidates, c => c.RuleKey == AlertRules.Freshness);
    }

    [Fact]
    public async Task AStaleSourceRaisesACriticalFreshnessAlert()
    {
        await using var db = fixture.CreateContext();
        var source = await NewSourceAsync(db);

        db.IngestionRuns.Add(Run(source.Id, RunStatus.Success, DateTimeOffset.UtcNow.AddHours(-30)));
        await db.SaveChangesAsync();

        var candidates = await CreateEngine(db).EvaluateAsync();

        var alert = Assert.Single(candidates,
            c => c.RuleKey == AlertRules.Freshness && c.SourceId == source.Id);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);

        // And it must be persisted, not merely returned.
        Assert.True(await db.Alerts.AnyAsync(
            a => a.SourceId == source.Id && a.RuleKey == AlertRules.Freshness && a.ResolvedAt == null));
    }

    [Fact]
    public async Task ARepeatedConditionDoesNotOpenASecondAlert()
    {
        await using var db = fixture.CreateContext();
        var source = await NewSourceAsync(db);

        db.IngestionRuns.Add(Run(source.Id, RunStatus.Success, DateTimeOffset.UtcNow.AddHours(-30)));
        await db.SaveChangesAsync();

        var engine = CreateEngine(db);
        await engine.EvaluateAsync();
        await engine.EvaluateAsync();
        await engine.EvaluateAsync();

        var alerts = await db.Alerts
            .Where(a => a.SourceId == source.Id && a.RuleKey == AlertRules.Freshness)
            .ToListAsync();

        Assert.Single(alerts);
    }

    [Fact]
    public async Task AnAlertAutoResolvesOnceTheConditionClears()
    {
        await using var db = fixture.CreateContext();
        var source = await NewSourceAsync(db);

        var stale = Run(source.Id, RunStatus.Success, DateTimeOffset.UtcNow.AddHours(-30));
        db.IngestionRuns.Add(stale);
        await db.SaveChangesAsync();

        var engine = CreateEngine(db);
        await engine.EvaluateAsync();
        Assert.NotEmpty(await engine.GetOpenAlertsAsync());

        // A fresh successful run clears the condition.
        db.IngestionRuns.Add(Run(source.Id, RunStatus.Success, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        await engine.EvaluateAsync();

        var open = await db.Alerts
            .Where(a => a.SourceId == source.Id && a.ResolvedAt == null)
            .ToListAsync();

        Assert.Empty(open);
    }

    [Fact]
    public async Task AVolumeCollapseIsDetectedEvenWhenEveryRunSucceeds()
    {
        await using var db = fixture.CreateContext();
        var source = await NewSourceAsync(db);

        // Seven healthy days at 100 rows, then today at 5 — the signature of an upstream
        // schema change that still returns HTTP 200.
        for (var day = 1; day <= 7; day++)
            db.IngestionRuns.Add(Run(source.Id, RunStatus.Success, DateTimeOffset.UtcNow.AddDays(-day), rows: 100));

        db.IngestionRuns.Add(Run(source.Id, RunStatus.Success, DateTimeOffset.UtcNow, rows: 5));
        await db.SaveChangesAsync();

        var ratio = await new KpiCalculator(db).GetVolumeRatioAsync(source.Id, 7);

        Assert.NotNull(ratio);
        Assert.True(ratio < Options.VolumeAnomalyThreshold, $"ratio {ratio} should breach the threshold");

        var candidates = await CreateEngine(db).EvaluateAsync();
        Assert.Contains(candidates, c => c.RuleKey == AlertRules.VolumeAnomaly && c.SourceId == source.Id);
    }

    [Fact]
    public async Task VolumeRatioIsUndefinedWithoutEnoughHistory()
    {
        await using var db = fixture.CreateContext();
        var source = await NewSourceAsync(db);

        db.IngestionRuns.Add(Run(source.Id, RunStatus.Success, DateTimeOffset.UtcNow.AddDays(-1), rows: 100));
        await db.SaveChangesAsync();

        // Two days of history cannot establish a median; guessing would produce false alerts.
        Assert.Null(await new KpiCalculator(db).GetVolumeRatioAsync(source.Id, 7));
    }

    /// <summary>
    /// A source pulled on request has no daily cadence, so it has no usual daily volume to
    /// fall short of. The numbers here are the ones that actually fired: The Odds API is
    /// Manual by default — nothing is fetched unasked — and it ran on three scattered days,
    /// which was enough to clear the three-day floor and produce a "31% of the trailing
    /// median" warning that read as upstream schema drift. It was a record of how often
    /// somebody pressed Pull lines.
    /// </summary>
    [Fact]
    public async Task VolumeRatioIsUndefinedForASourceThatDoesNotRunDaily()
    {
        await using var db = fixture.CreateContext();
        var source = await NewSourceAsync(db);

        var now = DateTimeOffset.UtcNow;
        db.IngestionRuns.Add(Run(source.Id, RunStatus.Success, now.AddDays(-6), rows: 1704));
        db.IngestionRuns.Add(Run(source.Id, RunStatus.Success, now.AddDays(-3), rows: 423));
        db.IngestionRuns.Add(Run(source.Id, RunStatus.Success, now.AddDays(-1), rows: 435));
        db.IngestionRuns.Add(Run(source.Id, RunStatus.Success, now, rows: 136));
        await db.SaveChangesAsync();

        // Three days out of seven is not a cadence. Judged against them the median is 435 and
        // today reads as 31% — the alert this test exists to stop.
        Assert.Null(await new KpiCalculator(db).GetVolumeRatioAsync(source.Id, 7));

        var candidates = await CreateEngine(db).EvaluateAsync();
        Assert.DoesNotContain(
            candidates, c => c.RuleKey == AlertRules.VolumeAnomaly && c.SourceId == source.Id);
    }

    /// <summary>A run that consumed budget. Requests are what the metered dimensions count.</summary>
    private static IngestionRun Spend(int sourceId, int requests, int credits = 0)
        => new()
        {
            SourceId = sourceId,
            JobKey = "test",
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Success,
            RowsIngested = 10,
            RequestsMade = requests,
            CreditsSpent = credits
        };

    [Fact]
    public async Task ApproachingAProvidersCeilingRaisesBudgetPressure()
    {
        await using var db = fixture.CreateContext();
        var source = await NewSourceAsync(db, dailyRequestLimit: 100);

        db.IngestionRuns.Add(Spend(source.Id, requests: 85));
        await db.SaveChangesAsync();

        var candidates = await CreateEngine(db).EvaluateAsync();

        var alert = Assert.Single(candidates,
            c => c.RuleKey == AlertRules.BudgetPressure && c.SourceId == source.Id);

        // Informational while there is still headroom — nothing has stopped working yet.
        Assert.Equal(AlertSeverity.Info, alert.Severity);

        // The runbook's first triage step asks which dimension is under pressure, so the alert
        // has to answer that from its own text.
        Assert.Contains("daily requests", alert.Message);
        Assert.Contains("85/100", alert.Message);
    }

    [Fact]
    public async Task AnExhaustedBudgetIsWarnRatherThanInfo()
    {
        await using var db = fixture.CreateContext();
        var source = await NewSourceAsync(db, dailyRequestLimit: 100);

        db.IngestionRuns.Add(Spend(source.Id, requests: 100));
        await db.SaveChangesAsync();

        var candidates = await CreateEngine(db).EvaluateAsync();
        var alert = Assert.Single(candidates,
            c => c.RuleKey == AlertRules.BudgetPressure && c.SourceId == source.Id);

        // At the ceiling the guard is refusing runs, so data has stopped arriving. That is a
        // degradation, not a heads-up.
        Assert.Equal(AlertSeverity.Warn, alert.Severity);
        Assert.Contains("refused", alert.Message);
    }

    [Fact]
    public async Task PressureEscalatesInPlaceRatherThanOpeningASecondAlert()
    {
        await using var db = fixture.CreateContext();
        var source = await NewSourceAsync(db, dailyRequestLimit: 100);

        db.IngestionRuns.Add(Spend(source.Id, requests: 85));
        await db.SaveChangesAsync();

        var engine = CreateEngine(db);
        await engine.EvaluateAsync();

        db.IngestionRuns.Add(Spend(source.Id, requests: 15));
        await db.SaveChangesAsync();
        await engine.EvaluateAsync();

        var alerts = await db.Alerts
            .Where(a => a.SourceId == source.Id && a.RuleKey == AlertRules.BudgetPressure)
            .ToListAsync();

        // One alert, escalated — not two. Severity has to follow the condition, or the feed keeps
        // reporting Info about a budget that is already spent.
        var alert = Assert.Single(alerts);
        Assert.Equal(AlertSeverity.Warn, alert.Severity);
    }

    [Fact]
    public async Task AnUnmeteredSourceNeverRaisesBudgetPressure()
    {
        await using var db = fixture.CreateContext();
        var source = await NewSourceAsync(db);

        db.IngestionRuns.Add(Spend(source.Id, requests: 5_000));
        await db.SaveChangesAsync();

        var candidates = await CreateEngine(db).EvaluateAsync();

        // No ceiling means utilisation is undefined, not zero. Treating it as 0% would report an
        // unmetered provider as comfortably healthy, which is a claim we cannot make.
        Assert.DoesNotContain(candidates,
            c => c.RuleKey == AlertRules.BudgetPressure && c.SourceId == source.Id);
    }

    [Fact]
    public async Task CreditPressureIsMeasuredSeparatelyFromRequestPressure()
    {
        await using var db = fixture.CreateContext();
        var source = await NewSourceAsync(db, dailyRequestLimit: 1_000, monthlyCreditLimit: 500);

        // Few requests, but each one cost credits — the dimension under pressure is the one the
        // provider actually bills on.
        db.IngestionRuns.Add(Spend(source.Id, requests: 10, credits: 450));
        await db.SaveChangesAsync();

        var candidates = await CreateEngine(db).EvaluateAsync();
        var alert = Assert.Single(candidates,
            c => c.RuleKey == AlertRules.BudgetPressure && c.SourceId == source.Id);

        Assert.Contains("monthly credits", alert.Message);
    }

    [Fact]
    public async Task BudgetPressureClearsOnceConsumptionRollsOff()
    {
        await using var db = fixture.CreateContext();
        var source = await NewSourceAsync(db, dailyRequestLimit: 100);

        // Spent yesterday, outside the trailing-24h window the daily ceiling counts over.
        db.IngestionRuns.Add(new IngestionRun
        {
            SourceId = source.Id,
            JobKey = "test",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-30),
            FinishedAt = DateTimeOffset.UtcNow.AddHours(-30),
            Status = RunStatus.Success,
            RequestsMade = 95
        });
        await db.SaveChangesAsync();

        var candidates = await CreateEngine(db).EvaluateAsync();

        Assert.DoesNotContain(candidates,
            c => c.RuleKey == AlertRules.BudgetPressure && c.SourceId == source.Id);
    }

    [Fact]
    public async Task DailyRollupIsIdempotent()
    {
        await using var db = fixture.CreateContext();
        var source = await NewSourceAsync(db);

        db.IngestionRuns.Add(Run(source.Id, RunStatus.Success, DateTimeOffset.UtcNow, rows: 42));
        await db.SaveChangesAsync();

        var kpi = new KpiCalculator(db);
        await kpi.RollupDailyAsync();
        await kpi.RollupDailyAsync();
        await kpi.RollupDailyAsync();

        var rows = await db.KpiDailies.Where(k => k.SourceId == source.Id).ToListAsync();

        Assert.Single(rows);
        Assert.Equal(42, rows[0].RowsIngested);
    }
}
