using LineOps.Core.Analytics;
using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Adapters;
using LineOps.Ingestion.Configuration;
using LineOps.Ingestion.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LineOps.Tests.Reliability;

/// <summary>
/// MLB's schedule joined to ESPN's games: its id, the doubleheader number and the probable
/// starters land on the fixture ESPN made, and a doubleheader's two games get the right numbers.
/// Also the player rule that stops two people with one name becoming one player.
/// </summary>
[Collection(PostgresCollection.Name)]
public class MlbSpineTests(PostgresFixture fixture)
{
    private static MlbSpineService Spine(LineOpsDbContext db)
        => new(db,
            new MlbStatsApiAdapter(new HttpClient(), Options.Create(new IngestionOptions()), NullLogger<MlbStatsApiAdapter>.Instance),
            NullLogger<MlbSpineService>.Instance);

    /// <summary>
    /// A private MLB for the test: the spine only reads the sport keyed "mlb", so this test owns
    /// that row's games for its day — a date far from anything else in the shared database.
    /// </summary>
    private static async Task<(Sport Sport, Team Home, Team Away, DateOnly Day)> SeedAsync(LineOpsDbContext db)
    {
        var sport = await db.Sports.FirstOrDefaultAsync(s => s.Key == "mlb")
                    ?? db.Sports.Add(new Sport { Key = "mlb", Name = "MLB" }).Entity;
        await db.SaveChangesAsync();

        var tag = Guid.NewGuid().ToString("N")[..6];
        var home = new Team { SportId = sport.Id, Name = $"Home {tag}", Abbrev = "HOM" };
        var away = new Team { SportId = sport.Id, Name = $"Away {tag}", Abbrev = "AWY" };
        db.Teams.AddRange(home, away);
        await db.SaveChangesAsync();

        // A unique day per test run, years out, so no other test's games share it.
        var day = new DateOnly(2031, 1, 1).AddDays(Random.Shared.Next(0, 3000));
        return (sport, home, away, day);
    }

    private static Game Fixture(Sport sport, Team home, Team away, DateOnly day, int hourEastern)
        => new()
        {
            SportId = sport.Id, HomeTeamId = home.Id, AwayTeamId = away.Id, SeasonYear = day.Year,
            StartsAt = LeagueClock.StartOf(day).AddHours(hourEastern), Status = GameStatus.Scheduled
        };

    private static MlbScheduledGame Scheduled(long pk, Team home, Team away, DateOnly day, int hourEastern,
        int number = 1, string doubleHeader = "N", string? homeStarter = null, string? awayStarter = null)
        => new(pk, LeagueClock.StartOf(day).AddHours(hourEastern), "R", number, doubleHeader, "Scheduled",
            // MLB's team id is unique per team, as the real one is; one derived from the pk once
            // gave two tests' teams the same id, and the spine rightly believed it.
            new MlbSide(100000 + home.Id, home.Name, null, null, homeStarter is null ? null : new MlbProbablePitcher(1, homeStarter)),
            new MlbSide(100000 + away.Id, away.Name, null, null, awayStarter is null ? null : new MlbProbablePitcher(2, awayStarter)));

    [Fact]
    public async Task A_game_gets_its_mlb_id_and_starters_and_the_team_remembers_its_id()
    {
        await using var db = fixture.CreateContext();
        var (sport, home, away, day) = await SeedAsync(db);
        var game = Fixture(sport, home, away, day, 19);
        db.Games.Add(game);
        await db.SaveChangesAsync();

        var outcome = await Spine(db).ApplyAsync(day,
            [Scheduled(776001, home, away, day, 19, homeStarter: "Chris Sale", awayStarter: "Kodai Senga")]);

        var after = await db.Games.AsNoTracking().FirstAsync(g => g.Id == game.Id);
        Assert.Equal(1, outcome.Matched);
        Assert.Equal("776001", after.ExternalIds[MlbSpineService.Key]);
        Assert.Equal("Chris Sale", after.HomeProbablePitcher);
        Assert.Equal("Kodai Senga", after.AwayProbablePitcher);
        Assert.Null(after.DoubleHeaderGame);
        Assert.True((await db.Teams.AsNoTracking().FirstAsync(t => t.Id == home.Id)).ExternalIds.ContainsKey(MlbSpineService.Key));
    }

    [Fact]
    public async Task A_doubleheader_is_numbered_in_the_order_it_is_played()
    {
        await using var db = fixture.CreateContext();
        var (sport, home, away, day) = await SeedAsync(db);
        var early = Fixture(sport, home, away, day, 13);
        var late = Fixture(sport, home, away, day, 18);
        db.Games.AddRange(late, early);
        await db.SaveChangesAsync();

        // Game 2 listed first, as a feed may: the numbers still follow start order.
        await Spine(db).ApplyAsync(day,
        [
            Scheduled(776102, home, away, day, 18, number: 2, doubleHeader: "S", homeStarter: "Second Starter"),
            Scheduled(776101, home, away, day, 13, number: 1, doubleHeader: "S", homeStarter: "First Starter")
        ]);

        var first = await db.Games.AsNoTracking().FirstAsync(g => g.Id == early.Id);
        var second = await db.Games.AsNoTracking().FirstAsync(g => g.Id == late.Id);

        Assert.Equal((1, "First Starter", "776101"), (first.DoubleHeaderGame!.Value, first.HomeProbablePitcher!, first.ExternalIds[MlbSpineService.Key]));
        Assert.Equal((2, "Second Starter", "776102"), (second.DoubleHeaderGame!.Value, second.HomeProbablePitcher!, second.ExternalIds[MlbSpineService.Key]));
    }

    [Fact]
    public async Task A_scratched_starter_is_taken_back_and_the_second_pass_matches_by_id()
    {
        await using var db = fixture.CreateContext();
        var (sport, home, away, day) = await SeedAsync(db);
        var game = Fixture(sport, home, away, day, 19);
        db.Games.Add(game);
        await db.SaveChangesAsync();

        await Spine(db).ApplyAsync(day, [Scheduled(776201, home, away, day, 19, homeStarter: "Announced Starter")]);

        // Moved an hour by a delay, and the starter scratched: found by id, and the name goes.
        await Spine(db).ApplyAsync(day, [Scheduled(776201, home, away, day, 20)]);

        var after = await db.Games.AsNoTracking().FirstAsync(g => g.Id == game.Id);
        Assert.Null(after.HomeProbablePitcher);
        Assert.Equal("776201", after.ExternalIds[MlbSpineService.Key]);
    }

    [Fact]
    public async Task A_game_espn_has_not_listed_is_left_for_later_not_created()
    {
        await using var db = fixture.CreateContext();
        var (sport, home, away, day) = await SeedAsync(db);
        var before = await db.Games.CountAsync();

        var outcome = await Spine(db).ApplyAsync(day, [Scheduled(776301, home, away, day, 19)]);

        Assert.Equal(1, outcome.Unmatched);
        Assert.Equal(before, await db.Games.CountAsync());
        _ = sport;
    }

    // ---- players --------------------------------------------------------------------------

    private static Player Known(int id, string name, int? team, string? espnId)
        => new()
        {
            Id = id, FullName = name, TeamId = team,
            ExternalIds = espnId is null ? [] : new Dictionary<string, string> { ["espn"] = espnId }
        };

    private static CanonicalPlayer Athlete(string id, string name) => new(id, "mlb", name, null, null, null);

    [Fact]
    public void Two_athletes_with_one_name_are_two_players()
    {
        // Will Smith the Dodgers catcher is known under ESPN id 100. Will Smith the pitcher, id
        // 200, is a different person — he used to be merged into the catcher.
        var existing = new List<Player> { Known(1, "Will Smith", team: 10, espnId: "100") };

        Assert.Null(StatsIngestionService.MatchPlayer(existing, Athlete("200", "Will Smith"), "espn", teamId: 20));
        Assert.Equal(1, StatsIngestionService.MatchPlayer(existing, Athlete("100", "Will Smith"), "espn", teamId: 10)!.Id);
    }

    [Fact]
    public void A_name_from_another_source_is_matched_on_the_same_team_and_refused_when_the_team_cannot_tell()
    {
        var existing = new List<Player>
        {
            Known(1, "Luis Garcia", team: 10, espnId: null),
            Known(2, "Luis Garcia", team: 20, espnId: null)
        };

        Assert.Equal(2, StatsIngestionService.MatchPlayer(existing, Athlete("300", "Luis Garcia"), "espn", teamId: 20)!.Id);
        Assert.Null(StatsIngestionService.MatchPlayer(existing, Athlete("300", "Luis Garcia"), "espn", teamId: null));
    }

    [Fact]
    public void The_markets_can_differ_by_sport_and_the_credit_cost_follows_them()
    {
        var options = new IngestionOptions();
        options.TheOddsApi.Markets = ["moneyline", "spread"];
        options.TheOddsApi.MarketsBySport["nfl"] = ["moneyline", "spread", "totals"];

        var adapter = new TheOddsApiAdapter(new HttpClient(), Options.Create(options), NullLogger<TheOddsApiAdapter>.Instance);

        Assert.Equal(3, ((IOddsSource)adapter).CreditsPerScan("nfl"));
        Assert.Equal(2, ((IOddsSource)adapter).CreditsPerScan("mlb"));
        Assert.Contains(Markets.Total, ((IOddsSource)adapter).MarketsFor("nfl"));
    }
}
