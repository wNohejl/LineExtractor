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
        await RemoveStrandedIncidentsAsync(ct);
    }

    /// <summary>
    /// Removes incidents that have lost both their subject and their author.
    ///
    /// <para>
    /// An incident is only ever created by promoting an alert, and promotion attaches that alert
    /// to it. So an incident with no alerts is one whose alert was deleted — which happens when
    /// the source that raised it is retired. Nothing is left to read to find out what it was
    /// about. If nobody wrote it up either, it is not evidence of anything: it cannot be
    /// explained, and it cannot be closed honestly, because closing one requires a root cause
    /// and there is no truthful cause to give for a feed that no longer exists.
    /// </para>
    ///
    /// <para>
    /// This is deliberately keyed on what the incident is left with rather than on which source
    /// it named. A database cleaned by an earlier build has neither the source nor its alerts,
    /// so there is nothing left to match a name against — and those are exactly the rows the Ops
    /// panel was still counting. An incident that still has its alert stays whether or not it
    /// has been written up: it can still be read, so it can still be answered.
    /// </para>
    /// </summary>
    private async Task RemoveStrandedIncidentsAsync(CancellationToken ct)
    {
        var stranded = await db.Incidents
            .Where(i => (i.RootCause == null || i.RootCause == string.Empty)
                        && !db.Alerts.Any(a => a.IncidentId == i.Id))
            .ExecuteDeleteAsync(ct);

        if (stranded > 0)
            logger.LogInformation(
                "Removed {Count} incident(s) left with no alert and no root cause", stranded);
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
    /// An incident survives only if somebody wrote it up. The rule used to be "incidents stay",
    /// on the grounds that an incident carries a root-cause analysis and the analysis is the
    /// point of it — true of one a person closed, and false of one the evaluator opened
    /// automatically and nobody ever touched. Two of those outlived the demo removal: still
    /// open, still counted on the Ops panel, and unresolvable, because closing an incident
    /// requires a root cause and there is no honest one to write about a source that no longer
    /// exists. So the line is the write-up rather than the incident. An analysis is a record a
    /// person made and it stays; an empty auto-opened shell is platform bookkeeping about a feed
    /// that is gone, and it goes with the rest of the bookkeeping.
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
        // The incidents they belonged to are swept separately, by what they are left with
        // rather than by which source they named.
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
