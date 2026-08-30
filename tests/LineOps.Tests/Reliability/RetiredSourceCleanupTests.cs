using LineOps.Core.Entities;
using LineOps.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LineOps.Tests.Reliability;

/// <summary>
/// The demo sources are gone from the codebase; these pin that they also leave the database.
///
/// <para>
/// Deleting an adapter stops new rows. It does nothing to the ones already written, and the
/// reliability layer reads its subjects from the <c>Sources</c> table — so a clone seeded before
/// the removal went on reporting a "Demo stats fixture" going stale, and spent its one critical
/// alert slot nagging about a feed that no longer existed. The cleanup runs on every start, so an
/// existing database heals itself rather than waiting for someone to run a script.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class RetiredSourceCleanupTests(PostgresFixture fixture)
{
    private DatabaseInitializer Create(LineOpsDbContext db)
        => new(db, NullLogger<DatabaseInitializer>.Instance);

    private static IngestionRun Run(int sourceId, DateTimeOffset at)
        => new()
        {
            SourceId = sourceId,
            JobKey = "odds:slate",
            StartedAt = at,
            FinishedAt = at.AddSeconds(1),
            Status = RunStatus.Success,
            RowsIngested = 12
        };

    /// <summary>Re-seeds the demo rows exactly as an older build left them.</summary>
    private static async Task<(Source Odds, Source Stats)> SeedRetiredAsync(LineOpsDbContext db)
    {
        var odds = new Source
        {
            Key = "demo",
            Name = "Demo fixture source",
            Kind = SourceKind.Odds,
            BaseUrl = "local://demo",
            Enabled = true
        };

        var stats = new Source
        {
            Key = "demo-stats",
            Name = "Demo stats fixture",
            Kind = SourceKind.Stats,
            BaseUrl = "local://demo-stats",
            Enabled = true
        };

        db.Sources.AddRange(odds, stats);
        await db.SaveChangesAsync();
        return (odds, stats);
    }

    [Fact]
    public async Task TheDemoSourcesAndEverythingRecordedAgainstThemAreRemoved()
    {
        await using var db = fixture.CreateContext();
        var (odds, stats) = await SeedRetiredAsync(db);

        var stale = DateTimeOffset.UtcNow.AddDays(-35);

        db.IngestionRuns.AddRange(Run(odds.Id, stale), Run(stats.Id, stale));

        db.KpiDailies.Add(new KpiDaily
        {
            Day = DateOnly.FromDateTime(stale.UtcDateTime),
            SourceId = stats.Id,
            FreshnessMinutes = 50_000,
            SuccessRate = 1,
            RunCount = 1
        });

        // The alert from the screenshot: one critical slot spent on a fixture feed.
        db.Alerts.Add(new Alert
        {
            RuleKey = "freshness",
            SourceId = stats.Id,
            Severity = AlertSeverity.Critical,
            Message = "Demo stats fixture: no successful ingestion for 842.4h (SLO 26h).",
            TriggeredAt = stale
        });

        await db.SaveChangesAsync();

        await Create(db).InitialiseAsync();

        Assert.False(await db.Sources.AnyAsync(s => s.Key == "demo" || s.Key == "demo-stats"));
        Assert.False(await db.IngestionRuns.AnyAsync(r => r.SourceId == odds.Id || r.SourceId == stats.Id));
        Assert.False(await db.KpiDailies.AnyAsync(k => k.SourceId == stats.Id));

        // The stale critical goes with the source that raised it. Resolving it instead would
        // leave a resolved alert pointing at a source id nothing can explain.
        Assert.False(await db.Alerts.AnyAsync(a => a.SourceId == stats.Id));
    }

    [Fact]
    public async Task TheRealSourcesAndTheirHistoryAreLeftAlone()
    {
        await using var db = fixture.CreateContext();

        await Create(db).InitialiseAsync();

        var espn = await db.Sources.SingleAsync(s => s.Key == "espn");
        db.IngestionRuns.Add(Run(espn.Id, DateTimeOffset.UtcNow.AddHours(-2)));
        await db.SaveChangesAsync();

        // A second start must not be a data-loss event for anything that is still a source.
        await Create(db).InitialiseAsync();

        Assert.True(await db.IngestionRuns.AnyAsync(r => r.SourceId == espn.Id));
        Assert.True(await db.Sources.AnyAsync(s => s.Key == "espn"));
        Assert.True(await db.Sources.AnyAsync(s => s.Key == "odds-api-io"));
    }

    [Fact]
    public async Task SeedingNeverPutsTheDemoSourcesBack()
    {
        await using var db = fixture.CreateContext();

        await Create(db).InitialiseAsync();
        await Create(db).InitialiseAsync();

        // The cleanup would be pointless if the seed re-added on the next start what it had
        // just deleted — the pair would fight, once per boot, forever.
        Assert.Empty(await db.Sources.Where(s => s.Key.StartsWith("demo")).ToListAsync());
    }
}
