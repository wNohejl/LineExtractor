using XLines.Core.Entities;
using XLines.Data.Placement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace XLines.Data;

/// <summary>The initializer's one setting: <see cref="MigrationPlacement.AllowRemoteMigrationKey"/>.</summary>
public sealed record MigrationPlacementOptions(bool AllowRemoteMigration);

/// <summary>
/// Applies migrations, keeps partitions ahead of the clock, and seeds reference rows.
/// Safe to run on every start: each step is idempotent.
///
/// <para>
/// Only on this machine's own database. Against another machine's (ADR 0023) it migrates
/// nothing, seeds nothing and cleans nothing: it checks that the schema is the one this code was
/// built for, and refuses with <see cref="RemoteDatabaseRefusedException"/> when it is not
/// (<see cref="MigrationPlacement"/>).
/// </para>
/// </summary>
public class DatabaseInitializer(XLinesDbContext db, ILogger<DatabaseInitializer> logger,
    MigrationPlacementOptions? placement = null)
{
    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        var connectionString = db.Database.GetConnectionString();
        var isOwn = DatabasePlacement.IsOwnDatabase(connectionString);

        // A local database is migrated without asking it anything first, exactly as before.
        if (!isOwn)
        {
            var host = DatabasePlacement.DescribeHost(connectionString);
            // An empty database has no history table, and asking it for one logs a failed command.
            // The catalog is asked directly: checking through EF's history repository still logged it.
            var hasHistory = await db.Database
                .SqlQuery<bool>($"SELECT to_regclass('\"__EFMigrationsHistory\"') IS NOT NULL AS \"Value\"")
                .SingleAsync(ct);
            IEnumerable<string> applied = hasHistory
                ? await db.Database.GetAppliedMigrationsAsync(ct)
                : [];
            var decision = MigrationPlacement.Decide(isOwn,
                applied,
                db.Database.GetMigrations(),
                placement?.AllowRemoteMigration ?? false,
                host);

            switch (decision.Action)
            {
                case MigrationAction.Refuse:
                    throw new RemoteDatabaseRefusedException(decision.Message!);

                case MigrationAction.Proceed:
                    // The owner's startup migrates, seeds and cleans. Partitions are the one step
                    // kept: every pull ensures them anyway, and a month boundary must not fail an
                    // insert pressed from this desk.
                    logger.LogInformation(
                        "The database at {Host} is not this machine's own and its schema matches this code: "
                        + "using it as it is, with no migration and no seed", host);
                    await EnsurePartitionsAsync(ct);
                    return;

                default:
                    logger.LogWarning(
                        "The database at {Host} is not this machine's own; migrating it anyway because {Key}=true",
                        host, MigrationPlacement.AllowRemoteMigrationKey);
                    break;
            }
        }

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
    /// rather than for a startup path. <c>scripts/sql/purge-demo-data.sql</c> does that part.
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
        await db.GameOfficials.Where(x => ids.Contains(x.SourceId)).ExecuteDeleteAsync(ct);
        await db.GameContexts.Where(x => ids.Contains(x.SourceId)).ExecuteDeleteAsync(ct);
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
            ("nhl", "NHL"),
            // Fought, not played: a bout is a game between two fighters (ADR 0021).
            ("ufc", "UFC")
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
            // The same account's events and scores, as the default stats source when ESPN is off
            // (ADR 0026). Its own row so its runs are its own in source health; the same 500
            // credits, which MonthlyCredits counts across both rows (SourceAccounts). Seeded off:
            // ReferenceReconciler turns it on only on a host that registers it (ESPN off, a key
            // held), so a desk running on ESPN never lists it in source health or alerts.
            new Source
            {
                Key = SourceAccounts.TheOddsApiSchedule,
                Name = "The Odds API (fixtures and scores)",
                Kind = SourceKind.Stats,
                BaseUrl = "https://api.the-odds-api.com/v4/",
                MonthlyCreditBudget = 500,
                Enabled = false
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

        // Each provider's terms, from one table (ADR 0024). Set here rather than on each row above
        // so the seed and the migration that classified existing rows cannot disagree.
        foreach (var source in wanted)
            source.Licence = SourceLicences.For(source.Key);

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
