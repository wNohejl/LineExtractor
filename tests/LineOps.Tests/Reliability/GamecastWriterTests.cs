using System.Text.Json.Nodes;
using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Data.Changes;
using LineOps.Ingestion.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace LineOps.Tests.Reliability;

/// <summary>
/// The play store, upserted a poll at a time. The feed writes the whole gamecast every fifteen
/// seconds while a game is on and a saved row is what raises <c>plays:{id}</c>, so a poll that
/// saw nothing new must write nothing, and a play ESPN rewrote after the fact must change in
/// place rather than arrive twice.
/// </summary>
[Collection(PostgresCollection.Name)]
public class GamecastWriterTests(PostgresFixture fixture)
{
    /// <summary>Counts the rows each <c>SaveChanges</c> wrote, and how many saves there were.</summary>
    private sealed class RowCounter : SaveChangesInterceptor
    {
        public int Rows { get; private set; }
        public int Saves { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Rows += result;
            Saves++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed record Seeded(int GameId, int HomeTeamId, int AwayTeamId, string SourceKey);

    /// <summary>A fresh context, the way each poll gets one.</summary>
    private LineOpsDbContext Context(params IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<LineOpsDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(interceptors)
            .Options);

    /// <summary>A source, a sport, LAR (ESPN 14) at home to LAC (ESPN 24), and their game.</summary>
    private async Task<Seeded> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sourceKey = $"gc-{suffix}";

        db.Sources.Add(new Source { Key = sourceKey, Name = "Test ESPN", Kind = SourceKind.Stats, BaseUrl = "local://test" });

        var sport = new Sport { Key = $"gc-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = $"Rams {suffix}", Abbrev = "LAR", ExternalIds = new() { [sourceKey] = "14" } };
        var away = new Team { SportId = sport.Id, Name = $"Chargers {suffix}", Abbrev = "LAC", ExternalIds = new() { [sourceKey] = "24" } };
        db.Teams.AddRange(home, away);
        await db.SaveChangesAsync();

        var game = new Game
        {
            SportId = sport.Id,
            HomeTeamId = home.Id,
            AwayTeamId = away.Id,
            StartsAt = DateTimeOffset.UtcNow.AddHours(-1),
            Status = GameStatus.Live
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();

        return new Seeded(game.Id, home.Id, away.Id, sourceKey);
    }

    private static readonly DateTimeOffset Kickoff = new(2026, 11, 1, 20, 25, 0, TimeSpan.Zero);

    private static CanonicalDrive Drive(string id, int sequence, string team, bool current = false, string? result = "Punt")
        => new(id, sequence, team, $"{sequence} plays", current ? null : result, 12, 4, "2:10",
            IsScore: false, IsCurrent: current, StartPeriod: 1, StartClock: "15:00", StartYardLine: 25,
            EndPeriod: 1, EndClock: "12:50", EndYardLine: current ? null : 37);

    private static CanonicalPlay Play(
        string id, long sequence, string? drive, string team, string text = "Pass short right", DateTimeOffset? modified = null)
        => new(id, sequence, drive, Period: 1, Clock: "14:20", ClockSeconds: 860, TypeId: "24", Type: "Pass Reception",
            Text: text, HomeScore: 0, AwayScore: 0, IsScoring: false, IsTurnover: false, IsPenalty: false, Yardage: 7,
            SourceTeamId: team,
            Start: new CanonicalPlaySituation(1, 10, 25, 75, "1st & 10 at LAC 25", "LAC 25", team),
            End: new CanonicalPlaySituation(2, 3, 32, 68, "2nd & 3 at LAC 32", "LAC 32", team),
            // More places than the column keeps, and an offset: both must read back as unchanged.
            HomeWinPct: 0.523456m, SpreadCoverPctHome: 0.4812m, TotalOverPct: null,
            Wallclock: new DateTimeOffset(2026, 11, 1, 13, 26, 5, TimeSpan.FromHours(-7)),
            Modified: modified ?? Kickoff.AddMinutes(2));

    /// <summary>Two drives — LAC's done, LAR's in progress — and three plays across them.</summary>
    private static CanonicalGamecast Gamecast(params CanonicalPlay[] extra)
        => new("401873032", "in",
            [Drive("40187303201", 1, "24"), Drive("40187303202", 2, "14", current: true)],
            [
                Play("4018730321001", 1001, "40187303201", "24"),
                Play("4018730321002", 1002, "40187303201", "24", "Run left for 5"),
                Play("4018730321003", 1003, "40187303202", "14", "Kickoff returned"),
                .. extra
            ]);

    [Fact]
    public async Task TheFirstWriteInsertsEveryPlayAndDrive()
    {
        var seeded = await SeedAsync();

        GamecastWriteResult result;
        await using (var db = Context())
            result = await new GamecastWriter(db).WriteAsync(seeded.GameId, seeded.SourceKey, Gamecast(), CancellationToken.None);

        Assert.Equal(new GamecastWriteResult(PlaysInserted: 3, PlaysUpdated: 0, DrivesInserted: 2, DrivesUpdated: 0), result);

        await using var read = fixture.CreateContext();
        var plays = await read.GamePlays.Where(p => p.GameId == seeded.GameId).OrderBy(p => p.Sequence).ToListAsync();
        var drives = await read.GameDrives.Where(d => d.GameId == seeded.GameId).OrderBy(d => d.Sequence).ToListAsync();

        Assert.Equal(["4018730321001", "4018730321002", "4018730321003"], plays.Select(p => p.SourcePlayId));
        Assert.Equal([1, 2], drives.Select(d => d.Sequence));

        var first = plays[0];
        Assert.Equal("Pass short right", first.Text);
        Assert.Equal(seeded.AwayTeamId, first.TeamId);
        Assert.Equal(0.5235m, first.HomeWinPct);
        Assert.Equal(0.4812m, first.SpreadCoverPctHome);
        Assert.Null(first.TotalOverPct);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 20, 26, 5, TimeSpan.Zero), first.Wallclock);
        Assert.Equal(1, JsonNode.Parse(first.StartSituation!)!["down"]!.GetValue<int>());
        Assert.Equal("2nd & 3 at LAC 32", JsonNode.Parse(first.EndSituation!)!["downDistanceText"]!.GetValue<string>());

        Assert.Equal(seeded.AwayTeamId, drives[0].TeamId);
        Assert.Equal(seeded.HomeTeamId, drives[1].TeamId);
        Assert.False(drives[0].IsCurrent);
        Assert.True(drives[1].IsCurrent);
        Assert.Equal("Punt", drives[0].Result);
    }

    [Fact]
    public async Task TheSameGamecastAgainWritesNothing()
    {
        var seeded = await SeedAsync();

        await using (var db = Context())
            await new GamecastWriter(db).WriteAsync(seeded.GameId, seeded.SourceKey, Gamecast(), CancellationToken.None);

        var counter = new RowCounter();
        GamecastWriteResult again;
        await using (var db = Context(counter))
            again = await new GamecastWriter(db).WriteAsync(seeded.GameId, seeded.SourceKey, Gamecast(), CancellationToken.None);

        Assert.Equal(new GamecastWriteResult(0, 0, 0, 0), again);
        Assert.False(again.Changed);
        Assert.Equal(0, counter.Saves);
    }

    [Fact]
    public async Task AStringLongerThanItsColumnIsCutToFitRatherThanFailingTheSave()
    {
        var seeded = await SeedAsync();

        // Every short string over its column: one of them would fail the save for the whole game.
        var drive = Drive("40187303201", 1, "24", result: new string('R', 100)) with
        {
            TimeElapsed = new string('1', 30),
            StartClock = new string('2', 30),
            EndClock = new string('3', 30)
        };
        var play = Play("4018730321001", 1001, "40187303201", "24") with
        {
            Type = new string('T', 100),
            Clock = new string('9', 30)
        };
        var gamecast = new CanonicalGamecast("401873032", "in", [drive], [play]);

        await using (var db = Context())
            await new GamecastWriter(db).WriteAsync(seeded.GameId, seeded.SourceKey, gamecast, CancellationToken.None);

        await using (var read = fixture.CreateContext())
        {
            var stored = await read.GamePlays.SingleAsync(p => p.GameId == seeded.GameId);
            Assert.Equal(new string('T', 64), stored.Type);
            Assert.Equal(new string('9', 16), stored.Clock);

            var storedDrive = await read.GameDrives.SingleAsync(d => d.GameId == seeded.GameId);
            Assert.Equal(new string('R', 64), storedDrive.Result);
            Assert.Equal(16, storedDrive.TimeElapsed!.Length);
            Assert.Equal(16, storedDrive.StartClock!.Length);
            Assert.Equal(16, storedDrive.EndClock!.Length);
        }

        // And the cut value is what the next poll compares against, so it reads as unchanged.
        await using (var db = Context())
        {
            var again = await new GamecastWriter(db).WriteAsync(seeded.GameId, seeded.SourceKey, gamecast, CancellationToken.None);
            Assert.False(again.Changed);
        }
    }

    [Fact]
    public async Task APlayEspnRewroteIsUpdatedInPlace()
    {
        var seeded = await SeedAsync();

        await using (var db = Context())
            await new GamecastWriter(db).WriteAsync(seeded.GameId, seeded.SourceKey, Gamecast(), CancellationToken.None);

        long id;
        await using (var read = fixture.CreateContext())
            id = (await read.GamePlays.SingleAsync(p => p.GameId == seeded.GameId && p.SourcePlayId == "4018730321002")).Id;

        // A review: the run is now a fumble, and ESPN stamps the play modified.
        var reviewed = Gamecast() with
        {
            Plays =
            [
                Play("4018730321001", 1001, "40187303201", "24"),
                Play("4018730321002", 1002, "40187303201", "24", "Run left for 5, FUMBLES (reversed on review)", Kickoff.AddMinutes(6)),
                Play("4018730321003", 1003, "40187303202", "14", "Kickoff returned")
            ]
        };

        var counter = new RowCounter();
        GamecastWriteResult result;
        await using (var db = Context(counter))
            result = await new GamecastWriter(db).WriteAsync(seeded.GameId, seeded.SourceKey, reviewed, CancellationToken.None);

        Assert.Equal(new GamecastWriteResult(0, 1, 0, 0), result);
        Assert.Equal(1, counter.Saves);

        await using var after = fixture.CreateContext();
        var plays = await after.GamePlays.Where(p => p.GameId == seeded.GameId).ToListAsync();
        Assert.Equal(3, plays.Count);

        var play = plays.Single(p => p.SourcePlayId == "4018730321002");
        Assert.Equal(id, play.Id);
        Assert.Equal("Run left for 5, FUMBLES (reversed on review)", play.Text);
        Assert.Equal(Kickoff.AddMinutes(6), play.Modified);
    }

    [Fact]
    public async Task ANewPlayIsAddedAndNothingIsDeleted()
    {
        var seeded = await SeedAsync();

        await using (var db = Context())
            await new GamecastWriter(db).WriteAsync(seeded.GameId, seeded.SourceKey, Gamecast(), CancellationToken.None);

        // The next poll: one more play, and a payload that has dropped an earlier one.
        var next = Gamecast(Play("4018730321004", 1004, "40187303202", "14", "Pass deep left")) is var g
            ? g with { Plays = g.Plays.Where(p => p.SourcePlayId != "4018730321001").ToList() }
            : null!;

        GamecastWriteResult result;
        await using (var db = Context())
            result = await new GamecastWriter(db).WriteAsync(seeded.GameId, seeded.SourceKey, next, CancellationToken.None);

        Assert.Equal(new GamecastWriteResult(1, 0, 0, 0), result);

        await using var read = fixture.CreateContext();
        Assert.Equal(4, await read.GamePlays.CountAsync(p => p.GameId == seeded.GameId));
    }

    [Fact]
    public async Task DriveSeqLinksEachPlayToItsDrive()
    {
        var seeded = await SeedAsync();
        var gamecast = Gamecast(Play("4018730321009", 1009, "no-such-drive", "14"));

        await using (var db = Context())
            await new GamecastWriter(db).WriteAsync(seeded.GameId, seeded.SourceKey, gamecast, CancellationToken.None);

        await using var read = fixture.CreateContext();
        var drives = await read.GameDrives.Where(d => d.GameId == seeded.GameId).ToDictionaryAsync(d => d.Sequence);
        var plays = await read.GamePlays.Where(p => p.GameId == seeded.GameId).ToDictionaryAsync(p => p.SourcePlayId);

        Assert.Equal(1, plays["4018730321001"].DriveSeq);
        Assert.Equal(1, plays["4018730321002"].DriveSeq);
        Assert.Equal(2, plays["4018730321003"].DriveSeq);
        Assert.Null(plays["4018730321009"].DriveSeq);

        // The drive a play names is the drive of the team that has the ball in it.
        Assert.Equal(drives[2].TeamId, plays["4018730321003"].TeamId);
    }

    [Fact]
    public async Task ATeamNamingNeitherSideReadsAsNoOneAndIsNeverCreated()
    {
        var seeded = await SeedAsync();
        var gamecast = Gamecast(Play("4018730321005", 1005, "40187303202", "999"));

        int teamsBefore;
        await using (var read = fixture.CreateContext())
            teamsBefore = await read.Teams.CountAsync();

        await using (var db = Context())
            await new GamecastWriter(db).WriteAsync(seeded.GameId, seeded.SourceKey, gamecast, CancellationToken.None);

        await using var after = fixture.CreateContext();
        Assert.Null((await after.GamePlays.SingleAsync(p => p.GameId == seeded.GameId && p.SourcePlayId == "4018730321005")).TeamId);
        Assert.Equal(teamsBefore, await after.Teams.CountAsync());
    }

    [Fact]
    public async Task AWriteIsAnnouncedAsPlaysForItsGame()
    {
        var seeded = await SeedAsync();

        var heard = new List<string>();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        connection.Notification += (_, e) => { lock (heard) heard.Add(e.Payload); };

        await using (var listen = new NpgsqlCommand($"LISTEN {DataTopics.Channel}", connection))
            await listen.ExecuteNonQueryAsync();

        await using (var db = Context(new ChangeNotifier(NullLogger<ChangeNotifier>.Instance)))
            await new GamecastWriter(db).WriteAsync(seeded.GameId, seeded.SourceKey, Gamecast(), CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            while (true)
                await connection.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        lock (heard)
            Assert.Equal($"plays:{seeded.GameId}", Assert.Single(heard));
    }

    [Fact]
    public async Task AnUnregisteredSourceIsRefused()
    {
        var seeded = await SeedAsync();

        await using var db = Context();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new GamecastWriter(db).WriteAsync(seeded.GameId, "no-such-source", Gamecast(), CancellationToken.None));
    }
}
