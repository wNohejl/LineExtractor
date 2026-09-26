using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Reliability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LineOps.Tests.Reliability;

/// <summary>
/// The data rules: what is wrong with the games rather than with a feed. Each test seeds its own
/// sport, so the counts it asserts on are its own whatever else the shared database holds.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DataQualityTests(PostgresFixture fixture)
{
    private static readonly TimeSpan Grace = TimeSpan.FromHours(12);
    private static readonly TimeSpan Lookback = TimeSpan.FromDays(45);

    private sealed record Seeded(Sport Sport, Func<double, GameStatus, Task<Game>> Add);

    private static async Task<Seeded> NewSportAsync(LineOpsDbContext db, bool enabled = true)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var sport = new Sport { Key = $"dq-{suffix}", Name = "TEST", Enabled = enabled };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = $"Home {suffix}", Abbrev = "HOM" };
        var away = new Team { SportId = sport.Id, Name = $"Away {suffix}", Abbrev = "AWY" };
        db.Teams.AddRange(home, away);
        await db.SaveChangesAsync();

        return new Seeded(sport, async (hoursAgo, status) =>
        {
            var game = new Game
            {
                SportId = sport.Id,
                HomeTeamId = home.Id,
                AwayTeamId = away.Id,
                StartsAt = DateTimeOffset.UtcNow.AddHours(-hoursAgo),
                Status = status,
                SeasonYear = 2026
            };
            db.Games.Add(game);
            await db.SaveChangesAsync();
            return game;
        });
    }

    [Fact]
    public async Task A_game_open_long_after_its_start_is_unfinished_and_a_recent_one_is_not()
    {
        await using var db = fixture.CreateContext();
        var seeded = await NewSportAsync(db);

        await seeded.Add(5 * 24, GameStatus.Live);    // the September outage: Live for days
        await seeded.Add(20, GameStatus.Scheduled);   // never marked started at all
        await seeded.Add(2, GameStatus.Live);         // genuinely in progress
        await seeded.Add(30, GameStatus.Postponed);   // not owed
        await seeded.Add(60 * 24, GameStatus.Live);   // outside the lookback: the backfill's

        var counts = await new DataQuality(db).UnfinishedAsync(Grace, Lookback);

        var mine = Assert.Single(counts, c => c.SportKey == seeded.Sport.Key);
        Assert.Equal(2, mine.Games);
    }

    [Fact]
    public async Task A_final_without_a_box_score_or_a_close_is_counted()
    {
        await using var db = fixture.CreateContext();
        var seeded = await NewSportAsync(db);

        await seeded.Add(30, GameStatus.Final);   // bare: neither
        var whole = await seeded.Add(30, GameStatus.Final);

        var source = await SourceAsync(db);
        var player = new Player { SportId = seeded.Sport.Id, FullName = $"Player {Guid.NewGuid():N}" };
        db.Players.Add(player);
        await db.SaveChangesAsync();

        db.PlayerGameStats.Add(new PlayerGameStat
        {
            GameId = whole.Id, PlayerId = player.Id, SourceId = source.Id, CapturedAt = whole.StartsAt
        });
        db.ClosingLines.Add(new ClosingLine
        {
            GameId = whole.Id, SourceId = source.Id, Book = "DraftKings", Market = Markets.Moneyline,
            Outcome = "home", PriceAmerican = -120, CapturedAt = whole.StartsAt, PromotedAt = whole.StartsAt
        });
        await db.SaveChangesAsync();

        var quality = new DataQuality(db);

        var noStats = Assert.Single(await quality.FinalsWithoutStatsAsync(Grace, Lookback),
            c => c.SportKey == seeded.Sport.Key);
        var noClose = Assert.Single(await quality.FinalsWithoutCloseAsync(Grace, Lookback),
            c => c.SportKey == seeded.Sport.Key);

        Assert.Equal(1, noStats.Games);
        Assert.Equal(1, noClose.Games);
    }

    [Fact]
    public async Task A_disabled_sport_is_not_watched()
    {
        await using var db = fixture.CreateContext();
        var seeded = await NewSportAsync(db, enabled: false);

        await seeded.Add(5 * 24, GameStatus.Live);

        var counts = await new DataQuality(db).UnfinishedAsync(Grace, Lookback);

        Assert.DoesNotContain(counts, c => c.SportKey == seeded.Sport.Key);
    }

    [Fact]
    public async Task The_engine_raises_one_sourceless_warn_per_rule()
    {
        await using var db = fixture.CreateContext();
        var seeded = await NewSportAsync(db);
        await seeded.Add(5 * 24, GameStatus.Live);

        var engine = new AlertEngine(db, new KpiCalculator(db), new BudgetCalculator(db),
            new OptionsWrapper<ReliabilityOptions>(new ReliabilityOptions()), NullLogger<AlertEngine>.Instance);

        var candidates = await engine.EvaluateAsync();

        var alert = Assert.Single(candidates, c => c.RuleKey == AlertRules.UnfinishedGames);
        Assert.Null(alert.SourceId);
        Assert.Equal(AlertSeverity.Warn, alert.Severity);
        Assert.Contains(seeded.Sport.Key.ToUpperInvariant(), alert.Message);
    }

    private static async Task<Source> SourceAsync(LineOpsDbContext db)
    {
        var source = new Source
        {
            Key = $"dq-src-{Guid.NewGuid():N}", Name = "Test stats", Kind = SourceKind.Stats,
            BaseUrl = "local://test", Enabled = true
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source;
    }

    [Fact]
    public async Task A_season_is_counted_as_held_final_with_stats_and_with_a_close_of_each_kind()
    {
        await using var db = fixture.CreateContext();
        var seeded = await NewSportAsync(db);

        await seeded.Add(30, GameStatus.Final);   // bare: neither stats nor a close
        var marketClosed = await seeded.Add(40, GameStatus.Final);
        var referenceClosed = await seeded.Add(50, GameStatus.Final);
        await seeded.Add(60, GameStatus.Postponed);
        await seeded.Add(-20, GameStatus.Scheduled);

        var stats = await SourceAsync(db);
        var odds = new Source
        {
            Key = $"dq-odds-{Guid.NewGuid():N}", Name = "Test odds", Kind = SourceKind.Odds,
            BaseUrl = "local://test", Enabled = true
        };
        db.Sources.Add(odds);
        var player = new Player { SportId = seeded.Sport.Id, FullName = $"Player {Guid.NewGuid():N}" };
        db.Players.Add(player);
        await db.SaveChangesAsync();

        foreach (var game in new[] { marketClosed, referenceClosed })
            db.PlayerGameStats.Add(new PlayerGameStat
            {
                GameId = game.Id, PlayerId = player.Id, SourceId = stats.Id, CapturedAt = game.StartsAt
            });

        ClosingLine Close(Game game, Source source) => new()
        {
            GameId = game.Id, SourceId = source.Id, Book = "draftkings", Market = Markets.Moneyline,
            Outcome = "home", PriceAmerican = -120, CapturedAt = game.StartsAt, PromotedAt = game.StartsAt
        };
        db.ClosingLines.AddRange(Close(marketClosed, odds), Close(referenceClosed, stats));
        await db.SaveChangesAsync();

        var season = Assert.Single(await new DataQuality(db).SeasonsAsync(),
            s => s.SportKey == seeded.Sport.Key);

        Assert.Equal(2026, season.SeasonYear);
        Assert.Equal(5, season.Held);
        Assert.Equal(3, season.Final);
        Assert.Equal(1, season.Postponed);
        Assert.Equal(2, season.WithStats);
        Assert.Equal(1, season.FinalsWithoutStats);
        Assert.Equal(1, season.WithMarketClose);
        Assert.Equal(2, season.WithClose);
        Assert.Null(season.Expected);   // a test league has no published schedule
    }

    [Fact]
    public async Task A_hidden_league_has_no_season_report()
    {
        await using var db = fixture.CreateContext();
        var seeded = await NewSportAsync(db, enabled: false);
        await seeded.Add(30, GameStatus.Final);

        Assert.DoesNotContain(await new DataQuality(db).SeasonsAsync(), s => s.SportKey == seeded.Sport.Key);
    }
}
