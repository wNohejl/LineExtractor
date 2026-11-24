using XLines.Core.Analytics;
using XLines.Core.Contracts;
using XLines.Core.Entities;
using XLines.Data;
using XLines.Ingestion.Adapters;
using XLines.Ingestion.Configuration;
using XLines.Ingestion.Services;
using XLines.Reliability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace XLines.Tests.Reliability;

/// <summary>
/// What the adapters already download and used to throw away — a game's circumstances, its
/// officials, MLB's lineups and the opening line — written through the shared persist path: a fetch
/// that states more fills and corrects, a fetch that states less erases nothing, a crew or a lineup
/// is replaced whole, and the open sits beside the close without ever being read as it.
/// </summary>
[Collection(PostgresCollection.Name)]
public class GameContextIngestionTests(PostgresFixture fixture)
{
    /// <summary>A stats source returning whatever the test last handed it.</summary>
    private sealed class StubSource(string key) : IStatsSource
    {
        public string Key => key;

        public StatsFetchResult Result { get; set; } = new([], [], [], new FetchCost(1));

        public Task<StatsFetchResult> FetchScheduleAsync(string s, DateOnly d, CancellationToken ct) => Task.FromResult(Result);

        public Task<StatsFetchResult> FetchRosterAsync(string s, CancellationToken ct) => Task.FromResult(Result);

        public Task<StatsFetchResult> FetchBoxScoresAsync(string s, DateOnly d, CancellationToken ct) => Task.FromResult(Result);
    }

    private sealed record Seeded(Sport Sport, Source Source, StubSource Stub, CanonicalGame Game);

    private static async Task<Seeded> SeedAsync(XLinesDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var key = $"ctx-{suffix}";

        var sport = new Sport { Key = $"ctx-{suffix}", Name = "TEST" };
        var source = new Source { Key = key, Name = "Stub stats", Kind = SourceKind.Stats, BaseUrl = "local://stub" };
        db.AddRange(sport, source);
        await db.SaveChangesAsync();

        var game = new CanonicalGame($"ev-{suffix}", sport.Key, $"Home {suffix}", $"Away {suffix}",
            new DateTimeOffset(2026, 9, 14, 0, 20, 0, TimeSpan.Zero), "final", 24, 20);

        return new Seeded(sport, source, new StubSource(key), game);
    }

    private static StatsIngestionService Service(XLinesDbContext db)
        => new(db, new EntityResolver(db),
            new CreditBudgetGuard(new BudgetCalculator(db), NullLogger<CreditBudgetGuard>.Instance),
            NullLogger<StatsIngestionService>.Instance);

    private static async Task<IngestionOutcome> IngestAsync(XLinesDbContext db, Seeded s, StatsFetchResult result)
    {
        s.Stub.Result = result;
        return await Service(db).IngestAsync(s.Stub, s.Sport.Key, "test", new DateOnly(2026, 9, 13), CancellationToken.None);
    }

    private static StatsFetchResult With(CanonicalGame game, params CanonicalGameContext[] contexts)
        => new([game], [], [], new FetchCost(1)) { GameContexts = contexts };

    private static async Task<int> GameIdAsync(XLinesDbContext db, Seeded s)
        => (await db.Games.AsNoTracking().Where(g => g.SportId == s.Sport.Id).ToListAsync())
            .Single(g => g.ExternalIds.GetValueOrDefault(s.Source.Key) == s.Game.SourceGameId).Id;

    private static async Task<GameContext> ContextAsync(XLinesDbContext db, Seeded s)
    {
        var id = await GameIdAsync(db, s);
        return await db.GameContexts.AsNoTracking().SingleAsync(c => c.GameId == id && c.SourceId == s.Source.Id);
    }

    private static async Task<List<GameOfficial>> CrewAsync(XLinesDbContext db, Seeded s)
    {
        var id = await GameIdAsync(db, s);
        return await db.GameOfficials.AsNoTracking()
            .Where(o => o.GameId == id && o.SourceId == s.Source.Id)
            .OrderBy(o => o.Order)
            .ToListAsync();
    }

    [Fact]
    public async Task A_later_read_fills_and_corrects_and_a_read_that_knows_less_erases_nothing()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        var id = s.Game.SourceGameId;

        // Before the gates close: venue and forecast, no crowd.
        await IngestAsync(db, s, With(s.Game, new CanonicalGameContext(id)
        {
            VenueName = "Lambeau Field", VenueCity = "Green Bay", VenueState = "WI", Indoor = false,
            Surface = GameSurfaces.Grass, WeatherCondition = "Partly Cloudy", TemperatureF = 61,
            WindSpeedMph = 9, WindDirection = "NW", PrecipitationPercent = 10,
            HomeWinPercent = 58.4m, AwayWinPercent = 41.6m
        }));

        // After: the crowd, and the temperature taken again.
        await IngestAsync(db, s, With(s.Game, new CanonicalGameContext(id) { Attendance = 78_123, TemperatureF = 58 }));

        // A third read that has lost all of it.
        var quiet = await IngestAsync(db, s, With(s.Game, new CanonicalGameContext(id)));

        var context = await ContextAsync(db, s);
        Assert.Equal(RunStatus.Success, quiet.Status);
        Assert.Equal(("Lambeau Field", "Green Bay", "WI", false, GameSurfaces.Grass),
            (context.VenueName, context.VenueCity, context.VenueState, context.Indoor, context.Surface));
        Assert.Equal(("Partly Cloudy", 58, 9, "NW", 10),
            (context.WeatherCondition, context.TemperatureF, context.WindSpeedMph, context.WindDirection, context.PrecipitationPercent));
        Assert.Equal(78_123, context.Attendance);
        Assert.Equal((58.4m, 41.6m), (context.HomeWinPercent, context.AwayWinPercent));
        Assert.Null(context.MatchupQuality);
        Assert.Null(context.DayNight);
    }

    [Fact]
    public async Task A_read_that_changes_nothing_writes_nothing()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        var stated = new CanonicalGameContext(s.Game.SourceGameId)
        {
            VenueName = "Crypto.com Arena", Indoor = true, HomeWinPercent = 62.345m, MatchupQuality = 71.2m,
            Officials = [new("Scott Foster", "Crew Chief"), new("Tony Brothers", "Referee"), new("Ben Taylor", "Umpire")]
        };

        await IngestAsync(db, s, With(s.Game, stated));
        var first = await ContextAsync(db, s);
        var crew = await CrewAsync(db, s);

        await IngestAsync(db, s, With(s.Game, stated));
        var second = await ContextAsync(db, s);

        // Stored at the column's two places, and compared there, so the re-read is no change.
        Assert.Equal(62.35m, second.HomeWinPercent);
        Assert.Equal(first.UpdatedAt, second.UpdatedAt);
        Assert.Equal(crew.Select(o => o.Id), (await CrewAsync(db, s)).Select(o => o.Id));
    }

    [Fact]
    public async Task A_crew_is_replaced_whole_by_one_that_names_somebody_and_kept_by_one_that_names_nobody()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        var id = s.Game.SourceGameId;

        await IngestAsync(db, s, With(s.Game, new CanonicalGameContext(id)
        {
            Officials = [new("Pat Hoberg", "Home Plate"), new("Lance Barksdale", "First Base"), new("Dan Bellino", "Second Base")]
        }));

        // A late change: the crew as now posted, numbered by the provider out of list order.
        await IngestAsync(db, s, With(s.Game, new CanonicalGameContext(id)
        {
            Officials =
            [
                new("Mark Carlson", "Second Base", Order: 3), new("Pat Hoberg", "Home Plate", Order: 1),
                new("Lance Barksdale", "First Base", Order: 2), new("Alan Porter", "Third Base", Order: 4)
            ]
        }));

        // Then a read with no officials at all, and one with an empty list.
        await IngestAsync(db, s, With(s.Game, new CanonicalGameContext(id) { VenueName = "Busch Stadium" }));
        await IngestAsync(db, s, With(s.Game, new CanonicalGameContext(id) { Officials = [] }));

        var crew = await CrewAsync(db, s);
        Assert.Equal(
            new[] { (1, "Pat Hoberg", "Home Plate"), (2, "Lance Barksdale", "First Base"), (3, "Mark Carlson", "Second Base"), (4, "Alan Porter", "Third Base") },
            crew.Select(o => (o.Order, o.Name, o.Role!)).ToArray());

        // Asked by official: the games an umpire worked are a query on the name.
        Assert.True(await db.GameOfficials.AnyAsync(o => o.Name == "Alan Porter" && o.SourceId == s.Source.Id));
        Assert.False(await db.GameOfficials.AnyAsync(o => o.Name == "Dan Bellino" && o.SourceId == s.Source.Id));
    }

    [Fact]
    public async Task A_lineup_is_replaced_side_by_side_and_a_read_that_posts_none_leaves_it()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        var id = s.Game.SourceGameId;

        IReadOnlyList<CanonicalLineupSlot> home = [new(1, "660271", "Shohei Ohtani", "DH"), new(2, "605141", "Mookie Betts", "SS")];
        IReadOnlyList<CanonicalLineupSlot> away = [new(1, "592450", "Aaron Judge", "RF")];
        IReadOnlyList<CanonicalLineupSlot> homeRevised = [new(1, "605141", "Mookie Betts", "SS"), new(2, "660271", "Shohei Ohtani", "DH")];

        await IngestAsync(db, s, With(s.Game, new CanonicalGameContext(id) { HomeLineup = home, AwayLineup = away }));
        await IngestAsync(db, s, With(s.Game, new CanonicalGameContext(id) { HomeLineup = homeRevised }));
        await IngestAsync(db, s, With(s.Game, new CanonicalGameContext(id) { HomeLineup = [], AwayLineup = null }));

        var context = await ContextAsync(db, s);
        Assert.Equal(homeRevised, GameContext.ParseLineup(context.HomeLineup));
        Assert.Equal(away, GameContext.ParseLineup(context.AwayLineup));
    }

    [Fact]
    public async Task A_context_finds_a_game_held_under_the_providers_id_when_the_payload_carries_no_game()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        await IngestAsync(db, s, new StatsFetchResult([s.Game], [], [], new FetchCost(1)));

        // A summary read on its own, as the live box is: no games, only what it says about one.
        await IngestAsync(db, s, new StatsFetchResult([], [], [], new FetchCost(1))
        {
            GameContexts = [new CanonicalGameContext(s.Game.SourceGameId) { Attendance = 18_064 },
                new CanonicalGameContext("no-such-game") { Attendance = 1 }]
        });

        Assert.Equal(18_064, (await ContextAsync(db, s)).Attendance);
    }

    [Fact]
    public async Task A_context_of_nothing_is_no_row()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        await IngestAsync(db, s, With(s.Game, new CanonicalGameContext(s.Game.SourceGameId) { VenueName = "  ", Officials = [] }));

        var id = await GameIdAsync(db, s);
        Assert.False(await db.GameContexts.AnyAsync(c => c.GameId == id));
    }

    // ---- the opening line ---------------------------------------------------------------------

    private static CanonicalOdds Close(Seeded s, string book, string market, string outcome, decimal? line, int price,
        CanonicalOpening? opening = null)
        => new(s.Game.SourceGameId, book, market, outcome, line, price, s.Game.StartsAt) { Opening = opening };

    private static async Task<Dictionary<(string Market, string Outcome), ClosingLine>> ClosesAsync(XLinesDbContext db, Seeded s)
    {
        var id = await GameIdAsync(db, s);
        return await db.ClosingLines.AsNoTracking()
            .Where(c => c.GameId == id && c.SourceId == s.Source.Id)
            .ToDictionaryAsync(c => (c.Market, c.Outcome));
    }

    [Fact]
    public async Task The_open_is_stored_beside_the_close_and_the_close_is_what_it_was()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        var (home, away) = (s.Game.HomeTeamName, s.Game.AwayTeamName);

        await IngestAsync(db, s, new StatsFetchResult([s.Game], [], [], new FetchCost(1),
        [
            Close(s, "ESPN BET", Markets.Moneyline, home, null, -180, new CanonicalOpening(null, -150)),
            Close(s, "ESPN BET", Markets.Moneyline, away, null, 150),
            Close(s, "ESPN BET", Markets.Spread, home, -4.5m, -110, new CanonicalOpening(-3m, null)),
            Close(s, "ESPN BET", Markets.Total, "over", 44.5m, -108, new CanonicalOpening(46.5m, -110))
        ]));

        var closes = await ClosesAsync(db, s);

        Assert.Equal((null, -180, null, -150), Shape(closes[(Markets.Moneyline, home)]));
        Assert.Equal((null, 150, null, null), Shape(closes[(Markets.Moneyline, away)]));
        Assert.Equal((-4.5m, -110, -3m, null), Shape(closes[(Markets.Spread, home)]));
        Assert.Equal((44.5m, -108, 46.5m, -110), Shape(closes[(Markets.Total, "over")]));

        // A reader of the close — the total the refill is owed — sees the close, and the game is not owed.
        Assert.False(await Service(db).ClosingLinesOwed(s.Sport.Key).AnyAsync());

        static (decimal?, int, decimal?, int?) Shape(ClosingLine c) => (c.Line, c.PriceAmerican, c.OpenLine, c.OpenPriceAmerican);
    }

    [Fact]
    public async Task A_held_close_gets_its_open_from_a_later_walk_of_the_same_book_and_never_loses_it()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        // Stored before opens were read.
        await IngestAsync(db, s, new StatsFetchResult([s.Game], [], [], new FetchCost(1),
            [Close(s, "ESPN BET", Markets.Total, "over", 44.5m, -108), Close(s, "ESPN BET", Markets.Total, "under", 44.5m, -112)]));

        // A re-walk with opens: the close it states differently is not taken, the open is.
        await IngestAsync(db, s, new StatsFetchResult([s.Game], [], [], new FetchCost(1),
        [
            Close(s, "ESPN BET", Markets.Total, "over", 43.5m, -120, new CanonicalOpening(46.5m, -110)),
            // Another book's open is not this close's open.
            Close(s, "DraftKings", Markets.Total, "under", 45m, -110, new CanonicalOpening(47m, -105))
        ]));

        // And a walk that states no open erases none.
        await IngestAsync(db, s, new StatsFetchResult([s.Game], [], [], new FetchCost(1),
            [Close(s, "ESPN BET", Markets.Total, "over", 44.5m, -108)]));

        var closes = await ClosesAsync(db, s);
        var over = closes[(Markets.Total, "over")];
        var under = closes[(Markets.Total, "under")];

        Assert.Equal((44.5m, -108, 46.5m, -110), (over.Line, over.PriceAmerican, over.OpenLine, over.OpenPriceAmerican));
        Assert.Equal(("ESPN BET", 44.5m, -112, (decimal?)null, (int?)null),
            (under.Book, under.Line, under.PriceAmerican, under.OpenLine, under.OpenPriceAmerican));
    }

    [Fact]
    public void An_open_of_another_book_or_of_nothing_changes_nothing()
    {
        var row = new ClosingLine { Book = "ESPN BET", Market = Markets.Spread, Outcome = "PHI", Line = -3m, PriceAmerican = -110, OpenLine = -2.5m };
        CanonicalOdds Line(string book, CanonicalOpening? opening)
            => new("e", book, Markets.Spread, "PHI", -3m, -110, DateTimeOffset.UnixEpoch) { Opening = opening };

        Assert.False(StatsIngestionService.ApplyOpening(row, Line("DraftKings", new CanonicalOpening(-1m, -105))));
        Assert.False(StatsIngestionService.ApplyOpening(row, Line("ESPN BET", new CanonicalOpening(null, null))));
        Assert.False(StatsIngestionService.ApplyOpening(row, Line("ESPN BET", null)));
        Assert.True(StatsIngestionService.ApplyOpening(row, Line("espn bet", new CanonicalOpening(null, -115))));
        Assert.Equal((-2.5m, -115), (row.OpenLine, row.OpenPriceAmerican));
    }

    // ---- MLB's hydrations, through the spine ---------------------------------------------------

    [Fact]
    public async Task The_spine_writes_mlbs_context_under_its_own_personal_use_source()
    {
        await using var db = fixture.CreateContext();
        var sport = await db.Sports.FirstOrDefaultAsync(s => s.Key == "mlb")
                    ?? db.Sports.Add(new Sport { Key = "mlb", Name = "MLB" }).Entity;
        await db.SaveChangesAsync();

        var tag = Guid.NewGuid().ToString("N")[..6];
        var home = new Team { SportId = sport.Id, Name = $"Home {tag}" };
        var away = new Team { SportId = sport.Id, Name = $"Away {tag}" };
        db.Teams.AddRange(home, away);
        await db.SaveChangesAsync();

        var day = new DateOnly(2031, 1, 1).AddDays(Random.Shared.Next(3000, 6000));
        var game = new Game
        {
            SportId = sport.Id, HomeTeamId = home.Id, AwayTeamId = away.Id, SeasonYear = day.Year,
            StartsAt = LeagueClock.StartOf(day).AddHours(19), Status = GameStatus.Scheduled
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();

        var pk = 900000 + Random.Shared.Next(0, 99999);
        var scheduled = new MlbScheduledGame(pk, game.StartsAt, "R", 1, "N", "Scheduled",
            new MlbSide(200000 + home.Id, home.Name, null, null, null),
            new MlbSide(200000 + away.Id, away.Name, null, null, null))
        {
            Context = new CanonicalGameContext(pk.ToString())
            {
                VenueName = "Wrigley Field", DayNight = GameDayNight.Night, WindSpeedMph = 14, WindDirection = "Out To LF",
                Officials = [new("Pat Hoberg", "Home Plate")],
                HomeLineup = [new(1, "1", "Leadoff Hitter", "CF")]
            }
        };

        var spine = new MlbSpineService(db,
            new MlbStatsApiAdapter(new HttpClient(), Options.Create(new IngestionOptions()), NullLogger<MlbStatsApiAdapter>.Instance),
            NullLogger<MlbSpineService>.Instance);

        var outcome = await spine.ApplyAsync(day, [scheduled]);

        var source = await db.Sources.AsNoTracking().SingleAsync(s => s.Key == MlbSpineService.Key);
        var context = await db.GameContexts.AsNoTracking().SingleAsync(c => c.GameId == game.Id);

        Assert.Equal(2, outcome.Contexts);
        Assert.Equal((SourceLicence.PersonalUse, false), (source.Licence, source.Enabled));
        Assert.Equal(source.Id, context.SourceId);
        Assert.Equal(("Wrigley Field", GameDayNight.Night, 14, "Out To LF"),
            (context.VenueName, context.DayNight, context.WindSpeedMph, context.WindDirection));
        Assert.Equal("Leadoff Hitter", GameContext.ParseLineup(context.HomeLineup).Single().FullName);
        Assert.Equal("Pat Hoberg", (await db.GameOfficials.AsNoTracking().SingleAsync(o => o.GameId == game.Id)).Name);
    }
}
