using XLines.Core.Analytics;
using XLines.Core.Contracts;
using XLines.Core.Entities;
using XLines.Data;
using XLines.Data.CrossReference;

namespace XLines.Tests.Reliability;

/// <summary>
/// The readers keep to the tier they are asked for (ADR 0024).
///
/// <para>
/// A visitor to a shared desk is shown only what a displayable source supplied. What matters is
/// that the omission is complete and that it says nothing new: a personal-use close left out
/// leaves a game with no close, which the board already explains and the log already leaves
/// ungraded, and a personal-use box score left out leaves a game with none. The operator, and
/// everyone on a local desk, read exactly what they read before.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class DataTierReaderTests(PostgresFixture fixture)
{
    private sealed class Visitor : IDataTier
    {
        public bool CanSeePersonalUseSources => false;
    }

    private static readonly IDataTier Operator = FullDataTier.Instance;
    private static readonly IDataTier Guest = new Visitor();

    private sealed record Scaffold(
        Sport Sport, Team Home, Team Away, Game Game,
        Source Market, Source Espn, Source Evaluation);

    /// <summary>
    /// A game played four hours ago and three sources: a book market whose terms allow display,
    /// a stats provider whose terms do not (ESPN's place), and an odds feed on an evaluation tier.
    /// </summary>
    private static async Task<Scaffold> SeedAsync(XLinesDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var sport = new Sport { Key = $"tier-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = $"Home {suffix}", Abbrev = "HOM" };
        var away = new Team { SportId = sport.Id, Name = $"Away {suffix}", Abbrev = "AWY" };
        db.Teams.AddRange(home, away);

        var market = new Source
        {
            Key = $"tier-market-{suffix}",
            Name = "Market",
            Kind = SourceKind.Odds,
            BaseUrl = "local://market",
            Licence = SourceLicence.Displayable
        };
        var espn = new Source
        {
            Key = $"tier-espn-{suffix}",
            Name = "Stats",
            Kind = SourceKind.Stats,
            BaseUrl = "local://stats",
            Licence = SourceLicence.PersonalUse
        };
        var evaluation = new Source
        {
            Key = $"tier-eval-{suffix}",
            Name = "Evaluation odds",
            Kind = SourceKind.Odds,
            BaseUrl = "local://eval",
            Licence = SourceLicence.PersonalUse
        };
        db.Sources.AddRange(market, espn, evaluation);
        await db.SaveChangesAsync();

        var game = new Game
        {
            SportId = sport.Id,
            HomeTeamId = home.Id,
            AwayTeamId = away.Id,
            StartsAt = DateTimeOffset.UtcNow.AddHours(-4),
            Status = GameStatus.Final,
            HomeScore = 5,
            AwayScore = 3,
            SeasonYear = 2026
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();

        return new Scaffold(sport, home, away, game, market, espn, evaluation);
    }

    private static ClosingLine Close(Scaffold s, Source source, string book, string market, string outcome,
        int american, decimal? line = null)
        => new()
        {
            GameId = s.Game.Id,
            SourceId = source.Id,
            Book = book,
            Market = market,
            Outcome = outcome,
            Line = line,
            PriceAmerican = american,
            CapturedAt = s.Game.StartsAt.AddMinutes(-30),
            PromotedAt = s.Game.StartsAt.AddMinutes(10)
        };

    private static async Task<BoardRow> BoardRowAsync(XLinesDbContext db, Scaffold s, IDataTier tier)
        => Assert.Single(await new BoardService(db, tier).GetAsync(
            s.Sport.Key, TimeSpan.FromHours(24), TimeSpan.FromHours(14)));

    // ---- The board --------------------------------------------------------------------------

    /// <summary>
    /// ESPN's reference close is the only number this game has. The operator is shown it; a
    /// visitor is shown the game unpriced, with the board's own explanation, and nothing that
    /// came from the close.
    /// </summary>
    [Fact]
    public async Task A_close_only_a_personal_use_source_holds_is_the_operators_alone()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        db.ClosingLines.Add(Close(s, s.Espn, "espn-bet", Markets.Moneyline, s.Home.Name, -120));
        await db.SaveChangesAsync();

        var operatorRow = await BoardRowAsync(db, s, Operator);
        Assert.True(operatorRow.HasPrices);
        Assert.Equal(-120, operatorRow.Moneyline.First!.PriceAmerican);

        var visitorRow = await BoardRowAsync(db, s, Guest);
        Assert.False(visitorRow.HasPrices);
        Assert.Null(visitorRow.Moneyline.First);
        Assert.NotNull(visitorRow.Unpriced);

        Assert.Equal(-120, (await new BoardService(db, Operator).GetRowAsync(s.Game.Id))!.Moneyline.First!.PriceAmerican);
        Assert.False((await new BoardService(db, Guest).GetRowAsync(s.Game.Id))!.HasPrices);
    }

    /// <summary>
    /// Where the book market closed the game too, a visitor still has a close — the market's,
    /// which is the one the board prefers anyway.
    /// </summary>
    [Fact]
    public async Task A_visitor_keeps_the_book_markets_close()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        db.ClosingLines.AddRange(
            Close(s, s.Market, "fanduel", Markets.Moneyline, s.Home.Name, -105),
            Close(s, s.Espn, "espn-bet", Markets.Moneyline, s.Home.Name, -130));
        await db.SaveChangesAsync();

        var row = await BoardRowAsync(db, s, Guest);

        Assert.True(row.HasPrices);
        Assert.Equal("fanduel", row.Moneyline.First!.Book);
        Assert.Equal(-105, row.Moneyline.First.PriceAmerican);
    }

    /// <summary>
    /// Live scans are per source too: an odds feed on an evaluation tier is not shown to a
    /// visitor even while the board is live, and the displayable feed's scan is.
    /// </summary>
    [Fact]
    public async Task A_visitor_is_not_shown_scans_from_an_evaluation_feed()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        s.Game.StartsAt = DateTimeOffset.UtcNow.AddHours(3);
        s.Game.Status = GameStatus.Scheduled;
        db.OddsSnapshots.AddRange(
            Scan(s, s.Evaluation, "pinnacle", 150),
            Scan(s, s.Market, "draftkings", 120));
        await db.SaveChangesAsync();

        var operatorRow = await BoardRowAsync(db, s, Operator);
        Assert.Equal(2, operatorRow.BookCount);
        Assert.Equal("pinnacle", operatorRow.Moneyline.First!.Book);

        var visitorRow = await BoardRowAsync(db, s, Guest);
        Assert.Equal(1, visitorRow.BookCount);
        Assert.Equal("draftkings", visitorRow.Moneyline.First!.Book);
    }

    /// <summary>
    /// The gamecast's header carries the board's close for the same reader: a visitor who is shown
    /// no close on the row is shown none on the chart either.
    /// </summary>
    [Fact]
    public async Task The_gamecast_header_keeps_the_boards_close_to_the_tier()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        db.ClosingLines.Add(Close(s, s.Espn, "espn-bet", Markets.Spread, s.Home.Name, -110, -1.5m));
        await db.SaveChangesAsync();

        var contexts = new Contexts(fixture);

        var operatorHeader = await new XLines.Data.Gamecast.GamecastService(contexts, Operator).HeaderAsync(s.Game.Id);
        Assert.Equal(-1.5m, operatorHeader!.Close!.HomeSpread);

        var visitorHeader = await new XLines.Data.Gamecast.GamecastService(contexts, Guest).HeaderAsync(s.Game.Id);
        Assert.Null(visitorHeader!.Close);
    }

    private sealed class Contexts(PostgresFixture fixture) : Microsoft.EntityFrameworkCore.IDbContextFactory<XLinesDbContext>
    {
        public XLinesDbContext CreateDbContext() => fixture.CreateContext();
    }

    private static OddsSnapshot Scan(Scaffold s, Source source, string book, int american)
        => new()
        {
            GameId = s.Game.Id,
            SourceId = source.Id,
            Book = book,
            Market = Markets.Moneyline,
            Outcome = s.Home.Name,
            PriceAmerican = american,
            CapturedAt = DateTimeOffset.UtcNow,
            IngestionRunId = 0
        };

    // ---- The game log -----------------------------------------------------------------------

    /// <summary>
    /// The log grades a game against its close. A game only ESPN closed is graded for the
    /// operator and ungraded for a visitor — not graded against nothing, and not a loss.
    /// </summary>
    [Fact]
    public async Task A_personal_use_close_does_not_grade_a_visitors_log()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        db.ClosingLines.Add(Close(s, s.Espn, "espn-bet", Markets.Spread, s.Home.Name, -110, -1.5m));
        await db.SaveChangesAsync();

        var operatorLog = await new GameLogService(db, Operator).TeamGameLogAsync(s.Home.Id, TimeSpan.FromDays(2));
        Assert.True(Assert.Single(operatorLog).HasClosingLine);

        var visitorLog = await new GameLogService(db, Guest).TeamGameLogAsync(s.Home.Id, TimeSpan.FromDays(2));
        var row = Assert.Single(visitorLog);
        Assert.False(row.HasClosingLine);
        Assert.Null(row.AtsResult);
    }

    /// <summary>A box score and a player's lines from a personal-use source are the operator's alone.</summary>
    [Fact]
    public async Task A_personal_use_box_score_is_the_operators_alone()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        var player = await AddPlayerWithLineAsync(db, s, s.Game);

        var operatorLogs = new GameLogService(db, Operator);
        Assert.NotNull(await operatorLogs.BoxScoreAsync(s.Game.Id));
        Assert.Single(await operatorLogs.PlayerGameLogAsync(player.Id));
        Assert.Equal([2026], await operatorLogs.SeasonsForPlayerAsync(player.Id));

        var visitorLogs = new GameLogService(db, Guest);
        Assert.Null(await visitorLogs.BoxScoreAsync(s.Game.Id));
        Assert.Empty(await visitorLogs.PlayerGameLogAsync(player.Id));
        Assert.Empty(await visitorLogs.SeasonsForPlayerAsync(player.Id));
    }

    // ---- The matchup ------------------------------------------------------------------------

    /// <summary>Form is averaged from stat lines, so a visitor with none to see has no form to read.</summary>
    [Fact]
    public async Task Form_from_personal_use_lines_is_the_operators_alone()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        await AddPlayerWithLineAsync(db, s, s.Game);

        var next = new Game
        {
            SportId = s.Sport.Id,
            HomeTeamId = s.Home.Id,
            AwayTeamId = s.Away.Id,
            StartsAt = DateTimeOffset.UtcNow.AddDays(1),
            SeasonYear = 2026
        };
        db.Games.Add(next);
        await db.SaveChangesAsync();

        var operatorForm = await new MatchupCrossReference(db, Operator).GetAsync(next.Id, TimeSpan.FromDays(7));
        Assert.Single(operatorForm!.Home);

        var visitorForm = await new MatchupCrossReference(db, Guest).GetAsync(next.Id, TimeSpan.FromDays(7));
        Assert.True(visitorForm!.IsEmpty);

        var visitorTeam = await new MatchupCrossReference(db, Guest).GetTeamAsync(s.Home.Id, TimeSpan.FromDays(7));
        Assert.Empty(visitorTeam!.Roster);
    }

    private static async Task<Player> AddPlayerWithLineAsync(XLinesDbContext db, Scaffold s, Game game)
    {
        var player = new Player { SportId = s.Sport.Id, TeamId = s.Home.Id, FullName = $"Batter {s.Home.Abbrev}" };
        db.Players.Add(player);
        await db.SaveChangesAsync();

        db.PlayerGameStats.Add(new PlayerGameStat
        {
            PlayerId = player.Id,
            GameId = game.Id,
            SourceId = s.Espn.Id,
            TeamId = s.Home.Id,
            StatLine = """{"H":"2","AB":"4"}""",
            CapturedAt = game.StartsAt
        });
        await db.SaveChangesAsync();
        return player;
    }

    // ---- The classification -----------------------------------------------------------------

    /// <summary>
    /// The terms read in the going-public plan §4, as the seed and the migration apply them. An
    /// unknown key is personal use, so a provider nobody has classified is not shown to anyone.
    /// </summary>
    [Theory]
    [InlineData("the-odds-api", SourceLicence.Displayable)]
    [InlineData("balldontlie", SourceLicence.Displayable)]
    [InlineData("espn", SourceLicence.PersonalUse)]
    [InlineData("mlb-statsapi", SourceLicence.PersonalUse)]
    [InlineData("odds-api-io", SourceLicence.PersonalUse)]
    [InlineData("a-provider-nobody-has-read", SourceLicence.PersonalUse)]
    public void Each_provider_carries_the_licence_its_terms_give(string key, SourceLicence expected)
        => Assert.Equal(expected, SourceLicences.For(key));

    [Fact]
    public void Personal_use_is_the_default_a_new_source_gets()
        => Assert.Equal(SourceLicence.PersonalUse, new Source().Licence);
}
