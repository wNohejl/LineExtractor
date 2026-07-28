using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Services;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Tests.Reliability;

/// <summary>
/// Entity resolution across sources, which is the genuinely hard part of multi-source
/// ingestion and the part that fails silently when it fails.
/// </summary>
[Collection(PostgresCollection.Name)]
public class EntityResolverIntegrationTests(PostgresFixture fixture)
{
    private static async Task<Sport> SeedSportAsync(LineOpsDbContext db)
    {
        var sport = new Sport { Key = $"res-{Guid.NewGuid():N}"[..12], Name = "TEST" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();
        return sport;
    }

    [Fact]
    public async Task ATeamFirstSeenWithoutAnIdIsUpgradedWhenOneArrives()
    {
        await using var db = fixture.CreateContext();
        var resolver = new EntityResolver(db);
        var sport = await SeedSportAsync(db);

        // First sighting: a source that gives only a name. The abbreviation gets derived.
        var first = await resolver.ResolveTeamAsync(sport, "espn", "Detroit Tigers", CancellationToken.None);

        Assert.Equal("Detroit Tigers", first.ExternalIds["espn"]);

        // Second sighting, same source, now carrying ESPN's own id and abbreviation.
        var second = await resolver.ResolveTeamAsync(
            sport, "espn", new CanonicalTeamRef("Detroit Tigers", "6", "DET"), CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("DET", second.Abbrev);
        Assert.Equal("6", second.ExternalIds["espn"]);
    }

    [Fact]
    public async Task AKnownGameStillRefreshesItsTeamIdentity()
    {
        await using var db = fixture.CreateContext();
        var resolver = new EntityResolver(db);
        var sport = await SeedSportAsync(db);

        var startsAt = DateTimeOffset.UtcNow.AddHours(-3);

        var nameOnly = new CanonicalGame(
            "evt-77", sport.Key, "Detroit Tigers", "Kansas City Royals", startsAt, "final", 2, 3);

        var created = await resolver.ResolveGameAsync(sport, "espn", nameOnly, CancellationToken.None);

        // Now the same game arrives again, from the same source, carrying real team identity.
        var withIdentity = nameOnly with
        {
            Home = new CanonicalTeamRef("Detroit Tigers", "6", "DET"),
            Away = new CanonicalTeamRef("Kansas City Royals", "7", "KC")
        };

        var resolved = await resolver.ResolveGameAsync(sport, "espn", withIdentity, CancellationToken.None);

        Assert.Equal(created.Id, resolved.Id);

        // The point: identity must land on a database that already has its fixtures. Refreshing
        // only while creating a game meant a provider that started supplying ids improved
        // nothing for anyone who had already backfilled.
        var teams = await db.Teams.Where(t => t.SportId == sport.Id).ToListAsync();

        Assert.Equal("DET", teams.Single(t => t.Name == "Detroit Tigers").Abbrev);
        Assert.Equal("6", teams.Single(t => t.Name == "Detroit Tigers").ExternalIds["espn"]);
        Assert.Equal("KC", teams.Single(t => t.Name == "Kansas City Royals").Abbrev);
        Assert.Equal("7", teams.Single(t => t.Name == "Kansas City Royals").ExternalIds["espn"]);
    }

    [Fact]
    public async Task ANameOnlyLookupDoesNotOverwriteAStoredProviderId()
    {
        await using var db = fixture.CreateContext();
        var resolver = new EntityResolver(db);
        var sport = await SeedSportAsync(db);

        await resolver.ResolveTeamAsync(
            sport, "espn", new CanonicalTeamRef("Detroit Tigers", "6", "DET"), CancellationToken.None);

        // One source reaches this method by two routes in a single ingest: game resolution,
        // which carries the provider's team id, and player upsert, which knows only a name.
        // The name-only route must not undo the id — it did, microseconds after it was stored,
        // which made the whole improvement look like it had never shipped.
        var byName = await resolver.ResolveTeamAsync(
            sport, "espn", "Detroit Tigers", CancellationToken.None);

        Assert.Equal("6", byName.ExternalIds["espn"]);
        Assert.Equal("DET", byName.Abbrev);
    }

    [Fact]
    public async Task TheProvidersIdWinsOverAChangedName()
    {
        await using var db = fixture.CreateContext();
        var resolver = new EntityResolver(db);
        var sport = await SeedSportAsync(db);

        await resolver.ResolveTeamAsync(
            sport, "espn", new CanonicalTeamRef("Cleveland Indians", "5", "CLE"), CancellationToken.None);

        // Same franchise, renamed. Matching on the name would create a second team and split
        // its history in half; the id is what stops that.
        var renamed = await resolver.ResolveTeamAsync(
            sport, "espn", new CanonicalTeamRef("Cleveland Guardians", "5", "CLE"), CancellationToken.None);

        var teams = await db.Teams.Where(t => t.SportId == sport.Id).ToListAsync();

        Assert.Single(teams);
        Assert.Equal("5", renamed.ExternalIds["espn"]);
    }
}
