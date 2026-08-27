using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Services;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Tests.Reliability;

/// <summary>
/// Two games between the same two teams, a day apart, are two games.
///
/// <para>
/// Resolution used to unify "the same matchup within 24 hours" — a comment saying "the same
/// day" over a condition that meant something else. A baseball series is the counter-example
/// the rule was never tested against: teams play the same opponent on consecutive days, and a
/// night game followed by a day game is about eighteen hours apart, comfortably inside the
/// window. The second sighting therefore matched the first game's row, overwrote its start time
/// and score, and stamped its own provider id over the original.
/// </para>
///
/// <para>
/// The loss was silent and permanent: no error, no failed run, one fixture per series simply
/// gone, and the surviving row carrying one game's identifier beside another game's result.
/// The Tigers' record read five straight defeats because two of the games behind it had been
/// merged away.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class GameSeriesResolutionTests(PostgresFixture fixture)
{
    private static async Task<Sport> SeedSportAsync(LineOpsDbContext db)
    {
        var sport = new Sport { Key = $"ser-{Guid.NewGuid():N}"[..12], Name = "TEST" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();
        return sport;
    }

    private static CanonicalGame Game(string id, DateTimeOffset startsAt, int home, int away)
        => new(
            SourceGameId: id,
            SportKey: "ignored",
            HomeTeamName: "Pittsburgh Pirates",
            AwayTeamName: "Detroit Tigers",
            StartsAt: startsAt,
            Status: "final",
            HomeScore: home,
            AwayScore: away,
            Home: new CanonicalTeamRef("Pittsburgh Pirates", "23", "PIT"),
            Away: new CanonicalTeamRef("Detroit Tigers", "6", "DET"));

    /// <summary>
    /// The real fixtures that exposed this: ESPN events 401816572 and 401816587, seventeen
    /// hours and fifty-five minutes apart. Resolved newest first, because that is the order the
    /// history backfill walks in.
    /// </summary>
    [Fact]
    public async Task Consecutive_games_in_a_series_stay_separate()
    {
        await using var db = fixture.CreateContext();
        var resolver = new EntityResolver(db);
        var sport = await SeedSportAsync(db);

        var later = new DateTimeOffset(2026, 8, 19, 16, 35, 0, TimeSpan.Zero);
        var earlier = new DateTimeOffset(2026, 8, 18, 22, 40, 0, TimeSpan.Zero);

        Assert.True((later - earlier).TotalHours < 24, "the fixtures must sit inside the old window");

        var second = await resolver.ResolveGameAsync(
            sport, "espn", Game("401816587", later, 4, 1), CancellationToken.None);

        var first = await resolver.ResolveGameAsync(
            sport, "espn", Game("401816572", earlier, 3, 4), CancellationToken.None);

        Assert.NotEqual(second.Id, first.Id);

        var stored = await db.Games
            .Where(g => g.SportId == sport.Id)
            .OrderBy(g => g.StartsAt)
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(2, stored.Count);

        // Each row keeps its own identifier, start time and score — the three things the
        // collision destroyed.
        Assert.Equal("401816572", stored[0].ExternalIds["espn"]);
        Assert.Equal(earlier, stored[0].StartsAt);
        Assert.Equal(3, stored[0].HomeScore);

        Assert.Equal("401816587", stored[1].ExternalIds["espn"]);
        Assert.Equal(later, stored[1].StartsAt);
        Assert.Equal(4, stored[1].HomeScore);
    }

    /// <summary>
    /// A doubleheader is the same trap at closer range: two games, same teams, same day, a few
    /// hours apart. A rule that merged them would be wrong in exactly the way the 24-hour one
    /// was, so it is pinned here rather than left to be rediscovered.
    /// </summary>
    [Fact]
    public async Task A_doubleheader_is_two_games()
    {
        await using var db = fixture.CreateContext();
        var resolver = new EntityResolver(db);
        var sport = await SeedSportAsync(db);

        var opener = new DateTimeOffset(2026, 8, 18, 17, 5, 0, TimeSpan.Zero);
        var nightcap = opener.AddHours(4);

        await resolver.ResolveGameAsync(sport, "espn", Game("dh-1", opener, 2, 1), CancellationToken.None);
        await resolver.ResolveGameAsync(sport, "espn", Game("dh-2", nightcap, 0, 7), CancellationToken.None);

        var stored = await db.Games.Where(g => g.SportId == sport.Id).AsNoTracking().ToListAsync();

        Assert.Equal(2, stored.Count);
    }

    /// <summary>
    /// The unification the slow path exists for still has to work: a second provider naming the
    /// same fixture must land on the existing row rather than duplicating it.
    /// </summary>
    [Fact]
    public async Task A_second_provider_still_unifies_onto_the_same_game()
    {
        await using var db = fixture.CreateContext();
        var resolver = new EntityResolver(db);
        var sport = await SeedSportAsync(db);

        var startsAt = new DateTimeOffset(2026, 8, 18, 22, 40, 0, TimeSpan.Zero);

        var fromEspn = await resolver.ResolveGameAsync(
            sport, "espn", Game("401816572", startsAt, 3, 4), CancellationToken.None);

        // A different provider, its own identifier, the same fixture a few minutes off.
        var fromBook = await resolver.ResolveGameAsync(
            sport, "the-odds-api", Game("book-xyz", startsAt.AddMinutes(5), 3, 4), CancellationToken.None);

        Assert.Equal(fromEspn.Id, fromBook.Id);

        var stored = await db.Games.Where(g => g.SportId == sport.Id).AsNoTracking().ToListAsync();
        var only = Assert.Single(stored);

        Assert.Equal("401816572", only.ExternalIds["espn"]);
        Assert.Equal("book-xyz", only.ExternalIds["the-odds-api"]);
    }
    /// <summary>
    /// A provider correcting the start time of a fixture it has already named is believed.
    /// Games get moved — rain, television, doubleheaders collapsed into one date — and a start
    /// time that can never change is a row that quietly disagrees with the schedule for ever.
    /// </summary>
    [Fact]
    public async Task A_provider_can_correct_the_start_time_of_its_own_game()
    {
        await using var db = fixture.CreateContext();
        var resolver = new EntityResolver(db);
        var sport = await SeedSportAsync(db);

        var announced = new DateTimeOffset(2026, 8, 18, 22, 40, 0, TimeSpan.Zero);
        var moved = announced.AddHours(2);

        var created = await resolver.ResolveGameAsync(
            sport, "espn", Game("401816572", announced, 0, 0), CancellationToken.None);

        var corrected = await resolver.ResolveGameAsync(
            sport, "espn", Game("401816572", moved, 3, 4), CancellationToken.None);

        Assert.Equal(created.Id, corrected.Id);

        var only = Assert.Single(await db.Games.Where(g => g.SportId == sport.Id).AsNoTracking().ToListAsync());

        Assert.Equal(moved, only.StartsAt);
        Assert.Equal(3, only.HomeScore);
    }
}
