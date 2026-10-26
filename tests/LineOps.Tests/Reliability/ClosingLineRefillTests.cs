using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Services;
using LineOps.Reliability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LineOps.Tests.Reliability;

/// <summary>
/// The backport of closing lines for finished games that have none: which games are asked about,
/// what is written for them, and that a close once written is left alone.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ClosingLineRefillTests(PostgresFixture fixture)
{
    /// <summary>A provider that closes every game it is asked about at 47.5, and records the asking.</summary>
    private sealed class StubCloser(string key) : IClosingLineSource
    {
        public string Key => key;

        public List<string> Asked { get; } = [];

        public Task<IReadOnlyList<CanonicalOdds>> FetchClosingLinesAsync(
            string sportKey, ClosingLineRequest game, CancellationToken ct)
        {
            Asked.Add(game.SourceGameId);

            if (game.SourceGameId == "no-line")
                return Task.FromResult<IReadOnlyList<CanonicalOdds>>([]);

            IReadOnlyList<CanonicalOdds> lines =
            [
                new(game.SourceGameId, "ESPN BET", Markets.Total, "over", 47.5m, -110, game.StartsAt),
                new(game.SourceGameId, "ESPN BET", Markets.Total, "under", 47.5m, -110, game.StartsAt),
                new(game.SourceGameId, "ESPN BET", Markets.Moneyline, game.HomeTeamName, null, -400, game.StartsAt),
                new(game.SourceGameId, "ESPN BET", Markets.Moneyline, game.AwayTeamName, null, 300, game.StartsAt)
            ];

            return Task.FromResult(lines);
        }
    }

    private sealed record Seeded(Sport Sport, Source Source, Game Owed, Game Closed, Game Upcoming, Game NoLine);

    private static async Task<Seeded> SeedAsync(LineOpsDbContext db, string sourceKey)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var sport = new Sport { Key = $"refill-{suffix}", Name = "TEST" };
        var home = new Team { Sport = sport, Name = "Philadelphia Eagles", Abbrev = "PHI" };
        var away = new Team { Sport = sport, Name = "Dallas Cowboys", Abbrev = "DAL" };
        var source = new Source { Key = sourceKey, Name = "Stub closer", Kind = SourceKind.Stats, BaseUrl = "local://stub" };

        Game At(string eventId, GameStatus status, int daysAgo) => new()
        {
            Sport = sport,
            HomeTeam = home,
            AwayTeam = away,
            StartsAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
            Status = status,
            HomeScore = status == GameStatus.Final ? 24 : null,
            AwayScore = status == GameStatus.Final ? 20 : null,
            SeasonYear = 2025,
            ExternalIds = new Dictionary<string, string> { [sourceKey] = eventId }
        };

        var owed = At("owed", GameStatus.Final, 30);
        var closed = At("closed", GameStatus.Final, 20);
        var upcoming = At("upcoming", GameStatus.Scheduled, -2);
        var noLine = At("no-line", GameStatus.Final, 40);

        db.AddRange(sport, home, away, source, owed, closed, upcoming, noLine);
        await db.SaveChangesAsync();

        // A game that already has a closing total from somewhere has had its close read.
        db.ClosingLines.Add(new ClosingLine
        {
            GameId = closed.Id,
            SourceId = source.Id,
            Book = "draftkings",
            Market = Markets.Total,
            Outcome = "over",
            Line = 44.5m,
            PriceAmerican = -110,
            CapturedAt = closed.StartsAt
        });

        // And a side the provider already gave for the owed game is not written twice.
        db.ClosingLines.Add(new ClosingLine
        {
            GameId = owed.Id,
            SourceId = source.Id,
            Book = "DraftKings",
            Market = Markets.Moneyline,
            Outcome = "Philadelphia Eagles",
            PriceAmerican = -390,
            CapturedAt = owed.StartsAt
        });

        await db.SaveChangesAsync();
        return new Seeded(sport, source, owed, closed, upcoming, noLine);
    }

    private static StatsIngestionService CreateService(LineOpsDbContext db)
        => new(
            db,
            new EntityResolver(db),
            new CreditBudgetGuard(new BudgetCalculator(db), NullLogger<CreditBudgetGuard>.Instance),
            NullLogger<StatsIngestionService>.Instance);

    [Fact]
    public async Task Only_finals_without_a_closing_total_are_asked_about()
    {
        await using var db = fixture.CreateContext();
        var key = $"closer-{Guid.NewGuid():N}"[..20];
        var seeded = await SeedAsync(db, key);
        var closer = new StubCloser(key);

        await CreateService(db).RefillClosingLinesAsync(closer, seeded.Sport.Key, "test", 100, CancellationToken.None);

        Assert.Equal(["owed", "no-line"], closer.Asked);
    }

    [Fact]
    public async Task What_the_provider_states_is_written_once_and_never_over_a_side_already_held()
    {
        await using var db = fixture.CreateContext();
        var key = $"closer-{Guid.NewGuid():N}"[..20];
        var seeded = await SeedAsync(db, key);
        var closer = new StubCloser(key);

        var first = await CreateService(db).RefillClosingLinesAsync(closer, seeded.Sport.Key, "test", 100, CancellationToken.None);

        // Over, under and the away moneyline; the home moneyline was already held at -390.
        Assert.Equal(3, first.RowsIngested);

        var lines = await db.ClosingLines.AsNoTracking().Where(c => c.GameId == seeded.Owed.Id).ToListAsync();
        Assert.Equal(4, lines.Count);
        Assert.Equal(-390, lines.Single(l => l.Market == Markets.Moneyline && l.Outcome == "Philadelphia Eagles").PriceAmerican);
        Assert.All(lines.Where(l => l.Market == Markets.Total), l => Assert.Equal(47.5m, l.Line));

        // The owed game now has its total and is not asked again; the game with no line still is.
        var again = new StubCloser(key);
        var second = await CreateService(db).RefillClosingLinesAsync(again, seeded.Sport.Key, "test", 100, CancellationToken.None);

        Assert.Equal(["no-line"], again.Asked);
        Assert.Equal(0, second.RowsIngested);
        Assert.Empty(await db.ClosingLines.Where(c => c.GameId == seeded.NoLine.Id).ToListAsync());
    }

    [Fact]
    public async Task The_refill_is_recorded_as_a_run()
    {
        await using var db = fixture.CreateContext();
        var key = $"closer-{Guid.NewGuid():N}"[..20];
        var seeded = await SeedAsync(db, key);

        var outcome = await CreateService(db).RefillClosingLinesAsync(
            new StubCloser(key), seeded.Sport.Key, "espn:closing-lines", 100, CancellationToken.None);

        var run = await db.IngestionRuns.AsNoTracking().SingleAsync(r => r.Id == outcome.RunId);
        Assert.Equal(RunStatus.Success, run.Status);
        Assert.Equal(2, run.RequestsMade);
        Assert.Equal(3, run.RowsIngested);
        Assert.Equal(seeded.Source.Id, run.SourceId);
    }
}
