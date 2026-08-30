using System.Text.Json;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Data.CrossReference;

namespace LineOps.Tests.Reliability;

/// <summary>
/// Game-grain reads: the rows a record is made of, and how each sat against the number.
///
/// The rules worth pinning are the ones where being wrong is invisible. A game with no stored
/// closing line must come back ungraded rather than graded as a loss; the ATS column must be
/// read from the handicap quoted against <i>that</i> team; and consensus across disagreeing
/// books must not be moved by one stale outlier.
/// </summary>
[Collection(PostgresCollection.Name)]
public class GameLogServiceTests(PostgresFixture fixture)
{
    private sealed record Scaffold(Sport Sport, Team Home, Team Away, Source Source);

    private static async Task<Scaffold> SeedAsync(LineOpsDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var sport = new Sport { Key = $"log-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = $"Home {suffix}", Abbrev = "HOM" };
        var away = new Team { SportId = sport.Id, Name = $"Away {suffix}", Abbrev = "AWY" };
        db.Teams.AddRange(home, away);

        var source = new Source
        {
            Key = $"log-src-{suffix}",
            Name = "Test source",
            Kind = SourceKind.Odds,
            BaseUrl = "local://test"
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();

        return new Scaffold(sport, home, away, source);
    }

    private static async Task<Game> AddGameAsync(
        LineOpsDbContext db,
        Scaffold s,
        DateTimeOffset startsAt,
        int? homeScore = null,
        int? awayScore = null,
        GameStatus status = GameStatus.Final,
        bool flipVenue = false)
    {
        var game = new Game
        {
            SportId = s.Sport.Id,
            HomeTeamId = flipVenue ? s.Away.Id : s.Home.Id,
            AwayTeamId = flipVenue ? s.Home.Id : s.Away.Id,
            StartsAt = startsAt,
            Status = status,
            HomeScore = homeScore,
            AwayScore = awayScore
        };

        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game;
    }

    private static async Task AddClosingAsync(
        LineOpsDbContext db, Scaffold s, Game game, string market, string outcome, decimal line,
        string book = "testbook")
    {
        db.ClosingLines.Add(new ClosingLine
        {
            GameId = game.Id,
            SourceId = s.Source.Id,
            Book = book,
            Market = market,
            Outcome = outcome,
            Line = line,
            PriceAmerican = -110,
            CapturedAt = game.StartsAt
        });

        await db.SaveChangesAsync();
    }

    private static GameLogService ServiceFor(LineOpsDbContext db) => new(db);

    [Fact]
    public async Task Team_log_returns_games_newest_first()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var now = DateTimeOffset.UtcNow;
        await AddGameAsync(db, s, now.AddDays(-5), 4, 2);
        await AddGameAsync(db, s, now.AddDays(-1), 3, 1);
        await AddGameAsync(db, s, now.AddDays(-3), 2, 5);

        var log = await ServiceFor(db).TeamGameLogAsync(s.Home.Id, TimeSpan.FromDays(30));

        Assert.Equal(3, log.Count);
        Assert.True(log[0].StartsAt > log[1].StartsAt);
        Assert.True(log[1].StartsAt > log[2].StartsAt);
    }

    /// <summary>
    /// The failure that would be invisible: a game nobody priced must not be graded. Reporting
    /// it as a loss against the spread would understate every team's ATS record by exactly the
    /// number of games the desk was not running for.
    /// </summary>
    [Fact]
    public async Task A_game_with_no_closing_line_is_ungraded_not_lost()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(-2), 7, 1);

        var log = await ServiceFor(db).TeamGameLogAsync(s.Home.Id, TimeSpan.FromDays(30));

        var row = Assert.Single(log);
        Assert.Null(row.AtsResult);
        Assert.Null(row.TotalResult);
        Assert.Null(row.ClosingSpread);
        Assert.False(row.HasClosingLine);

        // The result itself is still known — only the market reading is missing.
        Assert.True(row.Won);
    }

    [Fact]
    public async Task A_team_covering_its_spread_reads_as_a_cover()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        // Home wins by 4 as a 1.5-point favourite.
        var game = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(-2), 6, 2);
        await AddClosingAsync(db, s, game, Markets.Spread, s.Home.Name, -1.5m);

        var log = await ServiceFor(db).TeamGameLogAsync(s.Home.Id, TimeSpan.FromDays(30));

        Assert.Equal(EntryResult.Win, Assert.Single(log).AtsResult);
    }

    [Fact]
    public async Task A_favourite_winning_by_less_than_the_spread_does_not_cover()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        // Home wins by 1 as a 2.5-point favourite.
        var game = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(-2), 3, 2);
        await AddClosingAsync(db, s, game, Markets.Spread, s.Home.Name, -2.5m);

        var log = await ServiceFor(db).TeamGameLogAsync(s.Home.Id, TimeSpan.FromDays(30));

        Assert.Equal(EntryResult.Loss, Assert.Single(log).AtsResult);
    }

    /// <summary>
    /// The handicap has to be the one quoted against this team. Taking the opposite side's
    /// number and negating it looks equivalent and is not, because the two are only mirror
    /// images when the books agree.
    /// </summary>
    [Fact]
    public async Task The_away_team_is_graded_on_its_own_handicap()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        // Away wins by 3 as a 1.5-point underdog: covers comfortably.
        var game = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(-2), 2, 5);
        await AddClosingAsync(db, s, game, Markets.Spread, s.Away.Name, 1.5m);
        await AddClosingAsync(db, s, game, Markets.Spread, s.Home.Name, -1.5m);

        var awayLog = await ServiceFor(db).TeamGameLogAsync(s.Away.Id, TimeSpan.FromDays(30));
        var homeLog = await ServiceFor(db).TeamGameLogAsync(s.Home.Id, TimeSpan.FromDays(30));

        Assert.Equal(EntryResult.Win, Assert.Single(awayLog).AtsResult);
        Assert.Equal(EntryResult.Loss, Assert.Single(homeLog).AtsResult);
    }

    [Fact]
    public async Task A_whole_number_spread_landing_exactly_is_a_push()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        // Home wins by exactly 3 as a 3-point favourite.
        var game = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(-2), 6, 3);
        await AddClosingAsync(db, s, game, Markets.Spread, s.Home.Name, -3m);

        var log = await ServiceFor(db).TeamGameLogAsync(s.Home.Id, TimeSpan.FromDays(30));

        Assert.Equal(EntryResult.Push, Assert.Single(log).AtsResult);
    }

    [Fact]
    public async Task The_total_is_graded_from_the_overs_point_of_view()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        // Nine runs against a total of 8.5 — an over.
        var game = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(-2), 6, 3);
        await AddClosingAsync(db, s, game, Markets.Total, "over", 8.5m);

        var log = await ServiceFor(db).TeamGameLogAsync(s.Home.Id, TimeSpan.FromDays(30));
        var row = Assert.Single(log);

        Assert.Equal(EntryResult.Win, row.TotalResult);
        Assert.Equal(8.5m, row.ClosingTotal);
    }

    /// <summary>
    /// One book hanging a stale number must not drag the consensus. A mean would let it; the
    /// median is why it does not.
    /// </summary>
    [Fact]
    public async Task Consensus_across_books_ignores_an_outlier()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var game = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(-2), 5, 4);

        await AddClosingAsync(db, s, game, Markets.Total, "over", 8.5m, "book-a");
        await AddClosingAsync(db, s, game, Markets.Total, "over", 8.5m, "book-b");
        await AddClosingAsync(db, s, game, Markets.Total, "over", 20m, "stale-book");

        var log = await ServiceFor(db).TeamGameLogAsync(s.Home.Id, TimeSpan.FromDays(30));

        Assert.Equal(8.5m, Assert.Single(log).ClosingTotal);
    }

    [Fact]
    public async Task A_live_game_has_a_score_but_no_result()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var game = await AddGameAsync(
            db, s, DateTimeOffset.UtcNow.AddHours(-1), 2, 1, GameStatus.Live);

        await AddClosingAsync(db, s, game, Markets.Spread, s.Home.Name, -1.5m);

        var log = await ServiceFor(db).TeamGameLogAsync(s.Home.Id, TimeSpan.FromDays(30));
        var row = Assert.Single(log);

        Assert.Null(row.Won);
        Assert.Null(row.AtsResult);

        // The line was still captured — only the grading is withheld.
        Assert.Equal(-1.5m, row.ClosingSpread);
    }

    [Fact]
    public async Task Venue_is_read_from_the_teams_own_side()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(-2), 3, 1);
        await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(-4), 3, 1, flipVenue: true);

        var log = await ServiceFor(db).TeamGameLogAsync(s.Home.Id, TimeSpan.FromDays(30));

        Assert.Equal(2, log.Count);
        Assert.True(log[0].Home);
        Assert.False(log[1].Home);

        // Away from home, the scores swap round: the flipped game was a 1-3 defeat.
        Assert.Equal(1, log[1].ScoreFor);
        Assert.Equal(3, log[1].ScoreAgainst);
        Assert.False(log[1].Won);
    }

    [Fact]
    public async Task Head_to_head_excludes_the_game_it_was_asked_about()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var now = DateTimeOffset.UtcNow;
        await AddGameAsync(db, s, now.AddDays(-20), 3, 1);
        await AddGameAsync(db, s, now.AddDays(-10), 2, 4);
        var upcoming = await AddGameAsync(db, s, now.AddDays(1), status: GameStatus.Scheduled);

        var meetings = await ServiceFor(db).HeadToHeadAsync(upcoming.Id);

        Assert.Equal(2, meetings.Count);
        Assert.DoesNotContain(meetings, m => m.GameId == upcoming.Id);
    }

    [Fact]
    public async Task Head_to_head_finds_meetings_at_either_venue()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var now = DateTimeOffset.UtcNow;
        await AddGameAsync(db, s, now.AddDays(-20), 3, 1);
        await AddGameAsync(db, s, now.AddDays(-10), 5, 2, flipVenue: true);
        var upcoming = await AddGameAsync(db, s, now.AddDays(1), status: GameStatus.Scheduled);

        var meetings = await ServiceFor(db).HeadToHeadAsync(upcoming.Id);

        Assert.Equal(2, meetings.Count);
        Assert.Contains(meetings, m => m.SubjectWasHome);
        Assert.Contains(meetings, m => !m.SubjectWasHome);
    }

    /// <summary>
    /// Every row reads from the upcoming home team's side, so a column of winners means one
    /// thing all the way down rather than flipping with the venue.
    /// </summary>
    [Fact]
    public async Task Head_to_head_results_are_from_one_sides_perspective()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var now = DateTimeOffset.UtcNow;

        // Subject (s.Home) wins at home 3-1, then wins away 2-5 as the visitor.
        await AddGameAsync(db, s, now.AddDays(-20), 3, 1);
        await AddGameAsync(db, s, now.AddDays(-10), 2, 5, flipVenue: true);

        var upcoming = await AddGameAsync(db, s, now.AddDays(1), status: GameStatus.Scheduled);

        var meetings = await ServiceFor(db).HeadToHeadAsync(upcoming.Id);

        Assert.All(meetings, m => Assert.True(m.SubjectWon));
    }

    [Fact]
    public async Task Head_to_head_honours_its_limit()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var now = DateTimeOffset.UtcNow;

        for (var i = 2; i <= 9; i++)
            await AddGameAsync(db, s, now.AddDays(-i * 3), 3, 1);

        var upcoming = await AddGameAsync(db, s, now.AddDays(1), status: GameStatus.Scheduled);

        var meetings = await ServiceFor(db).HeadToHeadAsync(upcoming.Id, take: 5);

        Assert.Equal(5, meetings.Count);
    }

    [Fact]
    public async Task Teams_that_never_met_return_nothing()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var upcoming = await AddGameAsync(
            db, s, DateTimeOffset.UtcNow.AddDays(1), status: GameStatus.Scheduled);

        Assert.Empty(await ServiceFor(db).HeadToHeadAsync(upcoming.Id));
    }

    [Fact]
    public async Task Player_log_returns_appearances_newest_first_with_their_lines()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var player = new Player
        {
            SportId = s.Sport.Id,
            TeamId = s.Home.Id,
            FullName = "Test Player",
            Position = "SS"
        };

        db.Players.Add(player);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var older = await AddGameAsync(db, s, now.AddDays(-6), 3, 1);
        var newer = await AddGameAsync(db, s, now.AddDays(-2), 5, 4);

        foreach (var (game, hits) in new[] { (older, "1"), (newer, "3") })
        {
            db.PlayerGameStats.Add(new PlayerGameStat
            {
                PlayerId = player.Id,
                GameId = game.Id,
                SourceId = s.Source.Id,
                StatLine = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["H"] = hits,
                    ["AB"] = "4",
                    ["category"] = "batting"
                }),
                CapturedAt = game.StartsAt
            });
        }

        await db.SaveChangesAsync();

        var log = await ServiceFor(db).PlayerGameLogAsync(player.Id);

        Assert.Equal(2, log.Count);
        Assert.Equal("3", log[0].Stats["H"]);
        Assert.Equal("1", log[1].Stats["H"]);

        // The metadata key the box-score parser adds is not a stat and must not become a column.
        Assert.DoesNotContain("category", log[0].Stats.Keys);
    }

    [Fact]
    public async Task Player_log_reports_the_result_from_that_players_side()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var player = new Player
        {
            SportId = s.Sport.Id,
            TeamId = s.Away.Id,
            FullName = "Away Player"
        };

        db.Players.Add(player);
        await db.SaveChangesAsync();

        // Away wins 1-6.
        var game = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(-2), 1, 6);

        db.PlayerGameStats.Add(new PlayerGameStat
        {
            PlayerId = player.Id,
            GameId = game.Id,
            SourceId = s.Source.Id,
            StatLine = JsonSerializer.Serialize(new Dictionary<string, string> { ["H"] = "2" }),
            CapturedAt = game.StartsAt
        });

        await db.SaveChangesAsync();

        var row = Assert.Single(await ServiceFor(db).PlayerGameLogAsync(player.Id));

        Assert.False(row.Home);
        Assert.Equal(6, row.ScoreFor);
        Assert.Equal(1, row.ScoreAgainst);
        Assert.True(row.Won);
    }
}
