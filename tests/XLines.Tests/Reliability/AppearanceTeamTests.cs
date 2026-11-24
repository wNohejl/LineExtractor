using System.Text.Json;
using XLines.Core.Contracts;
using XLines.Core.Entities;
using XLines.Data;
using XLines.Data.CrossReference;
using XLines.Ingestion.Services;
using XLines.Reliability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace XLines.Tests.Reliability;

/// <summary>
/// A stat line belongs to the team it was made for, not to wherever the player is now.
///
/// The case that forced it: a player who changed clubs between seasons. Last season's games
/// were played for the old club, and reading them through the player's current team put a whole
/// season of numbers on a side that never fielded him — while the side that did lost him from
/// the season he actually played in.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AppearanceTeamTests(PostgresFixture fixture)
{
    private sealed class StubSource(StatsFetchResult result) : IStatsSource
    {
        public string Key { get; init; } = "stub";

        public Task<StatsFetchResult> FetchScheduleAsync(string s, DateOnly d, CancellationToken ct)
            => Task.FromResult(result);

        public Task<StatsFetchResult> FetchRosterAsync(string s, CancellationToken ct)
            => Task.FromResult(result);

        public Task<StatsFetchResult> FetchBoxScoresAsync(string s, DateOnly d, CancellationToken ct)
            => Task.FromResult(result);
    }

    private sealed record Scaffold(Sport Sport, string SourceKey, string Old, string New, string Third);

    private static async Task<Scaffold> SeedAsync(XLinesDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sport = new Sport { Key = $"side-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);

        var sourceKey = $"stub-{suffix}";
        db.Sources.Add(new Source
        {
            Key = sourceKey,
            Name = "Stub stats",
            Kind = SourceKind.Stats,
            BaseUrl = "local://stub"
        });
        await db.SaveChangesAsync();

        return new Scaffold(sport, sourceKey, $"Old Club {suffix}", $"New Club {suffix}", $"Third Club {suffix}");
    }

    private static StatsFetchResult Day(
        Scaffold s, string gameId, string home, string away, DateTimeOffset at, int season, string playedFor)
        => new(
            Games: [new CanonicalGame(gameId, s.Sport.Key, home, away, at, "final", 24, 20, SeasonYear: season)],
            Players: [new CanonicalPlayer("p1", s.Sport.Key, "Moved On", "WR", null, playedFor)],
            PlayerStats:
            [
                new CanonicalPlayerStat("p1", gameId,
                    JsonSerializer.Serialize(new Dictionary<string, string> { ["REC"] = "6", ["YDS"] = "88" }),
                    TeamName: playedFor)
            ],
            Cost: new FetchCost(1));

    private static StatsIngestionService Service(XLinesDbContext db)
        => new(
            db,
            new EntityResolver(db),
            new CreditBudgetGuard(new BudgetCalculator(db), NullLogger<CreditBudgetGuard>.Instance),
            NullLogger<StatsIngestionService>.Instance);

    private static Task Ingest(XLinesDbContext db, Scaffold s, StatsFetchResult day)
        => Service(db).IngestAsync(
            new StubSource(day) { Key = s.SourceKey }, s.Sport.Key, "test",
            DateOnly.FromDateTime(day.Games[0].StartsAt.UtcDateTime), CancellationToken.None);

    /// <summary>
    /// This season first, then last season re-walked — the order a backfill runs in, newest
    /// day first. Last season was played for the old club; the player is now at the new one.
    /// </summary>
    private static async Task<Scaffold> MovedBetweenSeasonsAsync(XLinesDbContext db)
    {
        var s = await SeedAsync(db);

        await Ingest(db, s, Day(s, "g-2026", s.New, s.Third,
            new DateTimeOffset(2026, 9, 13, 17, 0, 0, TimeSpan.Zero), 2026, playedFor: s.New));

        await Ingest(db, s, Day(s, "g-2025", s.Third, s.Old,
            new DateTimeOffset(2025, 10, 5, 17, 0, 0, TimeSpan.Zero), 2025, playedFor: s.Old));

        return s;
    }

    [Fact]
    public async Task Each_line_keeps_the_side_it_was_made_for()
    {
        await using var db = fixture.CreateContext();
        var s = await MovedBetweenSeasonsAsync(db);

        var sides = await db.PlayerGameStats
            .Where(x => x.Player!.SportId == s.Sport.Id)
            .Select(x => new { x.Game!.SeasonYear, Team = x.Team!.Name })
            .ToDictionaryAsync(x => x.SeasonYear, x => x.Team);

        Assert.Equal(s.Old, sides[2025]);
        Assert.Equal(s.New, sides[2026]);
    }

    [Fact]
    public async Task An_older_day_does_not_move_the_player_off_their_current_team()
    {
        await using var db = fixture.CreateContext();
        var s = await MovedBetweenSeasonsAsync(db);

        // The 2025 day ran last. It used to win, leaving the player at the club he had left.
        var team = await db.Players
            .Where(p => p.SportId == s.Sport.Id)
            .Select(p => p.Team!.Name)
            .SingleAsync();

        Assert.Equal(s.New, team);
    }

    [Fact]
    public async Task A_teams_season_roster_is_who_played_for_it_that_season()
    {
        await using var db = fixture.CreateContext();
        var s = await MovedBetweenSeasonsAsync(db);
        var lookup = new MatchupCrossReference(db);

        var oldClub = await db.Teams.SingleAsync(t => t.Name == s.Old);
        var newClub = await db.Teams.SingleAsync(t => t.Name == s.New);

        var old2025 = await lookup.GetTeamAsync(oldClub.Id, TimeSpan.FromDays(30), seasonYear: 2025);
        var new2025 = await lookup.GetTeamAsync(newClub.Id, TimeSpan.FromDays(30), seasonYear: 2025);
        var new2026 = await lookup.GetTeamAsync(newClub.Id, TimeSpan.FromDays(30), seasonYear: 2026);

        Assert.Equal(["Moved On"], old2025!.Roster.Select(r => r.Name));
        Assert.Empty(new2025!.Roster);
        Assert.Equal(1, Assert.Single(new2026!.Roster).Appearances);
    }

    [Fact]
    public async Task The_player_log_says_who_each_game_was_played_for()
    {
        await using var db = fixture.CreateContext();
        var s = await MovedBetweenSeasonsAsync(db);
        var player = await db.Players.SingleAsync(p => p.SportId == s.Sport.Id);

        var log = new GameLogService(db);
        var last = Assert.Single(await log.PlayerGameLogAsync(player.Id, seasonYear: 2025));

        // Played away at the third club for the old one — not "vs" anyone, as reading the game
        // through the current team would have it.
        Assert.Equal(s.Old, last.Team);
        Assert.Equal(s.Third, last.Opponent);
        Assert.False(last.Home);

        // And it carries what the opponent's crest is keyed by: the sport and the opponent's
        // own abbreviation, not the side the player was on.
        var third = await db.Teams.SingleAsync(t => t.Name == s.Third);
        Assert.False(string.IsNullOrEmpty(third.Abbrev));
        Assert.Equal(s.Sport.Key, last.SportKey);
        Assert.Equal(third.Abbrev, last.OpponentAbbrev);
    }

    [Fact]
    public async Task The_team_log_names_each_opponent_by_its_abbreviation()
    {
        await using var db = fixture.CreateContext();
        var s = await MovedBetweenSeasonsAsync(db);
        var third = await db.Teams.SingleAsync(t => t.Name == s.Third);
        var fresh = await db.Teams.SingleAsync(t => t.Name == s.New);

        var log = new GameLogService(db);
        var rows = await log.TeamGameLogAsync(third.Id, TimeSpan.FromDays(3650));

        // Home to the old club in 2025, away at the new one in 2026: the abbreviation follows
        // the opponent in each game, whichever side the subject was.
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(s.Sport.Key, r.SportKey));
        Assert.Equal(fresh.Abbrev, rows.Single(r => r.Opponent == s.New).OpponentAbbrev);
        var old = await db.Teams.SingleAsync(t => t.Name == s.Old);
        Assert.Equal(old.Abbrev, rows.Single(r => r.Opponent == s.Old).OpponentAbbrev);
    }
}
