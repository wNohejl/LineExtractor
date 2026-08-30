using LineOps.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LineOps.Data;

/// <summary>
/// Applies migrations, keeps partitions ahead of the clock, and seeds reference rows.
/// Safe to run on every start: each step is idempotent.
/// </summary>
public class DatabaseInitializer(LineOpsDbContext db, ILogger<DatabaseInitializer> logger)
{
    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);
        await EnsurePartitionsAsync(ct);
        await SeedSportsAsync(ct);
        await SeedSourcesAsync(ct);
        await RemoveRetiredSourcesAsync(ct);
    }

    /// <summary>
    /// Source keys that were seeded once and are no longer part of the platform.
    ///
    /// The two demo fixtures are the only members and are expected to stay the only ones: they
    /// fabricated prices and rosters so a cold clone had something to show, and the platform now
    /// runs on real feeds only.
    /// </summary>
    private static readonly string[] RetiredSourceKeys = ["demo", "demo-stats"];

    /// <summary>
    /// Removes a retired source and everything recorded against it.
    ///
    /// <para>
    /// Deleting the code stops new rows; it does not remove the ones already written. A database
    /// seeded before this change still holds the demo <c>Sources</c> rows, and the reliability
    /// layer reads its subjects from that table — so an existing clone would go on reporting a
    /// "Demo stats fixture" source going stale, and spend a critical alert slot on a feed that no
    /// longer exists. That is the opposite of what the reliability layer is for.
    /// </para>
    ///
    /// <para>
    /// Idempotent and cheap: on a database that has already been cleaned, or a fresh one that
    /// never had the rows, this is a handful of deletes matching nothing. Ordered so no foreign
    /// key is left pointing at a row that has already gone. Fabricated prices and stat lines go
    /// with the source, because they were never real. Fixtures the demo source invented before
    /// it was taught to quote the real schedule are <i>not</i> touched here — removing a game can
    /// take a journal entry with it, and deleting what someone recorded is a decision for them
    /// rather than for a startup path. <c>scripts/purge-demo-data.sql</c> does that part.
    /// </para>
    ///
    /// <para>
    /// Incidents survive for the same reason: an incident carries a written root-cause analysis,
    /// and the analysis is the point of it. Its alerts go, so nothing is still <i>open</i> about
    /// a source that no longer exists, but the write-up stays where its author left it.
    /// </para>
    /// </summary>
    private async Task RemoveRetiredSourcesAsync(CancellationToken ct)
    {
        var ids = await db.Sources
            .Where(s => RetiredSourceKeys.Contains(s.Key))
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (ids.Count == 0)
            return;

        // Children first, then the source itself.
        await db.ClosingLines.Where(x => ids.Contains(x.SourceId)).ExecuteDeleteAsync(ct);
        await db.OddsSnapshots.Where(x => ids.Contains(x.SourceId)).ExecuteDeleteAsync(ct);
        await db.PlayerGameStats.Where(x => ids.Contains(x.SourceId)).ExecuteDeleteAsync(ct);
        await db.StatSnapshots.Where(x => ids.Contains(x.SourceId)).ExecuteDeleteAsync(ct);
        await db.BackfillCheckpoints.Where(x => ids.Contains(x.SourceId)).ExecuteDeleteAsync(ct);
        await db.IngestionRuns.Where(x => ids.Contains(x.SourceId)).ExecuteDeleteAsync(ct);
        await db.KpiDailies.Where(x => ids.Contains(x.SourceId)).ExecuteDeleteAsync(ct);

        // The alerts go with the source that raised them — including the open critical one
        // nagging that a fixture feed has not run. There is nothing left to page anyone about.
        await db.Alerts.Where(x => x.SourceId != null && ids.Contains(x.SourceId.Value))
            .ExecuteDeleteAsync(ct);

        var removed = await db.Sources.Where(s => ids.Contains(s.Id)).ExecuteDeleteAsync(ct);

        logger.LogInformation("Removed {Count} retired source(s) and their recorded history", removed);
    }

    /// <summary>
    /// Guarantees a partition exists for this month and the next two. Called at startup and
    /// before each ingestion run, so crossing a month boundary is never an insert failure.
    /// </summary>
    public async Task EnsurePartitionsAsync(CancellationToken ct = default)
    {
        for (var offset = 0; offset <= 2; offset++)
        {
            var target = DateTimeOffset.UtcNow.AddMonths(offset);
            await db.Database.ExecuteSqlRawAsync(
                "SELECT lineops_ensure_odds_partition({0})", [target], ct);
        }
    }

    private async Task SeedSportsAsync(CancellationToken ct)
    {
        var wanted = new (string Key, string Name)[]
        {
            ("nfl", "NFL"),
            ("nba", "NBA"),
            ("mlb", "MLB"),
            ("nhl", "NHL")
        };

        var existing = await db.Sports.Select(s => s.Key).ToListAsync(ct);

        foreach (var (key, name) in wanted.Where(w => !existing.Contains(w.Key)))
            db.Sports.Add(new Sport { Key = key, Name = name });

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded sports");
        }
    }

    /// <summary>
    /// Registers the providers with their published free-tier ceilings. These numbers are the
    /// contract the budget guard enforces — they are what keeps running costs at zero.
    /// </summary>
    private async Task SeedSourcesAsync(CancellationToken ct)
    {
        var wanted = new[]
        {
            new Source
            {
                Key = "odds-api-io",
                Name = "odds-api.io",
                Kind = SourceKind.Odds,
                BaseUrl = "https://api.odds-api.io/v3/",
                RateLimitPerHour = 100,
                RateLimitPerDay = 500,
                Enabled = true
            },
            new Source
            {
                Key = "the-odds-api",
                Name = "The Odds API",
                Kind = SourceKind.Odds,
                BaseUrl = "https://api.the-odds-api.com/v4/",
                MonthlyCreditBudget = 500,
                Enabled = true
            },
            new Source
            {
                Key = "balldontlie",
                Name = "BALLDONTLIE",
                Kind = SourceKind.Stats,
                BaseUrl = "https://api.balldontlie.io/v1/",
                RateLimitPerHour = 300,
                Enabled = true
            },
            new Source
            {
                Key = "espn",
                Name = "ESPN (unofficial)",
                Kind = SourceKind.Stats,
                BaseUrl = "https://site.api.espn.com/apis/site/v2/sports/",
                Enabled = true
            }
        };

        var existing = await db.Sources.Select(s => s.Key).ToListAsync(ct);

        foreach (var source in wanted.Where(s => !existing.Contains(s.Key)))
            db.Sources.Add(source);

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded sources");
        }
    }
}
