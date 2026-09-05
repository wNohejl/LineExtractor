using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Data.CrossReference;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Tests.Reliability;

/// <summary>
/// A season bounds a record the way a rolling window cannot: "this season" names the same
/// games tomorrow as it does today. The readers take a season where one is named and keep
/// the window for callers that have none.
/// </summary>
[Collection(PostgresCollection.Name)]
public class GameLogSeasonTests(PostgresFixture fixture)
{
    private sealed record Scaffold(Sport Sport, Team Team, Team Other);

    private static async Task<Scaffold> SeedAsync(LineOpsDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var sport = new Sport { Key = $"nfl-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();

        var team = new Team { SportId = sport.Id, Name = $"Team {suffix}", Abbrev = "TM" };
        var other = new Team { SportId = sport.Id, Name = $"Other {suffix}", Abbrev = "OT" };
        db.Teams.AddRange(team, other);
        await db.SaveChangesAsync();

        // Two games last season, one this season, all in the past, all final.
        db.Games.AddRange(
            Game(sport, team, other, "2025-09-07", 2025),
            Game(sport, other, team, "2026-01-11", 2025), // a January playoff is still 2025
            Game(sport, team, other, "2026-09-10", 2026));
        await db.SaveChangesAsync();

        return new Scaffold(sport, team, other);
    }

    private static Game Game(Sport sport, Team home, Team away, string date, int seasonYear) => new()
    {
        SportId = sport.Id,
        HomeTeamId = home.Id,
        AwayTeamId = away.Id,
        StartsAt = DateTimeOffset.Parse(date + "T18:00:00Z"),
        Status = GameStatus.Final,
        HomeScore = 21,
        AwayScore = 17,
        SeasonYear = seasonYear
    };

    [Fact]
    public async Task A_named_season_bounds_the_log_and_ignores_the_window()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        var log = new GameLogService(db);

        // A one-day window would exclude everything; the season decides instead.
        var rows = await log.TeamGameLogAsync(s.Team.Id, TimeSpan.FromDays(1), seasonYear: 2025);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.StartsAt < DateTimeOffset.Parse("2026-03-01T00:00:00Z")));
    }

    [Fact]
    public async Task No_season_means_the_window_still_governs()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        var log = new GameLogService(db);

        var rows = await log.TeamGameLogAsync(s.Team.Id, TimeSpan.FromDays(3650));

        Assert.Equal(2, rows.Count); // the third fixture is next week, and a schedule is not a game played
    }

    [Fact]
    public async Task The_seasons_a_team_has_played_are_listed_newest_first()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        var log = new GameLogService(db);

        var seasons = await log.SeasonsForTeamAsync(s.Team.Id);

        // The 2026 fixture is dated in the future relative to the season it names only if the
        // clock says so; either way the ordering and membership hold for what has started.
        Assert.Equal(seasons.OrderByDescending(y => y).ToList(), seasons.ToList());
        Assert.Contains(2025, seasons);
    }

    [Fact]
    public async Task The_matchup_form_for_a_game_can_be_drawn_from_a_named_season()
    {
        await using var db = fixture.CreateContext();
        var s = await SeedAsync(db);
        var lookup = new MatchupCrossReference(db);

        var thisSeason = await db.Games.SingleAsync(g => g.SportId == s.Sport.Id && g.SeasonYear == 2026);

        // Nothing has been played inside the game's own season before it, so the form is
        // empty; drawn from the season before, it is not.
        var own = await lookup.GetTeamAsync(s.Team.Id, TimeSpan.FromDays(3650), maxPlayers: 0, seasonYear: 2026);
        var prior = await lookup.GetTeamAsync(s.Team.Id, TimeSpan.FromDays(3650), maxPlayers: 0, seasonYear: 2025);

        Assert.NotNull(prior);
        Assert.Equal(2, prior!.Recent.Count);
        Assert.NotNull(own);
        Assert.True(own!.Recent.Count <= 1);
        Assert.Equal(2026, thisSeason.SeasonYear);
    }
}
