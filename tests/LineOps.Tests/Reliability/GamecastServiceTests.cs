using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Data.Gamecast;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Tests.Reliability;

/// <summary>
/// What the Gamecast window reads. Every open window asks for the plays after the last one it
/// holds on each <c>plays:{id}</c> notice, so that read must return exactly the new plays, in
/// order; and the window draws from records, never from jsonb, so every json column arrives
/// parsed — or as nothing, when it cannot be read.
/// </summary>
[Collection(PostgresCollection.Name)]
public class GamecastServiceTests(PostgresFixture fixture)
{
    /// <summary>A context per operation, the way the web host's factory hands them out.</summary>
    private sealed class Factory(PostgresFixture fixture) : IDbContextFactory<LineOpsDbContext>
    {
        public LineOpsDbContext CreateDbContext() => fixture.CreateContext();
    }

    private GamecastService Service() => new(new Factory(fixture));

    private sealed record Seeded(int GameId, int HomeTeamId, int AwayTeamId, int StatsSourceId, int OddsSourceId, string HomeName);

    /// <summary>LAR at home to LAC, live in the second quarter, with a stats and an odds source.</summary>
    private async Task<Seeded> SeedAsync(Action<Game>? live = null)
    {
        await using var db = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var stats = new Source { Key = $"gs-espn-{suffix}", Name = "Test ESPN", Kind = SourceKind.Stats, BaseUrl = "local://test" };
        var odds = new Source { Key = $"gs-odds-{suffix}", Name = "Test odds", Kind = SourceKind.Odds, BaseUrl = "local://test" };
        db.Sources.AddRange(stats, odds);

        var sport = new Sport { Key = $"gs-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = $"Rams {suffix}", Abbrev = "LAR" };
        var away = new Team { SportId = sport.Id, Name = $"Chargers {suffix}", Abbrev = "LAC" };
        db.Teams.AddRange(home, away);
        await db.SaveChangesAsync();

        var game = new Game
        {
            SportId = sport.Id,
            HomeTeamId = home.Id,
            AwayTeamId = away.Id,
            StartsAt = DateTimeOffset.UtcNow.AddHours(-1),
            Status = GameStatus.Live,
            HomeScore = 10,
            AwayScore = 3
        };
        live?.Invoke(game);
        db.Games.Add(game);
        await db.SaveChangesAsync();

        return new Seeded(game.Id, home.Id, away.Id, stats.Id, odds.Id, home.Name);
    }

    private static GamePlay Play(Seeded s, long sequence, int? drive, int? team = null) => new()
    {
        GameId = s.GameId,
        SourceId = s.StatsSourceId,
        SourcePlayId = $"p{sequence}",
        Sequence = sequence,
        DriveSeq = drive,
        Period = 2,
        Clock = "4:12",
        ClockSeconds = 252,
        Type = "Rush",
        Text = $"Play {sequence}",
        HomeScore = 10,
        AwayScore = 3,
        Yardage = 4,
        TeamId = team ?? s.AwayTeamId,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static GameDrive Drive(Seeded s, int sequence, int team, bool current = false) => new()
    {
        GameId = s.GameId,
        SourceId = s.StatsSourceId,
        SourceDriveId = $"d{sequence}",
        Sequence = sequence,
        TeamId = team,
        Result = current ? null : "TD",
        Yards = 62,
        OffensivePlays = 7,
        TimeElapsed = "3:48",
        IsScore = !current,
        IsCurrent = current,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private async Task AddAsync(params object[] rows)
    {
        await using var db = fixture.CreateContext();
        db.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static ClosingLine Close(Seeded s, string book, string market, string outcome, decimal line) => new()
    {
        GameId = s.GameId,
        SourceId = s.OddsSourceId,
        Book = book,
        Market = market,
        Outcome = outcome,
        Line = line,
        PriceAmerican = -110,
        CapturedAt = DateTimeOffset.UtcNow.AddHours(-1)
    };

    [Fact]
    public async Task TheHeaderCarriesTheStateOfPlayWithTheLinescoreParsed()
    {
        var s = await SeedAsync(g =>
        {
            g.Period = 2;
            g.DisplayClock = "4:12";
            g.ClockSeconds = 252;
            g.StatusDetail = "Q2 4:12";
            g.Down = 2;
            g.Distance = 6;
            g.YardLine = 80;
            g.DownDistanceText = "2nd & 6 at LAC 20";
            g.PossessionText = "LAC 20";
            g.IsRedZone = false;
            g.HomeTimeouts = 3;
            g.AwayTimeouts = 2;
            g.LastPlay = "J.Herbert pass short right";
            g.Linescore = """{"home":[7,3],"away":[3,0]}""";
        });

        await using (var db = fixture.CreateContext())
        {
            var game = await db.Games.SingleAsync(g => g.Id == s.GameId);
            game.PossessionTeamId = s.AwayTeamId;
            await db.SaveChangesAsync();
        }

        var header = await Service().HeaderAsync(s.GameId);

        Assert.NotNull(header);
        Assert.Equal(s.GameId, header.GameId);
        Assert.Equal("LAR", header.Home.Abbrev);
        Assert.Equal(s.AwayTeamId, header.Away.Id);
        Assert.Equal(GameStatus.Live, header.Status);
        Assert.Equal((10, 3), (header.HomeScore, header.AwayScore));
        Assert.Equal("Q2 4:12", header.StatusDetail);
        Assert.Equal(252, header.ClockSeconds);
        Assert.Equal(s.AwayTeamId, header.PossessionTeamId);
        Assert.Equal((2, 6, 80), (header.Down, header.Distance, header.YardLine));
        Assert.Equal((3, 2), (header.HomeTimeouts, header.AwayTimeouts));
        Assert.Equal([7, 3], header.HomeLinescore);
        Assert.Equal([3, 0], header.AwayLinescore);
        Assert.Null(header.Diamond);
        Assert.Null(header.Close);
    }

    [Fact]
    public async Task TheHeaderCarriesThePreGameCloseFromTheMarket()
    {
        var s = await SeedAsync();
        await AddAsync(
            Close(s, "draftkings", Markets.Spread, s.HomeName, -3.5m),
            Close(s, "draftkings", Markets.Total, "over", 44.5m),
            Close(s, "draftkings", Markets.Total, "under", 44.5m));

        var header = await Service().HeaderAsync(s.GameId);

        Assert.Equal(new GamecastClose(HomeSpread: -3.5m, Total: 44.5m, IsClosing: true), header!.Close);
    }

    [Fact]
    public async Task AnUnreadableLinescoreIsEmptyAndABaseballSituationIsParsed()
    {
        var s = await SeedAsync(g =>
        {
            // Valid json, the wrong shape: the column cannot hold anything worse.
            g.Linescore = """{"home":[7,"x"],"away":3}""";
            g.Situation = """{"balls":2,"strikes":1,"outs":2,"onFirst":true,"onSecond":false,"onThird":true,"batter":"A.Judge","pitcher":"G.Cole"}""";
        });

        var header = await Service().HeaderAsync(s.GameId);

        Assert.Empty(header!.HomeLinescore);
        Assert.Empty(header.AwayLinescore);
        Assert.Equal(new GamecastDiamond(2, 1, 2, true, false, true, "A.Judge", "G.Cole"), header.Diamond);
    }

    [Fact]
    public async Task AGameThatDoesNotExistHasNoHeader()
        => Assert.Null(await Service().HeaderAsync(int.MaxValue));

    [Fact]
    public async Task PlaysSinceReturnsOnlyTheLaterPlaysInSequenceOrder()
    {
        var s = await SeedAsync();
        var later = Play(s, 1003, 1);
        later.StartSituation = """{"down":1,"distance":10,"yardLine":75,"yardsToEndzone":75,"downDistanceText":"1st & 10 at LAC 25","possessionText":"LAC 25","teamId":""" + s.AwayTeamId + "}";
        later.EndSituation = """["not", "a", "situation"]""";
        later.HomeWinPct = 0.5235m;
        later.IsPenalty = true;
        await AddAsync(Play(s, 1002, 1), later, Play(s, 1001, 1), Play(s, 1004, 2));

        var plays = await Service().PlaysSinceAsync(s.GameId, 1002);

        Assert.Equal([1003L, 1004L], plays.Select(p => p.Sequence));
        var first = plays[0];
        Assert.Equal("Play 1003", first.Text);
        Assert.Equal(1, first.DriveSeq);
        Assert.True(first.IsPenalty);
        Assert.Equal(0.5235, first.HomeWinPct!.Value, 4);
        Assert.Equal(new GamecastSituation(1, 10, 75, 75, "1st & 10 at LAC 25", "LAC 25", s.AwayTeamId), first.Start);
        Assert.Null(first.End);

        Assert.Equal(4, (await Service().PlaysAsync(s.GameId)).Count);
        Assert.Empty(await Service().PlaysSinceAsync(s.GameId, 1004));
    }

    /// <summary>
    /// The adapter stores a play ESPN sent no sequenceNumber for at sequence 0, so "every play"
    /// is its own read rather than "after 0" — which would leave those out — and plays that share
    /// a sequence come back in the order they were stored.
    /// </summary>
    [Fact]
    public async Task EveryPlayIncludesThoseStoredAtSequenceZeroWithTiesInIdOrder()
    {
        var s = await SeedAsync();
        var first = Play(s, 0, null);
        var second = Play(s, 0, null);
        second.SourcePlayId = "p0b";
        await AddAsync(first);
        await AddAsync(second);
        await AddAsync(Play(s, 5, 1));

        var plays = await Service().PlaysAsync(s.GameId);

        Assert.Equal([0L, 0L, 5L], plays.Select(p => p.Sequence));
        Assert.True(plays[0].Id < plays[1].Id);
        Assert.Equal(first.Id, plays[0].Id);

        // After a sequence still means strictly after it, ties ordered the same way.
        Assert.Equal([5L], (await Service().PlaysSinceAsync(s.GameId, 0)).Select(p => p.Sequence));
    }

    [Fact]
    public async Task DrivesCarryTheirPlayCountsAndSequenceRangeInOrder()
    {
        var s = await SeedAsync();
        await AddAsync(
            Drive(s, 2, s.HomeTeamId, current: true),
            Drive(s, 1, s.AwayTeamId),
            Play(s, 1001, 1), Play(s, 1002, 1), Play(s, 1005, 1),
            Play(s, 1006, 2, s.HomeTeamId));

        var drives = await Service().DrivesAsync(s.GameId);

        Assert.Equal([1, 2], drives.Select(d => d.Sequence));
        var done = drives[0];
        Assert.Equal(s.AwayTeamId, done.TeamId);
        Assert.Equal("LAC", done.TeamAbbrev);
        Assert.Equal((7, 62, "3:48", "TD"), (done.OffensivePlays, done.Yards, done.TimeElapsed, done.Result));
        Assert.True(done.IsScore);
        Assert.False(done.IsCurrent);
        Assert.Equal((3, 1001L, 1005L), (done.PlayCount, done.FirstSequence, done.LastSequence));

        var current = drives[1];
        Assert.True(current.IsCurrent);
        Assert.Equal((1, 1006L, 1006L), (current.PlayCount, current.FirstSequence, current.LastSequence));
    }

    [Fact]
    public async Task ADriveWithNoPlaysYetHasNoRange()
    {
        var s = await SeedAsync();
        await AddAsync(Drive(s, 1, s.AwayTeamId, current: true));

        var drive = Assert.Single(await Service().DrivesAsync(s.GameId));

        Assert.Equal(0, drive.PlayCount);
        Assert.Null(drive.FirstSequence);
        Assert.Null(drive.LastSequence);
    }

    [Fact]
    public async Task TheProbabilitySeriesSkipsPlaysWithoutAProbability()
    {
        var s = await SeedAsync();
        var a = Play(s, 1001, 1);
        a.HomeWinPct = 0.5m;
        a.SpreadCoverPctHome = 0.48m;
        a.TotalOverPct = 0.51m;
        var b = Play(s, 1002, 1);
        var c = Play(s, 1003, 1);
        c.HomeWinPct = 0.62m;
        await AddAsync(c, b, a);

        var series = await Service().ProbabilitySeriesAsync(s.GameId);

        Assert.Equal([1001L, 1003L], series.Select(p => p.Sequence));
        Assert.Equal(new GamecastProbability(1001, 2, "4:12", 0.5, 0.48, 0.51), series[0]);
        Assert.Null(series[1].SpreadCoverHome);
    }

    [Fact]
    public async Task ScoringPlaysAreOnlyTheScoresInOrder()
    {
        var s = await SeedAsync();
        var td = Play(s, 1004, 1);
        td.IsScoring = true;
        var fg = Play(s, 1009, 2, s.HomeTeamId);
        fg.IsScoring = true;
        await AddAsync(fg, Play(s, 1001, 1), td);

        var scores = await Service().ScoringPlaysAsync(s.GameId);

        Assert.Equal([1004L, 1009L], scores.Select(p => p.Sequence));
        Assert.All(scores, p => Assert.True(p.IsScoring));
    }
}
