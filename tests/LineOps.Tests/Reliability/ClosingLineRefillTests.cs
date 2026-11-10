using System.Globalization;
using LineOps.Core.Analytics;
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
/// what is written for them, that a close once written is left alone, and that a game the
/// provider has no close for is set aside rather than asked again on every press.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ClosingLineRefillTests(PostgresFixture fixture)
{
    /// <summary>A provider that closes every game it is asked about at 47.5, and records the asking.</summary>
    private sealed class StubCloser(string key) : IClosingLineSource
    {
        public string Key => key;

        public List<string> Asked { get; } = [];

        /// <summary>The events it has no line for.</summary>
        public HashSet<string> NoLineFor { get; init; } = ["no-line"];

        /// <summary>The outcome it bills the over as; a name too long to store makes the write fail.</summary>
        public string Over { get; init; } = "over";

        public Task<IReadOnlyList<CanonicalOdds>> FetchClosingLinesAsync(
            string sportKey, ClosingLineRequest game, CancellationToken ct)
        {
            Asked.Add(game.SourceGameId);

            if (NoLineFor.Contains(game.SourceGameId))
                return Task.FromResult<IReadOnlyList<CanonicalOdds>>([]);

            IReadOnlyList<CanonicalOdds> lines =
            [
                new(game.SourceGameId, "ESPN BET", Markets.Total, Over, 47.5m, -110, game.StartsAt),
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

    private static string Key() => $"closer-{Guid.NewGuid():N}"[..20];

    private static string Day(DateTimeOffset instant)
        => LeagueClock.DateOf(instant).ToString("MMM d", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Only_finals_without_a_closing_total_are_asked_about()
    {
        await using var db = fixture.CreateContext();
        var key = Key();
        var seeded = await SeedAsync(db, key);
        var closer = new StubCloser(key);

        await CreateService(db).RefillClosingLinesAsync(closer, seeded.Sport.Key, "test", 100, CancellationToken.None);

        Assert.Equal(["owed", "no-line"], closer.Asked);
    }

    /// <summary>
    /// A final the source holds no event id for cannot be asked, so it is never asked nor set
    /// aside — and the menu's count leaves it out, so what it quotes is what a press asks.
    /// </summary>
    [Fact]
    public async Task A_final_the_source_has_no_id_for_is_not_quoted_as_one_to_ask()
    {
        await using var db = fixture.CreateContext();
        var key = Key();
        var seeded = await SeedAsync(db, key);
        var unknown = new Game
        {
            SportId = seeded.Sport.Id,
            HomeTeamId = seeded.Owed.HomeTeamId,
            AwayTeamId = seeded.Owed.AwayTeamId,
            StartsAt = DateTimeOffset.UtcNow.AddDays(-10),
            Status = GameStatus.Final,
            SeasonYear = 2025,
            ExternalIds = new Dictionary<string, string> { ["another-source"] = "elsewhere" }
        };
        db.Games.Add(unknown);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var now = DateTimeOffset.UtcNow;

        // Owed a close all the same: no source has given it one.
        Assert.Contains(unknown.Id, await service.ClosingLinesOwed(seeded.Sport.Key).Select(g => g.Id).ToListAsync());
        Assert.Equal(2, await service.ClosingLinesToAskAsync(seeded.Sport.Key, key, now, 100, CancellationToken.None));

        var closer = new StubCloser(key);
        var outcome = await service.RefillClosingLinesAsync(closer, seeded.Sport.Key, "test", 100, CancellationToken.None, now: now);

        Assert.Equal(["owed", "no-line"], closer.Asked);
        Assert.Equal(2, (await db.IngestionRuns.AsNoTracking().SingleAsync(r => r.Id == outcome.RunId)).RequestsMade);
        Assert.False(await db.ClosingLineMisses.AnyAsync(m => m.GameId == unknown.Id));

        // Still owed, and neither to ask nor set aside: only the game ESPN had no line for waits
        // on a retry date, so the roll-up's "set aside" is one, not two.
        Assert.Contains(unknown.Id, await service.ClosingLinesOwed(seeded.Sport.Key).Select(g => g.Id).ToListAsync());
        Assert.Equal(1, await service.ClosingLinesSetAsideAsync(seeded.Sport.Key, key, now, CancellationToken.None));
    }

    [Fact]
    public async Task What_the_provider_states_is_written_once_and_never_over_a_side_already_held()
    {
        await using var db = fixture.CreateContext();
        var key = Key();
        var seeded = await SeedAsync(db, key);
        var closer = new StubCloser(key);

        var first = await CreateService(db).RefillClosingLinesAsync(closer, seeded.Sport.Key, "test", 100, CancellationToken.None);

        // Over, under and the away moneyline; the home moneyline was already held at -390.
        Assert.Equal(3, first.RowsIngested);

        var lines = await db.ClosingLines.AsNoTracking().Where(c => c.GameId == seeded.Owed.Id).ToListAsync();
        Assert.Equal(4, lines.Count);
        Assert.Equal(-390, lines.Single(l => l.Market == Markets.Moneyline && l.Outcome == "Philadelphia Eagles").PriceAmerican);
        Assert.All(lines.Where(l => l.Market == Markets.Total), l => Assert.Equal(47.5m, l.Line));

        // What the refill adds to the database is the close, and only the close: rows in
        // ClosingLines under the closing-line source, captured at the game's start, and nothing
        // in the scan tier — a backported game gets no snapshot, no opening line, no price from
        // any other moment than the close the source stated.
        Assert.All(lines, l => Assert.Equal(seeded.Source.Id, l.SourceId));
        Assert.All(lines.Where(l => l.PriceAmerican != -390),
            l => Assert.Equal(seeded.Owed.StartsAt, l.CapturedAt, TimeSpan.FromSeconds(1)));
        Assert.Equal(0, await db.OddsSnapshots.CountAsync(o => o.GameId == seeded.Owed.Id));

        // The owed game now has its total and is not asked again; the game with no line is set
        // aside, so the next press asks nothing at all.
        var again = new StubCloser(key);
        var second = await CreateService(db).RefillClosingLinesAsync(again, seeded.Sport.Key, "test", 100, CancellationToken.None);

        Assert.Empty(again.Asked);
        Assert.Equal(0, second.RowsIngested);
        Assert.Empty(await db.ClosingLines.Where(c => c.GameId == seeded.NoLine.Id).ToListAsync());
    }

    [Fact]
    public async Task The_refill_is_recorded_as_a_run_that_says_what_it_asked()
    {
        await using var db = fixture.CreateContext();
        var key = Key();
        var seeded = await SeedAsync(db, key);
        var now = DateTimeOffset.UtcNow;

        var outcome = await CreateService(db).RefillClosingLinesAsync(
            new StubCloser(key), seeded.Sport.Key, "espn:closing-lines", 100, CancellationToken.None, now: now);

        var run = await db.IngestionRuns.AsNoTracking().SingleAsync(r => r.Id == outcome.RunId);
        Assert.Equal(RunStatus.Success, run.Status);
        Assert.Equal(2, run.RequestsMade);
        Assert.Equal(3, run.RowsIngested);
        Assert.Equal(seeded.Source.Id, run.SourceId);
        Assert.Null(run.Error);
        Assert.Equal(
            $"{seeded.Sport.Key.ToUpperInvariant()}: 2 asked, 1 answered; 1 set aside until {Day(now + TimeSpan.FromDays(14))}.",
            run.Detail);
    }

    [Fact]
    public async Task A_run_with_nothing_to_ask_says_whether_anything_is_owed()
    {
        await using var db = fixture.CreateContext();
        var key = Key();
        var seeded = await SeedAsync(db, key);
        var service = CreateService(db);
        var now = DateTimeOffset.UtcNow;
        var sport = seeded.Sport.Key.ToUpperInvariant();

        // Two presses: the first answers one game and sets the other aside; the second has none to
        // ask, and says the one it is holding back rather than "nothing owed".
        await service.RefillClosingLinesAsync(new StubCloser(key), seeded.Sport.Key, "test", 100, CancellationToken.None, now: now);
        var second = await service.RefillClosingLinesAsync(new StubCloser(key), seeded.Sport.Key, "test", 100, CancellationToken.None, now: now);

        var run = await db.IngestionRuns.AsNoTracking().SingleAsync(r => r.Id == second.RunId);
        Assert.Equal($"{sport}: nothing to ask; 1 set aside until {Day(now + TimeSpan.FromDays(14))}.", run.Detail);
        Assert.Equal(RunStatus.Success, run.Status);

        // Once the last game has its close, there is nothing owed at all.
        var closed = await service.RefillClosingLinesAsync(
            new StubCloser(key) { NoLineFor = [] }, seeded.Sport.Key, "test", 100, CancellationToken.None, now: now.AddDays(15));
        var third = await service.RefillClosingLinesAsync(
            new StubCloser(key), seeded.Sport.Key, "test", 100, CancellationToken.None, now: now.AddDays(15));

        Assert.Equal(4, closed.RowsIngested);
        Assert.Equal($"{sport}: nothing owed.", (await db.IngestionRuns.AsNoTracking().SingleAsync(r => r.Id == third.RunId)).Detail);
    }

    [Fact]
    public async Task A_game_with_no_line_is_set_aside_skipped_retried_and_cleared_once_it_closes()
    {
        await using var db = fixture.CreateContext();
        var key = Key();
        var seeded = await SeedAsync(db, key);
        var now = DateTimeOffset.UtcNow;

        // Asked, no line: set aside for two weeks.
        await CreateService(db).RefillClosingLinesAsync(new StubCloser(key), seeded.Sport.Key, "test", 100, CancellationToken.None, now: now);

        var miss = await db.ClosingLineMisses.AsNoTracking().SingleAsync(m => m.GameId == seeded.NoLine.Id);
        Assert.Equal(seeded.Source.Id, miss.SourceId);
        Assert.Equal(1, miss.Attempts);
        Assert.Equal(now, miss.LastTriedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(now + TimeSpan.FromDays(14), miss.RetryAt, TimeSpan.FromMilliseconds(1));

        // The answered game has no miss to remember.
        Assert.False(await db.ClosingLineMisses.AnyAsync(m => m.GameId == seeded.Owed.Id));

        // Still owed — it has no close — but not asked while its retry date is ahead.
        var stats = CreateService(db);
        Assert.Contains(seeded.NoLine.Id, await stats.ClosingLinesOwed(seeded.Sport.Key).Select(g => g.Id).ToListAsync());
        Assert.Equal(0, await stats.ClosingLinesToAskAsync(seeded.Sport.Key, key, now.AddDays(13), 100, CancellationToken.None));

        var early = new StubCloser(key);
        await CreateService(db).RefillClosingLinesAsync(early, seeded.Sport.Key, "test", 100, CancellationToken.None, now: now.AddDays(13));
        Assert.Empty(early.Asked);

        // Past it, asked again; a second miss waits twice as long.
        var later = now.AddDays(15);
        Assert.Equal(1, await stats.ClosingLinesToAskAsync(seeded.Sport.Key, key, later, 100, CancellationToken.None));

        var retry = new StubCloser(key);
        await CreateService(db).RefillClosingLinesAsync(retry, seeded.Sport.Key, "test", 100, CancellationToken.None, now: later);
        Assert.Equal(["no-line"], retry.Asked);

        miss = await db.ClosingLineMisses.AsNoTracking().SingleAsync(m => m.GameId == seeded.NoLine.Id);
        Assert.Equal(2, miss.Attempts);
        Assert.Equal(later + TimeSpan.FromDays(28), miss.RetryAt, TimeSpan.FromMilliseconds(1));

        // ESPN fills the close in; the next ask after the retry date writes it and forgets the miss.
        var filled = new StubCloser(key) { NoLineFor = [] };
        var outcome = await CreateService(db).RefillClosingLinesAsync(
            filled, seeded.Sport.Key, "test", 100, CancellationToken.None, now: later.AddDays(29));

        Assert.Equal(["no-line"], filled.Asked);
        Assert.Equal(4, outcome.RowsIngested);
        Assert.False(await db.ClosingLineMisses.AnyAsync(m => m.GameId == seeded.NoLine.Id));
        Assert.DoesNotContain(seeded.NoLine.Id, await stats.ClosingLinesOwed(seeded.Sport.Key).Select(g => g.Id).ToListAsync());
    }

    [Theory]
    [InlineData(1, 14)]
    [InlineData(2, 28)]
    [InlineData(3, 56)]
    [InlineData(4, 90)]
    [InlineData(12, 90)]
    public void Each_miss_doubles_the_wait_up_to_ninety_days(int attempts, int days)
        => Assert.Equal(TimeSpan.FromDays(days), StatsIngestionService.MissWait(attempts));

    [Fact]
    public async Task A_write_that_throws_fails_the_run_with_its_error_rather_than_leaving_it_running()
    {
        await using var db = fixture.CreateContext();
        var key = Key();
        var seeded = await SeedAsync(db, key);

        // An outcome longer than the column holds: the save throws.
        var closer = new StubCloser(key) { Over = new string('o', 200) };
        var outcome = await CreateService(db).RefillClosingLinesAsync(closer, seeded.Sport.Key, "test", 100, CancellationToken.None);

        Assert.Equal(RunStatus.Failed, outcome.Status);
        Assert.Equal(0, outcome.RowsIngested);

        await using var read = fixture.CreateContext();
        var run = await read.IngestionRuns.AsNoTracking().SingleAsync(r => r.Id == outcome.RunId);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.NotNull(run.FinishedAt);
        Assert.StartsWith("DbUpdateException", run.Error);
        Assert.NotNull(run.Detail);

        // Nothing half-written: no close for the owed game's total, no miss for the game with no line.
        Assert.False(await read.ClosingLines.AnyAsync(c => c.GameId == seeded.Owed.Id && c.Market == Markets.Total));
        Assert.False(await read.ClosingLineMisses.AnyAsync(m => m.GameId == seeded.NoLine.Id));
    }

    [Fact]
    public async Task A_fight_is_owed_its_moneyline_and_a_game_its_total()
    {
        await using var db = fixture.CreateContext();
        var key = Key();

        var ufc = await db.Sports.FirstOrDefaultAsync(s => s.Key == "ufc") ?? new Sport { Key = "ufc", Name = "UFC" };
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var red = new Team { Sport = ufc, Name = $"Red {suffix}", Abbrev = "RED" };
        var blue = new Team { Sport = ufc, Name = $"Blue {suffix}", Abbrev = "BLU" };
        var source = new Source { Key = key, Name = "Stub closer", Kind = SourceKind.Stats, BaseUrl = "local://stub" };

        // Far from any other test's fights, so a ranged refill asks only these.
        var night = new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(Random.Shared.Next(1, 500_000));

        Game Bout(string eventId, int minutes) => new()
        {
            Sport = ufc,
            HomeTeam = red,
            AwayTeam = blue,
            StartsAt = night.AddMinutes(minutes),
            Status = GameStatus.Final,
            SeasonYear = 1990,
            ExternalIds = new Dictionary<string, string> { [key] = eventId }
        };

        var withMoneyline = Bout("ml", 0);
        var withTotalOnly = Bout("total", 1);
        var withNothing = Bout("none", 2);

        db.AddRange(red, blue, source, withMoneyline, withTotalOnly, withNothing);
        if (ufc.Id == 0)
            db.Add(ufc);
        await db.SaveChangesAsync();

        static ClosingLine Close(Game g, int sourceId, string market, string outcome, decimal? line) => new()
        {
            GameId = g.Id, SourceId = sourceId, Book = "ESPN BET", Market = market, Outcome = outcome,
            Line = line, PriceAmerican = -150, CapturedAt = g.StartsAt
        };

        db.ClosingLines.AddRange(
            Close(withMoneyline, source.Id, Markets.Moneyline, red.Name, null),
            Close(withTotalOnly, source.Id, Markets.Total, "over", 2.5m));
        await db.SaveChangesAsync();

        var owed = await CreateService(db).ClosingLinesOwed("ufc").Select(g => g.Id).ToListAsync();

        // A fight with a moneyline has had its close read, whatever else it lacks; one with only a
        // rounds total has not.
        Assert.DoesNotContain(withMoneyline.Id, owed);
        Assert.Contains(withTotalOnly.Id, owed);
        Assert.Contains(withNothing.Id, owed);

        // A game is owed its total: a moneyline alone does not settle it.
        var seeded = await SeedAsync(db, Key());
        db.ClosingLines.Add(Close(seeded.NoLine, seeded.Source.Id, Markets.Moneyline, "Dallas Cowboys", null));
        await db.SaveChangesAsync();
        Assert.Contains(seeded.NoLine.Id, await CreateService(db).ClosingLinesOwed(seeded.Sport.Key).Select(g => g.Id).ToListAsync());

        Assert.Equal(Markets.Moneyline, StatsIngestionService.OwedMarket("ufc"));
        Assert.Equal(Markets.Total, StatsIngestionService.OwedMarket("nfl"));

        // A fight answered with its moneyline is answered, and is not set aside.
        var closer = new StubCloser(key) { NoLineFor = ["none"] };
        var outcome = await CreateService(db).RefillClosingLinesAsync(
            closer, "ufc", "test", 100, CancellationToken.None, from: night, to: night.AddMinutes(3));

        Assert.Equal(["none", "total"], closer.Asked);
        var run = await db.IngestionRuns.AsNoTracking().SingleAsync(r => r.Id == outcome.RunId);
        Assert.StartsWith("UFC: 2 asked, 1 answered; 1 set aside until ", run.Detail);
        Assert.False(await db.ClosingLineMisses.AnyAsync(m => m.GameId == withTotalOnly.Id));
        Assert.True(await db.ClosingLineMisses.AnyAsync(m => m.GameId == withNothing.Id));
    }

    [Fact]
    public void The_job_says_how_many_were_asked_apart_from_how_many_are_without_a_close()
    {
        Assert.Equal(
            "0 closing lines written; 0 of 3 games asked got a close; 520 set aside until their retry date.",
            IngestionJobs.ClosingLinesSentence(0, 3, 0, new IngestionJobs.OwedCloses(520, 0, 520)));

        Assert.Equal(
            "12 closing lines written; 3 of 3 games asked got a close.",
            IngestionJobs.ClosingLinesSentence(12, 3, 3, new IngestionJobs.OwedCloses(0, 0)));

        // A run's ceiling is not a setting-aside: 700 owed, 600 asked now, none set aside.
        Assert.Equal(
            "0 closing lines written; 0 of 600 games asked got a close; 100 still to ask.",
            IngestionJobs.ClosingLinesSentence(0, 600, 0, new IngestionJobs.OwedCloses(700, 100)));

        Assert.Equal(
            "0 closing lines written; no game is owed a close.",
            IngestionJobs.ClosingLinesSentence(0, 0, 0, new IngestionJobs.OwedCloses(0, 0)));
    }

    [Fact]
    public void The_run_detail_names_the_sport_and_when_the_set_aside_games_come_back()
    {
        var nov23 = new DateTimeOffset(2026, 11, 23, 18, 0, 0, TimeSpan.Zero);

        Assert.Equal("NFL: 2 asked, 0 answered; 2 set aside until Nov 23.",
            StatsIngestionService.RefillDetail("nfl", 2, 0, [nov23, nov23]));
        Assert.Equal("MLB: 1 asked, 1 answered.",
            StatsIngestionService.RefillDetail("mlb", 1, 1, []));
        Assert.Equal("UFC: 3 asked, 0 answered; 3 set aside until between Nov 23 and Jan 6.",
            StatsIngestionService.RefillDetail("ufc", 3, 0, [nov23, nov23.AddDays(14), nov23.AddDays(44)]));
    }
}
