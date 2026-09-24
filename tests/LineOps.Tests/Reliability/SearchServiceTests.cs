using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Data.CrossReference;

namespace LineOps.Tests.Reliability;

/// <summary>
/// What "mets" finds. One rule for the palette, the journal's game picker and the Players
/// window: every word matches one of the fields it could mean, only enabled sports, and a typed
/// wildcard is a character.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SearchServiceTests(PostgresFixture fixture)
{
    private sealed record World(Sport Sport, Team Home, Team Away, Game Game, Player Starter, Player Bench, string Tag);

    private static async Task<World> SeedAsync(LineOpsDbContext db, bool enabled = true)
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var sport = new Sport { Key = $"ss-{tag}", Name = "TEST", Enabled = enabled };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = $"Harbor {tag} Gulls", Abbrev = $"H{tag}" };
        var away = new Team { SportId = sport.Id, Name = $"Summit {tag} Rams", Abbrev = $"S{tag}" };
        db.Teams.AddRange(home, away);
        await db.SaveChangesAsync();

        var game = new Game
        {
            SportId = sport.Id, HomeTeamId = home.Id, AwayTeamId = away.Id,
            StartsAt = DateTimeOffset.UtcNow.AddDays(-1), Status = GameStatus.Final, SeasonYear = 2026
        };
        var starter = new Player { SportId = sport.Id, TeamId = home.Id, FullName = $"Rowan {tag} Smith", Position = "SS" };
        var bench = new Player { SportId = sport.Id, TeamId = away.Id, FullName = $"Alex {tag} Smith", Position = "C" };
        db.Games.Add(game);
        db.Players.AddRange(starter, bench);
        await db.SaveChangesAsync();

        var source = new Source { Key = $"ss-src-{tag}", Name = "t", Kind = SourceKind.Stats, BaseUrl = "local://t" };
        db.Sources.Add(source);
        await db.SaveChangesAsync();

        db.PlayerGameStats.Add(new PlayerGameStat { PlayerId = starter.Id, GameId = game.Id, SourceId = source.Id, CapturedAt = game.StartsAt });
        await db.SaveChangesAsync();

        return new World(sport, home, away, game, starter, bench, tag);
    }

    [Fact]
    public async Task A_team_is_found_by_name_or_abbreviation()
    {
        await using var db = fixture.CreateContext();
        var w = await SeedAsync(db);
        var search = new SearchService(db);

        Assert.Contains(await search.TeamsAsync($"gulls {w.Tag}"), t => t.Id == w.Home.Id);
        Assert.Contains(await search.TeamsAsync($"H{w.Tag}"), t => t.Id == w.Home.Id);
    }

    [Fact]
    public async Task A_player_is_found_by_name_and_team_and_the_one_who_played_leads()
    {
        await using var db = fixture.CreateContext();
        var w = await SeedAsync(db);
        var search = new SearchService(db);

        var smiths = await search.PlayersAsync($"smith {w.Tag}");
        Assert.Equal(w.Starter.Id, smiths[0].Id);

        // "smith" and the away team's name: only the one on that team.
        var onRams = await search.PlayersAsync($"smith {w.Tag} rams");
        Assert.Equal(w.Bench.Id, Assert.Single(onRams).Id);
    }

    [Fact]
    public async Task A_matchup_is_both_teams()
    {
        await using var db = fixture.CreateContext();
        var w = await SeedAsync(db);

        var games = await new SearchService(db).GamesAsync($"gulls rams {w.Tag}");

        Assert.Equal(w.Game.Id, Assert.Single(games).Id);
    }

    [Fact]
    public async Task A_hidden_league_does_not_come_back_through_search()
    {
        await using var db = fixture.CreateContext();
        var w = await SeedAsync(db, enabled: false);
        var search = new SearchService(db);

        Assert.Empty(await search.TeamsAsync(w.Tag));
        Assert.Empty(await search.PlayersAsync(w.Tag));
        Assert.Empty(await search.GamesAsync(w.Tag));
    }

    [Fact]
    public async Task A_typed_wildcard_is_a_character()
    {
        await using var db = fixture.CreateContext();
        var w = await SeedAsync(db);

        Assert.Empty(await new SearchService(db).TeamsAsync($"{w.Tag}_"));
        Assert.Empty(await new SearchService(db).TeamsAsync("%"));
    }

    [Theory]
    [InlineData("reds braves", true)]
    [InlineData("REDS", true)]
    [InlineData("reds mets", false)]
    [InlineData("", true)]
    [InlineData("cin atl", true)]
    public void The_in_memory_rule_is_the_same_rule(string text, bool expected)
        => Assert.Equal(expected, SearchService.MatchesAllWords(text,
            "Atlanta Braves", "Cincinnati Reds", "ATL", "CIN"));
}
