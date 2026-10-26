using LineOps.Core.Analytics;
using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LineOps.Ingestion.Services;

/// <summary>
/// Ingests schedules, scores, rosters and box scores.
///
/// Mirrors <see cref="OddsIngestionService"/>: same run recording, same budget guard, same
/// failure semantics — so the reliability layer treats stats and odds sources identically
/// and one KPI dashboard covers both.
/// </summary>
public class StatsIngestionService(
    LineOpsDbContext db,
    EntityResolver resolver,
    CreditBudgetGuard budget,
    ILogger<StatsIngestionService> logger)
{
    /// <summary>Schedules, scores and box scores for a day.</summary>
    public Task<IngestionOutcome> IngestAsync(
        IStatsSource source, string sportKey, string jobKey, DateOnly date, CancellationToken ct)
        => IngestAsync(source, sportKey, jobKey, date, schedulesOnly: false, ct);

    /// <summary>
    /// Schedules and scores only — no per-game box scores.
    ///
    /// A box-score pass costs one request per finished game on top of the scoreboard, and
    /// before the games are played there is nothing to summarise. Fetching a slate for today
    /// and a slate for yesterday are therefore very different prices, and only one of them is
    /// worth paying twice a day.
    /// </summary>
    public Task<IngestionOutcome> IngestScheduleAsync(
        IStatsSource source, string sportKey, string jobKey, DateOnly date, CancellationToken ct)
        => IngestAsync(source, sportKey, jobKey, date, schedulesOnly: true, ct);

    private async Task<IngestionOutcome> IngestAsync(
        IStatsSource source,
        string sportKey,
        string jobKey,
        DateOnly date,
        bool schedulesOnly,
        CancellationToken ct)
    {
        var sourceRow = await db.Sources.FirstOrDefaultAsync(s => s.Key == source.Key, ct)
            ?? throw new InvalidOperationException($"Source '{source.Key}' is not registered.");

        var run = new IngestionRun
        {
            SourceId = sourceRow.Id,
            JobKey = jobKey,
            StartedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Running
        };

        db.IngestionRuns.Add(run);
        await db.SaveChangesAsync(ct);

        try
        {
            if (!await budget.TryReserveAsync(sourceRow, estimatedRequests: 2, ct))
            {
                run.Status = RunStatus.Partial;
                run.Error = "Skipped: source is at its configured rate/credit budget.";
                run.FinishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return new IngestionOutcome(run.Id, run.Status, 0, run.Error);
            }

            if (source is IFailureInjectable injectable)
                injectable.FailureMode = sourceRow.FailureMode;

            var result = schedulesOnly
                ? await source.FetchScheduleAsync(sportKey, date, ct)
                : await source.FetchBoxScoresAsync(sportKey, date, ct);

            run.RequestsMade = result.Cost.Requests;
            run.CreditsSpent = result.Cost.Credits;

            var rows = await PersistAsync(sourceRow, result, sportKey, run.Id, ct);

            run.RowsIngested = rows;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.Status = RunStatus.Success;

            await db.SaveChangesAsync(ct);

            logger.LogInformation("{Source}/{Sport}: {Rows} stat rows", source.Key, sportKey, rows);
            return new IngestionOutcome(run.Id, run.Status, rows, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A persist that threw leaves the change tracker holding exactly the entities that
            // could not be saved. Recording the failure means calling SaveChanges again, which
            // would retry them and throw a second time — out of the catch block, past the run
            // row, and into the caller. That turned one bad day into a dead backfill. Discard
            // the pending work first, then re-read the run to carry the outcome.
            db.ChangeTracker.Clear();

            var failed = await db.IngestionRuns.FirstOrDefaultAsync(r => r.Id == run.Id, ct);
            var error = $"{ex.GetType().Name}: {ex.Message}";

            if (failed is not null)
            {
                failed.Status = RunStatus.Failed;
                failed.Error = error;
                failed.FinishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            logger.LogError(ex, "{Source}/{Sport}: stats ingestion failed", source.Key, sportKey);
            return new IngestionOutcome(run.Id, RunStatus.Failed, 0, error);
        }
    }

    /// <summary>
    /// Finished games in one sport with no closing total from any source, asked of a provider
    /// that can state a close per game — the backport for a season the box-score walk stored
    /// without lines. What it finds is written exactly as the walk writes it, under the stats
    /// provider's own source and only where that source holds nothing for the side yet.
    ///
    /// <para>
    /// A game the provider has no line for stays owed and is asked again next time; there is no
    /// marker for "asked and found nothing", because ESPN does fill a close in after the fact,
    /// and the request is cheap. Where it has none, there is none: nothing is fabricated.
    /// </para>
    /// </summary>
    public async Task<IngestionOutcome> RefillClosingLinesAsync(
        IClosingLineSource source, string sportKey, string jobKey, int limit, CancellationToken ct)
    {
        var sourceRow = await db.Sources.FirstOrDefaultAsync(s => s.Key == source.Key, ct)
            ?? throw new InvalidOperationException($"Source '{source.Key}' is not registered.");

        var run = new IngestionRun
        {
            SourceId = sourceRow.Id,
            JobKey = jobKey,
            StartedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Running
        };

        db.IngestionRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var owed = await ClosingLinesOwed(sportKey)
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .OrderByDescending(g => g.StartsAt)
            .Take(limit)
            .ToListAsync(ct);

        var lines = new List<CanonicalOdds>();
        var gameMap = new Dictionary<string, Game>();
        var requests = 0;
        var failures = 0;

        // A fight's odds live under its card's event, not its own id (ADR 0021).
        var owedIds = owed.Select(g => g.Id).ToList();
        var cards = await db.Bouts
            .Where(b => owedIds.Contains(b.GameId))
            .ToDictionaryAsync(b => b.GameId, b => b.EventId, ct);

        foreach (var game in owed)
        {
            ct.ThrowIfCancellationRequested();

            if (!game.ExternalIds.TryGetValue(source.Key, out var eventId)
                || game.HomeTeam is null || game.AwayTeam is null)
                continue;

            try
            {
                requests++;
                var found = await source.FetchClosingLinesAsync(sportKey,
                    new ClosingLineRequest(eventId, game.HomeTeam.Name, game.AwayTeam.Name, game.StartsAt,
                        cards.GetValueOrDefault(game.Id)), ct);

                gameMap[eventId] = game;
                lines.AddRange(found);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One game ESPN cannot answer for must not cost the rest of the season.
                failures++;
                logger.LogWarning(ex, "{Source}: closing odds unavailable for {GameId}", source.Key, eventId);
            }
        }

        var written = await PersistClosingLinesAsync(sourceRow, lines, gameMap, ct);

        run.RequestsMade = requests;
        run.RowsIngested = written;
        run.FinishedAt = DateTimeOffset.UtcNow;
        run.Status = failures == 0 || written > 0 ? RunStatus.Success : RunStatus.Failed;
        run.Error = failures > 0 ? $"{failures} of {requests} games had no answer from the provider." : null;
        await db.SaveChangesAsync(ct);

        var filled = lines.Select(l => l.SourceGameId).Distinct().Count();
        logger.LogInformation("{Source}/{Sport}: {Filled} of {Owed} games owed a close got one, {Rows} lines",
            source.Key, sportKey, filled, owed.Count, written);

        return new IngestionOutcome(run.Id, run.Status, written, run.Error);
    }

    /// <summary>
    /// Finals in one sport that no source has given a closing total. The total, because it is
    /// the market a stats provider's walk is likeliest to have missed and the one every game has
    /// — a game with a closing total from somewhere has had its close read.
    /// </summary>
    public IQueryable<Game> ClosingLinesOwed(string sportKey)
        => db.Games.Where(g => g.Sport!.Key == sportKey
                               && g.Status == GameStatus.Final
                               && !db.ClosingLines.Any(c => c.GameId == g.Id && c.Market == Markets.Total));

    private async Task<int> PersistAsync(
        Source sourceRow, StatsFetchResult result, string sportKey, long runId, CancellationToken ct)
    {
        var sport = await resolver.ResolveSportAsync(sportKey, ct);
        var rows = 0;

        // Games first: scores arriving here are what make settlement possible.
        var gameMap = new Dictionary<string, Game>();
        foreach (var canonical in result.Games)
        {
            var game = await resolver.ResolveGameAsync(sport, sourceRow.Key, canonical, ct);
            gameMap[canonical.SourceGameId] = game;
            rows++;

            if (canonical.Bout is { } bout)
                rows += await UpsertBoutAsync(game, bout, ct);
        }

        // Resolve each stat line to a game *before* creating anyone, so that a player only
        // enters the database because they played in a game we can point at.
        //
        // A player who never appears in a box score is a row that will never be read: the
        // cross-reference skips them, the Players panel shows them with nothing, and they cost
        // an index entry each. Any source that reports a roster ahead of the games it played in
        // produces exactly that: every player created, and every stat line then dropped for
        // having nowhere to go.
        var resolved = new List<(CanonicalPlayerStat Stat, Game Game)>();
        Dictionary<string, Game>? knownByExternalId = null;

        foreach (var stat in result.PlayerStats)
        {
            if (!gameMap.TryGetValue(stat.SourceGameId, out var game))
            {
                // The box score references a game this payload did not carry. Load the sport's
                // games once and index them, rather than re-querying per stat line.
                knownByExternalId ??= (await db.Games
                        .Where(g => g.SportId == sport.Id)
                        .ToListAsync(ct))
                    .Where(g => g.ExternalIds.ContainsKey(sourceRow.Key))
                    .GroupBy(g => g.ExternalIds[sourceRow.Key])
                    .ToDictionary(g => g.Key, g => g.First());

                if (!knownByExternalId.TryGetValue(stat.SourceGameId, out game))
                    continue;

                gameMap[stat.SourceGameId] = game;
            }

            resolved.Add((stat, game));
        }

        var participated = resolved.Select(r => r.Stat.SourcePlayerId).ToHashSet();
        var participants = result.Players.Where(p => participated.Contains(p.SourcePlayerId)).ToList();

        if (participants.Count < result.Players.Count)
        {
            logger.LogInformation(
                "{Source}/{Sport}: {Kept} of {Total} reported players actually appeared; the rest are not stored",
                sourceRow.Key, sportKey, participants.Count, result.Players.Count);
        }

        // The newest game each player appears in within this payload, which decides whether it
        // is allowed to say where the player is now (see UpsertPlayersAsync).
        var playedAt = resolved
            .GroupBy(r => r.Stat.SourcePlayerId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.Game.StartsAt));

        var playerMap = await UpsertPlayersAsync(sport, sourceRow.Key, participants, playedAt, ct);
        rows += playerMap.Count;

        // Storage allows one line per player per game per source, and the key is the
        // *resolved* PlayerId — not the provider's athlete id. Those are not the same thing:
        // UpsertPlayersAsync falls back to matching on full name, so two source ids can land
        // on one Player row, and a source that reports a player under several stat categories
        // arrives with several lines to begin with. Either way two inserts with one key end
        // up in the same transaction and the whole day dies on a duplicate.
        //
        // So the batch is keyed the way the constraint is, after resolution, and a repeat
        // updates the row already in flight rather than adding a second.
        var written = new Dictionary<(int PlayerId, int GameId), PlayerGameStat>();
        var collisions = 0;

        foreach (var (stat, game) in resolved)
        {
            if (!playerMap.TryGetValue(stat.SourcePlayerId, out var player))
                continue;

            // Second sighting of a key already handled in this batch. The row is in the change
            // tracker and not yet in the database, so the query below would not find it and we
            // would add a duplicate.
            if (written.TryGetValue((player.Id, game.Id), out var pending))
            {
                collisions++;

                if (pending.StatLine != stat.StatLineJson)
                {
                    pending.StatLine = stat.StatLineJson;
                    pending.CapturedAt = DateTimeOffset.UtcNow;
                    pending.IngestionRunId = runId;
                }

                continue;
            }

            var side = await SideAsync(sport, sourceRow.Key, stat, game, ct);

            // Upsert: a re-run of the same day refreshes the line rather than duplicating it.
            var existing = await db.PlayerGameStats.FirstOrDefaultAsync(
                s => s.PlayerId == player.Id && s.GameId == game.Id && s.SourceId == sourceRow.Id, ct);

            if (existing is null)
            {
                existing = new PlayerGameStat
                {
                    PlayerId = player.Id,
                    GameId = game.Id,
                    SourceId = sourceRow.Id,
                    TeamId = side,
                    StatLine = stat.StatLineJson,
                    CapturedAt = DateTimeOffset.UtcNow,
                    IngestionRunId = runId
                };

                db.PlayerGameStats.Add(existing);
                rows++;
            }
            else if (existing.StatLine != stat.StatLineJson
                     || (side is not null && existing.TeamId != side))
            {
                // A re-walk is also how a line stored before sides were recorded gets one, so
                // a line whose numbers have not moved is still refreshed when its side has.
                existing.StatLine = stat.StatLineJson;
                existing.TeamId = side ?? existing.TeamId;
                existing.CapturedAt = DateTimeOffset.UtcNow;
                existing.IngestionRunId = runId;
                rows++;
            }

            written[(player.Id, game.Id)] = existing;
        }

        if (collisions > 0)
        {
            // Worth saying out loud: it usually means two of the provider's athlete ids
            // resolved to one player, which is an entity-resolution question rather than a
            // storage one.
            logger.LogWarning(
                "{Source}/{Sport}: {Count} stat lines resolved onto a player/game already written; merged",
                sourceRow.Key, sportKey, collisions);
        }

        rows += await PersistClosingLinesAsync(sourceRow, result.Lines, gameMap, ct);

        await db.SaveChangesAsync(ct);
        return rows;
    }

    /// <summary>
    /// Stores what the line closed at, for games that have already finished.
    ///
    /// <para>
    /// These are a <i>reference</i>, never the market. They are written under the stats
    /// provider's own source id, which is what keeps them distinguishable downstream — a reader
    /// wanting the market can require rows from an odds provider and fall back to these only
    /// when a fixture has none. Nothing here can contaminate a book consensus, because nothing
    /// here claims to be one (ADR 0011).
    /// </para>
    ///
    /// <para>
    /// Written once and left alone. A closing line is a fact about a moment that has passed, so
    /// a later walk over the same day must not rewrite it — and re-walking days is the ordinary
    /// case, not the exception.
    /// </para>
    /// </summary>
    private async Task<int> PersistClosingLinesAsync(
        Source sourceRow,
        IReadOnlyList<CanonicalOdds> lines,
        Dictionary<string, Game> gameMap,
        CancellationToken ct)
    {
        if (lines.Count == 0)
            return 0;

        var gameIds = gameMap.Values.Select(g => g.Id).ToHashSet();

        // A fight's close names its fighters as the provider bills them; a fighter whose name is
        // shared is held under a qualified one ("Bruno Silva (Flyweight)"), and a side the board
        // cannot pair is a close nobody reads. So a fight's sides are written as its own (ADR 0021).
        var fighters = await db.Games
            .Where(g => gameIds.Contains(g.Id) && FightSports.Keys.Contains(g.Sport!.Key))
            .Select(g => new { g.Id, Home = g.HomeTeam!.Name, Away = g.AwayTeam!.Name })
            .ToDictionaryAsync(g => g.Id, g => (g.Home, g.Away), ct);

        var held = (await db.ClosingLines
                .Where(c => c.SourceId == sourceRow.Id && gameIds.Contains(c.GameId))
                .Select(c => new { c.GameId, c.Market, c.Outcome })
                .ToListAsync(ct))
            .Select(c => (c.GameId, c.Market, c.Outcome))
            .ToHashSet();

        var written = 0;

        foreach (var line in lines)
        {
            if (!gameMap.TryGetValue(line.SourceGameId, out var game))
                continue;

            var outcome = line.Outcome;

            if (line.Market != Markets.Total && fighters.TryGetValue(game.Id, out var corners))
            {
                if (FighterNames.Same(outcome, corners.Home) && !FighterNames.Same(outcome, corners.Away))
                    outcome = corners.Home;
                else if (FighterNames.Same(outcome, corners.Away) && !FighterNames.Same(outcome, corners.Home))
                    outcome = corners.Away;
            }

            if (!held.Add((game.Id, line.Market, outcome)))
                continue;

            db.ClosingLines.Add(new ClosingLine
            {
                GameId = game.Id,
                SourceId = sourceRow.Id,
                Book = line.Book,
                Market = line.Market,
                Outcome = outcome,
                Line = line.Line,
                PriceAmerican = line.PriceAmerican,
                CapturedAt = line.CapturedAt
            });

            written++;
        }

        if (written > 0)
            logger.LogInformation("{Source}: {Count} closing lines recorded", sourceRow.Key, written);

        return written;
    }

    /// <summary>
    /// A fight's card and result beside its game (ADR 0021): written on first sight, refreshed as
    /// the card changes and the result arrives. The provider is the authority on its own card, so
    /// every field follows it — except a result, which a later read that has lost it (a fight
    /// center gone quiet, the scoreboard standing in) does not erase.
    /// </summary>
    /// <returns>1 when the row was written or changed.</returns>
    private async Task<int> UpsertBoutAsync(Game game, CanonicalBout canonical, CancellationToken ct)
    {
        var bout = await db.Bouts.FirstOrDefaultAsync(b => b.GameId == game.Id, ct);

        if (bout is null)
        {
            bout = new Bout { GameId = game.Id };
            db.Bouts.Add(bout);
        }

        var before = (bout.EventId, bout.EventName, bout.EventShortName, bout.CardSegment, bout.MatchNumber, bout.WeightClass,
            bout.ScheduledRounds, bout.TitleFight, bout.HomeRecord, bout.AwayRecord, bout.Outcome, bout.Method,
            bout.MethodDetail, bout.EndRound, bout.EndSeconds);

        bout.EventId = canonical.EventId;
        bout.EventName = Truncate(canonical.EventName, 160)!;
        bout.EventShortName = Truncate(canonical.EventShortName, 64) ?? bout.EventShortName;
        bout.CardSegment = Truncate(canonical.CardSegment, 32) ?? bout.CardSegment;
        bout.MatchNumber = canonical.MatchNumber ?? bout.MatchNumber;
        bout.WeightClass = Truncate(canonical.WeightClass, 48) ?? bout.WeightClass;
        bout.ScheduledRounds = canonical.ScheduledRounds;
        bout.TitleFight = canonical.TitleFight;
        bout.HomeRecord = Truncate(canonical.HomeRecord, 16) ?? bout.HomeRecord;
        bout.AwayRecord = Truncate(canonical.AwayRecord, 16) ?? bout.AwayRecord;

        if (canonical.Outcome is not null || bout.Outcome is null)
        {
            bout.Outcome = canonical.Outcome;
            bout.Method = Truncate(canonical.Method, 64) ?? (canonical.Outcome is null ? null : bout.Method);
            bout.MethodDetail = Truncate(canonical.MethodDetail, 96);
            bout.EndRound = canonical.EndRound;
            bout.EndSeconds = canonical.EndSeconds;
        }

        // A no contest has no winner and so no score, whatever a scoreboard said before the ruling.
        if (bout.Outcome == BoutOutcome.NoContest && (game.HomeScore is not null || game.AwayScore is not null))
        {
            game.HomeScore = null;
            game.AwayScore = null;
        }

        var after = (bout.EventId, bout.EventName, bout.EventShortName, bout.CardSegment, bout.MatchNumber, bout.WeightClass,
            bout.ScheduledRounds, bout.TitleFight, bout.HomeRecord, bout.AwayRecord, bout.Outcome, bout.Method,
            bout.MethodDetail, bout.EndRound, bout.EndSeconds);

        if (db.Entry(bout).State != EntityState.Added && before == after)
            return 0;

        await db.SaveChangesAsync(ct);
        return 1;
    }

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];

    /// <summary>
    /// The team a stat line was made for, when the source said and it is one of the game's two
    /// sides. A name that resolves to neither is not believed: storing it would credit a game
    /// to a club that was not in it, which is worse than falling back to the player's team.
    /// </summary>
    private async Task<int?> SideAsync(
        Sport sport, string sourceKey, CanonicalPlayerStat stat, Game game, CancellationToken ct)
    {
        if (stat.TeamName is null)
            return null;

        var team = await resolver.ResolveTeamAsync(
            sport, sourceKey, new CanonicalTeamRef(stat.TeamName, stat.SourceTeamId), ct);

        return team.Id == game.HomeTeamId || team.Id == game.AwayTeamId ? team.Id : null;
    }

    private async Task<Dictionary<string, Player>> UpsertPlayersAsync(
        Sport sport,
        string sourceKey,
        IReadOnlyList<CanonicalPlayer> canonicals,
        IReadOnlyDictionary<string, DateTimeOffset> playedAt,
        CancellationToken ct)
    {
        var map = new Dictionary<string, Player>();

        if (canonicals.Count == 0)
            return map;

        var existing = await db.Players.Where(p => p.SportId == sport.Id).ToListAsync(ct);

        // Each known player's most recent stored appearance. A payload older than that is
        // history, and history does not get to say where a player is now: a backfill walks
        // newest day first, so letting every day write the team left each player on the
        // oldest club the walk reached.
        var latest = await db.PlayerGameStats
            .Where(s => s.Game!.SportId == sport.Id)
            .GroupBy(s => s.PlayerId)
            .Select(g => new { g.Key, Latest = g.Max(s => s.Game!.StartsAt) })
            .ToDictionaryAsync(x => x.Key, x => x.Latest, ct);

        foreach (var canonical in canonicals)
        {
            // The team first, so a name shared by two people can be told apart by it — by the
            // provider's id where it gave one, since a fighter's team is the fighter, and two
            // fighters can share a name (ADR 0021).
            var team = canonical.TeamName is null
                ? null
                : await resolver.ResolveTeamAsync(sport, sourceKey,
                    new CanonicalTeamRef(canonical.TeamName, canonical.SourceTeamId), ct);

            var player = MatchPlayer(existing, canonical, sourceKey, team?.Id);

            if (player is null)
            {
                player = new Player
                {
                    SportId = sport.Id,
                    FullName = canonical.FullName,
                    Position = canonical.Position,
                    Status = canonical.Status
                };

                db.Players.Add(player);
                existing.Add(player);
            }

            if (!player.ExternalIds.ContainsKey(sourceKey))
            {
                player.ExternalIds = new Dictionary<string, string>(player.ExternalIds)
                {
                    [sourceKey] = canonical.SourcePlayerId
                };
            }

            // Players move between teams, so the team follows the newest game on record — and
            // only the newest. An older day re-walked keeps its side on the stat line instead.
            var current = !latest.TryGetValue(player.Id, out var last)
                          || !playedAt.TryGetValue(canonical.SourcePlayerId, out var played)
                          || played >= last;

            if (team is not null && current)
                player.TeamId = team.Id;

            map[canonical.SourcePlayerId] = player;
        }

        await db.SaveChangesAsync(ct);
        return map;
    }

    /// <summary>
    /// The stored player a source's athlete is, if any.
    ///
    /// <para>
    /// By the source's own id first — that is identity. By name only as a fallback, for a player
    /// another source created, and never onto a player this source already knows under a
    /// different id: that is a different person with the same name. It used to take any name
    /// match, so two ESPN athletes called Will Smith became one player and one of every pair of
    /// their stat lines was thrown away as a duplicate (ADR 0009's open item). Where more than one
    /// name match remains, the one on the athlete's team is the one; where the team cannot decide
    /// between them, none is taken and a new player is made — a duplicate row can be merged
    /// later, a merged person cannot be split.
    /// </para>
    /// </summary>
    public static Player? MatchPlayer(
        IReadOnlyList<Player> existing, CanonicalPlayer canonical, string sourceKey, int? teamId)
    {
        var byId = existing.FirstOrDefault(p =>
            p.ExternalIds.TryGetValue(sourceKey, out var id) && id == canonical.SourcePlayerId);

        if (byId is not null)
            return byId;

        var byName = existing
            .Where(p => string.Equals(p.FullName, canonical.FullName, StringComparison.Ordinal)
                        && !p.ExternalIds.ContainsKey(sourceKey))
            .ToList();

        if (byName.Count <= 1)
            return byName.FirstOrDefault(p => teamId is null || p.TeamId is null || p.TeamId == teamId);

        var onTeam = byName.Where(p => teamId is not null && p.TeamId == teamId).ToList();
        return onTeam.Count == 1 ? onTeam[0] : null;
    }
}
