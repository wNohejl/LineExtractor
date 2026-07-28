using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Ingestion.Services;

/// <summary>
/// Resolves provider-specific identifiers onto our canonical rows.
///
/// This is the genuinely hard part of multi-source ingestion: ESPN's game id is not
/// odds-api.io's game id, and team names differ in punctuation and abbreviation. We match
/// on the source's own id first (cheap and exact), then fall back to normalised team names
/// plus start date, and record the discovered id so the next run takes the fast path.
/// </summary>
public class EntityResolver(LineOpsDbContext db)
{
    private readonly Dictionary<string, Sport> _sportCache = [];

    /// <summary>
    /// Teams already resolved in this scope, keyed by sport, source and the provider's own
    /// reference. A slate is thirty teams playing fifteen games, so without this the identity
    /// refresh below would re-query the same handful of rows for every game on the card.
    /// </summary>
    private readonly Dictionary<(int SportId, string SourceKey, string Reference), Team> _teamCache = [];

    public async Task<Sport> ResolveSportAsync(string sportKey, CancellationToken ct)
    {
        if (_sportCache.TryGetValue(sportKey, out var cached))
            return cached;

        var sport = await db.Sports.FirstOrDefaultAsync(s => s.Key == sportKey, ct);
        if (sport is null)
        {
            sport = new Sport { Key = sportKey, Name = sportKey.ToUpperInvariant() };
            db.Sports.Add(sport);
            await db.SaveChangesAsync(ct);
        }

        _sportCache[sportKey] = sport;
        return sport;
    }

    public Task<Team> ResolveTeamAsync(
        Sport sport, string sourceKey, string teamName, CancellationToken ct)
        => ResolveTeamAsync(sport, sourceKey, new CanonicalTeamRef(teamName), ct);

    /// <summary>
    /// Finds or creates a team, preferring the provider's own identifiers over its wording.
    ///
    /// <para>
    /// Three attempts, strongest first: the provider's team id, an exact name, then a
    /// normalised name. The id match matters because names are a weak key — providers disagree
    /// on punctuation and wording — and a wrong match here silently merges two franchises.
    /// </para>
    ///
    /// <para>
    /// The provider's abbreviation is taken when offered. We used to derive one from the name,
    /// which produced "CR" for the Colorado Rockies and "SFG" for the Giants where ESPN itself
    /// says "COL" and "SF" — invented identifiers that then appeared throughout the UI. A
    /// derived abbreviation is now only the fallback for a source that gives none.
    /// </para>
    /// </summary>
    public async Task<Team> ResolveTeamAsync(
        Sport sport, string sourceKey, CanonicalTeamRef reference, CancellationToken ct)
    {
        var cacheKey = (sport.Id, sourceKey, reference.SourceTeamId ?? reference.Name);

        if (_teamCache.TryGetValue(cacheKey, out var hit))
            return hit;

        var normalised = Normalise(reference.Name);
        Team? team = null;

        // Strongest: this source has told us its id for a team before.
        if (reference.SourceTeamId is { } sourceTeamId)
        {
            var known = await db.Teams.Where(t => t.SportId == sport.Id).ToListAsync(ct);
            team = known.FirstOrDefault(t =>
                t.ExternalIds.TryGetValue(sourceKey, out var id) && id == sourceTeamId);
        }

        team ??= await db.Teams
            .Where(t => t.SportId == sport.Id)
            .FirstOrDefaultAsync(t => t.Name == reference.Name, ct);

        if (team is null)
        {
            // Last chance on a normalised comparison — providers differ on
            // punctuation ("St. Louis" vs "St Louis") and casing.
            var candidates = await db.Teams.Where(t => t.SportId == sport.Id).ToListAsync(ct);
            team = candidates.FirstOrDefault(t => Normalise(t.Name) == normalised);
        }

        if (team is null)
        {
            team = new Team
            {
                SportId = sport.Id,
                Name = reference.Name,
                Abbrev = reference.Abbrev ?? Abbreviate(reference.Name)
            };
            db.Teams.Add(team);
            await db.SaveChangesAsync(ct);
        }
        else if (reference.Abbrev is { Length: > 0 } official && team.Abbrev != official)
        {
            // A team first seen through a source with no abbreviation carries a derived one;
            // the first source that knows the real one corrects it.
            team.Abbrev = official;
            await db.SaveChangesAsync(ct);
        }

        // The external id is the provider's id when there is one. Storing the name here — which
        // is what happened before — made the map useless for exactly the lookup it exists for.
        //
        // Crucially this never *downgrades*. The same source reaches this method by two routes
        // within one ingest: game resolution, which carries the provider's team id, and player
        // upsert, which has only a team name. Writing unconditionally meant the second route
        // overwrote the id with the name microseconds after the first stored it, so the ids
        // never survived a run and the improvement looked like it had not shipped.
        var hasEntry = team.ExternalIds.TryGetValue(sourceKey, out var existing);

        var write = reference.SourceTeamId is { } id
            ? !hasEntry || existing != id          // a real id always wins
            : !hasEntry;                           // a name only fills a gap

        if (write)
        {
            team.ExternalIds = new Dictionary<string, string>(team.ExternalIds)
            {
                [sourceKey] = reference.SourceTeamId ?? reference.Name
            };

            await db.SaveChangesAsync(ct);
        }

        _teamCache[cacheKey] = team;
        return team;
    }

    public async Task<Game> ResolveGameAsync(
        Sport sport, string sourceKey, CanonicalGame canonical, CancellationToken ct)
    {
        // Fast path: we have seen this provider id before.
        var candidates = await db.Games
            .Where(g => g.SportId == sport.Id
                        && g.StartsAt > canonical.StartsAt.AddDays(-2)
                        && g.StartsAt < canonical.StartsAt.AddDays(2))
            .ToListAsync(ct);

        var game = candidates.FirstOrDefault(g =>
            g.ExternalIds.TryGetValue(sourceKey, out var id) && id == canonical.SourceGameId);

        // Team identity is refreshed even when the game is already known.
        //
        // It used to happen only while creating a game, which meant a provider that started
        // supplying ids and official abbreviations improved nothing on a database that already
        // had its fixtures — every team kept the abbreviation we invented for it and the name
        // we had stored in place of an id. Teams are a bounded set and cached per scope, so on
        // a fifteen-game slate this is thirty lookups once and free thereafter.
        if (game is not null && (canonical.Home is not null || canonical.Away is not null))
        {
            if (canonical.Home is { } homeRef)
                await ResolveTeamAsync(sport, sourceKey, homeRef, ct);

            if (canonical.Away is { } awayRef)
                await ResolveTeamAsync(sport, sourceKey, awayRef, ct);
        }

        if (game is null)
        {
            // Use the provider's team identity when it gave one; otherwise the name is all
            // there is to go on.
            var home = await ResolveTeamAsync(
                sport, sourceKey, canonical.Home ?? new CanonicalTeamRef(canonical.HomeTeamName), ct);

            var away = await ResolveTeamAsync(
                sport, sourceKey, canonical.Away ?? new CanonicalTeamRef(canonical.AwayTeamName), ct);

            // Slow path: same matchup on the same day is the same game, regardless of provider.
            game = candidates.FirstOrDefault(g =>
                g.HomeTeamId == home.Id
                && g.AwayTeamId == away.Id
                && Math.Abs((g.StartsAt - canonical.StartsAt).TotalHours) < 24);

            if (game is null)
            {
                game = new Game
                {
                    SportId = sport.Id,
                    HomeTeamId = home.Id,
                    AwayTeamId = away.Id,
                    StartsAt = canonical.StartsAt,
                    Status = MapStatus(canonical.Status)
                };
                db.Games.Add(game);
            }

            game.ExternalIds = new Dictionary<string, string>(game.ExternalIds)
            {
                [sourceKey] = canonical.SourceGameId
            };

            await db.SaveChangesAsync(ct);
        }

        // Scores and status arrive later than the fixture itself, so always refresh them.
        var changed = false;

        if (canonical.HomeScore is not null && game.HomeScore != canonical.HomeScore)
        {
            game.HomeScore = canonical.HomeScore;
            changed = true;
        }

        if (canonical.AwayScore is not null && game.AwayScore != canonical.AwayScore)
        {
            game.AwayScore = canonical.AwayScore;
            changed = true;
        }

        var status = MapStatus(canonical.Status);
        if (canonical.Status is not null && game.Status != status)
        {
            game.Status = status;
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync(ct);

        return game;
    }

    private static GameStatus MapStatus(string? raw) => raw?.ToLowerInvariant() switch
    {
        "live" or "in_progress" or "inprogress" or "status_in_progress" => GameStatus.Live,
        "final" or "settled" or "complete" or "status_final" => GameStatus.Final,
        "postponed" or "cancelled" or "canceled" => GameStatus.Postponed,
        _ => GameStatus.Scheduled
    };

    private static string Normalise(string name)
        => new(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string Abbreviate(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 1
            ? words[0][..Math.Min(3, words[0].Length)].ToUpperInvariant()
            : new string(words.Select(w => w[0]).ToArray()).ToUpperInvariant();
    }
}
