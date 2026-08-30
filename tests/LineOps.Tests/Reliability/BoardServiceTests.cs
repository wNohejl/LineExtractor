using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Data.CrossReference;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Tests.Reliability;

/// <summary>
/// Which book has the best number.
///
/// This is the one piece of arithmetic on the desk that directly recommends an action, so it
/// is the one where being confidently wrong costs money. In particular "best" is not "biggest
/// number": on a handicap the line outranks the price, and which line is better depends on
/// which side you are taking.
/// </summary>
[Collection(PostgresCollection.Name)]
public class BoardServiceTests(PostgresFixture fixture)
{
    private sealed record Scaffold(Sport Sport, Game Game, Source Source, Team Home, Team Away);

    private static async Task<Scaffold> SeedAsync(LineOpsDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var sport = new Sport { Key = $"bd-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = $"Home {suffix}", Abbrev = "HOM" };
        var away = new Team { SportId = sport.Id, Name = $"Away {suffix}", Abbrev = "AWY" };
        db.Teams.AddRange(home, away);

        var source = new Source
        {
            Key = $"bd-src-{suffix}",
            Name = "Test odds",
            Kind = SourceKind.Odds,
            BaseUrl = "local://test"
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();

        var game = new Game
        {
            SportId = sport.Id,
            HomeTeamId = home.Id,
            AwayTeamId = away.Id,
            StartsAt = DateTimeOffset.UtcNow.AddHours(4),
            Status = GameStatus.Scheduled
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();

        return new Scaffold(sport, game, source, home, away);
    }

    private static OddsSnapshot Price(
        Scaffold s, string book, string market, string outcome, int american, decimal? line = null)
        => new()
        {
            GameId = s.Game.Id,
            SourceId = s.Source.Id,
            Book = book,
            Market = market,
            Outcome = outcome,
            Line = line,
            PriceAmerican = american,
            CapturedAt = DateTimeOffset.UtcNow,
            IngestionRunId = 0
        };

    private static async Task<BoardRow> LoadAsync(LineOpsDbContext db, Scaffold s)
    {
        var rows = await new BoardService(db).GetAsync(s.Sport.Key, TimeSpan.FromHours(24));
        return Assert.Single(rows);
    }

    [Fact]
    public async Task TheMoneylineBestIsTheOneThatPaysMost()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        db.OddsSnapshots.AddRange(
            Price(s, "draftkings", Markets.Moneyline, s.Home.Name, 120),
            Price(s, "fanduel", Markets.Moneyline, s.Home.Name, 135),
            Price(s, "bet365", Markets.Moneyline, s.Home.Name, 128),
            Price(s, "betmgm", Markets.Moneyline, s.Home.Name, -105));
        await db.SaveChangesAsync();

        var row = await LoadAsync(db, s);
        var best = row.Moneyline.First!;

        Assert.Equal("fanduel", best.Book);
        Assert.Equal(135, best.PriceAmerican);
        Assert.Equal(4, best.Rungs.Count);
    }

    [Fact]
    public async Task OnAHandicapTheLineOutranksThePrice()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        // The trap: -105 is the better *price*, but +1.5 is the better *bet*. A comparator
        // that sorted on price alone would recommend the worse one with total confidence.
        db.OddsSnapshots.AddRange(
            Price(s, "draftkings", Markets.Spread, s.Away.Name, -105, 1.0m),
            Price(s, "fanduel", Markets.Spread, s.Away.Name, -110, 1.5m));
        await db.SaveChangesAsync();

        var row = await LoadAsync(db, s);
        var best = row.Spread.Second!;

        Assert.Equal("fanduel", best.Book);
        Assert.Equal(1.5m, best.Line);
    }

    [Fact]
    public async Task PriceBreaksATieOnTheSameLine()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        db.OddsSnapshots.AddRange(
            Price(s, "draftkings", Markets.Spread, s.Home.Name, -115, -1.5m),
            Price(s, "bet365", Markets.Spread, s.Home.Name, -104, -1.5m),
            Price(s, "betmgm", Markets.Spread, s.Home.Name, -110, -1.5m));
        await db.SaveChangesAsync();

        var row = await LoadAsync(db, s);

        Assert.Equal("bet365", row.Spread.First!.Book);
    }

    [Fact]
    public async Task AnOverWantsTheLowestTotalAndAnUnderTheHighest()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        db.OddsSnapshots.AddRange(
            Price(s, "draftkings", Markets.Total, "over", -110, 8.5m),
            Price(s, "fanduel", Markets.Total, "over", -110, 9.0m),
            Price(s, "draftkings", Markets.Total, "under", -110, 8.5m),
            Price(s, "fanduel", Markets.Total, "under", -110, 9.0m));
        await db.SaveChangesAsync();

        var row = await LoadAsync(db, s);

        // The asymmetry that makes this a per-market comparator rather than one sort.
        Assert.Equal(8.5m, row.Total.First!.Line);
        Assert.Equal("draftkings", row.Total.First.Book);

        Assert.Equal(9.0m, row.Total.Second!.Line);
        Assert.Equal("fanduel", row.Total.Second.Book);
    }

    [Fact]
    public async Task TheEdgeIsTheGapBetweenBestAndWorst()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        db.OddsSnapshots.AddRange(
            Price(s, "draftkings", Markets.Moneyline, s.Home.Name, 100),
            Price(s, "fanduel", Markets.Moneyline, s.Home.Name, 120));
        await db.SaveChangesAsync();

        var row = await LoadAsync(db, s);

        // +100 implies 50.0%, +120 implies 45.5% — about 4.5 points of implied probability,
        // which is what shopping is worth on this line.
        Assert.Equal(4.5, row.Moneyline.First!.EdgePoints!.Value, precision: 1);
    }

    [Fact]
    public async Task BooksThatAgreeShowNoEdge()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        db.OddsSnapshots.AddRange(
            Price(s, "draftkings", Markets.Moneyline, s.Home.Name, -110),
            Price(s, "fanduel", Markets.Moneyline, s.Home.Name, -110));
        await db.SaveChangesAsync();

        var row = await LoadAsync(db, s);

        // The rail collapses and the cell says so, rather than implying a choice worth making.
        Assert.Equal(0, row.Moneyline.First!.EdgePoints!.Value, precision: 3);
    }

    [Fact]
    public async Task ADifferingLineIsReportedEvenWhenEveryPriceMatches()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        // The case a price-only comparison calls "books agree" and gets exactly backwards:
        // identical prices on three different totals is the largest real difference there is.
        db.OddsSnapshots.AddRange(
            Price(s, "draftkings", Markets.Total, "over", -110, 7m),
            Price(s, "fanduel", Markets.Total, "over", -110, 8m),
            Price(s, "bet365", Markets.Total, "over", -110, 9m));
        await db.SaveChangesAsync();

        var row = await LoadAsync(db, s);
        var best = row.Total.First!;

        Assert.True(best.LinesVary);
        Assert.Equal(0, best.EdgePoints!.Value, precision: 3);

        // And the best over is still the lowest total.
        Assert.Equal(7m, best.Line);
    }

    [Fact]
    public async Task MatchingLinesAndPricesAreReportedAsAgreement()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        db.OddsSnapshots.AddRange(
            Price(s, "draftkings", Markets.Total, "over", -110, 8.5m),
            Price(s, "fanduel", Markets.Total, "over", -110, 8.5m));
        await db.SaveChangesAsync();

        var row = await LoadAsync(db, s);

        Assert.False(row.Total.First!.LinesVary);
    }

    [Fact]
    public async Task OnlyTheNewestObservationPerBookCounts()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var stale = Price(s, "draftkings", Markets.Moneyline, s.Home.Name, 200);
        stale.CapturedAt = DateTimeOffset.UtcNow.AddHours(-2);

        db.OddsSnapshots.AddRange(stale, Price(s, "draftkings", Markets.Moneyline, s.Home.Name, 110));
        await db.SaveChangesAsync();

        var row = await LoadAsync(db, s);

        // The scan tier is append-only, so "current" is the latest row. A board that ranked
        // stale prices would send you to a book that has already moved.
        Assert.Equal(110, row.Moneyline.First!.PriceAmerican);
        Assert.Single(row.Moneyline.First.Rungs);
    }

    [Fact]
    public async Task AnUnpricedGameStillAppearsAndSaysWhy()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var row = await LoadAsync(db, s);

        // A game nobody has priced yet is information — it is on the slate and not yet
        // shoppable — so it belongs on the board rather than being filtered out of it.
        Assert.False(row.HasPrices);
        Assert.False(row.Moneyline.IsPriced);

        // And a row of dashes is ambiguous between a broken feed, a fixture nobody quoted and
        // a game already under way. Only one is worth acting on, so the row states which.
        Assert.NotNull(row.Unpriced);
        Assert.Contains("no odds scan", row.Unpriced!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AFixtureTheScanMissedIsDistinguishedFromNoScanAtAll()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        // A second fixture in the same window that *did* get priced proves the feed is alive,
        // so the unpriced one is a coverage gap rather than an outage — which is the difference
        // between "pull data" and "the provider does not carry this game".
        var other = new Game
        {
            SportId = s.Sport.Id,
            HomeTeamId = s.Away.Id,
            AwayTeamId = s.Home.Id,
            StartsAt = DateTimeOffset.UtcNow.AddHours(6),
            Status = GameStatus.Scheduled
        };
        db.Games.Add(other);
        await db.SaveChangesAsync();

        db.OddsSnapshots.Add(new OddsSnapshot
        {
            GameId = other.Id,
            SourceId = s.Source.Id,
            Book = "draftkings",
            Market = Markets.Moneyline,
            Outcome = s.Away.Name,
            PriceAmerican = -110,
            CapturedAt = DateTimeOffset.UtcNow,
            IngestionRunId = 0
        });
        await db.SaveChangesAsync();

        var rows = await new BoardService(db).GetAsync(s.Sport.Key, TimeSpan.FromHours(24));
        var unpriced = rows.Single(r => r.Game.Id == s.Game.Id);

        Assert.Contains("did not cover", unpriced.Unpriced!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PriceAgeIsReportedSoStalenessIsVisible()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        var snapshot = Price(s, "draftkings", Markets.Moneyline, s.Home.Name, -110);
        snapshot.CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-40);

        db.OddsSnapshots.Add(snapshot);
        await db.SaveChangesAsync();

        var row = await LoadAsync(db, s);

        Assert.Null(row.Unpriced);
        Assert.True(row.Age > TimeSpan.FromMinutes(30));
    }

    // ---- The close, once the scan tier has let go of the game ----------------------------
    //
    // ADR 0010 deletes scans for a game that has started and keeps one closing line per book.
    // The board only read the scan tier, so every game in play went blank — sixteen empty rows
    // on a nineteen-game slate — and explained itself by describing the retention policy. The
    // close is the number worth having: it is what the market concluded, and it is what CLV is
    // measured against. These pin that it is shown, that it is labelled as a close, and that it
    // never displaces a live market.

    private static ClosingLine Close(
        Scaffold s, string book, string market, string outcome, int american, decimal? line = null)
        => new()
        {
            GameId = s.Game.Id,
            SourceId = s.Source.Id,
            Book = book,
            Market = market,
            Outcome = outcome,
            Line = line,
            PriceAmerican = american,
            CapturedAt = s.Game.StartsAt.AddMinutes(-30),
            PromotedAt = s.Game.StartsAt.AddMinutes(10)
        };

    /// <summary>A game that has already been played, with its result on record.</summary>
    private static async Task<Scaffold> SeedPlayedAsync(LineOpsDbContext db)
    {
        var s = await SeedAsync(db);

        s.Game.StartsAt = DateTimeOffset.UtcNow.AddHours(-4);
        s.Game.Status = GameStatus.Final;
        s.Game.AwayScore = 3;
        s.Game.HomeScore = 5;

        await db.SaveChangesAsync();
        return s;
    }

    private static async Task<BoardRow> LoadPlayedAsync(LineOpsDbContext db, Scaffold s)
    {
        var rows = await new BoardService(db).GetAsync(
            s.Sport.Key, TimeSpan.FromHours(24), TimeSpan.FromHours(14));

        return Assert.Single(rows);
    }

    [Fact]
    public async Task AGameThatHasStartedKeepsItsClosingLine()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedPlayedAsync(db);

        db.ClosingLines.AddRange(
            Close(s, "draftkings", Markets.Moneyline, s.Home.Name, -115),
            Close(s, "fanduel", Markets.Moneyline, s.Home.Name, -105));
        await db.SaveChangesAsync();

        var row = await LoadPlayedAsync(db, s);

        // Not a dash and not an explanation: the number the market finished on.
        Assert.True(row.HasPrices);
        Assert.Null(row.Unpriced);
        Assert.Equal(-105, row.Moneyline.First!.PriceAmerican);
        Assert.Equal("fanduel", row.Moneyline.First.Book);

        // And it is marked, so the cell can mute it and say what it is on hover rather than
        // letting it read as a price still on offer.
        Assert.True(row.PricesAreClosing);
        Assert.True(row.Moneyline.First.IsClosing);
    }

    [Fact]
    public async Task AClosingRowReportsNoAgeBecauseAClosedLineCannotBeStale()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedPlayedAsync(db);

        db.ClosingLines.Add(Close(s, "draftkings", Markets.Moneyline, s.Home.Name, -110));
        await db.SaveChangesAsync();

        var row = await LoadPlayedAsync(db, s);

        // The header reads the oldest age as "quietest line unmoved". A close is old by
        // construction and will never move again, so counting it there would report a settled
        // number as a still market and send an operator chasing it.
        Assert.NotNull(row.PricedAt);
        Assert.Null(row.Age);
    }

    [Fact]
    public async Task TheLiveMarketOutranksTheClose()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);

        // Both tiers hold this game — which happens between promotion and the prune that
        // follows it. The scan is the truth while one exists; the close is the fallback.
        db.OddsSnapshots.Add(Price(s, "draftkings", Markets.Moneyline, s.Home.Name, 140));
        db.ClosingLines.Add(Close(s, "fanduel", Markets.Moneyline, s.Home.Name, 999));
        await db.SaveChangesAsync();

        var row = await LoadAsync(db, s);

        Assert.False(row.PricesAreClosing);
        Assert.Equal(140, row.Moneyline.First!.PriceAmerican);
        Assert.Single(row.Moneyline.First.Rungs);
    }

    [Fact]
    public async Task AStartedGameWithNoCloseSaysThatRatherThanQuotingThePolicy()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedPlayedAsync(db);

        var row = await LoadPlayedAsync(db, s);

        Assert.False(row.HasPrices);

        // The old wording — "pre-match prices are dropped once a game starts" — was said of
        // every started game, including the ones whose close was sitting unread in the
        // permanent record. It described storage rather than this fixture.
        Assert.NotNull(row.Unpriced);
        Assert.Contains("no closing line", row.Unpriced!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AGameThatHasBeenPlayedIsStillOnTheBoard()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedPlayedAsync(db);

        // The default three-hour reach is a live-market window. A surface an operator also
        // asks "how did tonight go" of has to hold the evening, so the lookback is a parameter.
        var narrow = await new BoardService(db).GetAsync(s.Sport.Key, TimeSpan.FromHours(24));
        Assert.Empty(narrow);

        var wide = await LoadPlayedAsync(db, s);

        Assert.Equal(GameStatus.Final, wide.Game.Status);
        Assert.True(wide.HasScore);
        Assert.Equal("AWY 3–5 HOM", Scoreline.Format(wide.Game));
    }

    [Fact]
    public async Task OneGameLoadedByIdFallsBackToTheCloseTheSameWay()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedPlayedAsync(db);

        db.ClosingLines.Add(Close(s, "draftkings", Markets.Total, "over", -110, 8.5m));
        await db.SaveChangesAsync();

        // The follow-up views load by id rather than being handed a row, so they have to make
        // the same choice — otherwise opening "Every book" on a played game shows nothing.
        var row = await new BoardService(db).GetRowAsync(s.Game.Id);

        Assert.NotNull(row);
        Assert.True(row!.PricesAreClosing);
        Assert.Equal(8.5m, row.Total.First!.Line);
    }
}
