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
            },
            new Source
            {
                Key = "demo",
                Name = "Demo fixture source",
                Kind = SourceKind.Odds,
                BaseUrl = "local://demo",
                Enabled = true
            },
            new Source
            {
                Key = "demo-stats",
                Name = "Demo stats fixture",
                Kind = SourceKind.Stats,
                BaseUrl = "local://demo-stats",
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
