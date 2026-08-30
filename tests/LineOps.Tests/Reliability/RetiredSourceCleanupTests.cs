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

    /// <summary>
    /// An incident about a retired source survives only if somebody wrote it up.
    ///
    /// <para>
    /// The cleanup kept every incident on the grounds that "an incident carries a written
    /// root-cause analysis, and the analysis is the point of it". That is true of an incident
    /// somebody closed and false of one the evaluator opened automatically and nobody ever
    /// touched. Two of those were left behind by the demo removal: still Open, still counted on
    /// the Ops panel, and unresolvable — closing one requires a root cause, and there is no
    /// honest root cause to write for a source that no longer exists.
    /// </para>
    ///
    /// <para>
    /// So the line is the write-up, not the incident: an analysis is a record somebody made and
    /// it stays; an empty auto-opened shell about a deleted feed is platform bookkeeping and
    /// goes with the rest of it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AnUnwrittenIncidentAboutARetiredSourceGoesWithIt()
    {
        await using var db = fixture.CreateContext();
        var (odds, _) = await SeedRetiredAsync(db);

        var unwritten = new Incident
        {
            Title = "Demo fixture source: no successful ingestion for 721.2h",
            Severity = AlertSeverity.Critical,
            Status = IncidentStatus.Open,
            OpenedAt = DateTimeOffset.UtcNow.AddDays(-5)
        };

        var written = new Incident
        {
            Title = "Demo fixture source: success rate 44% over 18 runs",
            Severity = AlertSeverity.Warn,
            Status = IncidentStatus.Resolved,
            OpenedAt = DateTimeOffset.UtcNow.AddDays(-30),
            ResolvedAt = DateTimeOffset.UtcNow.AddDays(-29),
            RootCause = "The fixture yielded to the real provider mid-run.",
            CorrectiveActions = "Pinned the adapter selection to one kind per run."
        };

        db.Incidents.AddRange(unwritten, written);
        await db.SaveChangesAsync();

        db.Alerts.AddRange(
            new Alert
            {
                RuleKey = "freshness",
                SourceId = odds.Id,
                Severity = AlertSeverity.Critical,
                Message = "no successful ingestion",
                TriggeredAt = DateTimeOffset.UtcNow.AddDays(-5),
                IncidentId = unwritten.Id
            },
            new Alert
            {
                RuleKey = "success_rate",
                SourceId = odds.Id,
                Severity = AlertSeverity.Warn,
                Message = "success rate 44%",
                TriggeredAt = DateTimeOffset.UtcNow.AddDays(-30),
                IncidentId = written.Id
            });

        await db.SaveChangesAsync();

        await Create(db).InitialiseAsync();

        Assert.False(await db.Incidents.AnyAsync(i => i.Id == unwritten.Id));
        Assert.True(await db.Incidents.AnyAsync(i => i.Id == written.Id));
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
