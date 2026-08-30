using System.Text.Json;
using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Services;
using LineOps.Reliability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LineOps.Tests.Reliability;

/// <summary>
/// What ingestion is allowed to allocate.
///
/// The rule is that a row exists because something happened. A player who did not appear in a
/// game we can point at is not stored — not stored with zeroes, not stored as an empty shell —
/// because every such row costs an index entry and is read by nothing. This is enforced in the
/// shared persist path rather than per adapter, so a future source cannot opt out of it.
/// </summary>
[Collection(PostgresCollection.Name)]
public class StatsStorageRulesTests(PostgresFixture fixture)
{
    /// <summary>A stats source returning exactly what the test hands it.</summary>
    private sealed class StubSource(StatsFetchResult result) : IStatsSource
    {
        public string Key { get; init; } = "stub";

        public Task<StatsFetchResult> FetchScheduleAsync(string s, DateOnly d, CancellationToken ct)
            => Task.FromResult(result);

        public Task<StatsFetchResult> FetchRosterAsync(string s, CancellationToken ct)
            => Task.FromResult(result);

        public Task<StatsFetchResult> FetchBoxScoresAsync(string s, DateOnly d, CancellationToken ct)
            => Task.FromResult(result);
    }

    private static async Task<(Sport Sport, Source Source)> SeedAsync(LineOpsDbContext db, string sourceKey)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var sport = new Sport { Key = $"rule-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);

        var source = new Source
        {
            Key = sourceKey,
            Name = "Stub stats",
            Kind = SourceKind.Stats,
            BaseUrl = "local://stub"
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();

        return (sport, source);
    }

    private StatsIngestionService CreateService(LineOpsDbContext db)
        => new(
            db,
            new EntityResolver(db),
            new CreditBudgetGuard(new BudgetCalculator(db), NullLogger<CreditBudgetGuard>.Instance),
            NullLogger<StatsIngestionService>.Instance);

    private static string Line(params (string Key, string Value)[] pairs)
        => JsonSerializer.Serialize(pairs.ToDictionary(p => p.Key, p => p.Value));

    [Fact]
    public async Task PlayersReportedWithoutAResolvableGameAreNotStored()
    {
        await using var db = fixture.CreateContext();
        var sourceKey = $"stub-{Guid.NewGuid():N}"[..20];
        var (sport, _) = await SeedAsync(db, sourceKey);

        // A roster with stat lines but no games — the shape the demo fixture produces. The
        // lines have nowhere to land, so the players behind them are not worth a row either.
        var result = new StatsFetchResult(
            Games: [],
            Players:
            [
                new CanonicalPlayer("p1", sport.Key, "Never Appeared", "QB", null, null),
                new CanonicalPlayer("p2", sport.Key, "Also Never", "RB", null, null)
            ],
            PlayerStats:
            [
                new CanonicalPlayerStat("p1", "no-such-game", Line(("YDS", "100"))),
                new CanonicalPlayerStat("p2", "no-such-game", Line(("YDS", "60")))
            ],
            Cost: new FetchCost(1));

        var outcome = await CreateService(db).IngestAsync(
            new StubSource(result) { Key = sourceKey }, sport.Key, "test", new DateOnly(2026, 7, 1),
            CancellationToken.None);

        Assert.Equal(RunStatus.Success, outcome.Status);
        Assert.False(await db.Players.AnyAsync(p => p.SportId == sport.Id));
        Assert.Equal(0, outcome.RowsIngested);
    }

    [Fact]
    public async Task OnlyPlayersWhoActuallyAppearedAreStored()
    {
        await using var db = fixture.CreateContext();
        var sourceKey = $"stub-{Guid.NewGuid():N}"[..20];
        var (sport, _) = await SeedAsync(db, sourceKey);

        var result = new StatsFetchResult(
            Games:
            [
                new CanonicalGame("g1", sport.Key, "Home Side", "Away Side",
                    new DateTimeOffset(2026, 7, 1, 18, 0, 0, TimeSpan.Zero), "final", 4, 2)
            ],
            Players:
            [
                new CanonicalPlayer("p1", sport.Key, "Did Play", "1B", null, "Home Side"),
                new CanonicalPlayer("p2", sport.Key, "Bench Arm", "RP", null, "Home Side")
            ],
            // Only p1 has a line. p2 was on the roster and did not appear.
            PlayerStats: [new CanonicalPlayerStat("p1", "g1", Line(("H", "2"), ("AB", "4")))],
            Cost: new FetchCost(1));

        await CreateService(db).IngestAsync(
            new StubSource(result) { Key = sourceKey }, sport.Key, "test", new DateOnly(2026, 7, 1),
            CancellationToken.None);

        var stored = await db.Players.Where(p => p.SportId == sport.Id).Select(p => p.FullName).ToListAsync();

        Assert.Equal(["Did Play"], stored);

        // And exactly one stat row, carrying a real reading rather than an empty shell.
        var line = await db.PlayerGameStats
            .Where(s => s.Player!.SportId == sport.Id)
            .Select(s => s.StatLine)
            .SingleAsync();

        Assert.Contains("\"H\"", line);
    }
}
