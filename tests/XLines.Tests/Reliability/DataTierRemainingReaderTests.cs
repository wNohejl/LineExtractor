using XLines.Core.Contracts;
using XLines.Core.Entities;
using XLines.Data;
using XLines.Data.CrossReference;
using XLines.Data.Gamecast;
using Microsoft.EntityFrameworkCore;

namespace XLines.Tests.Reliability;

/// <summary>
/// The readers ADR 0024 listed as remaining, held to the tier as <see cref="DataTierReaderTests"/>
/// holds the first three: the gamecast's plays and drives, the fight readers, the columns ESPN and
/// MLB write onto a game, the injury report and the search's order.
///
/// <para>
/// Each test reads the same rows twice, once as the operator (everyone on a local desk reads as
/// the operator: <see cref="FullDataTier"/>) and once as a visitor to a shared desk. The operator's
/// answer is the answer the reader gave before it asked; the visitor's has nothing a personal-use
/// source supplied, and says so in the words the desk already uses for a game with nothing yet.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class DataTierRemainingReaderTests(PostgresFixture fixture)
{
    private sealed class Visitor : IDataTier
    {
        public bool CanSeePersonalUseSources => false;
    }

    private static readonly IDataTier Operator = FullDataTier.Instance;
    private static readonly IDataTier Guest = new Visitor();

    private sealed class Contexts(PostgresFixture fixture) : IDbContextFactory<XLinesDbContext>
    {
        public XLinesDbContext CreateDbContext() => fixture.CreateContext();
    }

    private sealed record Scaffold(Sport Sport, Team Home, Team Away, Game Game, Source Market, Source Espn);

    /// <summary>
    /// A game under way in a league of its own, ESPN's state of play and MLB's probables on it, and
    /// two sources: a displayable book market and a personal-use stats feed in ESPN's place.
    /// </summary>
    private static async Task<Scaffold> SeedAsync(XLinesDbContext db, string? sportKey = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var sport = sportKey is null ? null : await db.Sports.FirstOrDefaultAsync(s => s.Key == sportKey);
        if (sport is null)
        {
            sport = new Sport { Key = sportKey ?? $"tier2-{suffix}", Name = "TEST" };
            db.Sports.Add(sport);
            await db.SaveChangesAsync();
        }

        var home = new Team { SportId = sport.Id, Name = $"Home {suffix}", Abbrev = "HOM" };
        var away = new Team { SportId = sport.Id, Name = $"Away {suffix}", Abbrev = "AWY" };
        db.Teams.AddRange(home, away);

        var market = new Source
        {
            Key = $"tier2-market-{suffix}",
            Name = "Market",
            Kind = SourceKind.Odds,
            BaseUrl = "local://market",
            Licence = SourceLicence.Displayable
        };
        var espn = new Source
        {
            Key = $"tier2-espn-{suffix}",
            Name = "Stats",
            Kind = SourceKind.Stats,
            BaseUrl = "local://stats",
            Licence = SourceLicence.PersonalUse
        };
        db.Sources.AddRange(market, espn);
        await db.SaveChangesAsync();

        var game = new Game
        {
            SportId = sport.Id,
            HomeTeamId = home.Id,
            AwayTeamId = away.Id,
            StartsAt = DateTimeOffset.UtcNow.AddHours(-1),
            Status = GameStatus.Live,
            HomeScore = 10,
            AwayScore = 3,
            SeasonYear = 2026,
            HomeProbablePitcher = "Chris Sale",
            AwayProbablePitcher = "Kodai Senga",
            Period = 2,
            DisplayClock = "4:12",
            ClockSeconds = 252,
            StatusDetail = "Q2 4:12",
            PossessionTeamId = away.Id,
            Down = 2,
            Distance = 6,
            YardLine = 34,
            DownDistanceText = "2nd & 6 at HOM 34",
            PossessionText = "HOM 34",
            IsRedZone = false,
            HomeTimeouts = 3,
            AwayTimeouts = 2,
            LastPlay = "A pass incomplete short left",
            Linescore = """{"home":[10,0],"away":[3,0]}""",
            Situation = """{"balls":1,"strikes":2,"outs":1}""",
            LiveUpdatedAt = DateTimeOffset.UtcNow
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();

        return new Scaffold(sport, home, away, game, market, espn);
    }

    // ---- The gamecast -----------------------------------------------------------------------

    /// <summary>
    /// Plays, drives and the probability line are ESPN's rows. A visitor's gamecast has none of
    /// them — the state a game is in before its first play — and the operator's is unchanged.
    /// </summary>
    [Fact]
    public async Task A_personal_use_gamecast_is_the_operators_alone()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        db.GameDrives.Add(new GameDrive
        {
            GameId = s.Game.Id,
            SourceId = s.Espn.Id,
            SourceDriveId = "d1",
            Sequence = 1,
            TeamId = s.Away.Id,
            Result = "TD",
            IsScore = true,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.GamePlays.AddRange(
            Play(s, 1001, scoring: false),
            Play(s, 1002, scoring: true));
        await db.SaveChangesAsync();

        var contexts = new Contexts(fixture);
        var operatorCast = new GamecastService(contexts, Operator);
        var visitorCast = new GamecastService(contexts, Guest);

        Assert.Equal(2, (await operatorCast.PlaysAsync(s.Game.Id)).Count);
        Assert.Single(await operatorCast.PlaysSinceAsync(s.Game.Id, 1001));
        Assert.Single(await operatorCast.ScoringPlaysAsync(s.Game.Id));
        Assert.Equal(2, Assert.Single(await operatorCast.DrivesAsync(s.Game.Id)).PlayCount);
        Assert.Equal(2, (await operatorCast.ProbabilitySeriesAsync(s.Game.Id)).Count);

        Assert.Empty(await visitorCast.PlaysAsync(s.Game.Id));
        Assert.Empty(await visitorCast.PlaysSinceAsync(s.Game.Id, 0));
        Assert.Empty(await visitorCast.ScoringPlaysAsync(s.Game.Id));
        Assert.Empty(await visitorCast.DrivesAsync(s.Game.Id));
        Assert.Empty(await visitorCast.ProbabilitySeriesAsync(s.Game.Id));
    }

    private static GamePlay Play(Scaffold s, long sequence, bool scoring) => new()
    {
        GameId = s.Game.Id,
        SourceId = s.Espn.Id,
        SourcePlayId = $"p{sequence}",
        Sequence = sequence,
        DriveSeq = 1,
        Text = $"Play {sequence}",
        IsScoring = scoring,
        HomeWinPct = 0.6m,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    // ---- The columns on a game --------------------------------------------------------------

    /// <summary>
    /// ESPN's state of play and MLB's probables are columns, so they are blanked rather than
    /// filtered: a visitor's game is its teams, its start, its status and its score.
    /// </summary>
    [Fact]
    public async Task A_visitors_game_carries_no_state_of_play_and_no_probables()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var operatorRow = Assert.Single(await new BoardService(db, Operator).GetAsync(s.Sport.Key, TimeSpan.FromHours(24)));
        Assert.Equal("A pass incomplete short left", operatorRow.Game.LastPlay);
        Assert.Equal("Chris Sale", operatorRow.Game.HomeProbablePitcher);
        Assert.Equal("Q2 4:12", (await new BoardService(db, Operator).GetRowAsync(s.Game.Id))!.Game.StatusDetail);

        foreach (var game in new[]
                 {
                     Assert.Single(await new BoardService(db, Guest).GetAsync(s.Sport.Key, TimeSpan.FromHours(24))).Game,
                     (await new BoardService(db, Guest).GetRowAsync(s.Game.Id))!.Game
                 })
        {
            AssertBlank(game);

            // The spine stays: the fixture and its score are what every reader hangs off.
            Assert.Equal(GameStatus.Live, game.Status);
            Assert.Equal(10, game.HomeScore);
            Assert.Equal(3, game.AwayScore);
            Assert.Equal(s.Home.Name, game.HomeTeam!.Name);
        }
    }

    /// <summary>The gamecast's header is the board's game, so it is blanked with it.</summary>
    [Fact]
    public async Task The_gamecast_header_carries_no_state_of_play_for_a_visitor()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        var contexts = new Contexts(fixture);

        var operatorHeader = (await new GamecastService(contexts, Operator).HeaderAsync(s.Game.Id))!;
        Assert.Equal("A pass incomplete short left", operatorHeader.LastPlay);
        Assert.Equal([10, 0], operatorHeader.HomeLinescore);
        Assert.NotNull(operatorHeader.Diamond);
        Assert.Equal(2, operatorHeader.Down);

        var visitorHeader = (await new GamecastService(contexts, Guest).HeaderAsync(s.Game.Id))!;
        Assert.Null(visitorHeader.LastPlay);
        Assert.Null(visitorHeader.StatusDetail);
        Assert.Null(visitorHeader.DisplayClock);
        Assert.Null(visitorHeader.Down);
        Assert.Null(visitorHeader.DownDistanceText);
        Assert.Null(visitorHeader.Diamond);
        Assert.Null(visitorHeader.LiveUpdatedAt);
        Assert.Empty(visitorHeader.HomeLinescore);
        Assert.Empty(visitorHeader.AwayLinescore);
        Assert.Equal(10, visitorHeader.HomeScore);
    }

    /// <summary>The helper changes nothing for a reader who may see everything.</summary>
    [Fact]
    public void The_operators_game_is_left_as_it_was()
    {
        var game = new Game { LastPlay = "x", HomeProbablePitcher = "y", Linescore = "{}", Period = 3 };

        Assert.Same(game, game.ForTier(Operator));
        Assert.Equal("x", game.LastPlay);
        Assert.Equal("y", game.HomeProbablePitcher);
        Assert.Equal(3, game.Period);
    }

    private static void AssertBlank(Game game)
    {
        Assert.Null(game.HomeProbablePitcher);
        Assert.Null(game.AwayProbablePitcher);
        Assert.Null(game.Period);
        Assert.Null(game.DisplayClock);
        Assert.Null(game.ClockSeconds);
        Assert.Null(game.StatusDetail);
        Assert.Null(game.PossessionTeamId);
        Assert.Null(game.Down);
        Assert.Null(game.Distance);
        Assert.Null(game.YardLine);
        Assert.Null(game.DownDistanceText);
        Assert.Null(game.PossessionText);
        Assert.Null(game.IsRedZone);
        Assert.Null(game.HomeTimeouts);
        Assert.Null(game.AwayTimeouts);
        Assert.Null(game.LastPlay);
        Assert.Null(game.Linescore);
        Assert.Null(game.Situation);
        Assert.Null(game.LiveUpdatedAt);
    }

    // ---- Fights -----------------------------------------------------------------------------

    /// <summary>
    /// A bout's card and a fighter's profile are ESPN's alone (no <c>SourceId</c>: the tables are
    /// single-source by construction). A visitor is told the game is a fight and who won it — the
    /// spine — and nothing of the card; the tape names the fighters and has no profile for either.
    /// </summary>
    [Fact]
    public async Task A_bouts_card_and_the_fighters_profiles_are_the_operators_alone()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db, "ufc");

        s.Game.Status = GameStatus.Final;
        db.Bouts.Add(new Bout
        {
            GameId = s.Game.Id,
            EventId = "600041234",
            EventName = "UFC 300: Pereira vs. Hill",
            EventShortName = "UFC 300",
            CardSegment = "Main Card",
            MatchNumber = 1,
            WeightClass = "Light Heavyweight",
            ScheduledRounds = 5,
            TitleFight = true,
            HomeRecord = "9-2-0",
            AwayRecord = "12-1-0",
            Outcome = BoutOutcome.Win,
            Method = "KO/TKO",
            MethodDetail = "Punch",
            EndRound = 1,
            EndSeconds = 194
        });
        db.FighterProfiles.AddRange(
            new FighterProfile { TeamId = s.Home.Id, Nickname = "Poatan", Stance = "Orthodox", Record = "9-2-0", RefreshedAt = DateTimeOffset.UtcNow },
            new FighterProfile { TeamId = s.Away.Id, Nickname = "Sweet Dreams", Stance = "Orthodox", Record = "12-1-0", RefreshedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        // The operator: everything ESPN stated.
        var operatorFights = new FightService(db, Operator);
        var operatorTape = (await operatorFights.TapeAsync(s.Game.Id))!;
        Assert.Equal("UFC 300", operatorTape.Bout.EventShortName);
        Assert.Equal("Poatan", operatorTape.Home.Profile!.Nickname);
        Assert.Contains(operatorTape.Physical, r => r.Label == "Record");
        Assert.Equal("KO/TKO", (await operatorFights.BoutsAsync([s.Game.Id]))[s.Game.Id].Method);
        Assert.NotNull((await operatorFights.FighterAsync(s.Home.Id))!.Profile);
        Assert.Equal(5, (await new BoardService(db, Operator).GetRowAsync(s.Game.Id))!.Bout!.ScheduledRounds);

        // The visitor: a fight, its two fighters and its winner; no card, no profile.
        var visitorFights = new FightService(db, Guest);
        var visitorTape = (await visitorFights.TapeAsync(s.Game.Id))!;
        AssertNoCard(visitorTape.Bout);
        Assert.Null(visitorTape.Home.Profile);
        Assert.Null(visitorTape.Away.Profile);
        Assert.Empty(visitorTape.Physical);
        Assert.Empty(visitorTape.Career);
        Assert.Empty(visitorTape.PerFight);
        Assert.Equal(s.Home.Name, visitorTape.Home.Team.Name);

        AssertNoCard((await visitorFights.BoutsAsync([s.Game.Id]))[s.Game.Id]);
        AssertNoCard((await visitorFights.BoutAsync(s.Game.Id))!.Value.Bout);
        Assert.Null((await visitorFights.FighterAsync(s.Home.Id))!.Profile);

        var visitorRow = (await new BoardService(db, Guest).GetRowAsync(s.Game.Id))!;
        AssertNoCard(visitorRow.Bout!);
    }

    private static void AssertNoCard(Bout bout)
    {
        Assert.Equal(string.Empty, bout.EventName);
        Assert.Null(bout.EventShortName);
        Assert.Null(bout.CardSegment);
        Assert.Null(bout.MatchNumber);
        Assert.Null(bout.WeightClass);
        Assert.Equal(0, bout.ScheduledRounds);
        Assert.False(bout.TitleFight);
        Assert.Null(bout.HomeRecord);
        Assert.Null(bout.AwayRecord);
        Assert.Null(bout.Method);
        Assert.Null(bout.MethodDetail);
        Assert.Null(bout.EndRound);
        Assert.Null(bout.EndSeconds);

        // Who won is to a bout what the final score is to a game.
        Assert.Equal(BoutOutcome.Win, bout.Outcome);
    }

    // ---- The injury report ------------------------------------------------------------------

    /// <summary>
    /// The board's quarterback is read from passing lines and the injury report. With the lines
    /// from a displayable source, a visitor still has a starter — but read as if no report named
    /// anyone, since the report is ESPN's. The operator's row weighs the report as before.
    /// </summary>
    [Fact]
    public async Task The_injury_report_does_not_choose_a_visitors_quarterback()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db, "nfl");
        var now = DateTimeOffset.UtcNow;

        s.Game.StartsAt = now.AddHours(4);
        s.Game.Status = GameStatus.Scheduled;

        Game Played(int daysAgo) => new()
        {
            SportId = s.Sport.Id,
            HomeTeamId = s.Home.Id,
            AwayTeamId = s.Away.Id,
            StartsAt = now.AddDays(-daysAgo),
            Status = GameStatus.Final,
            SeasonYear = 2026
        };

        var weekTwo = Played(50);
        var lastWeek = Played(8);
        db.Games.AddRange(weekTwo, lastWeek);

        var suffix = s.Home.Name[^8..];
        var murray = new Player
        {
            SportId = s.Sport.Id,
            TeamId = s.Home.Id,
            FullName = $"Kyler Murray {suffix}",
            InjuryStatus = "Out",
            InjuryNote = "Ruled out.",
            InjuryUpdatedAt = now.AddHours(-2)
        };
        var wentz = new Player
        {
            SportId = s.Sport.Id,
            TeamId = s.Home.Id,
            FullName = $"Carson Wentz {suffix}",
            InjuryStatus = "Active",
            InjuryUpdatedAt = now.AddHours(-2)
        };
        db.Players.AddRange(murray, wentz);
        await db.SaveChangesAsync();

        // Lines from the displayable source, so the only personal-use input left is the report.
        PlayerGameStat Passing(Player p, Game g, string catt) => new()
        {
            PlayerId = p.Id,
            GameId = g.Id,
            SourceId = s.Market.Id,
            TeamId = s.Home.Id,
            StatLine = $"{{\"C/ATT\":\"{catt}\"}}",
            CapturedAt = g.StartsAt
        };
        db.PlayerGameStats.AddRange(Passing(wentz, weekTwo, "11/20"), Passing(murray, lastWeek, "17/29"));
        await db.SaveChangesAsync();

        async Task<BoardRow> RowAsync(IDataTier tier)
            => (await new BoardService(db, tier).GetAsync("nfl", TimeSpan.FromHours(24))).Single(r => r.Game.Id == s.Game.Id);

        var operatorRow = await RowAsync(Operator);
        Assert.Equal(wentz.FullName, operatorRow.HomeQuarterback);
        Assert.Contains("Ruled out.", operatorRow.HomeQuarterbackBasis);

        var visitorRow = await RowAsync(Guest);
        Assert.Equal(murray.FullName, visitorRow.HomeQuarterback);
        Assert.Equal(Starters.LastGameBasis, visitorRow.HomeQuarterbackBasis);
    }

    // ---- The search -------------------------------------------------------------------------

    /// <summary>
    /// Players are ordered by how many box-score lines they have. An order is a reading of the
    /// lines, so a visitor's is by name until a displayable source writes them.
    /// </summary>
    [Fact]
    public async Task The_search_orders_a_visitors_players_without_personal_use_lines()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        var tag = s.Home.Name[^8..];

        var abe = new Player { SportId = s.Sport.Id, TeamId = s.Home.Id, FullName = $"Abe {tag}" };
        var zed = new Player { SportId = s.Sport.Id, TeamId = s.Home.Id, FullName = $"Zed {tag}" };
        db.Players.AddRange(abe, zed);
        await db.SaveChangesAsync();

        db.PlayerGameStats.Add(new PlayerGameStat
        {
            PlayerId = zed.Id,
            GameId = s.Game.Id,
            SourceId = s.Espn.Id,
            TeamId = s.Home.Id,
            StatLine = """{"H":"2"}""",
            CapturedAt = s.Game.StartsAt
        });
        await db.SaveChangesAsync();

        var operatorOrder = await new SearchService(db, Operator).PlayersAsync(tag);
        Assert.Equal([zed.Id, abe.Id], operatorOrder.Select(p => p.Id));

        var visitorOrder = await new SearchService(db, Guest).PlayersAsync(tag);
        Assert.Equal([abe.Id, zed.Id], visitorOrder.Select(p => p.Id));
    }
}
