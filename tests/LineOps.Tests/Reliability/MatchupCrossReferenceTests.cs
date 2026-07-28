using System.Text.Json;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Data.CrossReference;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Tests.Reliability;

/// <summary>
/// The odds-to-history lookup: from a priced game, through its two rosters, to what those
/// players have actually done lately.
///
/// The rule worth pinning is that a player who did not play produces nothing — no row here,
/// just as there is no stat row in storage. Zero and absent are different facts and the
/// difference has to survive the whole way to the screen.
/// </summary>
[Collection(PostgresCollection.Name)]
public class MatchupCrossReferenceTests(PostgresFixture fixture)
{
    private sealed record Scaffold(Sport Sport, Team Home, Team Away, Source Source);

    private static async Task<Scaffold> SeedAsync(LineOpsDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var sport = new Sport { Key = $"xref-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = $"Home {suffix}", Abbrev = "HOM" };
        var away = new Team { SportId = sport.Id, Name = $"Away {suffix}", Abbrev = "AWY" };
        db.Teams.AddRange(home, away);

        var source = new Source
        {
            Key = $"xref-src-{suffix}",
            Name = "Test source",
            Kind = SourceKind.Stats,
            BaseUrl = "local://test"
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();

        return new Scaffold(sport, home, away, source);
    }

    private static async Task<Game> AddGameAsync(
        LineOpsDbContext db, Scaffold s, DateTimeOffset startsAt, GameStatus status = GameStatus.Final)
    {
        var game = new Game
        {
            SportId = s.Sport.Id,
            HomeTeamId = s.Home.Id,
            AwayTeamId = s.Away.Id,
            StartsAt = startsAt,
            Status = status
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game;
    }

    private static async Task<Player> AddPlayerAsync(
        LineOpsDbContext db, Scaffold s, int teamId, string name, string position)
    {
        var player = new Player
        {
            SportId = s.Sport.Id,
            TeamId = teamId,
            FullName = name,
            Position = position
        };
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player;
    }

    private static async Task AddLineAsync(
        LineOpsDbContext db, Scaffold s, Player player, Game game, Dictionary<string, string> stats)
    {
        db.PlayerGameStats.Add(new PlayerGameStat
        {
            PlayerId = player.Id,
            GameId = game.Id,
            SourceId = s.Source.Id,
            StatLine = JsonSerializer.Serialize(stats),
            CapturedAt = DateTimeOffset.UtcNow,
            IngestionRunId = 0
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PlayersWhoDidNotPlayProduceNoRowAtAll()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var upcoming = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddHours(4), GameStatus.Scheduled);
        var past = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(-3));

        var played = await AddPlayerAsync(db, s, s.Home.Id, "Played Recently", "1B");
        await AddPlayerAsync(db, s, s.Home.Id, "Did Not Play", "RP");

        await AddLineAsync(db, s, played, past, new() { ["AB"] = "4", ["H"] = "2" });

        var form = await new MatchupCrossReference(db).GetAsync(upcoming.Id, TimeSpan.FromDays(30));

        Assert.NotNull(form);

        // Absent, not zero. A bench arm with no appearances is not a player who went 0-for-4.
        var names = form!.Home.Select(p => p.Name).ToList();
        Assert.Contains("Played Recently", names);
        Assert.DoesNotContain("Did Not Play", names);
    }

    [Fact]
    public async Task FormIsSplitBySideAndCountsAppearances()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var upcoming = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddHours(4), GameStatus.Scheduled);
        var g1 = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(-2));
        var g2 = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(-5));

        var homePlayer = await AddPlayerAsync(db, s, s.Home.Id, "Home Bat", "CF");
        var awayPlayer = await AddPlayerAsync(db, s, s.Away.Id, "Away Bat", "SS");

        await AddLineAsync(db, s, homePlayer, g1, new() { ["AB"] = "4", ["H"] = "2" });
        await AddLineAsync(db, s, homePlayer, g2, new() { ["AB"] = "3", ["H"] = "1" });
        await AddLineAsync(db, s, awayPlayer, g1, new() { ["AB"] = "5", ["H"] = "0" });

        var form = await new MatchupCrossReference(db).GetAsync(upcoming.Id, TimeSpan.FromDays(30));

        var home = Assert.Single(form!.Home);
        var away = Assert.Single(form.Away);

        Assert.Equal(2, home.Appearances);
        Assert.Equal(1, away.Appearances);

        // Totals accumulate across appearances; averages divide by the games that had a reading.
        Assert.Equal(7d, home.Total("AB"));
        Assert.Equal(3d, home.Total("H"));
        Assert.Equal(1.5d, home.PerGame("H"));
    }

    [Fact]
    public async Task OnlyGamesInsideTheWindowAndBeforeThisOneAreCounted()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var upcoming = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddHours(4), GameStatus.Scheduled);
        var recent = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(-3));
        var ancient = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(-120));
        var later = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(6), GameStatus.Scheduled);

        var player = await AddPlayerAsync(db, s, s.Home.Id, "Windowed", "LF");

        await AddLineAsync(db, s, player, recent, new() { ["H"] = "2" });
        await AddLineAsync(db, s, player, ancient, new() { ["H"] = "9" });
        await AddLineAsync(db, s, player, later, new() { ["H"] = "5" });

        var form = await new MatchupCrossReference(db).GetAsync(upcoming.Id, TimeSpan.FromDays(30));

        var row = Assert.Single(form!.Home);

        // Form means recent form: the 120-day-old game is outside the window, and a game after
        // the one being priced has not happened yet from this vantage point.
        Assert.Equal(1, row.Appearances);
        Assert.Equal(2d, row.Total("H"));
    }

    [Fact]
    public async Task NonNumericStatValuesAreSkippedRatherThanCoerced()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var upcoming = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddHours(4), GameStatus.Scheduled);
        var past = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(-1));
        var player = await AddPlayerAsync(db, s, s.Home.Id, "Mixed Line", "P");

        await AddLineAsync(db, s, player, past, new()
        {
            ["IP"] = "6.0",
            ["TOI"] = "39:09",     // a duration, not a number
            ["H-AB"] = "2-5",      // a compound, not a number
            ["category"] = "pitching",
            ["K"] = "9"
        });

        var form = await new MatchupCrossReference(db).GetAsync(upcoming.Id, TimeSpan.FromDays(30));
        var row = Assert.Single(form!.Home);

        Assert.Equal(9d, row.Total("K"));
        Assert.Equal(6d, row.Total("IP"));

        // A wrong number is worse than a missing one, and "category" is metadata either way.
        Assert.Null(row.Total("TOI"));
        Assert.Null(row.Total("H-AB"));
        Assert.Null(row.Total("category"));
    }

    [Fact]
    public async Task RatesAreAveragedAndCountsAreTotalled()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var upcoming = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddHours(4), GameStatus.Scheduled);
        var g1 = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(-1));
        var g2 = await AddGameAsync(db, s, DateTimeOffset.UtcNow.AddDays(-2));

        var player = await AddPlayerAsync(db, s, s.Home.Id, "Rate And Count", "DH");

        await AddLineAsync(db, s, player, g1, new() { ["H"] = "2", ["AB"] = "4", ["AVG"] = "0.300" });
        await AddLineAsync(db, s, player, g2, new() { ["H"] = "1", ["AB"] = "4", ["AVG"] = "0.200" });

        var form = await new MatchupCrossReference(db).GetAsync(upcoming.Id, TimeSpan.FromDays(30));
        var row = Assert.Single(form!.Home);

        // Hits and at-bats are counted, so they add up.
        Assert.True(row.IsCounting("H"));
        Assert.Equal(3d, row.Summary("H"));
        Assert.Equal(8d, row.Summary("AB"));

        // A batting average is a rate. Two games at .300 and .200 is .250 — emphatically not
        // the .500 a sum would report, which is what this whole distinction exists to prevent.
        Assert.False(row.IsCounting("AVG"));
        Assert.Equal(0.250d, row.Summary("AVG")!.Value, precision: 3);
    }

    [Fact]
    public async Task AnUnknownGameReturnsNothingRatherThanThrowing()
    {
        await using var db = fixture.CreateContext();
        Assert.Null(await new MatchupCrossReference(db).GetAsync(-1, TimeSpan.FromDays(30)));
    }
}
