using LineOps.Core.Analytics;
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
    /// <summary>
    /// How far two providers' start times for the <i>same</i> fixture may differ.
    ///
    /// A book and a stats feed disagree by minutes about first pitch; nothing about one game
    /// puts them six hours apart. The next game in a series is a day away and a doubleheader's
    /// second game is about four hours away, so both fall outside this by construction — which
    /// is the whole point. The old window was 24 hours, and no status-keyed special case could
    /// make that safe: a neighbour left Live or Scheduled after an outage was still inside it.
    /// </summary>
    public static readonly TimeSpan SameFixtureDrift = TimeSpan.FromHours(6);

    private readonly Dictionary<string, SourceKind?> _sourceKindCache = [];

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
        var known = await db.Teams.Where(t => t.SportId == sport.Id).ToListAsync(ct);

        // Strongest: this source has told us its id for a team before.
        var team = reference.SourceTeamId is { } sourceTeamId
            ? known.FirstOrDefault(t => t.ExternalIds.TryGetValue(sourceKey, out var id) && id == sourceTeamId)
            : null;

        // Then the name — exact, then normalised, since providers differ on punctuation ("St. Louis"
        // vs "St Louis") and casing. Never onto a row this source already knows under another id:
        // that is someone else with the same name. Clubs do not share names; fighters do — two
        // "Bruno Silva"s have fought in the UFC — and taking the first would merge two careers.
        var unclaimed = known.Where(t => !ClaimedByAnother(t, sourceKey, reference.SourceTeamId)).ToList();

        team ??= unclaimed.FirstOrDefault(t => t.Name == reference.Name)
                 ?? unclaimed.FirstOrDefault(t => Normalise(t.Name) == normalised);

        if (team is null)
        {
            team = new Team
            {
                SportId = sport.Id,
                Name = UnusedName(known, reference),
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

    /// <summary>Whether this row carries an id from this source that is not the one being resolved.</summary>
    private static bool ClaimedByAnother(Team team, string sourceKey, string? sourceTeamId)
        => sourceTeamId is not null
           && team.ExternalIds.TryGetValue(sourceKey, out var existing)
           && existing != sourceTeamId
           && existing != team.Name;

    /// <summary>
    /// A name for a new side that no other side in the sport holds. Names are unique per sport, so
    /// a second fighter of the same name is written with what tells them apart — "Bruno Silva
    /// (Flyweight)" — and, failing that, the provider's id. Readers that compare fighters fold the
    /// bracket away (<see cref="FighterNames.Fold"/>).
    /// </summary>
    private static string UnusedName(IReadOnlyList<Team> known, CanonicalTeamRef reference)
    {
        if (known.All(t => t.Name != reference.Name))
            return reference.Name;

        foreach (var qualifier in new[] { reference.Qualifier, reference.SourceTeamId })
        {
            if (string.IsNullOrWhiteSpace(qualifier))
                continue;

            var name = $"{reference.Name} ({qualifier})";
            if (known.All(t => t.Name != name))
                return name;
        }

        return $"{reference.Name} ({Guid.NewGuid():N})"[..Math.Min(128, reference.Name.Length + 35)];
    }

    /// <summary>
    /// The UFC bout a book's fight is, or null when the desk holds none (ADR 0021).
    ///
    /// <para>
    /// A book lists every promotion under one MMA key and does not say which, so a fight is only
    /// ever <i>attached</i> to a bout ESPN created — never created from a book, which would put
    /// regional cards and other promotions on the desk. A bout is found by its two fighters, in
    /// either corner (books do not keep ESPN's red and blue), within a day and a half of the
    /// book's start: two fighters do not meet twice in a week, and a book's start for a card's
    /// fight is often the card's rather than the bout's. Both fighters by name, or one by name and
    /// the other by surname — a fighter billed under a new first name.
    /// </para>
    /// </summary>
    /// <returns>The bout's game with its fighters loaded, and whether the book's home is the bout's away.</returns>
    public async Task<(Game Game, bool Swapped)?> FindBoutAsync(
        Sport sport, string sourceKey, CanonicalGame canonical, CancellationToken ct)
    {
        var candidates = await db.Games
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .Where(g => g.SportId == sport.Id
                        && g.StartsAt > canonical.StartsAt - BoutDrift
                        && g.StartsAt < canonical.StartsAt + BoutDrift)
            .ToListAsync(ct);

        // This book has named the bout before: only which way round is left to read.
        var known = candidates.FirstOrDefault(g =>
            g.ExternalIds.TryGetValue(sourceKey, out var id) && id == canonical.SourceGameId);

        if (known is not null)
            return (known, Orientation(known, canonical)?.Swapped ?? false);

        var best = candidates
            .Where(g => !ClaimedByAnotherGameFrom(g, sourceKey, canonical.SourceGameId))
            .Select(g => (Game: g, Fit: Orientation(g, canonical)))
            .Where(x => x.Fit is not null)
            .OrderByDescending(x => x.Fit!.Value.Strength)
            .ThenBy(x => (x.Game.StartsAt - canonical.StartsAt).Duration())
            .FirstOrDefault();

        if (best.Game is not { } game)
            return null;

        game.ExternalIds = new Dictionary<string, string>(game.ExternalIds) { [sourceKey] = canonical.SourceGameId };
        await db.SaveChangesAsync(ct);

        return (game, best.Fit!.Value.Swapped);
    }

    /// <summary>How far a book's start for a fight may sit from ESPN's start for the bout.</summary>
    public static readonly TimeSpan BoutDrift = TimeSpan.FromHours(36);

    /// <summary>
    /// Whether a book's two fighters are this bout's, and which way round: strength 2 when both
    /// names agree, 1 when one does and the other's surname does, null when they are not this bout.
    /// </summary>
    private static (bool Swapped, int Strength)? Orientation(Game game, CanonicalGame canonical)
    {
        var home = game.HomeTeam?.Name;
        var away = game.AwayTeam?.Name;

        static int Fit(string? ours, string? theirs)
            => FighterNames.Same(ours, theirs) ? 2 : FighterNames.SameSurname(ours, theirs) ? 1 : 0;

        var straight = (Fit(home, canonical.HomeTeamName), Fit(away, canonical.AwayTeamName));
        var swapped = (Fit(home, canonical.AwayTeamName), Fit(away, canonical.HomeTeamName));

        static int? Strength((int A, int B) fit)
            => fit is (2, 2) ? 2 : fit is (2, 1) or (1, 2) ? 1 : null;

        return (Strength(straight), Strength(swapped)) switch
        {
            ({ } s, { } t) => t > s ? (true, t) : (false, s),
            ({ } s, null) => (false, s),
            (null, { } t) => (true, t),
            _ => null
        };
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

        // Whether the provider is talking about a fixture it has already named. Only then is
        // it authoritative about when that fixture starts — see the refresh below.
        var knownToThisProvider = game is not null;

        // Team identity is refreshed even when the game is already known.
        //
        // It used to happen only while creating a game, which meant a provider that started
        // supplying ids and official abbreviations improved nothing on a database that already
        // had its fixtures — every team kept the abbreviation we invented for it and the name
        // we had stored in place of an id. Teams are a bounded set and cached per scope, so on
        // a fifteen-game slate this is thirty lookups once and free thereafter.
        if (game is not null && (canonical.Home is not null || canonical.Away is not null))
        {
            var homeSide = canonical.Home is { } homeRef ? await ResolveTeamAsync(sport, sourceKey, homeRef, ct) : null;
            var awaySide = canonical.Away is { } awayRef ? await ResolveTeamAsync(sport, sourceKey, awayRef, ct) : null;

            // A bout's fighters follow the card (ADR 0021). A short-notice replacement keeps the
            // bout and changes a corner, and a corner read the wrong way round would put the
            // result's 1–0 on the loser. A club never changes its fixture's sides, so only a
            // fight is corrected, and only by the schedule's authority.
            if (FightSports.IsFight(sport.Key)
                && homeSide is not null && awaySide is not null
                && (game.HomeTeamId != homeSide.Id || game.AwayTeamId != awaySide.Id)
                && await IsScheduleAuthorityAsync(sourceKey, ct))
            {
                game.HomeTeamId = homeSide.Id;
                game.AwayTeamId = awaySide.Id;
                await db.SaveChangesAsync(ct);
            }
        }

        if (game is null)
        {
            // Use the provider's team identity when it gave one; otherwise the name is all
            // there is to go on.
            var home = await ResolveTeamAsync(
                sport, sourceKey, canonical.Home ?? new CanonicalTeamRef(canonical.HomeTeamName), ct);

            var away = await ResolveTeamAsync(
                sport, sourceKey, canonical.Away ?? new CanonicalTeamRef(canonical.AwayTeamName), ct);

            // Slow path: the same fixture as a *different* provider named it.
            //
            // This exists to unify one game across sources, so it must never merge two games
            // from the same source. A candidate already carrying a different id from this
            // provider is, by that provider's own reckoning, a different fixture — and the
            // fast path above has already ruled out it being this one.
            //
            // Without that exclusion the time window decides, and no window is safe: a series
            // puts the same two teams eighteen hours apart, a doubleheader puts them four.
            // Both are inside any window wide enough to absorb the scheduling drift this is
            // meant to tolerate, and the result was one fixture per series silently overwritten
            // by the next — its start time, its score and its identifier all replaced.
            //
            // The window is the drift two providers can show for one fixture, not a day: a
            // 24-hour window held both today's final and tomorrow's game when a book named
            // tomorrow's, and whichever row came first took the book's id and had its start
            // "corrected" a day forward. Within the window the nearest start time wins.
            game = candidates
                .Where(g => g.HomeTeamId == home.Id
                            && g.AwayTeamId == away.Id
                            && (g.StartsAt - canonical.StartsAt).Duration() < SameFixtureDrift
                            && !ClaimedByAnotherGameFrom(g, sourceKey, canonical.SourceGameId))
                .OrderBy(g => (g.StartsAt - canonical.StartsAt).Duration())
                .FirstOrDefault();

            if (game is null)
            {
                game = new Game
                {
                    SportId = sport.Id,
                    HomeTeamId = home.Id,
                    AwayTeamId = away.Id,
                    StartsAt = canonical.StartsAt,
                    Status = MapStatus(canonical.Status),
                    // The provider's stamp when it gives one; the calendar's rule when it does not.
                    SeasonYear = canonical.SeasonYear ?? SeasonCalendar.YearOf(sport.Key, canonical.StartsAt),
                    SeasonType = canonical.SeasonType ?? SeasonCalendar.TypeOf(sport.Key, canonical.StartsAt)
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

        // So does the schedule. A game can be moved, and the stats feed correcting the start
        // time of a fixture it has already named is the most reliable statement about it
        // available — so it is taken. A book is not a schedule authority: its commence time
        // differs from the feed's by minutes, and letting both write meant one UPDATE per game
        // per run for ever, with every start-time cutoff downstream depending on which
        // provider ran last.
        if (knownToThisProvider
            && game!.StartsAt != canonical.StartsAt
            && await IsScheduleAuthorityAsync(sourceKey, ct))
        {
            game.StartsAt = canonical.StartsAt;
            changed = true;
        }

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

        // The provider's season stamp outranks the rule that filled the column for rows written
        // before it existed, so a game ESPN sees again corrects itself.
        if (canonical.SeasonYear is { } seasonYear && game.SeasonYear != seasonYear)
        {
            game.SeasonYear = seasonYear;
            changed = true;
        }

        if (canonical.SeasonType is { } seasonType && game.SeasonType != seasonType)
        {
            game.SeasonType = seasonType;
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync(ct);

        return game;
    }

    /// <summary>
    /// Whether this row is already spoken for by a different fixture from the same provider.
    ///
    /// A provider's own identifier is the one thing that distinguishes two games it reports
    /// between the same teams at nearly the same time. Once a row carries one, no other id from
    /// that provider may claim it.
    /// </summary>
    private static bool ClaimedByAnotherGameFrom(Game game, string sourceKey, string sourceGameId)
        => game.ExternalIds.TryGetValue(sourceKey, out var existing) && existing != sourceGameId;

    /// <summary>
    /// Whether a provider's start time is believed over the one on record: stats feeds are,
    /// odds feeds are not. A key with no source row — a test double, a provider seeded later —
    /// is taken at its word, which is the behaviour a lone provider always had.
    /// </summary>
    private async Task<bool> IsScheduleAuthorityAsync(string sourceKey, CancellationToken ct)
    {
        if (!_sourceKindCache.TryGetValue(sourceKey, out var kind))
        {
            kind = await db.Sources
                .Where(s => s.Key == sourceKey)
                .Select(s => (SourceKind?)s.Kind)
                .FirstOrDefaultAsync(ct);

            _sourceKindCache[sourceKey] = kind;
        }

        return kind != SourceKind.Odds;
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
