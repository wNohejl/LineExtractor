using System.Text.Json;
using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Data.CrossReference;
using LineOps.Ingestion.Adapters;
using LineOps.Ingestion.Services;
using LineOps.Reliability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LineOps.Tests.Reliability;

/// <summary>
/// A UFC card through the real pipeline against Postgres (ADR 0021): UFC 300 as ESPN's fight
/// center recorded it, stored as bouts between fighters; a book's fight attached to the bout in
/// either corner and never created; and the fight rules applied at settlement.
/// </summary>
[Collection(PostgresCollection.Name)]
public class FightIngestionTests(PostgresFixture fixture)
{
    private sealed class StubStats(StatsFetchResult result, string key) : IStatsSource
    {
        public string Key => key;
        public Task<StatsFetchResult> FetchScheduleAsync(string s, DateOnly d, CancellationToken ct) => Task.FromResult(result);
        public Task<StatsFetchResult> FetchRosterAsync(string s, CancellationToken ct) => Task.FromResult(result);
        public Task<StatsFetchResult> FetchBoxScoresAsync(string s, DateOnly d, CancellationToken ct) => Task.FromResult(result);
    }

    private sealed class StubOdds(OddsFetchResult result, string key) : IOddsSource
    {
        public string Key => key;
        public IReadOnlyList<string> SupportedMarkets => Markets.V1;
        public Task<OddsFetchResult> FetchSlateAsync(string s, IReadOnlyList<string> m, CancellationToken ct) => Task.FromResult(result);
    }

    private static JsonElement Load(string fixture)
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture))).RootElement.Clone();

    /// <summary>UFC 300, with its ids made unique to this test so tests sharing the database do not meet.</summary>
    private static StatsFetchResult Ufc300(string tag)
    {
        var players = new List<CanonicalPlayer>();
        var stats = new List<CanonicalPlayerStat>();
        var rounds = EspnStatsAdapter.ReadScoreboardCards(Load("espn.ufc.scoreboard.json")).Single().Rounds;
        var games = EspnStatsAdapter.ParseFightCenter(Load("espn.ufc.fightcenter.json"), "ufc", rounds, players, stats).ToList();

        string Id(string id) => $"{id}-{tag}";
        string Name(string name) => $"{tag}{name}";

        return new StatsFetchResult(
            games.Select(g => g with
            {
                SourceGameId = Id(g.SourceGameId),
                HomeTeamName = Name(g.HomeTeamName),
                AwayTeamName = Name(g.AwayTeamName),
                Home = g.Home! with { Name = Name(g.Home!.Name), SourceTeamId = Id(g.Home.SourceTeamId!) },
                Away = g.Away! with { Name = Name(g.Away!.Name), SourceTeamId = Id(g.Away.SourceTeamId!) },
                Bout = g.Bout! with { EventId = Id(g.Bout!.EventId) }
            }).ToList(),
            players.Select(p => p with
            {
                SourcePlayerId = Id(p.SourcePlayerId),
                FullName = Name(p.FullName),
                SourceTeamId = Id(p.SourceTeamId!),
                TeamName = Name(p.TeamName!)
            }).ToList(),
            stats.Select(s => s with
            {
                SourcePlayerId = Id(s.SourcePlayerId),
                SourceGameId = Id(s.SourceGameId),
                SourceTeamId = Id(s.SourceTeamId!),
                TeamName = Name(s.TeamName!)
            }).ToList(),
            new FetchCost(2));
    }

    private static async Task<(Sport Sport, string Stats, string Odds)> SeedAsync(LineOpsDbContext db, string tag)
    {
        var sport = await db.Sports.FirstOrDefaultAsync(s => s.Key == "ufc");
        if (sport is null)
        {
            sport = new Sport { Key = "ufc", Name = "UFC" };
            db.Sports.Add(sport);
        }

        db.Sources.Add(new Source { Key = $"espn-{tag}", Name = "Stub ESPN", Kind = SourceKind.Stats, BaseUrl = "local://" });
        db.Sources.Add(new Source { Key = $"book-{tag}", Name = "Stub book", Kind = SourceKind.Odds, BaseUrl = "local://" });
        await db.SaveChangesAsync();

        return (sport, $"espn-{tag}", $"book-{tag}");
    }

    private static CreditBudgetGuard Budget(LineOpsDbContext db)
        => new(new BudgetCalculator(db), NullLogger<CreditBudgetGuard>.Instance);

    private static Task IngestCard(LineOpsDbContext db, string source, StatsFetchResult card)
        => new StatsIngestionService(db, new EntityResolver(db), Budget(db), NullLogger<StatsIngestionService>.Instance)
            .IngestAsync(new StubStats(card, source), "ufc", "test", new DateOnly(2024, 4, 13), CancellationToken.None);

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task ACardIsStoredAsBoutsBetweenFightersAndReWalksIdempotently()
    {
        await using var db = fixture.CreateContext();
        var tag = Tag();
        var (sport, source, _) = await SeedAsync(db, tag);
        var card = Ufc300(tag);

        await IngestCard(db, source, card);
        await IngestCard(db, source, card);

        var bouts = await db.Bouts.Include(b => b.Game!).ThenInclude(g => g.HomeTeam)
            .Include(b => b.Game!).ThenInclude(g => g.AwayTeam)
            .Where(b => b.EventId == $"600041053-{tag}").ToListAsync();

        Assert.Equal(13, bouts.Count);

        var main = bouts.Single(b => b.MatchNumber == 1);
        Assert.Equal($"{tag}Alex Pereira", main.Game!.HomeTeam!.Name);
        Assert.Equal((1, 0), (main.Game.HomeScore, main.Game.AwayScore));
        Assert.Equal(GameStatus.Final, main.Game.Status);
        Assert.Equal((BoutOutcome.Win, "KO/TKO", 1, 194), (main.Outcome!.Value, main.Method, main.EndRound!.Value, main.EndSeconds!.Value));
        Assert.True(main.TitleFight);

        var gameIds = bouts.Select(b => b.GameId).ToList();

        // Each fighter is a side and a player on it, with one fight stat line per bout.
        var lines = await db.PlayerGameStats.Include(s => s.Player).Where(s => gameIds.Contains(s.GameId)).ToListAsync();
        Assert.Equal(26, lines.Count);
        Assert.All(lines, l => Assert.Equal(l.Player!.TeamId, l.TeamId));

        Assert.Equal(26, await db.Teams.CountAsync(t => t.SportId == sport.Id && t.Name.StartsWith(tag)));
        Assert.Equal(13, await db.Games.CountAsync(g => gameIds.Contains(g.Id)));
    }

    [Fact]
    public async Task ABooksFightAttachesToTheBoutInEitherCornerAndNeverCreatesOne()
    {
        await using var db = fixture.CreateContext();
        var tag = Tag();
        var (sport, stats, odds) = await SeedAsync(db, tag);
        await IngestCard(db, stats, Ufc300(tag));

        var at = new DateTimeOffset(2024, 4, 13, 22, 0, 0, TimeSpan.Zero);
        var now = DateTimeOffset.UtcNow;

        // The book has Hill at home, writes Procházka without accents, and lists a fight from
        // another promotion that the desk does not hold.
        var slate = new OddsFetchResult(
            [
                new CanonicalGame($"bk-1-{tag}", "ufc", $"{tag}Jamahal Hill", $"{tag}Alex Pereira", at),
                new CanonicalGame($"bk-2-{tag}", "ufc", $"{tag}Jiri Prochazka", $"{tag}Aleksandar Rakic", at),
                new CanonicalGame($"bk-3-{tag}", "ufc", $"{tag}Patricio Freire", $"{tag}Somebody Else", at)
            ],
            [
                new CanonicalOdds($"bk-1-{tag}", "draftkings", Markets.Moneyline, $"{tag}Jamahal Hill", null, 110, now),
                new CanonicalOdds($"bk-1-{tag}", "draftkings", Markets.Moneyline, $"{tag}Alex Pereira", null, -138, now),
                new CanonicalOdds($"bk-1-{tag}", "draftkings", Markets.Total, "over", 1.5m, -175, now),
                new CanonicalOdds($"bk-2-{tag}", "draftkings", Markets.Moneyline, $"{tag}Jiri Prochazka", null, -250, now),
                new CanonicalOdds($"bk-3-{tag}", "draftkings", Markets.Moneyline, $"{tag}Patricio Freire", null, -300, now)
            ],
            new FetchCost(1));

        var games = await db.Games.CountAsync(g => g.SportId == sport.Id);

        await new OddsIngestionService(db, new EntityResolver(db), Budget(db), NullLogger<OddsIngestionService>.Instance)
            .IngestAsync(new StubOdds(slate, odds), "ufc", "test", CancellationToken.None);

        Assert.Equal(games, await db.Games.CountAsync(g => g.SportId == sport.Id));

        var main = await db.Games.Include(g => g.HomeTeam).SingleAsync(g => g.HomeTeam!.Name == $"{tag}Alex Pereira");
        Assert.Equal($"bk-1-{tag}", main.ExternalIds[odds]);

        var prices = await db.OddsSnapshots.Where(s => s.GameId == main.Id).ToListAsync();
        Assert.Equal(-138, prices.Single(p => p.Outcome == $"{tag}Alex Pereira").PriceAmerican);
        Assert.Equal(110, prices.Single(p => p.Outcome == $"{tag}Jamahal Hill").PriceAmerican);
        Assert.Equal(1.5m, prices.Single(p => p.Outcome == "over").Line);

        // The accent-free name lands under the bout's own spelling.
        Assert.True(await db.OddsSnapshots.AnyAsync(s => s.Outcome == $"{tag}Jiří Procházka" && s.PriceAmerican == -250));
        Assert.False(await db.OddsSnapshots.AnyAsync(s => s.Outcome.StartsWith("Patricio Freire")));
    }

    /// <summary>Describes every fighter as Alex Pereira's recorded pages — enough to fill a tape.</summary>
    private sealed class StubFighters(string key) : IFighterSource
    {
        public string Key => key;
        public int Asked { get; private set; }

        public Task<FighterFetchResult> FetchFighterAsync(string sportKey, string id, CancellationToken ct)
        {
            Asked++;
            var fighter = EspnStatsAdapter.ParseFighter(Load("espn.ufc.athlete.json"),
                EspnStatsAdapter.ParseFightSheets(Load("espn.ufc.athlete-stats.json")),
                EspnStatsAdapter.ParseCareerRates(Load("espn.ufc.athlete-statistics.json")),
                Load("espn.ufc.athlete-records.json"));

            return Task.FromResult(new FighterFetchResult(fighter! with { SourceFighterId = id }, new FetchCost(4)));
        }
    }

    [Fact]
    public async Task FightersAreDescribedOnceAndTheTapeReadsThem()
    {
        await using var db = fixture.CreateContext();
        var tag = Tag();
        var (_, source, _) = await SeedAsync(db, tag);
        await IngestCard(db, source, Ufc300(tag));

        var main = await db.Games.Include(g => g.HomeTeam).SingleAsync(g => g.HomeTeam!.Name == $"{tag}Alex Pereira");
        var fighterIds = new[] { main.HomeTeamId, main.AwayTeamId };

        var stub = new StubFighters(source);
        var profiles = new FighterProfileService(db, [stub], NullLogger<FighterProfileService>.Instance);

        // Owed: every fighter on the card has no profile. Taken once; a second pass owes nothing
        // for these two, since neither has fought since.
        Assert.Contains((await profiles.OwedAsync("ufc", source, CancellationToken.None)), o => o.TeamId == main.HomeTeamId);
        await profiles.RefreshAsync("ufc", "test", 500, CancellationToken.None);
        Assert.DoesNotContain(await profiles.OwedAsync("ufc", source, CancellationToken.None), o => fighterIds.Contains(o.TeamId));

        var held = await db.FighterProfiles.SingleAsync(p => p.TeamId == main.HomeTeamId);
        Assert.Equal((76m, 79m, "Orthodox"), (held.HeightInches!.Value, held.ReachInches!.Value, held.Stance));
        Assert.Equal(17, FighterPayload.ReadHistory(held.History).Count);

        var tape = (await new FightService(db).TapeAsync(main.Id))!;

        Assert.Equal("13-4-0", tape.Physical.Single(r => r.Label == "Record").Home);
        Assert.Equal("12-4-0", tape.Physical.Single(r => r.Label == "Record").Away);

        // The tape compares the fighters as they came into the bout: no fight after it is counted.
        Assert.All(tape.Home.History, f => Assert.True(f.Date < main.StartsAt));
        Assert.Contains(tape.Career, r => r.Label == "Sig Strk LPM" && r.Home == "5.01");
        Assert.Contains(tape.PerFight, r => r.Label == "Sig. strikes landed" && r.Home is not null);
        Assert.Contains(tape.PerFight, r => r.Label == "Last five" && r.Home is not null);
    }

    [Fact]
    public async Task TwoFightersOfOneNameStayTwoFighters()
    {
        await using var db = fixture.CreateContext();
        var tag = Tag();
        var (sport, source, _) = await SeedAsync(db, tag);
        var resolver = new EntityResolver(db);

        var middleweight = await resolver.ResolveTeamAsync(sport, source,
            new CanonicalTeamRef($"{tag}Bruno Silva", "4333158", "Silva") { Qualifier = "Middleweight" }, CancellationToken.None);
        var flyweight = await resolver.ResolveTeamAsync(sport, source,
            new CanonicalTeamRef($"{tag}Bruno Silva", "4428001", "Silva") { Qualifier = "Flyweight" }, CancellationToken.None);
        var again = await resolver.ResolveTeamAsync(sport, source,
            new CanonicalTeamRef($"{tag}Bruno Silva", "4428001", "Silva") { Qualifier = "Flyweight" }, CancellationToken.None);

        Assert.NotEqual(middleweight.Id, flyweight.Id);
        Assert.Equal(flyweight.Id, again.Id);
        Assert.Equal($"{tag}Bruno Silva (Flyweight)", flyweight.Name);
    }

    [Fact]
    public async Task SettlementGradesAFightByHowItEnded()
    {
        await using var db = fixture.CreateContext();
        var tag = Tag();
        var (_, source, _) = await SeedAsync(db, tag);
        await IngestCard(db, source, Ufc300(tag));

        var main = await db.Games.Include(g => g.HomeTeam).SingleAsync(g => g.HomeTeam!.Name == $"{tag}Alex Pereira");
        var bmf = await db.Games.Include(g => g.AwayTeam).SingleAsync(g => g.AwayTeam!.Name == $"{tag}Max Holloway");

        JournalEntry Bet(Game game, string market, string outcome, decimal? line = null) => new()
        {
            GameId = game.Id,
            Market = market,
            Outcome = outcome,
            LineTaken = line,
            Book = "draftkings",
            PriceTaken = -110,
            Stake = 10m,
            PlacedAt = game.StartsAt.AddHours(-1)
        };

        var pereira = Bet(main, Markets.Moneyline, $"{tag}Alex Pereira");
        var under = Bet(main, Markets.Total, "under", 1.5m);
        var holloway = Bet(bmf, Markets.Moneyline, $"{tag}Max Holloway");
        var over = Bet(bmf, Markets.Total, "over", 4.5m);
        db.JournalEntries.AddRange(pereira, under, holloway, over);

        // A no contest: the ruling arrives, and every bet on it is void.
        var noContest = await db.Games.Include(g => g.HomeTeam)
            .SingleAsync(g => g.HomeTeam!.Name == $"{tag}Charles Oliveira");
        var oliveira = Bet(noContest, Markets.Moneyline, $"{tag}Charles Oliveira");
        db.JournalEntries.Add(oliveira);

        var ruling = await db.Bouts.SingleAsync(b => b.GameId == noContest.Id);
        ruling.Outcome = BoutOutcome.NoContest;
        noContest.HomeScore = null;
        noContest.AwayScore = null;
        await db.SaveChangesAsync();

        await new SettlementService(db, NullLogger<SettlementService>.Instance).SettleAsync();

        Assert.Equal(EntryResult.Win, pereira.Result);
        Assert.Equal(EntryResult.Win, under.Result);
        Assert.Equal(EntryResult.Win, holloway.Result);
        Assert.Equal(EntryResult.Win, over.Result);
        Assert.Equal(EntryResult.Void, oliveira.Result);
    }
}
